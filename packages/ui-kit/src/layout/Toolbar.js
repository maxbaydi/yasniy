import React from "react";

export function Toolbar({ children, className = "" }) {
  const cls = ["yasn-toolbar"];
  if (className) cls.push(className);

  return React.createElement("div", { className: cls.join(" ").trim() }, children);
}
