import React from "react";

export function EmptyState({ title, description, action, className = "", ...props }) {
  const cls = ["yasn-empty-state"];
  if (className) cls.push(className);

  return React.createElement(
    "div",
    { className: cls.join(" ").trim(), ...props },
    title
      ? React.createElement("h3", {
          style: {
            margin: 0,
            marginBottom: "var(--yasn-space-2)",
            fontSize: "var(--yasn-fs-md)",
            fontWeight: "var(--yasn-fw-semibold)",
            color: "var(--yasn-text)",
          },
        }, title)
      : null,
    description
      ? React.createElement("p", {
          style: {
            margin: 0,
            marginBottom: action ? "var(--yasn-space-4)" : 0,
            fontSize: "var(--yasn-fs-sm)",
            color: "var(--yasn-text-muted)",
            lineHeight: "var(--yasn-leading-relaxed)",
          },
        }, description)
      : null,
    action ?? null
  );
}
