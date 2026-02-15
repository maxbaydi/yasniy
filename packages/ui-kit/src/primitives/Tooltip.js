import React, { useState, useRef, useEffect } from "react";

export function Tooltip({ children, content, placement = "top" }) {
  const [visible, setVisible] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const tooltipRef = useRef(null);

  const updatePosition = () => {
    if (!triggerRef.current || !tooltipRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const tooltipRect = tooltipRef.current.getBoundingClientRect();
    const gap = 8;
    let top = 0;
    let left = rect.left + rect.width / 2 - tooltipRect.width / 2;

    switch (placement) {
      case "top":
        top = rect.top - tooltipRect.height - gap;
        break;
      case "bottom":
        top = rect.bottom + gap;
        break;
      case "left":
        left = rect.left - tooltipRect.width - gap;
        top = rect.top + rect.height / 2 - tooltipRect.height / 2;
        break;
      case "right":
        left = rect.right + gap;
        top = rect.top + rect.height / 2 - tooltipRect.height / 2;
        break;
      default:
        top = rect.top - tooltipRect.height - gap;
    }

    left = Math.max(8, Math.min(left, window.innerWidth - tooltipRect.width - 8));
    top = Math.max(8, Math.min(top, window.innerHeight - tooltipRect.height - 8));
    setPos({ top, left });
  };

  useEffect(() => {
    if (visible) {
      updatePosition();
      const ro = new ResizeObserver(updatePosition);
      if (tooltipRef.current) ro.observe(tooltipRef.current);
      return () => ro.disconnect();
    }
  }, [visible, content]);

  const trigger = React.createElement(
    "span",
    {
      ref: triggerRef,
      onMouseEnter: () => setVisible(true),
      onMouseLeave: () => setVisible(false),
      onFocus: () => setVisible(true),
      onBlur: () => setVisible(false),
      "aria-describedby": visible ? "yasn-tooltip" : undefined,
      style: { display: "inline-block", cursor: "inherit" },
    },
    children
  );

  return React.createElement(
    React.Fragment,
    null,
    trigger,
    visible &&
      content &&
      React.createElement(
        "div",
        {
          id: "yasn-tooltip",
          ref: tooltipRef,
          className: "yasn-tooltip",
          role: "tooltip",
          style: {
            position: "fixed",
            top: pos.top,
            left: pos.left,
          },
        },
        content
      )
  );
}
