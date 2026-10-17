# Offline semantic wiki extraction prototype

This tool verifies and extracts the immutable `pf2e-remaster` 1.1.1 package at Git commit `2659a733a4519b76e64a727be2687794afc04f6b`. It has no network, production DI, live wiki writes, production index, or authorization cache.

## Native foundation under construction

The tool also contains B1 key/codec/preparation components and a bounded native shadow aggregate for explicitly disposable fixture worlds. Aggregate mutation, index symmetry, capacity, lifecycle, recovery and full-corpus fixture tests pass. Actual mid-iteration native cursor-error evidence remains an open B1 acceptance gate. Indexed queries and live authorization remain later gates in [the B1 plan](../../docs/superpowers/plans/2026-10-17-semantic-wiki-phase-b1.md). These components have no production registration or installation path. See the [measured results and remaining gates](../../docs/superpowers/reports/2026-10-17-semantic-native-foundation.md).

Record format version is 1, followed by a kind byte: page 1, facts 2, internal schema identity 3. Counts and lengths are signed little-endian int32; stored integer fields are little-endian int64. Strings are strict UTF-8. Page encoding retains every source field/stamp; facts retain each typed value and all provenance fields. Nullable draft author has an explicit presence byte. Invalid tags, Boolean bytes, lengths, UTF-8 and trailing data reject explicitly. Decoding requires a separate caller-owned model-memory reservation.

Native key ordering is separate from record encoding. IDs are positive unsigned 64-bit big-endian. Integer keys encode the signed value after XOR with the sign bit, also big-endian. Scalar tags are PageRef 1, Integer 2, Boolean 3, Keyword 4; every key scalar occupies eight bytes. PageRef/keyword scalars are numeric IDs, Boolean is zero or one. Registry kinds are Page 1, Property 2, Keyword 3, Collection 4, Class 5.

| Key | Layout | Bytes |
| --- | --- | ---: |
| Subject | subject ID | 8 |
| Forward | subject, property, scalar tag, scalar | 25 |
| Posting | collection, class, property, scalar tag, scalar, subject | 41 |
| Registry lookup | registry kind, SHA256 of exact external identity | 33 |
| Registry reverse | registry kind, ID | 9 |

Preparation measures inputs before allocation and detaches schema/page arrays before invoking the supplied fact enumerable. It stops at raw fact 129 even if entries would deduplicate. Its immutable certificate identifies the exact schema; request digest additionally covers the complete canonical page/facts, expected state version and deletion. Pure preparation's size estimate cannot authorize writes: the native aggregate checks the complete transactional delta before its first mutation, including registries, catalog, versions and receipts. Prototype limits remain experimental: 128 facts/subject, 64KiB canonical facts, 128KiB native key-plus-value reads, 8KiB structural descriptors, 16KiB summaries and 256KiB complete write deltas.

The embedded checked lock was independently compared with that Git tree. All 4,714 declared/required files (4,560,161 bytes) must match both exact SHA256 and Git blob SHA1 before any Markdown is interpreted. These are the manifest, all 4,711 declared page files, NOTICE.md and README.md. Extra undeclared files are ignored; the lock does not authenticate a checkout's history, remote, or any live wiki. The manifest's declared files must exactly match that set. Traversal and symlink paths are refused. Reads are bounded to 1 MiB/file, use cancellation, and extraction uses the verified immutable in-memory text.

Run these commands from the SharpMUSH repository root. Install the SDK selected by [global.json](../../global.json), following the repository's [SDK installation instructions](../../AGENTS.md#build--test-commands). A normal SDK installation on `PATH` is sufficient; if yours is installed in a custom directory, optionally prepend that directory to `PATH`. No session-specific SDK directory is required.

Create disk-backed scratch and the shared build-lock parent within this checkout:

```bash
export SEMANTIC_WORK="$PWD/work/semantic-wiki-prototype"
mkdir -p "$SEMANTIC_WORK/tmp" "$SEMANTIC_WORK/source"
export TMPDIR="$SEMANTIC_WORK/tmp"
export TMP="$TMPDIR" TEMP="$TMPDIR"
export SEMANTIC_BUILD_LOCK="$SEMANTIC_WORK/build.lock"
dotnet --version
```

Prepare the source separately from the offline CLI. The package lives in the `pf2e-remaster` subdirectory of [SharpMUSH/SharpMUSH.Packages](https://github.com/SharpMUSH/SharpMUSH.Packages). If you do not already have the pinned source, these acquisition commands require network access and create a separate checkout under the scratch directory:

```bash
git clone --no-checkout https://github.com/SharpMUSH/SharpMUSH.Packages.git "$SEMANTIC_WORK/source/SharpMUSH.Packages"
git -C "$SEMANTIC_WORK/source/SharpMUSH.Packages" checkout --detach 2659a733a4519b76e64a727be2687794afc04f6b
export SEMANTIC_PF2E_SOURCE="$SEMANTIC_WORK/source/SharpMUSH.Packages/pf2e-remaster"
```

For an existing checkout, instead set `SEMANTIC_PF2E_SOURCE` to its absolute `pf2e-remaster` directory. The CLI checks actual file content against the embedded lock; a claimed checkout revision does not suffice.

Build and run without fetching source or changing upstream files:

```bash
flock --close "$SEMANTIC_BUILD_LOCK" dotnet build tools/WikiSemantics -m:2 -nr:false
dotnet run --project tools/WikiSemantics --no-build -- dry-run \
  --source "$SEMANTIC_PF2E_SOURCE" \
  --output "$SEMANTIC_WORK/pf2e-dry-run"
```

The output directory must be new and outside the source directory. Containment uses a conservative case-insensitive directory-boundary comparison on every OS, including Linux with case-insensitive mounted volumes. Consequently, a case-distinct sibling such as `Corpus` versus `corpus/report` is refused even on a case-sensitive filesystem; choose an unambiguous sibling name such as `reports` instead. Existing output-path symlink ancestors are refused before reading the source, with checks repeated before creating/publishing staging. These checks do not guarantee safety against hostile concurrent filesystem replacement. Output is staged beside its destination, then renamed only after success; failed/cancelled staging is removed when filesystem permissions allow. A cleanup I/O or access failure is reported on stderr without replacing the original result or cancellation; inspect and remove any reported staging directory before retrying. Verification, extraction or coverage failure returns nonzero. The CLI never fetches or modifies upstream files.

`facts.jsonl` contains canonical, typed facts and complete provenance (package/version/commit, Git blob, path, authored field and evidence, extraction version, local override false). `pages.jsonl` maps opaque IDs to explicit package/namespace/source keys and titles. `report.json` records exact coverage, unknown ancestry and rejects. Missing supported evidence produces no fact and is counted separately from zero/false. Three attribution texts are retained verbatim in `licenses/`: NOTICE.md, README.md and orc_license_notice.md. Rules material uses the ORC License; the package is not endorsed or approved by Paizo.

Only leading authored feat/feature/item levels, spell/focus/cantrip/ritual ranks, header Traditions, reviewed class references in Class Feat Traits, reviewed ancestry references in Heritage Ancestry, and anchored Source footers are interpreted. Source citations retain their full wording in evidence, separately from the scalar source-book keyword. Category hubs are not gameplay subjects. Versatile ancestry is unknown; prose never supplies inferred mechanics or prerequisites.

IDs are deterministic **initial offline fixture IDs**, independent of titles and slugs as display text. They incorporate the pinned commit and explicit portable key to avoid title matching. This is not a production import/update algorithm: retained identity mappings and explicit migrations must preserve rename identities, and deletion/recreation must allocate a new incarnation. A new source commit must not reallocate existing live identities.

The six reviewed coverage checks are: level 3,396 subjects; rank 659; traditions 408 subjects/894 values; class 904 subjects/977 values including 47 Alchemist feats; ancestry 99 references and 7 unknown versatile heritages; source-book 4,580. A mismatch refuses publication rather than changing expectations.

Default authored sample and tamper/path tests are offline. Full acceptance is an explicit test named `VerifiedFullCorpusAgreesWithAuthoredHubAndDwarfHeritages`, requiring `SEMANTIC_PF2E_SOURCE=/absolute/path/to/pf2e-remaster`. It queries the reference evaluator, compares all 47 Alchemist feat keys and levels with the independently authored category hub, and checks the five Dwarf heritage keys. Sample tests alone do not establish full-corpus acceptance. For reproducibility run the CLI twice into different new directories and compare every output byte.

To run the full-source acceptance checks explicitly after the setup above:

```bash
flock --close "$SEMANTIC_BUILD_LOCK" dotnet build SharpMUSH.Tests -m:2 -nr:false
flock --close "$SEMANTIC_BUILD_LOCK" dotnet run --project SharpMUSH.Tests --no-build -- --treenode-filter '/*/*/SemanticExtractionTests/VerifiedFullCorpusAgreesWithAuthoredHubAndDwarfHeritages'
flock --close "$SEMANTIC_BUILD_LOCK" dotnet run --project SharpMUSH.Tests --no-build -- --treenode-filter '/*/*/SemanticExtractionTests/VerifiedPageDeclarationsCannotChangeAfterValidation'
```

`SEMANTIC_PF2E_SOURCE` is exported by setup and required by both Explicit tests. Missing source is a failure, not a skipped full-corpus acceptance claim.
