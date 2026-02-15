import React from "react";
import { Card, Badge } from "../primitives/index.js";

function statusVariant(handle) {
  if (!handle || typeof handle !== "object") return "default";
  if (handle.faulted) return "danger";
  if (handle.canceled) return "warning";
  if (handle.done) return "success";
  return "primary";
}

export function TaskHandleStatus({ handle, title = "Task" }) {
  if (!handle || typeof handle !== "object") {
    return React.createElement(
      Card,
      { title },
      React.createElement("p", { className: "yasn-status" }, "No task handle.")
    );
  }

  const status = handle.done ? "done" : handle.faulted ? "faulted" : handle.canceled ? "canceled" : "running";
  const result = handle.done && handle.result !== undefined ? handle.result : null;

  return React.createElement(
    Card,
    { title },
    React.createElement(
      "div",
      { style: { display: "grid", gap: "var(--yasn-space-2)" } },
      React.createElement(
        "div",
        { style: { display: "flex", gap: "var(--yasn-space-2)", alignItems: "center" } },
        React.createElement("span", { style: { fontSize: "var(--yasn-fs-sm)", color: "var(--yasn-text-muted)" } }, `task_id: ${handle.task_id ?? "—"}`),
        React.createElement(Badge, { variant: statusVariant(handle) }, status)
      ),
      result !== null
        ? React.createElement(
            "pre",
            { className: "yasn-pre", style: { margin: 0 } },
            typeof result === "string" ? result : JSON.stringify(result, null, 2)
          )
        : null
    )
  );
}
