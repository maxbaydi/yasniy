import React from "react";

export function AppShell({
  sidebar,
  sidebarPosition = "left",
  toolbar,
  children,
  className = "",
}) {
  const cls = ["yasn-app-shell"];
  if (sidebar) {
    cls.push(sidebarPosition === "right" ? "yasn-app-shell--sidebar-right" : "yasn-app-shell--sidebar-left");
  }
  if (className) cls.push(className);

  const content = React.createElement(
    "main",
    { style: { display: "flex", flexDirection: "column", minHeight: 0 } },
    toolbar,
    React.createElement("div", {
      style: { flex: 1, overflow: "auto", padding: "var(--yasn-space-4)" },
    }, children)
  );

  return React.createElement(
    "div",
    { className: cls.join(" ").trim(), style: { minHeight: "100vh" } },
    sidebar && sidebarPosition === "left" ? sidebar : null,
    content,
    sidebar && sidebarPosition === "right" ? sidebar : null
  );
}
