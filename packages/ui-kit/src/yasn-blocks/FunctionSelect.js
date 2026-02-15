import React from "react";
import { Select } from "../primitives/index.js";

export function FunctionSelect({ functions = [], value, onChange }) {
  const options = functions.map((fn) => ({
    value: fn.name,
    label: fn.signature ?? fn.name,
  }));

  return React.createElement(
    "label",
    { style: { display: "grid", gap: "var(--yasn-space-1)", minWidth: "260px" } },
    React.createElement("span", {
      style: {
        fontSize: "var(--yasn-fs-sm)",
        fontWeight: "var(--yasn-fw-semibold)",
        color: "var(--yasn-text-secondary)",
      },
    }, "Function"),
    React.createElement(Select, {
      value,
      onChange,
      options: functions.length === 0 ? [{ value: "", label: "No functions" }] : options,
      disabled: functions.length === 0,
    })
  );
}
