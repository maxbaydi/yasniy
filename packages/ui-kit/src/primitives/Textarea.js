import React from "react";

export function Textarea({
  value,
  onChange,
  placeholder,
  disabled = false,
  error = false,
  success = false,
  rows = 4,
  className = "",
  ...props
}) {
  const cls = ["yasn-textarea"];
  if (error) cls.push("yasn-textarea--error");
  if (success) cls.push("yasn-textarea--success");
  if (className) cls.push(className);

  return React.createElement("textarea", {
    value: value ?? "",
    onChange: (e) => onChange?.(e.target.value),
    placeholder,
    disabled,
    rows,
    "aria-invalid": error || undefined,
    className: cls.join(" ").trim(),
    ...props,
  });
}
