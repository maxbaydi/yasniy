import React from "react";

const VARIANTS = ["default", "primary", "success", "warning", "danger"];

export function Badge({
  variant = "default",
  children,
  className = "",
  ...props
}) {
  const v = VARIANTS.includes(variant) ? variant : "default";
  const cls = ["yasn-badge", `yasn-badge--${v}`];
  if (className) cls.push(className);

  return React.createElement(
    "span",
    { className: cls.join(" ").trim(), ...props },
    children
  );
}
