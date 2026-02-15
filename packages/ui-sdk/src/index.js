const defaultFetch = (...args) => fetch(...args);

const PRIMITIVE_NAMES = new Set([
  "Цел",
  "Дроб",
  "Лог",
  "Строка",
  "Пусто",
  "Любой",
  "Задача",
]);

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
    this.defaultAwaitResult = options.defaultAwaitResult ?? true;
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

  async call(functionName, argsOrNamed = [], options = {}) {
    const resetState =
      typeof options.resetState === "boolean"
        ? options.resetState
        : this.defaultResetState;
    const awaitResult =
      typeof options.awaitResult === "boolean"
        ? options.awaitResult
        : this.defaultAwaitResult;

    const payload = {
      function: functionName,
      reset_state: resetState,
      await_result: awaitResult,
    };

    if (Array.isArray(argsOrNamed)) {
      payload.args = argsOrNamed;
    } else if (isRecord(argsOrNamed)) {
      payload.named_args = argsOrNamed;
    } else {
      throw new YasnApiError("Call arguments must be an array or named arguments object", {
        status: 0,
        code: "invalid_call_args",
      });
    }

    const data = await this.#request("/call", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(isRecord(options.headers) ? options.headers : {}),
      },
      body: JSON.stringify(payload),
      signal: options.signal,
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
  const normalizedParams = params.map(normalizeSchemaParam);
  const returnTypeNode = normalizeTypeNode(
    item?.returnTypeNode ?? parseLegacyType(item?.returnType ?? "Любой")
  );
  const returnType =
    typeof item?.returnType === "string" && item.returnType.length > 0
      ? item.returnType
      : formatTypeNode(returnTypeNode);

  return {
    name: String(item?.name ?? ""),
    params: normalizedParams,
    returnType,
    returnTypeNode,
    isAsync: Boolean(item?.isAsync),
    isPublicApi:
      typeof item?.isPublicApi === "boolean" ? item.isPublicApi : true,
    schemaVersion: Number(item?.schemaVersion ?? 1),
    signature:
      typeof item?.signature === "string" && item.signature.length > 0
        ? item.signature
        : buildSignature(
            String(item?.name ?? ""),
            normalizedParams,
            returnType,
            Boolean(item?.isAsync)
          ),
    ui: isRecord(item?.ui) ? item.ui : {},
  };
}

export function normalizeSchemaParam(param) {
  const typeNode = normalizeTypeNode(
    param?.typeNode ?? parseLegacyType(param?.type ?? "Любой")
  );
  return {
    name: String(param?.name ?? ""),
    type:
      typeof param?.type === "string" && param.type.length > 0
        ? param.type
        : formatTypeNode(typeNode),
    typeNode,
    ui: isRecord(param?.ui) ? param.ui : buildUiHints(typeNode),
  };
}

export function normalizeTypeNode(raw) {
  if (isRecord(raw) && typeof raw.kind === "string") {
    const kind = raw.kind;
    if (kind === "primitive") {
      const name =
        typeof raw.name === "string" && raw.name.length > 0
          ? raw.name
          : "Любой";
      return {
        kind,
        name,
        nullable: name === "Пусто",
      };
    }

    if (kind === "list") {
      const element = normalizeTypeNode(raw.element ?? { kind: "primitive", name: "Любой" });
      return {
        kind,
        element,
        nullable: Boolean(raw.nullable),
      };
    }

    if (kind === "dict") {
      const key = normalizeTypeNode(raw.key ?? { kind: "primitive", name: "Любой" });
      const value = normalizeTypeNode(raw.value ?? { kind: "primitive", name: "Любой" });
      return {
        kind,
        key,
        value,
        nullable: Boolean(raw.nullable),
      };
    }

    if (kind === "union") {
      const variants = Array.isArray(raw.variants)
        ? raw.variants.map(normalizeTypeNode)
        : [primitiveType("Любой")];
      return {
        kind,
        variants,
        nullable:
          variants.some((variant) =>
            variant.kind === "primitive" && variant.name === "Пусто"
          ) || Boolean(raw.nullable),
      };
    }
  }

  if (typeof raw === "string") {
    return parseLegacyType(raw);
  }

  return primitiveType("Любой");
}

export function parseLegacyType(typeLabel) {
  const raw = String(typeLabel ?? "").trim();
  if (raw.length === 0) {
    return primitiveType("Любой");
  }

  const unionParts = splitTopLevel(raw, "|");
  if (unionParts.length > 1) {
    return {
      kind: "union",
      variants: unionParts.map(parseLegacyType),
      nullable: unionParts.some((part) => part.trim() === "Пусто"),
    };
  }

  if (raw.endsWith("?")) {
    const base = parseLegacyType(raw.slice(0, -1));
    return {
      kind: "union",
      variants: [base, primitiveType("Пусто")],
      nullable: true,
    };
  }

  const listPayload = extractGenericPayload(raw, "Список");
  if (listPayload !== null) {
    return {
      kind: "list",
      element: parseLegacyType(listPayload),
      nullable: false,
    };
  }

  const dictPayload = extractGenericPayload(raw, "Словарь");
  if (dictPayload !== null) {
    const [keyRaw, valueRaw] = splitTopLevel(dictPayload, ",");
    if (keyRaw && valueRaw) {
      return {
        kind: "dict",
        key: parseLegacyType(keyRaw),
        value: parseLegacyType(valueRaw),
        nullable: false,
      };
    }
  }

  return primitiveType(PRIMITIVE_NAMES.has(raw) ? raw : "Любой");
}

export function formatTypeNode(typeNode) {
  const node = normalizeTypeNode(typeNode);
  switch (node.kind) {
    case "primitive":
      return node.name ?? "Любой";
    case "list":
      return `Список<${formatTypeNode(node.element)}>`;
    case "dict":
      return `Словарь<${formatTypeNode(node.key)}, ${formatTypeNode(node.value)}>`;
    case "union":
      return (node.variants ?? []).map(formatTypeNode).join(" | ") || "Любой";
    default:
      return "Любой";
  }
}

export function isValueAssignableToType(value, typeNode) {
  const node = normalizeTypeNode(typeNode);
  if (node.kind === "primitive") {
    return isPrimitiveAssignable(value, node.name);
  }

  if (node.kind === "list") {
    return (
      Array.isArray(value) &&
      value.every((item) => isValueAssignableToType(item, node.element))
    );
  }

  if (node.kind === "dict") {
    if (!isRecord(value)) {
      return false;
    }

    return Object.entries(value).every(
      ([key, item]) =>
        isValueAssignableToType(key, node.key) &&
        isValueAssignableToType(item, node.value)
    );
  }

  if (node.kind === "union") {
    return (node.variants ?? []).some((variant) =>
      isValueAssignableToType(value, variant)
    );
  }

  return true;
}

function buildSignature(name, params, returnType, isAsync) {
  const args = params.map((param) => `${param.name}: ${param.type}`).join(", ");
  const prefix = isAsync ? "async " : "";
  return `${prefix}${name}(${args}) -> ${returnType}`;
}

function joinUrl(baseUrl, path) {
  if (/^https?:\/\//i.test(path)) {
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

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function primitiveType(name) {
  return {
    kind: "primitive",
    name,
    nullable: name === "Пусто",
  };
}

function isPrimitiveAssignable(value, name) {
  switch (name) {
    case "Любой":
      return true;
    case "Пусто":
      return value === null;
    case "Лог":
      return typeof value === "boolean";
    case "Строка":
      return typeof value === "string";
    case "Цел":
      return Number.isInteger(value);
    case "Дроб":
      return typeof value === "number" && Number.isFinite(value);
    case "Задача":
      return isRecord(value) && typeof value.task_id === "number";
    default:
      return true;
  }
}

function extractGenericPayload(raw, genericName) {
  if (!raw.startsWith(genericName)) {
    return null;
  }

  const angle = sliceBracketPayload(raw, "<", ">");
  if (angle !== null) {
    return angle;
  }

  return sliceBracketPayload(raw, "[", "]");
}

function sliceBracketPayload(raw, open, close) {
  const openIndex = raw.indexOf(open);
  if (openIndex < 0 || !raw.endsWith(close)) {
    return null;
  }

  return raw.slice(openIndex + 1, -1).trim();
}

function splitTopLevel(raw, delimiter) {
  const result = [];
  let angleDepth = 0;
  let squareDepth = 0;
  let roundDepth = 0;
  let token = "";

  for (const ch of raw) {
    if (ch === "<") {
      angleDepth += 1;
      token += ch;
      continue;
    }

    if (ch === ">") {
      angleDepth = Math.max(0, angleDepth - 1);
      token += ch;
      continue;
    }

    if (ch === "[") {
      squareDepth += 1;
      token += ch;
      continue;
    }

    if (ch === "]") {
      squareDepth = Math.max(0, squareDepth - 1);
      token += ch;
      continue;
    }

    if (ch === "(") {
      roundDepth += 1;
      token += ch;
      continue;
    }

    if (ch === ")") {
      roundDepth = Math.max(0, roundDepth - 1);
      token += ch;
      continue;
    }

    if (ch === delimiter && angleDepth === 0 && squareDepth === 0 && roundDepth === 0) {
      const trimmed = token.trim();
      if (trimmed.length > 0) {
        result.push(trimmed);
      }

      token = "";
      continue;
    }

    token += ch;
  }

  const trimmedTail = token.trim();
  if (trimmedTail.length > 0) {
    result.push(trimmedTail);
  }

  return result;
}

function buildUiHints(typeNode) {
  const node = normalizeTypeNode(typeNode);
  const control =
    node.kind === "list" || node.kind === "dict"
      ? "json"
      : node.kind === "union"
      ? "select"
      : node.kind === "primitive" && node.name === "Лог"
      ? "checkbox"
      : node.kind === "primitive" && (node.name === "Цел" || node.name === "Дроб")
      ? "number"
      : node.kind === "primitive" && node.name === "Любой"
      ? "json"
      : "text";

  return {
    control,
    placeholder: defaultPlaceholder(node),
    nullable: Boolean(node.nullable),
    required: !Boolean(node.nullable),
  };
}

function defaultPlaceholder(typeNode) {
  const node = normalizeTypeNode(typeNode);
  if (node.kind === "list") {
    return "[1, 2, 3]";
  }

  if (node.kind === "dict") {
    return "{\"key\":\"value\"}";
  }

  if (node.kind === "union") {
    return (node.variants ?? []).map(formatTypeNode).join(" | ") || "value";
  }

  if (node.kind === "primitive") {
    switch (node.name) {
      case "Цел":
        return "42";
      case "Дроб":
        return "3.14";
      case "Лог":
        return "true | false";
      case "Любой":
        return "{\"key\":\"value\"}";
      default:
        return "value";
    }
  }

  return "value";
}
