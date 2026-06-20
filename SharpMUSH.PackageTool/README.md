# sharpmush-package

An **offline** validator for SharpMUSH `package.yaml` manifests. It lints
softcode, application, and managed package manifests using the same engine the
SharpMUSH server uses (`PackageManifestService.ParseManifest`) — **no server,
database, or network connection required**. This makes it ideal for plugin /
package-template CI: a template repo can lint its `package.yaml` on every push
without booting a server.

For a `kind: managed` manifest it additionally verifies that every declared
binary file exists in the package directory and that its bytes match the
SHA-256 hash recorded in the manifest.

## Install

Install as a .NET global tool:

```bash
dotnet tool install --global SharpMUSH.PackageTool
```

Or as a local (per-repo) tool:

```bash
dotnet new tool-manifest        # once, if you have no .config/dotnet-tools.json
dotnet tool install SharpMUSH.PackageTool
dotnet tool run sharpmush-package validate .
```

The installed command is `sharpmush-package`.

## Usage

```bash
sharpmush-package validate <path-to-package-dir-or-package.yaml> [more paths…] [--strict]
sharpmush-package --help
```

Each `<path>` is either a **package directory** (one containing a
`package.yaml`) or a **`package.yaml` file** directly. You may pass several.

Examples:

```bash
# Validate a single package in the current directory
sharpmush-package validate .

# Validate several packages at once
sharpmush-package validate ./http-handler ./chargen-app

# Point straight at a manifest file
sharpmush-package validate ./my-package/package.yaml

# Fail the build on warnings too (recommended for release CI)
sharpmush-package validate . --strict
```

Output is one line per issue plus a per-package summary line:

```
OK    http-handler/package.yaml: http-handler 1.0.0 (softcode)
WARN  my-pkg/package.yaml: Warning at keywords: 7 keywords listed; at most 5 are used by the browse UI.
ERROR broken/package.yaml: Error at version: 'banana' is not a valid version.
FAIL  broken/package.yaml: 1 error(s).
PASSED   (or FAILED)
```

### Options

| Option       | Effect                                                          |
|--------------|----------------------------------------------------------------|
| `--strict`   | Treat warnings as failures (exit non-zero on any warning).     |
| `-h`, `--help` | Print help and exit 0.                                       |

## Exit codes

| Code | Meaning                                                              |
|------|---------------------------------------------------------------------|
| `0`  | All inputs valid (warnings allowed, unless `--strict`).             |
| `1`  | One or more inputs failed validation (or a warning under `--strict`). |
| `2`  | Usage error, or an input path was not found.                        |

## What it checks

- The manifest parses and every field is valid for its declared `kind`
  (`softcode`, `application`, `managed`).
- Symbolic `{{refs}}` resolve (internal object refs, `{{$well_known}}`,
  `{{?configure}}`, and `{{dependency/ref}}` against declared `depends`).
- Object/configure/well-known ref collisions, parent cycles, dependency vs.
  conflict overlaps, and the structural rules for each kind.
- For `kind: managed`: each `binaries.files[*]` entry's `file` exists in the
  package directory and its SHA-256 matches the manifest `sha256`.

## Use in GitHub Actions

A reusable composite action lives at
`.github/actions/validate-package` in the SharpMUSH repo. A template repo's
workflow can call it directly:

```yaml
jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: SharpMUSH/SharpMUSH/.github/actions/validate-package@main
        with:
          package-dir: .       # directory containing package.yaml
          # strict: 'true'     # optional: fail on warnings
```

### Action inputs

| Input         | Required | Default | Description                                                |
|---------------|----------|---------|------------------------------------------------------------|
| `package-dir` | no       | `.`     | Directory (or `package.yaml` path) to validate.            |
| `strict`      | no       | `false` | When `true`, passes `--strict` so warnings fail the build. |
| `tool-version`| no       | (latest)| Specific `SharpMUSH.PackageTool` version to install.       |
