import React, { useEffect } from "react";
import { Card } from "./Card.js";

export function Modal({
  open = false,
  onClose,
  title,
  children,
  className = "",
}) {
  useEffect(() => {
    if (!open) return;
    const handler = (e) => {
      if (e.key === "Escape") onClose?.();
    };
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [open, onClose]);

  if (!open) return null;

  const cls = ["yasn-modal"];
  if (className) cls.push(className);

  return React.createElement(
    "div",
    {
      className: "yasn-modal-backdrop",
      onClick: (e) => e.target === e.currentTarget && onClose?.(),
    },
    React.createElement(
      "div",
      { className: cls.join(" ").trim(), onClick: (e) => e.stopPropagation() },
      React.createElement(Card, { title }, children)
    )
  );
}
