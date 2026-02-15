import React from "react";

export function Toast({ message, variant = "default", className = "" }) {
  const cls = ["yasn-toast"];
  if (variant === "error") cls.push("yasn-toast--error");
  if (className) cls.push(className);

  return React.createElement(
    "div",
    { className: cls.join(" ").trim(), role: "status" },
    message
  );
}

export function ToastContainer({ toasts = [], onDismiss }) {
  if (toasts.length === 0) return null;

  return React.createElement(
    "div",
    { className: "yasn-toast-container" },
    toasts.map((t) =>
      React.createElement(Toast, {
        key: t.id,
        message: t.message,
        variant: t.variant ?? "default",
      })
    )
  );
}
