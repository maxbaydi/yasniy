import React, { useState, useRef, useEffect } from "react";

export function Dropdown({ trigger, children, align = "left" }) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    const handler = (e) => {
      if (containerRef.current && !containerRef.current.contains(e.target)) {
        setOpen(false);
      }
    };
    document.addEventListener("click", handler);
    return () => document.removeEventListener("click", handler);
  }, [open]);

  const triggerEl = React.isValidElement(trigger)
    ? React.cloneElement(trigger, {
        "aria-expanded": open,
        "aria-haspopup": "true",
        onClick: (e) => {
          e.stopPropagation();
          setOpen((v) => !v);
          trigger.props?.onClick?.(e);
        },
      })
    : React.createElement(
        "button",
        {
          type: "button",
          "aria-expanded": open,
          "aria-haspopup": "true",
          onClick: () => setOpen((v) => !v),
        },
        trigger
      );

  return React.createElement(
    "div",
    { ref: containerRef, style: { position: "relative", display: "inline-block" } },
    triggerEl,
    open &&
      React.createElement(
        "div",
        {
          className: "yasn-dropdown",
          role: "menu",
          style: {
            position: "absolute",
            top: "100%",
            left: align === "right" ? "auto" : 0,
            right: align === "right" ? 0 : "auto",
            marginTop: "var(--yasn-space-1)",
          },
        },
        children
      )
  );
}

export function DropdownItem({ children, onClick, ...props }) {
  return React.createElement(
    "button",
    {
      type: "button",
      role: "menuitem",
      className: "yasn-dropdown__item",
      onClick: (e) => {
        e.stopPropagation();
        onClick?.(e);
      },
      ...props,
    },
    children
  );
}

export function DropdownDivider() {
  return React.createElement("div", { className: "yasn-dropdown__divider", role: "separator" });
}
