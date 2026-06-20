# PACKAGE_ID — a SharpMUSH softcode package

A starter **softcode package** (`kind: softcode`) for [SharpMUSH](https://github.com/SharpMUSH/SharpMUSH).
A softcode package is a declarative YAML description of game objects + attributes
(and, via `@function`, global softcode functions) that the package manager
installs, upgrades, and uninstalls at runtime — no C#, no recompile. This is the
home for **game policy**: permissions, formatting, content.

See the [extensibility overview](https://github.com/SharpMUSH/SharpMUSH/blob/main/docs/design/extensibility-overview.md)
for where this layer sits, and the
[package manifest reference](https://github.com/SharpMUSH/SharpMUSH/blob/main/examples/packages/README.md)
for the full `package.yaml` format.

## What's here

```
PACKAGE_ID/
├── package.yaml                 # the manifest: one object, a $command, a global function, AINSTALL/STARTUP
├── README.md                    # this file
├── CHANGELOG.md
├── LICENSE                      # MIT placeholder — replace the copyright line
└── .github/workflows/
    └── validate.yml             # CI: validates package.yaml on every push/PR
```

## Fill in the blanks

1. Rename the package directory and set `package:` to your slug (lowercase, digits, hyphens, ≤64 chars).
2. Set `authors:`, `description:`, `homepage:`, and the `LICENSE` copyright line.
3. Replace the `starter_object` with your real objects/attributes. Write **every**
   MUSHcode value as a block scalar (`|-`) indented with **spaces**.
4. Use the `AINSTALL`/`STARTUP` convention for any global `@function` registrations
   (register once at install, re-register on every boot — the registry is in-memory).

## Installing it on a game

Add this repo as a remote in the admin panel (`/admin/packages`), then install
`PACKAGE_ID`. The server computes a changeset against the live DB, a wizard
reviews it, and only then is anything written. Upgrades use three-way merge; every
apply is a revision you can roll back.

## Publishing a release — the tag convention

Releases are **git tags** named `<package-dir>/v<semver>` (the Go-modules monorepo
convention), so one repo can hold many independently-versioned packages:

```
PACKAGE_ID/v0.1.0
PACKAGE_ID/v0.2.0
```

The version list a game sees is the tag list; installing a version checks out its
tag; HEAD is the development channel. **Release tags are immutable** — never move
or delete one; republish a fix as a new version. Bump `version:` in `package.yaml`
to match the tag before you cut it.

```bash
git tag PACKAGE_ID/v0.1.0
git push origin PACKAGE_ID/v0.1.0
```
