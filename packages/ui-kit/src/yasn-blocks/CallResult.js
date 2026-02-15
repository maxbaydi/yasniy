import React from "react";
import { Card } from "../primitives/index.js";

function safeStringify(value) {
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

export function CallResult({ result, error, title = "Result" }) {
  return React.createElement(
    Card,
    { title },
    error
      ? React.createElement("pre", { className: "yasn-error yasn-pre" }, error.message)
      : result !== null && result !== undefined
      ? React.createElement("pre", { className: "yasn-pre" }, safeStringify(result))
      : React.createElement("p", { className: "yasn-status" }, "No result yet.")
  );
}
