import React from "react";

const VARIANTS = ["primary", "secondary", "ghost", "danger"];

export function Button({
  variant = "primary",
  type = "button",
  disabled = false,
  loading = false,
  children,
  className = "",
  ...props
}) {
  const v = VARIANTS.includes(variant) ? variant : "primary";
  const cls = ["yasn-button", `yasn-button--${v}`];
  if (loading) cls.push("yasn-button--loading");
  if (className) cls.push(className);

  return React.createElement(
    "button",
    {
      type,
      disabled: disabled || loading,
      "aria-busy": loading,
      className: cls.join(" ").trim(),
      ...props,
    },
    children
  );
}
