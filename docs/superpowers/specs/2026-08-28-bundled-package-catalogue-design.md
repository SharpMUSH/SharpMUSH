# Bundled package catalogue: shipped is not installed

**Date:** 2026-08-28
**Status:** implemented (PR #841)

## Problem

`BundledPackages.All` does two unrelated jobs at once. It is the list of package manifests
embedded in the server assembly, and it is the list of packages
`DefaultPackagesBootstrapService` installs into every game at first boot. There is no way
to be one without the other: shipping a package with the server means forcing it on every
game that starts.

`wiki-reader` (#829) is the case that surfaced this. It shipped as
`examples/packages/wiki-reader/package.yaml` and was never added to `BundledPackages.All`,
so a game that boots from a clean database installs five packages and not it:

```text
[01:09:23 INF] Installed bundled http-handler v1.0.0 (revision 1).
[01:09:23 INF] Installed bundled profile-handler v1.2.0 (revision 1).
[01:09:23 INF] Installed bundled room-contents v1.0.0 (revision 1).
[01:09:24 INF] Installed bundled common-functions v1.1.0 (revision 1).
[01:09:27 INF] Installed bundled scene v1.6.0 (revision 1).
```

Adding it to that list would have installed `+wiki` and a master-room object into every
game whether the admin wanted them or not. The requirement is the opposite: a game should
be able to *have* an application available without having it enabled or installed.

The second half of the problem is delivery. Today the only way for an admin to install
anything is `browse → plan → apply` against a configured git remote
(`IPackageSourceService`), which needs a remote configured and network at install time. A
package that ships inside the image should be installable from the image.

## Design

### 1. The catalogue and the default-install set are different lists

`BundledPackages.Descriptor` gains `InstallAtFirstBoot`. `All` becomes the *shipped
catalogue*; the flag selects the subset installed at first boot.

| Package | Requires | InstallAtFirstBoot |
|---|---|---|
| http-handler | Http | true |
| profile-handler | Http | true |
| room-contents | Event | true |
| common-functions | None | true |
| scene | None | true |
| **wiki-reader** | **None** | **false** |

`SharpMUSH.Server.csproj` embeds `examples/packages/wiki-reader/package.yaml` as
`SharpMUSH.Server.BundledPackages.wiki-reader.package.yaml`, matching the five existing
`EmbeddedResource` entries. No change to how manifests are loaded.

### 2. Bootstrap installs the flagged set, and maintains whatever is installed

`DefaultPackagesBootstrapService` splits its single loop in two:

- **Install** only entries with `InstallAtFirstBoot`. Unchanged behaviour for the five.
- **Upgrade** any catalogue entry that is *already installed*, flagged or not, when this
  build ships a strictly newer version. An admin who opts into `wiki-reader` gets the same
  three-way merge, keep-mine conflict resolution and version gate the defaults get.

The rule in one sentence: the flag decides whether a package arrives, never whether it is
maintained once it is here.

### 3. `bundled` is a reserved remote, not a second install path

The install flow is not duplicated. The name `bundled` is reserved and resolved from
embedded resources instead of git, so `plan`, `apply`, review, revisions and rollback work
unchanged.

In `PackagesController`:

- `GetRemotes` prepends a synthetic `PackageRemoteRecord("bundled", "bundled:sharpmush",
  Official, null)`. It is never written to `sys_remotes`.
- `UpsertRemote` and `DeleteRemote` reject the reserved name.
- `Browse("bundled")` builds a `PackageRepoSnapshot` from the parsed embedded manifests —
  no clone, no network, works on a game with zero remotes configured.
- `FetchManifestAsync` short-circuits the reserved name to
  `BundledPackages.ManifestYaml(path)` with commit `"bundled"`. `Plan` and `Apply` need no
  changes of their own.
- `Apply` records `PackageApplySource("bundled:sharpmush", id, "bundled", null)` — the
  identity `DefaultPackagesBootstrapService` already writes. A package installed by hand
  and one installed at first boot are the same kind of registry row, so update checks,
  revisions and uninstall do not have to care which route it took.
- `CheckForUpdate` on an installed package whose `SourceRepo` is `bundled:sharpmush`
  compares the installed version against the embedded manifest instead of resolving a git
  remote. This is a live bug today, not a new case: no configured remote has that URL, so
  the handler synthesizes `PackageRemoteRecord("bundled:sharpmush", ..., Unknown, ...)` and
  hands it to `IPackageSourceService.CheckForUpdateAsync`, which tries to clone
  `bundled:sharpmush` as a git URL and fails with 502. Every one of the five defaults
  installed at first boot hits this.

### 4. Portal

No new page. The bundled entry appears in the remotes list — already badged `Official` by the
existing `TrustBadge` — with its delete action suppressed, because there is no row to delete.
`AdminPackageBrowse` and `AdminPackageReview` handle it like any other remote.

No new localizable string: a "shipped with this server" badge would mean 16 `.resx` files for one
label, and the synthesized README explains the catalogue when an admin browses it. If the label
turns out to be wanted it is a separate, self-contained change.

The README endpoint has no file to read for an embedded package, so it renders one from the
manifest: name, version, description, whether the package installs at first boot, its dependencies,
and the objects it creates. The catalogue root renders an index of the six.

### 5. Dependencies

`wiki-reader` depends on `common-functions >= 1.1`, which is bundled at 1.1.0 and installed
at first boot, so the dependency resolves against the live game without network. The
manifest's `depends.source` git coordinates stay as they are: they are the fallback for a
game that does not have it, not the primary resolution path.

## Decisions

- **Reserved remote over a dedicated catalogue service.** A separate `IPackageCatalogue`
  with its own `/api/packages/bundled/plan|apply` endpoints keeps git and embedded sources
  cleanly apart, but clones the entire review flow and gives the portal a second install
  screen to maintain. Six manifests do not justify it.
- **Catalogue stays hand-maintained.** Generating the embed list from
  `examples/packages/index.yaml` would ship every teaching example (hello-world, bbs-lite,
  starter-area) in the image. The catalogue is six entries chosen deliberately.
- **Bundled installs are indistinguishable from bootstrap installs.** Reusing
  `bundled:sharpmush` as the source repo rather than minting a new identity means no
  migration and no second code path in update/uninstall.

## Testing

- Fresh boot installs exactly the five flagged packages; `wiki-reader` is not installed.
- Every catalogue entry's embedded manifest loads, parses, and passes strict validation.
- An installed `wiki-reader` is upgraded when the build ships a newer version; an
  uninstalled one is never installed by bootstrap.
- `Browse("bundled")` on a game with no remotes configured lists all six.
- `plan` + `apply` against `bundled` installs `wiki-reader` with no network, and writes the
  same registry identity bootstrap writes.
- The reserved name cannot be created, renamed onto, or deleted.
- `CheckForUpdate` on a bundled-installed package does not attempt a git resolve.
