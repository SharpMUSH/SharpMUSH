# http-hooks

The default inbound HTTP handler softcode, delivered as a package. This is the
proof-of-concept for **attach mode** (decision 20.3): a package that manages
the softcode *on an existing object it does not own*.

## What it installs

All attributes land on the configured `http_handler` object (referenced as
`{{$http_handler}}`, #4 by default) — the package never creates or destroys
that object, it only manages its attributes:

- **Verb routers** `GET`/`POST`/`PUT`/`DELETE`/`PATCH`/`HEAD` — each maps a
  request path to a backtick sub-attribute (`GET /http/foo/bar` →
  `` GET`FOO`BAR ``), decodes the query string into `%q<form.*>`, and answers
  a clean `404 API NOT FOUND` for unrouted paths.
- **Character directory & profile API** (read-only):
  - `GET /http/characters` → `` GET`CHARACTERS `` — JSON array of visible players
  - `GET /http/profile?objid=#1:123` → `` GET`PROFILE `` — one character's public profile
  - `GET /http/profile/schema` → `` GET`PROFILE`SCHEMA `` — the portal's field schema
  - Helpers `` FN`FIELD ``, `` FN`CHARCAT ``, `` FN`CHARROW ``, `` FN`JARRINS ``,
    `` FN`CHARVIS `` — redefinable per game (categorization, visibility filtering).

## Why a package

These attributes used to be hardcoded in C# and seeded by a startup service.
As a package they gain the full plan/apply lifecycle: an admin can review the
softcode before it lands, **upgrade** it (with three-way merge protecting local
edits — redefine `` FN`CHARCAT `` and your version survives), **roll back**, or
**uninstall** it entirely. The softcode now lives in exactly one place — this
manifest.

## Attach mode

```yaml
objects:
  - ref: handler
    target: "{{$http_handler}}"   # attach to the existing object; create nothing
    attributes:
      GET: |- ...
```

`target:` makes this an attach object: only `attributes` are allowed (no
`type`/`name`/`parent`/`flags`/`locks`). On uninstall the managed attributes
are removed and the handler object is left in place.
