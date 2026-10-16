# Offline Rhost backup export

`RhostExport` converts a stopped, consistent Rhost native backup into the portable
flatfile and optional mail dumps consumed by SharpMUSH's existing Rhost import.
It is an operator-run tool referencing only `SharpMUSH.Library`; it does not open
a SharpMUSH world or run inside the server.

## Supported backups

The supported upstream revision is
`bfd2894c7239d02b8bec15c36939706be371715c`, using the default upstream caching
layout and Linux x64 little-endian ABI: 8-byte pointers, 4-byte `int`, and
8-byte `off_t`.

| Explicit backend | Required files under `--source` |
| --- | --- |
| `legacy-qdbm` | selected structural checkpoint, `<base>.db`, `<base>.dir`, `<base>.pag` |
| `mdbx` | selected structural checkpoint, `<base>.mdbx` |

`--base` is the native backing-store basename, commonly `netrhost.gdbm`; it is
not the structural checkpoint, commonly `netrhost.db`. All selected source files
must be direct children of the source directory. The checkpoint must start with
a native `+V` format-7 header containing `V_ATTRSEXT` (`0x400`). Historical GDBM,
other architectures, and nondefault caching layouts are unsupported. Missing
companions, mixed backend companions, symbolic links/reparse paths, overlapping
source/output paths, and existing output paths are refused.

The operator must stop Rhost or obtain a coordinated offline backup first.
Matching file hashes cannot establish that independently captured files belong
to one consistent checkpoint.

## Build and run

Use the SDK pinned in the repository's `global.json`:

```sh
dotnet build tools/RhostExport

dotnet run --no-build --project tools/RhostExport -- \
  --source /backups/rhost-offline \
  --base netrhost.gdbm \
  --checkpoint netrhost.db \
  --backend legacy-qdbm \
  --exporter /opt/rhost-verified/legacy/dbconvert \
  --profile /opt/rhost-verified/legacy/exporter-profile.json \
  --output /backups/rhost-portable-new
```

For MDBX, select `--backend mdbx` and the matching MDBX exporter/profile. Build
both exporters and profiles with the [fixture recipe](../../SharpMUSH.Tests/Services/TestData/RhostNative/README.md).
A profile uses these JSON fields:

```json
{
  "schema": 1,
  "upstreamRevision": "bfd2894c7239d02b8bec15c36939706be371715c",
  "backend": "legacy-qdbm",
  "rid": "linux-x64",
  "littleEndian": true,
  "pointerSize": 8,
  "intSize": 4,
  "offTSize": 8,
  "exporterSha256": "SHA256_OF_THE_TRUSTED_EXPORTER"
}
```

The SHA256 check binds the executable to the supplied profile. It does not
independently prove the profile's ABI declarations or make the executable safe.
Run only a trusted, reviewed exporter. Private working paths protect the source
from normal exporter writes; this is not an operating-system sandbox for an
arbitrary executable.

## Mail

`dbconvert` does not export native mail stores. Supply text produced by Rhost's
`wmail/unload` with `--mail mail.dump` and, when present, `--folders folders.dump`.
The text is parsed strictly before publication. A native `.mail` or `.folder`
store requires its corresponding portable companion; legacy QDBM stores use
`.mail.dir`/`.mail.pag` and `.folder.dir`/`.folder.pag`. The tool checks both the
selected basename and the basename without `.gdbm`. When no mail text is
supplied, the manifest explicitly records that omission.

## Output and failure behavior

The tool inventories source files with SHA256, creates private directories
under the output parent with mode `0700`, and makes writable copies with mode
`0600`. It verifies the copies and executes only:

```text
dbconvert <private-directory>/worldstore x
```

The raw checkpoint feeds stdin. No shell, check flag, or repair flag is used.
All three streams run concurrently. The defaults are a five-minute process
timeout, 1 GiB total selected source bytes, 100 MiB stdout, and 64 KiB stderr;
these limits are configurable through `RhostExportOptions` for API callers.
Cancellation, stream failure, or a byte-limit breach kills and awaits the child
process tree and drains/awaits its pumps. A nonzero exit, unknown diagnostics,
malformed portable world, or malformed mail prevents publication. Recognized
upstream success diagnostics, including the observed MDBX informational lines,
are accepted.

The tool validates the exported world with the strict Rhost reader, translates
it in memory to collect import diagnostics, and rehashes the originals. It then
writes `world.flat`, supplied `mail.dump`/`folders.dump`, and `manifest.json`
(provenance, inventories, counts, and warnings). One directory rename publishes
the output without overwriting an existing destination. Failed runs clean up
only their own private directories.

Successful export and strict parsing cannot independently prove that upstream
retrieved every external attribute. Backing-store corruption can produce a
syntactically valid but incomplete export. Retain the source backup, compare a
healthy-source semantic inventory where available, and inspect all warnings.
This bridge makes no universal completeness claim; [#1963](https://github.com/SharpMUSH/SharpMUSH/issues/1963)
remains open for the remaining integration and verification work.

The existing SharpMUSH importer writes its configured world directly; importing
these outputs is not a transactional promotion. Rehearse on a disposable world
and retain backups. Live import staging is tracked by [#1969](https://github.com/SharpMUSH/SharpMUSH/issues/1969).
