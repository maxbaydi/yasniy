import React from "react";

export function Select({
  value,
  onChange,
  options = [],
  placeholder,
  disabled = false,
  error = false,
  success = false,
  className = "",
  ...props
}) {
  const cls = ["yasn-select"];
  if (error) cls.push("yasn-select--error");
  if (success) cls.push("yasn-select--success");
  if (className) cls.push(className);

  const opts = Array.isArray(options)
    ? options.map((o) =>
        typeof o === "object" && o !== null
          ? { value: o.value ?? "", label: o.label ?? String(o.value ?? "") }
          : { value: String(o), label: String(o) }
      )
    : [];

  return React.createElement(
    "select",
    {
      value: value ?? "",
      onChange: (e) => onChange?.(e.target.value),
      disabled,
      "aria-invalid": error || undefined,
      className: cls.join(" ").trim(),
      ...props,
    },
    placeholder
      ? React.createElement("option", { value: "" }, placeholder)
      : null,
    opts.map((o) =>
      React.createElement("option", { key: o.value, value: o.value }, o.label)
    )
  );
}
