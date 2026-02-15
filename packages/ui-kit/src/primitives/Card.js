import React from "react";

export function Card({ title, children, className = "", ...props }) {
  const cls = ["yasn-card"];
  if (className) cls.push(className);

  return React.createElement(
    "article",
    { className: cls.join(" ").trim(), ...props },
    title
      ? React.createElement("h3", {
          style: {
            margin: 0,
            marginBottom: "var(--yasn-space-2)",
            color: "var(--yasn-text)",
            fontSize: "var(--yasn-fs-md)",
            fontWeight: "var(--yasn-fw-semibold)",
          },
        }, title)
      : null,
    children
  );
}
