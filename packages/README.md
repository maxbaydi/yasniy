# YASN UI Packages

Local UI packages that are intentionally separate from YASN language dependencies (`[dependencies]` in `yasn.toml`):

- `@yasn/ui-sdk` in `packages/ui-sdk` — data/contract, client, hooks
- `@yasn/ui-kit` in `packages/ui-kit` — visual blocks and UX

Install from local paths in your frontend app:

```json
{
  "dependencies": {
    "@yasn/ui-sdk": "file:../../packages/ui-sdk",
    "@yasn/ui-kit": "file:../../packages/ui-kit"
  }
}
```

Import theme in your app:

```js
import "@yasn/ui-kit/theme.css";
```

## UI-Kit structure

- **primitives**: Button, Input, Select, Checkbox, Textarea, Card, Badge, Modal, Toast, Tabs, Spinner, Skeleton, EmptyState, Tooltip, Dropdown
- **yasn-blocks**: FunctionForm, FunctionSelect, SchemaExplorer, CallResult, TaskHandleStatus, ErrorPanel
- **layout**: AppShell, Sidebar, Toolbar, Table, Kanban

Subpath exports: `@yasn/ui-kit/primitives`, `@yasn/ui-kit/yasn-blocks`, `@yasn/ui-kit/layout`.

Theme: CSS variables `--yasn-*`, light/dark via `data-yasn-theme="light"|"dark"`.

## SDK Notes (schema v2)

`@yasn/ui-sdk` normalizes schema to include:

- `typeNode` / `returnTypeNode`
- `ui` hints
- `isPublicApi` and `schemaVersion`

`client.call(name, argsOrNamed, options)` supports:

- positional: `client.call("sum", [1, 2])`
- named: `client.call("sum", { a: 1, b: 2 })`
- async handle: `client.call("sum", [1, 2], { awaitResult: false })`
