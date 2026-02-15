import React from "react";
import { Card } from "../primitives/index.js";

export function ErrorPanel({ error, title = "Error" }) {
  if (!error) return null;

  const message = error instanceof Error ? error.message : String(error);

  return React.createElement(
    Card,
    { title },
    React.createElement("pre", { className: "yasn-error yasn-pre" }, message)
  );
}
