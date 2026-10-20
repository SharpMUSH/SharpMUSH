# Wiki category index upgrade

The category value upgrade runs during server startup, before plugin migrations.
It rebuilds the derived category index from wiki page records while preserving
category keys, page IDs, source pages, and recent and namespace indexes.
Category values gain an eight-byte trailer ending in `FF 43 00 01`.
Allow a maintenance window. Startup awaits completion; category reads and ordinary
wiki writes refuse an incomplete migration. There is no duration estimate.
Additional pages and memberships increase the number of bounded transactions.

## Preserve a recovery point

Use exclusive ownership of the world during the upgrade. Prevent another server,
an old binary, or a raw core-table writer from opening or modifying the directory.
The migration lock serializes callers in one process; it does not coordinate
separate processes. Do not use an older binary even for read-only inspection.

Before the first upgrade start, preserve a completed pre-upgrade world copy and
record the old image digest, configuration, effective paths, and enabled overlays.
Use the existing server's wizard `@backup`, wait for completion, and inspect
`@backup/list` or `@storage/backups`. `@storage` reports backup/free-space needs.
Retain the chosen dated copy outside normal retention through verification.

Do not make an external file copy of a live `data.mdb`. The provider's LMDB
`mdb_env_copy` creates a consistent online world snapshot; Restic backs up completed
copies rather than the live world. `.incoming-*` directories are incomplete.
A completed copy is not proof that recovery has been rehearsed.

Preserve matching wiki assets, the Data Protection key ring, configuration and the
whole installed-plugin directory, including `plugins.state.json` and `.uploads/`.
A world copy alone does not include those files. Coordinate them with the recovery
point and retain required external secrets and the Restic password separately.
Record the exact snapshot ID and world-copy timestamp; a later “latest” may
already contain the upgraded world.

Follow [Backups and restore](../../deploy/README.md#backups-restic) and the
[restore drill](operator-handbook.md#restore-drill). Use the deployment's actual
Compose base file and every enabled overlay consistently throughout recovery.

## Migration and restart

The durable phases are Clearing, Building and Complete. Clearing deletes old
category rows without reading their values. Building processes one source page per
transaction; a page with many categories can span several transactions. Omitted,
null and empty category arrays are valid uncategorized pages and still advance.
A null element inside a category array or an unusable source is rejected.

Each transaction commits its category changes and checkpoint together. Complete
and its matching migration marker commit atomically. Category readiness is
published only after completion commits. Subsequent startups validate Complete
without rebuilding the corpus.

Cancellation or a failed batch leaves earlier committed progress intact. Keep the
world unavailable, stop the failed/restarting service, retain its logs, and resolve
the failure before restarting the same world with the same supported release.
Do not delete checkpoints, manufacture markers, edit pages or rewrite category
rows to force progress.

A partial-page continuation checks SHA-256 of its raw stored JSON. Reformatting or
re-serializing that source can cause a conflict even when visible content appears
unchanged. Resume requires the original source bytes and committed checkpoint.

## Migration limits

Configure these on the server process/container. Missing values use defaults.
Present values must be nonempty ASCII decimal digits without units, signs,
separators or whitespace. Invalid values fail startup; they are not clamped.
Settings are captured once per migration invocation; restart to apply a change.

| Environment variable | Default | Inclusive range |
|---|---:|---:|
| `SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_SOURCE_BYTES` | 8388608 (8 MiB) | 1–67108864 (64 MiB) |
| `SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_WORKING_BYTES` | 268435456 (256 MiB) | 1048576–4294967296 (4 GiB) |
| `SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_INDEX_OPS` | 128 | 1–128 |
| `SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_DELTA_BYTES` | 262144 (256 KiB) | 1–262144 |
| `SHARPMUSH_LIGHTNING_WIKI_CAT_MIGRATION_MAX_KEY_BATCH_BYTES` | 131072 (128 KiB) | 1–131072 |

Source bytes limit one raw JSON value; its eight-byte key is accounted separately.
Working bytes govern the managed reservation model, not process RSS or elapsed
time. A source below its byte limit may still exceed the working allowance.

Index delta counts key bytes per delete and key plus new-value bytes per put.
Clearing uses the smaller of key-batch and delta allowances. Selecting a source
also needs an eight-byte key allowance. Checkpoint/marker records and native read
probes have separate fixed bounds; there is no checkpoint-size setting.

## Failure diagnostics

Capacity errors identify required/configured bytes and the relevant setting.
For `source-bytes`, `key-bytes` or `delta-bytes`, review capacity and explicitly
raise that setting within its supported range, then restart. A Clearing key
blocked by stricter delta names `MAX_DELTA_BYTES`. For `working-bytes`, review
available resources and the working setting; increasing the source limit alone
does not increase working capacity.

If `RequiresEnvelopeReview` is reported, or the requirement exceeds a hard ceiling,
stop for implementation review or restore. No supported setting exceeds the table.
Native probe/byte failures, unsupported key/table envelopes and oversized
checkpoints also require investigation rather than unrelated setting changes.

`Wiki category migration checkpoint conflict.` can indicate competing ownership,
changed authority or modified continuation source. Preserve evidence and resolve
the cause; do not retry against guessed progress. Invalid authority, malformed
source, and invalid category values after Complete are storage/compatibility
errors, not reasons for fallback, source skipping or an automatic global rebuild.

A plugin cannot use the reserved migration ID `0004_wiki_category_value_v1`.
Correct that collision before restarting. `Wiki category migration is not ready.`
requires successful migration for the provider's current world; direct access is
not a readiness bypass. Inspect a completed copy with a compatible tool rather
than opening the live native file externally.

## Downgrade and restore

There is no reverse migration or supported in-place downgrade. Older binaries
have no compatibility guarantee for the new trailer or incomplete checkpoints;
they may misread author bytes or serve incomplete categories. The new marker
cannot make an unmodified older binary refuse the world.

To return to the previous release, stop and verify closure of all world owners.
Preserve the failed/upgraded directory separately, then restore the entire verified
pre-upgrade world with the retained old binary/configuration and matching assets,
key ring and installed-plugin state. Do not restore only category rows or Meta
markers. Restoring discards every world change after the chosen recovery point.

Use the recorded pre-upgrade snapshot and a fresh empty scratch restore volume.
Select a completed dated world copy, replace plugin files rather than merging,
and verify each recovery step before continuing. Leave services stopped after
any failure. Keep recovery artifacts until the restore drill verifies world
identity, logins, wiki assets, secrets and plugin behavior.
