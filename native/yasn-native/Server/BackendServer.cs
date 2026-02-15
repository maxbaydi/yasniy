using System.Net;
using System.Text;
using System.Text.Json;
using YasnNative.Bytecode;
using YasnNative.Core;

namespace YasnNative.Server;

public static class BackendServer
{
    public static void Serve(string sourcePath, string host = "127.0.0.1", int port = 8000)
    {
        var backend = BackendKernel.FromFile(sourcePath);
        var source = backend.SourcePath;
        var prefix = $"http://{host}:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Console.WriteLine($"[yasn-native] Backend started: {prefix}");
        Console.WriteLine($"[yasn-native] Source: {source}");

        while (true)
        {
            var ctx = listener.GetContext();
            _ = Task.Run(() => HandleRequest(ctx, backend, source));
        }
    }

    private static void HandleRequest(HttpListenerContext ctx, BackendKernel backend, string source)
    {
        try
        {
            var request = ctx.Request;
            var path = request.Url?.AbsolutePath ?? "/";

            if (request.HttpMethod == "OPTIONS")
            {
                SendCorsPreflight(ctx.Response);
                return;
            }

            if (path == "/health")
            {
                SendOk(ctx.Response, new Dictionary<string, object?>
                {
                    ["status"] = "ok",
                    ["source"] = source,
                });
                return;
            }

            if (path == "/functions")
            {
                SendOk(ctx.Response, new Dictionary<string, object?>
                {
                    ["functions"] = backend.ListFunctions(),
                });
                return;
            }

            if (path == "/schema")
            {
                SendOk(ctx.Response, new Dictionary<string, object?>
                {
                    ["functions"] = FunctionSchemaBuilder.ToJsonList(backend.ListSchema()),
                });
                return;
            }

            if (path == "/call" && request.HttpMethod == "POST")
            {
                var body = ReadJsonBody(request);
                var fnName = body.TryGetValue("function", out var fnRaw) ? fnRaw as string : null;
                var args = body.TryGetValue("args", out var argsRaw) && argsRaw is List<object?> list
                    ? list
                    : [];
                var resetState = body.TryGetValue("reset_state", out var resetRaw) && resetRaw is bool b && b;

                if (string.IsNullOrWhiteSpace(fnName))
                {
                    SendError(ctx.Response, 400, "invalid_request", "Field 'function' must be a non-empty string");
                    return;
                }

                var result = backend.Call(fnName, args, resetState);
                SendOk(ctx.Response, new Dictionary<string, object?>
                {
                    ["result"] = BytecodeCodec.NormalizeForJson(result),
                });
                return;
            }

            SendError(ctx.Response, 404, "not_found", $"Route not found: {path}");
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
}
