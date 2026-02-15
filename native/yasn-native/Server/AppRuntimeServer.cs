using System.Net;
using System.Text;
using System.Text.Json;
using YasnNative.App;
using YasnNative.Bytecode;
using YasnNative.Core;

namespace YasnNative.Server;

public static class AppRuntimeServer
{
    public static void Serve(AppBundle bundle, BackendKernel backend, UiAssetManifest assets, string host = "127.0.0.1", int port = 8080)
    {
        if (!assets.HasAssets)
        {
            throw new YasnException("В приложении нет UI-ресурсов. Упакуйте UI через --ui-dist");
        }

        var prefix = $"http://{host}:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        var appLabel = string.IsNullOrWhiteSpace(bundle.DisplayName) ? bundle.Name : bundle.DisplayName;
        Console.WriteLine($"[yasn] App runtime started: {prefix}");
        Console.WriteLine($"[yasn] App: {appLabel}");
        Console.WriteLine("[yasn] API endpoints: /api/functions, /api/schema, /api/call");

        while (true)
        {
            var ctx = listener.GetContext();
            _ = Task.Run(() => HandleRequest(ctx, bundle, backend, assets));
        }
    }

    private static void HandleRequest(HttpListenerContext ctx, AppBundle bundle, BackendKernel backend, UiAssetManifest assets)
    {
        try
        {
            var request = ctx.Request;
            var path = request.Url?.AbsolutePath ?? "/";

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                HandleApi(ctx, bundle, backend, path);
                return;
            }

            if (path == "/health")
            {
                SendJson(ctx.Response, 200, new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["data"] = new Dictionary<string, object?>
                    {
                        ["status"] = "ok",
                        ["app"] = bundle.Name,
                        ["mode"] = "run-app",
                    },
                });
                return;
            }

            SendStatic(ctx.Response, assets, path);
        }
        catch (YasnException ex)
        {
            SendError(ctx.Response, 500, "runtime_error", ex.Message);
        }
        catch (Exception ex)
        {
            SendError(ctx.Response, 500, "handler_crash", $"Unhandled request error: {ex.Message}");
        }
        finally
        {
            try
            {
                ctx.Response.OutputStream.Close();
            }
            catch
            {
                // ignore close failures
            }
        }
    }

    private static void HandleApi(HttpListenerContext ctx, AppBundle bundle, BackendKernel backend, string path)
    {
        var request = ctx.Request;
        if (request.HttpMethod == "OPTIONS")
        {
            SendCorsPreflight(ctx.Response);
            return;
        }

        if (path == "/api/health")
        {
            SendOk(ctx.Response, new Dictionary<string, object?>
            {
                ["status"] = "ok",
                ["app"] = bundle.Name,
                ["source"] = backend.SourcePath,
            });
            return;
        }

        if (path == "/api/functions")
        {
            SendOk(ctx.Response, new Dictionary<string, object?>
            {
                ["functions"] = backend.ListFunctions(),
            });
            return;
        }

        if (path == "/api/schema")
        {
            SendOk(ctx.Response, new Dictionary<string, object?>
            {
                ["schemaVersion"] = 2,
                ["functions"] = FunctionSchemaBuilder.ToJsonList(backend.ListSchema()),
            });
            return;
        }

        if (path == "/api/call")
        {
            if (request.HttpMethod != "POST")
            {
                SendError(ctx.Response, 405, "method_not_allowed", "Route /api/call supports only POST");
                return;
            }

            var body = ReadJsonBody(request);
            var fnName = body.TryGetValue("function", out var fnRaw) ? fnRaw as string : null;

            if (string.IsNullOrWhiteSpace(fnName))
            {
                SendError(ctx.Response, 400, "invalid_request", "Field 'function' must be a non-empty string");
                return;
            }

            if (!TryReadBoolean(body, "reset_state", defaultValue: false, out var resetState, out var boolError))
            {
                SendError(ctx.Response, boolError!.StatusCode, boolError.Code, boolError.Message);
                return;
            }

            if (!TryReadBoolean(body, "await_result", defaultValue: true, out var awaitResult, out boolError))
            {
                SendError(ctx.Response, boolError!.StatusCode, boolError.Code, boolError.Message);
                return;
            }

            body.TryGetValue("args", out var argsRaw);
            body.TryGetValue("named_args", out var namedArgsRaw);
            if (!backend.TryPrepareCall(fnName, argsRaw, namedArgsRaw, out var args, out var callError))
            {
                SendError(ctx.Response, callError!.StatusCode, callError.Code, callError.Message);
                return;
            }

            var result = backend.Call(fnName, args, resetState, awaitResult);
            SendOk(ctx.Response, new Dictionary<string, object?>
            {
                ["result"] = BytecodeCodec.NormalizeForJson(result),
            });
            return;
        }

        SendError(ctx.Response, 404, "not_found", $"Route not found: {path}");
    }

    private static void SendStatic(HttpListenerResponse response, UiAssetManifest assets, string path)
    {
        if (!assets.TryResolve(path, out var asset))
        {
            SendError(response, 404, "not_found", $"Static file not found: {path}");
            return;
        }

        response.StatusCode = 200;
        response.ContentType = asset.ContentType;
        response.ContentLength64 = asset.Content.Length;
        response.OutputStream.Write(asset.Content, 0, asset.Content.Length);
    }

    private static Dictionary<string, object?> ReadJsonBody(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, leaveOpen: true);
        var raw = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            result[prop.Name] = BytecodeCodec.DecodeJsonValue(prop.Value);
        }

        return result;
    }

    private static void SendOk(HttpListenerResponse response, object data)
    {
        SendJson(response, 200, new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["data"] = data,
        });
    }

    private static void SendError(HttpListenerResponse response, int status, string code, string message)
    {
        SendJson(response, status, new Dictionary<string, object?>
        {
            ["ok"] = false,
            ["error"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["message"] = message,
            },
        });
    }

    private static void SendJson(HttpListenerResponse response, int status, object payload)
    {
        var raw = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        });

        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = raw.Length;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Request-Id";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.OutputStream.Write(raw, 0, raw.Length);
    }

    private static void SendCorsPreflight(HttpListenerResponse response)
    {
        response.StatusCode = 204;
        response.ContentLength64 = 0;
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Request-Id";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
    }

    private static bool TryReadBoolean(
        Dictionary<string, object?> body,
        string key,
        bool defaultValue,
        out bool value,
        out BackendCallValidationError? error)
    {
        error = null;
        value = defaultValue;
        if (!body.TryGetValue(key, out var raw) || raw is null)
        {
            return true;
        }

        if (raw is bool b)
        {
            value = b;
            return true;
        }

        error = new BackendCallValidationError(400, "invalid_request", $"Field '{key}' must be a boolean");
        return false;
    }
}
