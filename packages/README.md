# YASN UI Packages

Local UI packages that are intentionally separate from YASN language dependencies (`[dependencies]` in `yasn.toml`):

- `@yasn/ui-sdk` in `packages/ui-sdk`
- `@yasn/ui-kit` in `packages/ui-kit`

Install from local paths in your frontend app:

```json
{
  "dependencies": {
    "@yasn/ui-sdk": "file:../../packages/ui-sdk",
    "@yasn/ui-kit": "file:../../packages/ui-kit"
  }
}
```
