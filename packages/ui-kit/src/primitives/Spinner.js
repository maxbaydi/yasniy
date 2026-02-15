import React from "react";

export function Spinner({ size = "md", className = "" }) {
  const sizeMap = { sm: 16, md: 24, lg: 32 };
  const px = sizeMap[size] ?? sizeMap.md;
  const cls = ["yasn-spinner"];
  if (className) cls.push(className);

  return React.createElement("span", {
    className: cls.join(" ").trim(),
    role: "status",
    "aria-label": "Loading",
    style: {
      display: "inline-block",
      width: px,
      height: px,
      border: "2px solid currentColor",
      borderRightColor: "transparent",
      borderRadius: "50%",
      animation: "yasn-spin 0.6s linear infinite",
    },
  });
}
