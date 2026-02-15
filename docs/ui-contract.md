# UI Contract and Runtime

This document defines the stable UI contract for YASN applications.

## 1. API contract

UI clients must call backend functions only through:

- `GET /functions`
- `GET /schema`
- `POST /call`

In `run-app` mode, the same contract is available under `/api/*`:

- `GET /api/functions`
- `GET /api/schema`
- `POST /api/call`

## 2. Response envelope

All successful responses use:

```json
{
  "ok": true,
  "data": {}
}
```

Error responses use:

```json
{
  "ok": false,
  "error": {
    "code": "error_code",
    "message": "Human-readable message"
  }
}
```

## 3. Public UI API exposure

`/functions` and `/schema` return only **public UI API** functions.

Rules:

- `main` is never exposed;
- internal generated names (`__мод_*`) are never exposed;
- if at least one function is marked with `экспорт`, only exported functions are exposed;
- otherwise all top-level functions (except `main` and internal names) are exposed.

## 4. `/schema` response (schema v2)

`/schema` includes machine-readable type model for typed UI generation.

```json
{
  "ok": true,
  "data": {
    "schemaVersion": 2,
    "functions": [
      {
        "name": "sum",
        "params": [
          {
            "name": "a",
            "type": "Цел",
            "typeNode": {
              "kind": "primitive",
              "name": "Цел",
              "display": "Цел",
              "nullable": false
            },
            "ui": {
              "control": "number",
              "placeholder": "42",
              "nullable": false,
              "required": true
            }
          }
        ],
        "returnType": "Цел",
        "returnTypeNode": {
          "kind": "primitive",
          "name": "Цел",
          "display": "Цел",
          "nullable": false
        },
        "isAsync": false,
        "isPublicApi": true,
        "signature": "sum(a: Цел) -> Цел",
        "schemaVersion": 2,
        "ui": {
          "exposure": "public"
        }
      }
    ]
  }
}
```

Backward compatibility:

- legacy `type` and `returnType` string fields are preserved;
- clients that ignore `typeNode` and `ui` keep working.

## 5. `/call` request

`POST /call` accepts **either** positional arguments **or** named arguments.

Positional form:

```json
{
  "function": "sum",
  "args": [2, 3],
  "reset_state": false,
  "await_result": true
}
```

Named form:

```json
{
  "function": "sum",
  "named_args": {
    "a": 2,
    "b": 3
  },
  "reset_state": false,
  "await_result": true
}
```

If `await_result = false`, API returns async task handle (`task_id`, `done`, `canceled`, `faulted`) instead of final function value.

Validation rules:

- `function` must be a non-empty string;
- only one of `args` or `named_args` is allowed;
- `await_result` and `reset_state` (if provided) must be boolean;
- argument count and runtime value types are validated against schema before VM call.

Common error codes:

- `invalid_request` (`400`)
- `invalid_arguments` (`400`)
- `unknown_function` (`404`)
- `method_not_allowed` (`405`)
- `runtime_error` (`500`)

## 6. Dev mode: backend + UI

To run backend and UI together during development, add to `yasn.toml`:

```toml
[run.dev]
frontend = "npx serve dist -l 5173"
frontend_cwd = "ui"
```

Then run `yasn run dev`. Backend and frontend start together. Build UI first: `cd ui && npm run build`.

UI must call backend API (CORS enabled). Use full URL in dev when UI is on a different port, e.g. `http://127.0.0.1:8090` if backend runs on 8090.

## 7. UI packaging

Use `--ui-dist` to embed static frontend files into `.yapp`:

```powershell
yasn pack app.яс -o app.yapp --ui-dist ui/dist
```

When a `.yapp` contains UI assets, `run-app` starts web runtime:

- serves static files from embedded dist;
- serves API under `/api/*`.

```powershell
yasn run-app app.yapp --host 127.0.0.1 --port 8080
```

## 8. JS dependencies

UI dependencies are separate from YASN `[dependencies]` in `yasn.toml`.

```json
{
  "dependencies": {
    "@yasn/ui-sdk": "file:../../packages/ui-sdk",
    "@yasn/ui-kit": "file:../../packages/ui-kit"
  }
}
```

Import theme in your app for styling:

```js
import "@yasn/ui-kit/theme.css";
```

Subpath exports: `@yasn/ui-kit/primitives`, `@yasn/ui-kit/yasn-blocks`, `@yasn/ui-kit/layout`.
