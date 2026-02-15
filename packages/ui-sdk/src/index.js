const defaultFetch = (...args) => fetch(...args);

export class YasnApiError extends Error {
  constructor(message, options = {}) {
    super(message);
    this.name = "YasnApiError";
    this.status = options.status ?? 0;
    this.code = options.code ?? "request_failed";
    this.payload = options.payload ?? null;
  }
}

export class YasnClient {
  constructor(options = {}) {
    this.baseUrl = normalizeBaseUrl(options.baseUrl ?? "/api");
    this.fetchImpl = options.fetchImpl ?? defaultFetch;
    this.defaultResetState = options.defaultResetState ?? false;
  }

  async functions() {
    const data = await this.#request("/functions", { method: "GET" });
    return Array.isArray(data.functions) ? data.functions : [];
  }

  async schema() {
    const data = await this.#request("/schema", { method: "GET" });
    const raw = Array.isArray(data.functions) ? data.functions : [];
    return raw.map(normalizeSchemaItem);
  }

  async call(functionName, args = [], options = {}) {
    const resetState =
      typeof options.resetState === "boolean"
        ? options.resetState
        : this.defaultResetState;

    const data = await this.#request("/call", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        function: functionName,
        args,
        reset_state: resetState,
      }),
    });

    return data.result;
  }

  async health() {
    return this.#request("/health", { method: "GET" });
  }

  async #request(path, init) {
    const response = await this.fetchImpl(joinUrl(this.baseUrl, path), init);
    const payload = await parseJson(response);

    if (!response.ok) {
      throw makeApiError(payload, response.status);
    }

    if (!payload || payload.ok !== true) {
      throw makeApiError(payload, response.status);
    }

    return payload.data ?? {};
  }
}

export function createYasnClient(options) {
  return new YasnClient(options);
}

export function normalizeSchemaItem(item) {
  const params = Array.isArray(item?.params) ? item.params : [];
  const normalizedParams = params.map((param) => ({
    name: String(param?.name ?? ""),
    type: String(param?.type ?? "Любой"),
  }));

  return {
    name: String(item?.name ?? ""),
    params: normalizedParams,
    returnType: String(item?.returnType ?? "Любой"),
    isAsync: Boolean(item?.isAsync),
    signature:
      typeof item?.signature === "string" && item.signature.length > 0
        ? item.signature
        : buildSignature(String(item?.name ?? ""), normalizedParams, String(item?.returnType ?? "Любой"), Boolean(item?.isAsync)),
  };
}

function buildSignature(name, params, returnType, isAsync) {
  const args = params.map((param) => `${param.name}: ${param.type}`).join(", ");
  const prefix = isAsync ? "async " : "";
  return `${prefix}${name}(${args}) -> ${returnType}`;
}

function joinUrl(baseUrl, path) {
  if (/^https?:\\/\\//i.test(path)) {
    return path;
  }

  const base = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
  const suffix = path.startsWith("/") ? path : `/${path}`;
  return `${base}${suffix}`;
}

function normalizeBaseUrl(value) {
  if (typeof value !== "string" || value.trim().length === 0) {
    return "/api";
  }

  const trimmed = value.trim();
  return trimmed.endsWith("/") ? trimmed.slice(0, -1) : trimmed;
}

function makeApiError(payload, status) {
  const message =
    payload?.error?.message ||
    payload?.message ||
    `Request failed with status ${status}`;
  const code = payload?.error?.code || "request_failed";
  return new YasnApiError(message, {
    status,
    code,
    payload,
  });
}

async function parseJson(response) {
  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes("application/json")) {
    const text = await response.text();
    return {
      ok: false,
      error: {
        code: "invalid_content_type",
        message: text || "Expected JSON response",
      },
    };
  }

  try {
    return await response.json();
  } catch {
    return {
      ok: false,
      error: {
        code: "invalid_json",
        message: "Response is not valid JSON",
      },
    };
  }
}
