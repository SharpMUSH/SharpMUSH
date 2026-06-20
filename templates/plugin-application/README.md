# PACKAGE_ID — a SharpMUSH Dynamic Application

A starter **Dynamic Application** (Area 21) for [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
An application is two packages working together:

- **`routes/`** (`kind: softcode`) — the HTTP-handler attributes that serve the
  form **schema** (`GET`) and handle the **submit** (`POST`). These attach as
  backtick children of the bundled `http-handler` verb routers.
- **`app/`** (`kind: application`) — the portal **registration** that turns those
  routes into a live page at `/apps/PACKAGE_ID`. It owns no objects; it `depends:`
  on `routes/` and carries only the `application:` block.

This mirrors the engine's `chargen` / `chargen-app` example pair. See the
[extensibility overview](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/design/extensibility-overview.md)
and the [package manifest reference](https://github.com/SharpMUSH/SharpMUSH/blob/main/examples/packages/README.md).

## What's here

```
PACKAGE_ID/
├── index.yaml                   # repo index: both packages, for fast discovery
├── routes/
│   └── package.yaml             # kind: softcode — schema (GET) + submit (POST) routes
├── app/
│   └── package.yaml             # kind: application — portal registration, depends on routes
├── README.md
├── CHANGELOG.md
├── LICENSE
└── .github/workflows/
    └── validate.yml             # CI: validates both package.yaml files
```

## How it installs

Add this repo as a remote (`/admin/packages`) and install `PACKAGE_ID-app`. The
manager:

1. resolves the `PACKAGE_ID-routes` dependency (the plan is **blocked** until the
   routes package is installed — install it first, or let the resolver fetch it),
2. prompts for `access` (the minimum portal role — a `{{?configure}}` ref), and
3. registers the application, so `/apps/PACKAGE_ID` renders the schema and the nav
   gains an entry for the chosen role and up.

Uninstalling `PACKAGE_ID-app` removes the portal registration; the routes stay
until you uninstall them too (and the manager blocks uninstalling routes while the
app is present).

## Fill in the blanks

1. Set the package ids, `authors:`, `description:`, `homepage:`, the `LICENSE`
   copyright line, and the `OWNER` in `source:` repo URLs.
2. Replace the sample `subject`/`body` form fields in `routes/package.yaml` with
   your real schema, and the submit validation with your real rules.
3. The route attribute paths are UPPERCASE backtick segments
   (`GET`PACKAGE_ID_UPPER`SCHEMA`); keep them in sync with `schema_url` /
   `submit_route` in `app/package.yaml`.

## Publishing — the tag convention

Each package is released independently as a git tag `<package-dir>/v<semver>`:

```
routes/v0.1.0
app/v0.1.0
```

Release tags are **immutable** — never move or delete one; republish a fix as a
new version. Bump each `version:` to match its tag before tagging.
