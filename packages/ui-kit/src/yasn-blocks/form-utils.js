import { normalizeTypeNode } from "@yasn/ui-sdk";

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function pickControl(param) {
  const typeNode = normalizeTypeNode(param.typeNode ?? param.type);
  const requested = typeof param.ui?.control === "string" ? param.ui.control : "";

  if (requested === "checkbox") return "checkbox";
  if (requested === "json") return "json";
  if (requested === "number") return "number";

  if (typeNode.kind === "primitive") {
    if (typeNode.name === "Лог") return "checkbox";
    if (typeNode.name === "Цел" || typeNode.name === "Дроб") return "number";
    if (typeNode.name === "Любой") return "json";
    return "text";
  }

  if (typeNode.kind === "list" || typeNode.kind === "dict") return "json";
  if (isBoolNullableUnion(typeNode)) return "bool-select";
  return "text";
}

export function defaultInputValue(param) {
  return pickControl(param) === "checkbox" ? false : "";
}

export function coerceInputValue(raw, param) {
  return coerceByType(raw, normalizeTypeNode(param.typeNode ?? param.type), param.name);
}

function coerceByType(raw, typeNode, paramName) {
  const node = normalizeTypeNode(typeNode);

  if (node.nullable && isEmpty(raw)) return null;
  if (node.kind === "primitive") return coercePrimitive(raw, node.name, paramName);

  if (node.kind === "list") {
    const value = parseJsonField(raw, paramName);
    if (!Array.isArray(value)) throw new Error(`Field '${paramName}' expects JSON array`);
    return value.map((item) => coerceByType(item, node.element, paramName));
  }

  if (node.kind === "dict") {
    const value = parseJsonField(raw, paramName);
    if (!isRecord(value)) throw new Error(`Field '${paramName}' expects JSON object`);
    const out = {};
    for (const [key, item] of Object.entries(value)) {
      out[String(coerceByType(key, node.key, paramName))] = coerceByType(item, node.value, paramName);
    }
    return out;
  }

  if (node.kind === "union") {
    const variants = Array.isArray(node.variants) ? node.variants : [];
    const nonNull = variants.filter((v) => !(v.kind === "primitive" && v.name === "Пусто"));
    if (variants.length === 0) return raw;
    for (const variant of nonNull) {
      try {
        return coerceByType(raw, variant, paramName);
      } catch {}
    }
    if (isEmpty(raw) && variants.some((v) => v.kind === "primitive" && v.name === "Пусто")) {
      return null;
    }
    throw new Error(
      `Field '${paramName}' does not match any union variant: ${variants.map(formatType).join(" | ")}`
    );
  }

  return raw;
}

function coercePrimitive(raw, primitiveName, paramName) {
  if (primitiveName === "Пусто") return null;

  if (primitiveName === "Любой") {
    if (typeof raw !== "string") return raw;
    const text = raw.trim();
    if (text.length === 0) return null;
    try {
      return JSON.parse(text);
    } catch {
      return raw;
    }
  }

  if (primitiveName === "Строка") return String(raw ?? "");

  if (primitiveName === "Лог") {
    if (typeof raw === "boolean") return raw;
    const text = String(raw ?? "").trim().toLowerCase();
    if (["true", "истина", "1"].includes(text)) return true;
    if (["false", "ложь", "0"].includes(text)) return false;
    throw new Error(`Field '${paramName}' expects boolean value`);
  }

  if (primitiveName === "Цел") {
    const number = Number(String(raw ?? "").replace(",", "."));
    if (!Number.isFinite(number) || !Number.isInteger(number)) {
      throw new Error(`Field '${paramName}' expects integer value`);
    }
    return number;
  }

  if (primitiveName === "Дроб") {
    const number = Number(String(raw ?? "").replace(",", "."));
    if (!Number.isFinite(number)) throw new Error(`Field '${paramName}' expects numeric value`);
    return number;
  }

  if (primitiveName === "Задача") {
    const value = parseJsonField(raw, paramName);
    if (!isRecord(value) || typeof value.task_id !== "number") {
      throw new Error(`Field '${paramName}' expects task object`);
    }
    return value;
  }

  return raw;
}

function parseJsonField(raw, paramName) {
  if (typeof raw !== "string") return raw;
  const text = raw.trim();
  if (text.length === 0) throw new Error(`Field '${paramName}' expects JSON value`);
  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`Field '${paramName}' expects valid JSON`);
  }
}

function isBoolNullableUnion(typeNode) {
  const node = normalizeTypeNode(typeNode);
  if (node.kind !== "union") return false;
  const variants = Array.isArray(node.variants) ? node.variants : [];
  const names = variants.filter((v) => v.kind === "primitive").map((v) => v.name);
  return names.includes("Лог") && names.includes("Пусто") && names.length === 2;
}

function isEmpty(value) {
  if (value === null || value === undefined) return true;
  if (typeof value === "string") return value.trim().length === 0;
  return false;
}

function formatType(typeNode) {
  const node = normalizeTypeNode(typeNode);
  if (node.kind === "primitive") return node.name ?? "Любой";
  if (node.kind === "list") return `Список<${formatType(node.element)}>`;
  if (node.kind === "dict") return `Словарь<${formatType(node.key)}, ${formatType(node.value)}>`;
  if (node.kind === "union") return (node.variants ?? []).map(formatType).join(" | ");
  return "Любой";
}

export function defaultPlaceholder(typeNode) {
  const node = normalizeTypeNode(typeNode);
  if (node.kind === "list") return "[1, 2, 3]";
  if (node.kind === "dict") return '{"key": "value"}';
  if (node.kind === "union") return (node.variants ?? []).map(formatType).join(" | ");
  if (node.kind === "primitive") {
    switch (node.name) {
      case "Цел": return "42";
      case "Дроб": return "3.14";
      case "Лог": return "true | false";
      case "Любой": return '{"key": "value"}';
      default: return "value";
    }
  }
  return "value";
}
