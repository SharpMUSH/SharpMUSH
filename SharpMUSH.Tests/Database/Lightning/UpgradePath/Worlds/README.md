# Kept worlds

Each file is a Lightning world written by a released SharpMUSH: `format-<n>.mdb.gz` is the gzipped `data.mdb` of a
world at world format `n`, holding what `UpgradeWorldContents.WriteAsync` writes. `UpgradePathTests` opens every
file here with the current build, migrates it, and checks that it reads back what was written, before and after a
restart.

- `format-1.mdb.gz`: format 1, which 1.0.0 writes.

A file is never rewritten once its release is out: it stands for worlds already on disk. When a release brings a
new format (a `CoreMigrations` entry with the next number), add that release's world:

```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/UpgradeWorldFixtureWriter/*"
```

The writer refuses to replace a file that exists. Set `SHARPMUSH_UPGRADE_WORLD_REPLACE=true` only to rewrite a
world whose release is not out yet.
