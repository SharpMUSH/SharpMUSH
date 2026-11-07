# Kept worlds

Each file is a Lightning world: `format-<n>.mdb.gz` is the gzipped `data.mdb` of a world at world format `n`,
holding what `UpgradeWorldContents.WriteAsync` writes. `UpgradePathTests` open every file here with the current
build, migrate it, and check that it reads back what was written, before and after a restart.

- `format-1.mdb.gz`: format 1, the format 1.0.0 writes. It was written by main before 1.0.0 was tagged.
  **When v1.0.0 is tagged, write it again from the tag** (below, with `SHARPMUSH_UPGRADE_WORLD_REPLACE=true`) so
  it holds exactly what 1.0.0 stores, and commit it. A format-1 change that lands before the tag reaches it as a
  migration in the meantime.

Once its release is out, a file is never written again: it stands for worlds already on disk. Every format older
than the current build's must have a world here (`TheFirstReleasedFormatIsKept` fails otherwise), so a release
that brings a new format (a `CoreMigrations` entry with the next number) adds its world, written from that
release's tag:

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/UpgradeWorldFixtureWriter/*"
```

The writer refuses to replace a file that exists unless `SHARPMUSH_UPGRADE_WORLD_REPLACE=true`.
