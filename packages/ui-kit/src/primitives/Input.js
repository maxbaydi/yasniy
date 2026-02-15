import React from "react";

export function Input({
  type = "text",
  value,
  onChange,
  placeholder,
  disabled = false,
  error = false,
  success = false,
  className = "",
  ...props
}) {
  const cls = ["yasn-input"];
  if (error) cls.push("yasn-input--error");
  if (success) cls.push("yasn-input--success");
  if (className) cls.push(className);

  return React.createElement("input", {
    type,
    value: value ?? "",
    onChange: (e) => onChange?.(e.target.value),
    placeholder,
    disabled,
    "aria-invalid": error || undefined,
    className: cls.join(" ").trim(),
    ...props,
  });
}
