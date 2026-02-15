import React from "react";
import { EmptyState } from "../primitives/index.js";

export function Kanban({ columns = [], renderCard, emptyColumnMessage }) {
  return React.createElement(
    "div",
    { className: "yasn-kanban" },
    columns.map((col) => {
      const id = typeof col === "object" ? col.id ?? col.key : col;
      const title = typeof col === "object" ? col.title ?? col.label ?? id : col;
      const items = typeof col === "object" && Array.isArray(col.items) ? col.items : [];

      return React.createElement(
        "div",
        { key: id, className: "yasn-kanban__column" },
        React.createElement(
          "div",
          { className: "yasn-kanban__column-header" },
          title
        ),
        React.createElement(
          "div",
          { className: "yasn-kanban__column-body" },
          items.length === 0
            ? React.createElement(
                EmptyState,
                {
                  description: emptyColumnMessage ?? "No items",
                  style: { padding: "var(--yasn-space-4)", margin: 0 },
                }
              )
            : items.map((item, idx) => {
                const key = typeof item === "object" && item !== null && "id" in item ? item.id : idx;
                return React.createElement(
                  "div",
                  { key, className: "yasn-kanban__card" },
                  typeof renderCard === "function" ? renderCard(item, id) : item
                );
              })
        )
      );
    })
  );
}
