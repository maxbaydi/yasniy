import React, { useEffect, useMemo, useState } from "react";
import { useYasnCall, useYasnSchema } from "@yasn/ui-sdk/react";

export function YasnPlayground({
  client,
  title = "YASN Playground",
  submitLabel = "Run",
  resetState = false,
}) {
  const { schema, loading: schemaLoading, error: schemaError, refresh } =
    useYasnSchema(client);
  const { call, loading: callLoading, result, error: callError } =
    useYasnCall(client);

  const [selectedFunction, setSelectedFunction] = useState("");
  const currentSchema = useMemo(
    () => schema.find((item) => item.name === selectedFunction) ?? null,
    [schema, selectedFunction]
  );

  useEffect(() => {
    if (!selectedFunction && schema.length > 0) {
      setSelectedFunction(schema[0].name);
    }
  }, [schema, selectedFunction]);

  const handleSubmit = async (args) => {
    if (!selectedFunction) {
      return;
    }

    await call(selectedFunction, args, { resetState });
  };

  return React.createElement(
    "section",
    { style: styles.panel },
    React.createElement("h2", { style: styles.title }, title),
    React.createElement(
      "div",
      { style: styles.toolbar },
      React.createElement(YasnFunctionSelect, {
        functions: schema,
        value: selectedFunction,
        onChange: setSelectedFunction,
      }),
      React.createElement(
        "button",
        {
          type: "button",
          onClick: () => refresh().catch(() => undefined),
          style: styles.secondaryButton,
        },
        "Refresh schema"
      )
    ),
    schemaLoading
      ? React.createElement("p", { style: styles.status }, "Loading schema...")
      : null,
    schemaError
      ? React.createElement("p", { style: styles.error }, schemaError.message)
      : null,
    currentSchema
      ? React.createElement(YasnAutoForm, {
          schema: currentSchema,
          submitLabel,
          loading: callLoading,
          onSubmit: handleSubmit,
        })
      : null,
    React.createElement(YasnResultCard, {
      result,
      error: callError,
    })
  );
}

export function YasnFunctionSelect({ functions, value, onChange }) {
  return React.createElement(
    "label",
    { style: styles.label },
    React.createElement("span", { style: styles.labelText }, "Function"),
    React.createElement(
      "select",
      {
        value,
        onChange: (event) => onChange(event.target.value),
        style: styles.select,
      },
      functions.map((fn) =>
        React.createElement(
          "option",
          { key: fn.name, value: fn.name },
          fn.signature ?? fn.name
        )
      )
    )
  );
}

export function YasnAutoForm({
  schema,
  submitLabel = "Run",
  loading = false,
  onSubmit,
}) {
  const initialValues = useMemo(
    () =>
      Object.fromEntries(
        (schema?.params ?? []).map((param) => [param.name, ""])
      ),
    [schema]
  );
  const [values, setValues] = useState(initialValues);

  useEffect(() => {
    setValues(initialValues);
  }, [initialValues]);

  const handleSubmit = (event) => {
    event.preventDefault();
    const args = (schema?.params ?? []).map((param) =>
      parseByType(values[param.name], param.type)
    );
    onSubmit(args);
  };

  return React.createElement(
    "form",
    { style: styles.form, onSubmit: handleSubmit },
    React.createElement("p", { style: styles.signature }, schema.signature),
    ...(schema?.params ?? []).map((param) =>
      React.createElement(
        "label",
        { key: param.name, style: styles.label },
        React.createElement(
          "span",
          { style: styles.labelText },
          `${param.name} (${param.type})`
        ),
        React.createElement("input", {
          value: values[param.name] ?? "",
          onChange: (event) =>
            setValues((prev) => ({
              ...prev,
              [param.name]: event.target.value,
            })),
          placeholder: placeholderByType(param.type),
          style: styles.input,
        })
      )
    ),
    React.createElement(
      "button",
      {
        type: "submit",
        disabled: loading,
        style: styles.primaryButton,
      },
      loading ? "Running..." : submitLabel
    )
  );
}

export function YasnResultCard({ result, error }) {
  return React.createElement(
    "article",
    { style: styles.resultCard },
    React.createElement("h3", { style: styles.resultTitle }, "Result"),
    error
      ? React.createElement("pre", { style: styles.error }, error.message)
      : null,
    !error && result !== null && result !== undefined
      ? React.createElement("pre", { style: styles.pre }, safeStringify(result))
      : null,
    !error && (result === null || result === undefined)
      ? React.createElement("p", { style: styles.status }, "No result yet.")
      : null
  );
}

function parseByType(raw, type) {
  const text = String(raw ?? "").trim();
  const normalizedType = String(type ?? "").toLowerCase();

  if (normalizedType.includes("цел") || normalizedType.includes("дроб")) {
    if (text.length === 0) {
      return 0;
    }

    const number = Number(text.replace(",", "."));
    return Number.isFinite(number) ? number : 0;
  }

  if (normalizedType.includes("лог")) {
    return text === "true" || text === "истина" || text === "1";
  }

  if (normalizedType.includes("список") || normalizedType.includes("словарь")) {
    if (text.length === 0) {
      return normalizedType.includes("список") ? [] : {};
    }

    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }

  return text;
}

function placeholderByType(type) {
  const normalizedType = String(type ?? "").toLowerCase();
  if (normalizedType.includes("список")) {
    return "[1, 2, 3]";
  }

  if (normalizedType.includes("словарь")) {
    return "{\"key\": \"value\"}";
  }

  if (normalizedType.includes("лог")) {
    return "true | false";
  }

  if (normalizedType.includes("цел") || normalizedType.includes("дроб")) {
    return "42";
  }

  return "value";
}

function safeStringify(value) {
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

const styles = {
  panel: {
    maxWidth: "860px",
    padding: "24px",
    margin: "24px auto",
    borderRadius: "16px",
    background:
      "linear-gradient(145deg, rgba(246,248,250,1) 0%, rgba(236,242,248,1) 100%)",
    border: "1px solid rgba(12, 26, 39, 0.12)",
    boxShadow: "0 12px 36px rgba(0, 24, 48, 0.12)",
    fontFamily:
      "\"IBM Plex Sans\", \"Segoe UI\", \"Helvetica Neue\", Arial, sans-serif",
  },
  title: {
    margin: 0,
    marginBottom: "14px",
    fontSize: "28px",
    color: "#0B253A",
  },
  toolbar: {
    display: "flex",
    gap: "12px",
    flexWrap: "wrap",
    alignItems: "end",
    marginBottom: "16px",
  },
  form: {
    display: "grid",
    gap: "12px",
    padding: "16px",
    borderRadius: "12px",
    background: "rgba(255,255,255,0.88)",
    border: "1px solid rgba(12, 26, 39, 0.08)",
  },
  label: {
    display: "grid",
    gap: "6px",
    minWidth: "260px",
  },
  labelText: {
    fontSize: "13px",
    fontWeight: 600,
    color: "#264257",
  },
  input: {
    border: "1px solid #AFC0D1",
    borderRadius: "10px",
    padding: "10px 12px",
    fontSize: "14px",
  },
  select: {
    border: "1px solid #AFC0D1",
    borderRadius: "10px",
    padding: "10px 12px",
    fontSize: "14px",
    minWidth: "340px",
  },
  primaryButton: {
    border: "none",
    borderRadius: "10px",
    padding: "10px 14px",
    background: "#0B6E4F",
    color: "#fff",
    fontWeight: 700,
    cursor: "pointer",
  },
  secondaryButton: {
    border: "1px solid #AFC0D1",
    borderRadius: "10px",
    padding: "10px 14px",
    background: "#fff",
    color: "#0B253A",
    cursor: "pointer",
  },
  signature: {
    margin: 0,
    color: "#35546A",
    fontWeight: 600,
  },
  resultCard: {
    marginTop: "16px",
    padding: "16px",
    borderRadius: "12px",
    background: "rgba(255,255,255,0.9)",
    border: "1px solid rgba(12, 26, 39, 0.08)",
  },
  resultTitle: {
    marginTop: 0,
    marginBottom: "10px",
    color: "#0B253A",
  },
  pre: {
    margin: 0,
    whiteSpace: "pre-wrap",
    wordBreak: "break-word",
    fontSize: "13px",
  },
  status: {
    margin: 0,
    color: "#406176",
  },
  error: {
    margin: 0,
    color: "#8F1D1D",
    whiteSpace: "pre-wrap",
  },
};
