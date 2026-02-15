import React from "react";

export function Skeleton({ width, height = "1em", className = "" }) {
  const cls = ["yasn-skeleton"];
  if (className) cls.push(className);

  return React.createElement("span", {
    className: cls.join(" ").trim(),
    "aria-hidden": true,
    style: {
      display: "inline-block",
      width: width ?? "100%",
      height,
      borderRadius: "var(--yasn-radius-sm)",
      background: "linear-gradient(90deg, var(--yasn-bg-soft) 25%, var(--yasn-border) 50%, var(--yasn-bg-soft) 75%)",
      backgroundSize: "200% 100%",
      animation: "yasn-skeleton-pulse 1.5s ease-in-out infinite",
    },
  });
}
