import React from "react";

export function Sidebar({ position = "left", children, className = "" }) {
  const cls = ["yasn-sidebar"];
  if (position === "right") cls.push("yasn-sidebar--right");
  if (className) cls.push(className);

  return React.createElement("aside", { className: cls.join(" ").trim() }, children);
}
