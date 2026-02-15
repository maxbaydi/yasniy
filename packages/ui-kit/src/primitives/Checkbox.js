import React from "react";

export function Checkbox({
  checked = false,
  onChange,
  disabled = false,
  className = "",
  ...props
}) {
  const cls = ["yasn-checkbox"];
  if (className) cls.push(className);

  return React.createElement("input", {
    type: "checkbox",
    checked: Boolean(checked),
    onChange: (e) => onChange?.(e.target.checked),
    disabled,
    className: cls.join(" ").trim(),
    ...props,
  });
}
