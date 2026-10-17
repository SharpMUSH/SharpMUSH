# Offline semantic wiki extraction prototype

This tool verifies and extracts the immutable `pf2e-remaster` 1.1.1 package at Git commit `2659a733a4519b76e64a727be2687794afc04f6b`. It has no network, production DI, live wiki writes, index, or authorization cache.

The embedded checked lock was independently compared with that Git tree. All 4,714 declared/required files (4,560,161 bytes) must match both exact SHA256 and Git blob SHA1 before any Markdown is interpreted. These are the manifest, all 4,711 declared page files, NOTICE.md and README.md. Extra undeclared files are ignored; the lock does not authenticate a checkout's history, remote, or any live wiki. The manifest's declared files must exactly match that set. Traversal and symlink paths are refused. Reads are bounded to 1 MiB/file, use cancellation, and extraction uses the verified immutable in-memory text.

Use the repository's pinned SDK and disk-backed temporary directory:

```bash
mkdir -p work/semantic-wiki-prototype/task3
export TMPDIR="$PWD/work/semantic-wiki-prototype/task3"
export TMP="$TMPDIR" TEMP="$TMPDIR"
export PATH="$PWD/work/issue-consolidation/dotnet:$PATH"
flock --close /temp/codex/sharpmush-build.lock dotnet build tools/WikiSemantics -m:2 -nr:false

dotnet run --project tools/WikiSemantics --no-build -- dry-run \
  --source /absolute/path/to/pf2e-remaster \
  --output "$PWD/work/semantic-wiki-prototype/pf2e-dry-run"
```

The output directory must be new and outside the source directory. Existing output-path symlink ancestors are refused before reading the source, with checks repeated before creating/publishing staging. These checks do not guarantee safety against hostile concurrent filesystem replacement. Output is staged beside its destination, then renamed only after success; failed/cancelled staging is removed. Verification, extraction or coverage failure returns nonzero. The CLI never fetches or modifies upstream files.

`facts.jsonl` contains canonical, typed facts and complete provenance (package/version/commit, Git blob, path, authored field and evidence, extraction version, local override false). `pages.jsonl` maps opaque IDs to explicit package/namespace/source keys and titles. `report.json` records exact coverage, unknown ancestry and rejects. Missing supported evidence produces no fact and is counted separately from zero/false. Three attribution texts are retained verbatim in `licenses/`: NOTICE.md, README.md and orc_license_notice.md. Rules material uses the ORC License; the package is not endorsed or approved by Paizo.

Only leading authored feat/feature/item levels, spell/focus/cantrip/ritual ranks, header Traditions, reviewed class references in Class Feat Traits, reviewed ancestry references in Heritage Ancestry, and anchored Source footers are interpreted. Source citations retain their full wording in evidence, separately from the scalar source-book keyword. Category hubs are not gameplay subjects. Versatile ancestry is unknown; prose never supplies inferred mechanics or prerequisites.

IDs are deterministic **initial offline fixture IDs**, independent of titles and slugs as display text. They incorporate the pinned commit and explicit portable key to avoid title matching. This is not a production import/update algorithm: retained identity mappings and explicit migrations must preserve rename identities, and deletion/recreation must allocate a new incarnation. A new source commit must not reallocate existing live identities.

The six reviewed coverage checks are: level 3,396 subjects; rank 659; traditions 408 subjects/894 values; class 904 subjects/977 values including 47 Alchemist feats; ancestry 99 references and 7 unknown versatile heritages; source-book 4,580. A mismatch refuses publication rather than changing expectations.

Default authored sample and tamper/path tests are offline. Full acceptance is an explicit test named `VerifiedFullCorpusAgreesWithAuthoredHubAndDwarfHeritages`, requiring `SEMANTIC_PF2E_SOURCE=/absolute/path/to/pf2e-remaster`. It queries the reference evaluator, compares all 47 Alchemist feat keys and levels with the independently authored category hub, and checks the five Dwarf heritage keys. Sample tests alone do not establish full-corpus acceptance. For reproducibility run the CLI twice into different new directories and compare every output byte.
