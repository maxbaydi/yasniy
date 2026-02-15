import React from "react";
import { EmptyState } from "../primitives/index.js";

export function Table({ columns = [], data = [], keyField = "id", renderRow, emptyMessage }) {
  if (data.length === 0) {
    return React.createElement(
      EmptyState,
      {
        title: "No data",
        description: emptyMessage ?? "No rows to display.",
      }
    );
  }

  return React.createElement(
    "div",
    { className: "yasn-table-wrapper" },
    React.createElement(
      "table",
      { className: "yasn-table" },
      React.createElement(
      "thead",
      null,
      React.createElement(
        "tr",
        null,
        columns.map((col) =>
          React.createElement(
            "th",
            {
              key: typeof col === "string" ? col : col.key ?? col.id,
              style: typeof col === "object" && col.width ? { width: col.width } : undefined,
            },
            typeof col === "string" ? col : col.label ?? col.header ?? col.key ?? col.id
          )
        )
      )
    ),
    React.createElement(
      "tbody",
      null,
      data.map((row, idx) => {
        const key = typeof row === "object" && row !== null && keyField in row
          ? row[keyField]
          : idx;
        if (typeof renderRow === "function") {
          return React.createElement(React.Fragment, { key }, renderRow(row, idx));
        }
        const colKeys = columns.map((c) => (typeof c === "object" ? c.key ?? c.id : c));
        return React.createElement(
          "tr",
          { key },
          colKeys.map((colKey) =>
            React.createElement(
              "td",
              { key: colKey },
              row[colKey] != null ? String(row[colKey]) : "—"
            )
          )
        );
      })
    )
    )
  );
}
