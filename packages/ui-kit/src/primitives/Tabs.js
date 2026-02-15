import React from "react";

export function Tabs({ tabs = [], activeId, onChange, className = "" }) {
  const cls = ["yasn-tabs"];
  if (className) cls.push(className);

  const items = Array.isArray(tabs)
    ? tabs.map((t) =>
        typeof t === "object" && t !== null
          ? { id: t.id ?? t.label, label: t.label ?? String(t.id ?? "") }
          : { id: String(t), label: String(t) }
      )
    : [];

  return React.createElement(
    "div",
    { className: "yasn-tabs-container" },
    React.createElement(
      "div",
      { className: cls.join(" ").trim() },
      items.map((tab) => {
      const isActive = activeId === tab.id;
      const tabCls = ["yasn-tabs__tab"];
      if (isActive) tabCls.push("yasn-tabs__tab--active");
      return React.createElement(
        "button",
        {
          key: tab.id,
          type: "button",
          className: tabCls.join(" ").trim(),
          onClick: () => onChange?.(tab.id),
        },
        tab.label
      );
    })
    )
  );
}

export function TabsPanel({ activeId, children }) {
  const childArray = React.Children.toArray(children);
  const active = childArray.find((c) => c.props?.tabId === activeId);
  return active ?? null;
}
