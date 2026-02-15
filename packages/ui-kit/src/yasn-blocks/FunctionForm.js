import React, { useMemo, useState, useEffect } from "react";
import { normalizeTypeNode } from "@yasn/ui-sdk";
import {
  pickControl,
  defaultInputValue,
  coerceInputValue,
  defaultPlaceholder,
} from "./form-utils.js";
import { Button, Input, Select, Checkbox, Textarea } from "../primitives/index.js";

function isRecord(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function ParamField({ param, value, error, onChange }) {
  const control = pickControl(param);
  const placeholder =
    typeof param.ui?.placeholder === "string" && param.ui.placeholder.length > 0
      ? param.ui.placeholder
      : defaultPlaceholder(param.typeNode ?? param.type);
  const required = param.ui?.required !== false;
  const help = param.ui?.help;

  const controlEl =
    control === "checkbox"
      ? React.createElement(Checkbox, { checked: Boolean(value), onChange })
      : control === "json"
      ? React.createElement(Textarea, {
          value: value ?? "",
          onChange,
          placeholder,
          rows: 4,
          error: Boolean(error),
        })
      : control === "bool-select"
      ? React.createElement(Select, {
          value: value ?? "",
          onChange,
          options: [
            { value: "", label: "null" },
            { value: "true", label: "true" },
            { value: "false", label: "false" },
          ],
          error: Boolean(error),
        })
      : React.createElement(Input, {
          type: control === "number" ? "number" : "text",
          value: value ?? "",
          onChange,
          placeholder,
          error: Boolean(error),
        });

  return React.createElement(
    "label",
    { style: { display: "grid", gap: "var(--yasn-space-1)", minWidth: "260px" } },
    React.createElement("span", {
      className: required ? "yasn-field-required" : "",
      style: {
        fontSize: "var(--yasn-fs-sm)",
        fontWeight: "var(--yasn-fw-semibold)",
        color: "var(--yasn-text-secondary)",
      },
    }, `${param.name} (${param.type})`),
    controlEl,
    help ? React.createElement("span", { className: "yasn-field-help" }, help) : null,
    error ? React.createElement("span", { className: "yasn-error" }, error) : null
  );
}

export function FunctionForm({
  schema,
  submitLabel = "Run",
  loading = false,
  onSubmit,
}) {
  const fields = useMemo(
    () =>
      (schema?.params ?? []).map((param) => {
        const typeNode = normalizeTypeNode(param.typeNode ?? param.type);
        return {
          ...param,
          typeNode,
          ui: isRecord(param.ui) ? param.ui : {},
        };
      }),
    [schema]
  );

  const initialValues = useMemo(
    () =>
      Object.fromEntries(fields.map((param) => [param.name, defaultInputValue(param)])),
    [fields]
  );

  const [values, setValues] = useState(initialValues);
  const [errors, setErrors] = useState({});

  useEffect(() => {
    setValues(initialValues);
    setErrors({});
  }, [initialValues]);

  const handleSubmit = (event) => {
    event.preventDefault();
    const nextErrors = {};
    const namedArgs = {};
    for (const param of fields) {
      try {
        namedArgs[param.name] = coerceInputValue(values[param.name], param);
      } catch (err) {
        nextErrors[param.name] = err.message;
      }
    }
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) return;
    onSubmit(namedArgs);
  };

  return React.createElement(
    "form",
    {
      onSubmit: handleSubmit,
      style: {
        display: "grid",
        gap: "var(--yasn-space-3)",
        padding: "var(--yasn-space-4)",
        borderRadius: "var(--yasn-radius-lg)",
        background: "var(--yasn-bg-panel)",
        border: "1px solid var(--yasn-border)",
      },
    },
    React.createElement("p", {
      style: { margin: 0, color: "var(--yasn-accent-muted)", fontWeight: "var(--yasn-fw-semibold)" },
    }, schema.signature),
    ...fields.map((param) =>
      React.createElement(ParamField, {
        key: param.name,
        param,
        value: values[param.name],
        error: errors[param.name],
        onChange: (nextValue) =>
          setValues((prev) => ({ ...prev, [param.name]: nextValue })),
      })
    ),
    React.createElement(
      Button,
      { type: "submit", variant: "primary", loading },
      submitLabel
    )
  );
}
