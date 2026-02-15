import React from "react";
import { Badge, EmptyState } from "../primitives/index.js";

export function SchemaExplorer({ schema = [], onSelect, selectedId }) {
  if (schema.length === 0) {
    return React.createElement(
      EmptyState,
      { title: "No functions", description: "No functions in schema." }
    );
  }

  return React.createElement(
    "ul",
    {
      style: {
        listStyle: "none",
        margin: 0,
        padding: 0,
        display: "grid",
        gap: "var(--yasn-space-2)",
      },
    },
    schema.map((fn) => {
      const isSelected = selectedId === fn.name;
      return React.createElement(
        "li",
        { key: fn.name },
        React.createElement(
          "button",
          {
            type: "button",
            onClick: () => onSelect?.(fn.name),
            style: {
              width: "100%",
              textAlign: "left",
              padding: "var(--yasn-space-2) var(--yasn-space-3)",
              borderRadius: "var(--yasn-radius-md)",
              border: `1px solid ${isSelected ? "var(--yasn-accent)" : "var(--yasn-border)"}`,
              background: isSelected ? "color-mix(in srgb, var(--yasn-accent) 12%, transparent)" : "var(--yasn-bg-panel)",
              cursor: "pointer",
              font: "inherit",
              color: "inherit",
            },
          },
          React.createElement(
            "div",
            {
              style: {
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                gap: "var(--yasn-space-2)",
              },
            },
            React.createElement("span", {
              style: { fontWeight: "var(--yasn-fw-semibold)" },
            }, fn.name),
            fn.isAsync ? React.createElement(Badge, { variant: "primary" }, "async") : null
          ),
          React.createElement("div", {
            style: {
              marginTop: "var(--yasn-space-1)",
              fontSize: "var(--yasn-fs-xs)",
              color: "var(--yasn-text-muted)",
              fontFamily: "var(--yasn-font-mono)",
            },
          }, fn.signature ?? `${fn.name}(...) -> ${fn.returnType ?? "Любой"}`)
        )
      );
    })
  );
}
