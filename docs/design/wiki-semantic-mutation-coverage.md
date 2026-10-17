# Semantic wiki mutation coverage (Phase A)

This inventory describes the current Lightning provider. There are no durable semantic epochs,
partition catalogs or transactional semantic indexes. A source body revision cannot certify a
query snapshot: title, categories, publication, requirements and ownership can change without it.
Classification below is conservative: a title affects projected output, and translated titles,
bodies and publication affect locale views. “Semantic mutation” includes authorization and query
membership dependencies, even when an operation does not edit authored facts.

## Explicit store API matrix

Each row names one `IWikiStore` method; `SemanticMutationCoverageTests` reflects the interface to
reject unclassified additions/removals. This is drift detection, not transactional correctness.

| API | Class | Current boundary/stamp | Future atomic obligation |
| --- | --- | --- | --- |
| GetPageBySlugAsync | Read | slug index + page | bounded identity lookup |
| GetPageByIdAsync | Read | page row | bounded identity lookup |
| GetRecentPagesAsync | Read | visibility before limit | authorized bounded reads |
| GetPagesAsync | Read | visibility before paging | authorized bounded reads |
| CountPagesAsync | Read | filtered count | explicit authorized count |
| CountPagesByStateAsync | Read | filtered count | explicit authorized count |
| CountPagesByCategoryAsync | Read | filtered categories | explicit authorized facets |
| GetPagesByCategoryAsync | Read | filtered ordered page rows | authorized bounded reads |
| GetRequirementsAsync | Read | requirement rows | coherent ACL snapshot |
| GetPinnedCategoriesAsync | Read | pin rows | presentation snapshot only |
| GetRevisionsAsync | Read | locale stream | history only |
| GetRevisionsBeforeAsync | Read | bounded locale cursor | history only |
| GetRevisionAsync | Read | locale revision | history only |
| GetTranslationSummariesAsync | Read | translation rows | coherent locale projection |
| GetTranslationsAsync | Read | translation bodies | offline extraction only |
| GetTranslationAsync | Read | locale row | coherent locale projection |
| CreatePageAsync | Semantic mutation | one write: ID allocation, slug uniqueness, page and source revision 1 | identity, facts, membership, projection and partition epoch |
| UpdatePageBodyAsync | Semantic mutation | one write: page + next source revision | facts/projection and partition epoch |
| DeletePageAsync | Semantic mutation | one write: page, slug, revisions, translations, page requirements removed | remove facts/membership; target liveness/version checks invalidate dependencies, without rewriting incoming edges |
| SetRequirementsAsync | Semantic mutation | one requirement write; page target existence checked; no source revision | authorization epoch for page/category/namespace dependents |
| SetPageTitleAsync | Semantic mutation | page title, slug retained; no source revision | projection epoch; never infer portable identity from title |
| SetPageMetadataAsync | Semantic mutation | categories/publication; no source revision | membership/projection and authorization epochs |
| SetCategoryPinnedAsync | Presentation-only | pin write; no source revision | no fact index write unless future query contract adds pins |
| WriteTranslationAsync | Semantic mutation | one write: locale body + independent revision with CAS | locale summary/projection/visibility stamp; canonical source facts unchanged |
| DeleteTranslationAsync | Semantic mutation | one write: locale row and history removed | locale fallback/projection/visibility stamp; canonical source facts unchanged |

A write is a Lightning `Store.WriteAsync` transaction, not a transaction spanning service calls.
Read methods do not advance stamps. Future obligations describe design requirements, not existing
implementation. Publication and read requirements both matter; a future epoch must also cover
role/permission changes outside these interfaces and reauthorize every relation target.

## Ownership outside IWikiStore

`IPackageRegistryService.GetPackageWikiPageAsync`, `GetPackageWikiPagesAsync` and
`GetAllPackageWikiPagesAsync` are reads. `UpsertPackageWikiPageAsync`,
`RemovePackageWikiPageAsync` and the ownership cascade in `RemoveInstalledPackageAsync` are
semantic mutations. Each registry write is separate from page writes. Ownership changes the
package collection membership used by `WikiCollectionsController` without advancing the page
body revision. Future membership rows and affected collection epochs must change in that same
ownership transaction. Registry baselines are not freshness stamps.

The test explicitly inventories ownership methods as well as the package-removal cascade.
The ownership-name filter is intentionally limited: future general registry APIs with wiki side
effects still require architectural review. Reflection cannot discover new hidden side effects.

## Call paths and failure boundaries

| Caller | Current path and boundary | Future semantic requirement / existing evidence |
| --- | --- | --- |
| Portal create/edit/delete | Wiki controllers → WikiStoreService → IWikiStore; normalization/rendering before storage | atomic page/fact writes; WikiHttpControllerTests, WikiAdminApiTests, WikiControllerProtectionTests |
| In-game writes | wiki commands/functions → IWikiService | same seam, never a portal-only invalidation; WikiCommandTests, WikiFunctionUnitTests |
| Startup pages | StarterWikiService → IWikiService | same create/update seam; StarterWikiServiceTests, WikiStartupSeedingTests |
| Source body restore | restore reads old WikiRevision and invokes normal body update | append new revision, never rewind identity/epoch; WikiRollbackAndRedlinkApiTests |
| Requirements | WikiRequirementsController / service → SetRequirementsAsync | page/category/namespace ACL invalidation, including relation targets; WikiRequirementsStoreTests, WikiAccessServiceTests |
| Package create/update | PackageInstallService.Apply → PackageWikiService.ApplyAsync | body/title/metadata/requirements/pins/ownership are separate writes, so a semantic transaction seam is required; PackageWikiInstallTests |
| Package adoption | planner resolves existing page, Apply writes baseline ownership | membership changes even if every page field stays; PackageWikiPlannerTests, PackageWikiInstallTests |
| Package removal/uninstall | empty desired wiki list; release ownership; unchanged pages queued for deletion after successful apply; changed pages retained as local | membership before/deletion after package commit requires explicit recovery design; PackageWikiInstallTests |
| Failed import/apply undo | PackageWriteTransaction tracks inverses; created page deleted; changed body restored with another update; title/metadata restore only if still equal to applied content; requirement restore is an inverse write; pin reversed only when changed | compensation is real, not one LMDB transaction; each inverse needs correct semantic writes; source-inspected PackageWikiService, no dedicated native wiki-compensation regression identified |
| Package revision rollback | PackageInstallService.Lifecycle.RestoreRevisionAsync restores objects/attributes; explicitly leaves wiki pages as they are | no invented wiki restore semantics; future changes need separate contract/tests |

Package compensation can leave additional source history because body restoration appends a
revision. Later edits are preserved by field checks for body/title/metadata; do not generalize
those checks to requirements. The package gate serializes package operations, not all wiki writers.

Namespace moves, source-locale changes and independent collection lifecycle mutation have no
supported IWikiStore API today. Future APIs must be classified and receive lifecycle tests before
indexes rely on them. Category/namespace requirement writes are supported; this does not imply a
namespace rename API. Delete/recreate allocates another opaque local ID and must never retarget
old PageRefs or inherit stale package membership by matching title/slug alone.

## Current evidence and deliberate gaps

Reuse native `Database/Lightning/WikiTests`: `SetTitleKeepsTheSlugAndWritesNoRevision`,
`DraftsAreReturnedToEveryReaderAndOnlyTheCountFiltersThem`,
`UpdateAppendsToTheSourceStreamInRevisionOrder`,
`TranslationRoundTripsAndAStaleExpectedRevisionIsRefused`, and
`DeleteRemovesThePageItsSlugEntryItsRevisionsAndItsTranslations`.
These pin title/metadata gaps, body advancement, independent locale revision/CAS and cascade.
`SemanticMutationCoverageTests` adds native restart-persistent requirement and ownership probes,
explicit delete/recreate identity, and interface drift checks. `WikiRequirementsStoreTests`
already covers category/page requirement persistence and delete cascade.
`WikiCollectionsControllerTests.RecreatedPageDoesNotInheritOldOwnershipAndCollectionSearchIsScoped`
covers stale ownership filtering at the collection consumer.

Dedicated native failed-apply compensation and package revision rollback probes were not identified
in the reused wiki suite; the matrix describes those paths from source inspection.

These tests establish present boundaries. They prove no semantic-index transaction, concurrent
revocation safety, durable epoch recovery, bounded query performance or package-wide atomicity.

## Approved authoring direction

Packages own qualified property schemas. The property namespace `pf2e` is registry-owned by the
`pf2e-remaster` package; it is independent of the package ID. Explicit manifest declarations, and later a staff editor,
create validated fact sets. Registered types are PageRef, signed integer, Boolean and bounded
normalized keyword; class/tradition are multivalued, level/rank single-valued. Validate schema,
cardinality and quotas before canonical facts are accepted. Missing is distinct from zero/false.
Extraction is an offline migration tool, never implicit runtime inference. Local overrides use
existing three-way package planning. Schema evolution requires versioned migration.

The following is a **proposed non-executable manifest sketch**. The existing manifest parser does
not support these keys; this is not documentation of a usable package format:

```yaml
semantic_schema:
  version: 1
  properties:
    pf2e:level: { type: integer, cardinality: one }
    pf2e:class: { type: PageRef, cardinality: many }
semantic_facts:
  - subject: { namespace: main, source_key: feats/example }
    values:
      pf2e:level: 2
      pf2e:class:
        - { package: pf2e-remaster, namespace: main, source_key: classes/fighter }
```

Portable identity is package + namespace + explicit source key, independent of display title;
opaque local IDs and explicit alias migrations preserve references. This sketch does not settle
manifest syntax or license any production schema/index implementation.
