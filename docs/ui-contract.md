# UI Contract and Runtime

This document defines the stable UI contract for YASN applications.

## 1. API contract

UI clients must call backend functions only through:

- `GET /functions`
- `GET /schema`
- `POST /call`

`/schema` returns function signatures and parameter types so UI can generate forms automatically.

## 2. Response format

All API responses use the envelope:

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

## 3. `/schema` response

```json
{
  "ok": true,
  "data": {
    "functions": [
      {
        "name": "sum",
        "params": [
          { "name": "a", "type": "Цел" },
          { "name": "b", "type": "Цел" }
        ],
        "returnType": "Цел",
        "isAsync": false,
        "signature": "sum(a: Цел, b: Цел) -> Цел"
      }
    ]
  }
}
```

## 4. UI packaging

Use `--ui-dist` to embed static frontend files into `.yapp`:

```powershell
yasn pack app.яс -o app.yapp --ui-dist ui/dist
```

When a `.yapp` contains UI assets, `run-app` starts a web runtime:

- serves static files from the embedded dist;
- routes `/api/*` to the app kernel (`/api/functions`, `/api/schema`, `/api/call`).

```powershell
yasn run-app app.yapp --host 127.0.0.1 --port 8080
```

## 5. JS dependencies

UI dependencies are separate from YASN `[dependencies]` in `yasn.toml`.
Use JS package manager dependencies for frontend:

- `@yasn/ui-sdk` for API client, React hooks, and error handling;
- `@yasn/ui-kit` for ready-to-use UI components and auto-generated function forms.

Example:

```json
{
  "dependencies": {
    "@yasn/ui-sdk": "file:../../packages/ui-sdk",
    "@yasn/ui-kit": "file:../../packages/ui-kit"
  }
}
```
