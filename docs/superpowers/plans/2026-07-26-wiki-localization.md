# Wiki Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make wiki page content (and therefore the `Help:` namespace) translatable per locale, with a visible-fallback read that can never 404 for locale reasons.

**Architecture:** Translations are **overlay rows** (`WikiTranslation`) hanging off `WikiPage`; the page keeps identity, metadata and the source-locale body. `IWikiService` gains five mechanical CRUD methods per backend and learns nothing about fallback. All fallback rules live in one pure `IWikiLocaleResolver`; all read-model construction and draft-visibility filtering lives in one `IWikiLocalizationService`. `?lang=` is the only locale mechanism and never changes the canonical slug.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor WASM, MudBlazor 9.x, ArangoDB (Core.Arango) / Memgraph (Neo4j Bolt) / SurrealDB, TUnit (not xUnit), bUnit for components, `OneOf<T1,T2>` discriminated unions, source-generated Mediator.

**Spec:** `docs/superpowers/specs/2026-07-26-wiki-localization-design.md` — approved and settled. Do not revisit its decisions.

> **Revised against the corrected spec (`b235648a`).** That commit fixed five design flaws, four of them real corrections rather than clarifications, and this plan has been updated to match: `SourceLocale` is **materialised once by the migration, never re-derived on read**; `Wiki.DefaultLocale` is a real parameter default with startup validation; `WikiHelpers.NormalizeLocale` validates and canonicalises at every write boundary while the read path stays permissive; `UpsertTranslationAsync` takes `expectedRevisionNumber` and never retries a conflict; and the unique revision constraint is corrected on **all three** backends, which disagree today. If a task below seems to contradict any of those, the spec wins — but say so rather than quietly resolving it, because that would mean this revision missed a spot.

## Global Constraints

- **C# files:** tabs, indent size 2. **Razor files:** spaces, indent size 4. **Line endings:** LF.
  - **Exceptions that already exist:** `SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs` and `SharpMUSH.Tests.Integration/Wiki/*.cs` are currently 4-space. **Formatting is now enforced** (see next bullet), so write new C# with tabs and let the formatter normalise whatever it normalises — do not hand-preserve spaces against the gate.
- **Formatting is enforced and will fail your build.** `VerifyEditorConfigFormatting` (`Directory.Build.targets`) runs `dotnet format whitespace` on every local build of a project whose `.cs` files changed; CI's `format` job runs it over the whole repo. On a **`FORMAT001`** failure, fix it with:
  ```bash
  dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"
  ```
  **Run it until it reports no changes — the formatter needs two passes to converge.** Use `--folder`, never the solution (`dotnet format` cannot load `SharpMUSH.sln` on the .NET 11 SDK).
  - Consequence for this plan: **do not compact anonymous-object initializers.** `csharp_new_line_before_members_in_object_initializers = false` is a semantic option that folder-mode formatting does not honour, so the formatter expands them to one member per line and re-compacting them fails the gate. Every anonymous object and bind-var dictionary in the code below is already written expanded; keep it that way, including where a neighbouring pre-existing line is compact.
  - Do **not** pass `-p:SkipFormatVerification=true` to make a build pass. It exists for CI, whose `format` job already covers the repo.
- `TreatWarningsAsErrors` is `true` in every project this plan touches **except** `SharpMUSH.Tests.BUnit`. **Never** disable it to make a build pass — fix the warning.
- Prefer `var` throughout; no `this.` qualifier.
- Services return `OneOf<T, NotFound>` for lookups and `OneOf<T, Error<string>>` for conflicts. **Never** nullable returns from a service.
- Test framework is **TUnit**: `[Test]`, `await Assert.That(x).IsEqualTo(y)`, optional `.Because("…")`.
- Engine/unit tests: `dotnet run --project SharpMUSH.Tests`. Component tests: `dotnet run --project SharpMUSH.Tests.BUnit`. Filter: `--treenode-filter "/*/*/ClassName/*"`.
- **Baseline that must stay green:** `SharpMUSH.Tests` = 4927 total / 4729 passed / 198 skipped / **0 failed**. `SharpMUSH.Tests.BUnit` = **271 passed**. `dotnet build` = 0 errors.
- **Integration tests DO run locally, under Podman.** An earlier revision of this plan asserted Docker was unavailable and told every integration task to defer verification to CI. That was wrong, and it was the most damaging error in the plan: Phase 2 is precisely where the three backends disagree, so deferring it turns a five-second local check into a confusing one-of-three red in CI. There is no `docker` binary, but Podman 6.x is installed with the rootless socket active and `DOCKER_HOST` already exported as `unix:///run/user/1000/podman/podman.sock`. Testcontainers reads that and works unmodified, starting its own `testcontainers-ryuk-*` reaper. Verified: `dotnet run --project SharpMUSH.Tests.Integration` → **248/248 passed** at `46610bfe`, no configuration. Diagnose container availability with `podman ps` or `echo $DOCKER_HOST`, never `which docker`.
- **Run each backend locally, then let CI confirm all three at once.** `SHARPMUSH_DATABASE_PROVIDER` selects the provider (`arangodb` is the default), so CI's matrix is reproducible here one entry at a time:
  ```bash
  for db in arangodb memgraph surrealdb; do
    SHARPMUSH_DATABASE_PROVIDER=$db dotnet run --project SharpMUSH.Tests.Integration
  done
  ```
  A task that adds an integration test is **not** done until you have run it against every provider it claims to support. CI remains the acceptance gate for all three simultaneously, but it is no longer the *only* evidence, and "asserted by CI" is no longer an acceptable substitute for having run it.
- **CI integration matrix:** `.github/workflows/_dotnet-build-test.yml`, job `test-integration`, `strategy.matrix.database: [arangodb, memgraph, surrealdb]`, command `dotnet run --project SharpMUSH.Tests.Integration --no-build --verbosity normal -- --output Detailed`. This job running green on all three backends is the acceptance gate for Tasks 6–9.
- **Configured default locale is `Wiki.DefaultLocale`, a real parameter default of `"en"` (`WikiOptions.DefaultLocaleFallback`) rather than a `required` member**, validated at startup by `ValidateSharpOptions`. At runtime read it only through `IOptionsMonitor<SharpMUSHOptions>`; the one literal lives on `WikiOptions.DefaultLocaleFallback` and nothing else hardcodes `"en"`.
- **`WikiPage.SourceLocale` is materialised once, never re-derived on read.** The property initializer default is `string.Empty` (an initializer cannot read configuration), but that is a *transient* state: `Migration_AddWikiTranslations` stamps every unstamped row once, and every create path stamps new pages. A page read back from storage always has a non-empty, canonical `SourceLocale`, and **nothing normalises empty → `Wiki.DefaultLocale` on read.** Re-deriving would mean an admin changing `wiki_default_locale` silently reinterprets the authored locale of every pre-existing page. The design's claim is therefore **"no schema migration and no content rewrite"**, *not* "no data migration" — there is one additive-column backfill.
- **The backfill has no rollback path, no language detection and no per-page override.** SharpMUSH is pre-production, so wiping and reseeding the database is acceptable recovery; all three would be speculative complexity for a scenario the project does not have. The migration logs the locale it stamped and the row count, which is enough to notice a wrong default. This is the first thing to revisit if a live game ever adopts SharpMUSH with existing wiki content.
- **Locales are canonicalised and validated at every write boundary, and the read path stays permissive.** `WikiHelpers.NormalizeLocale` returns `OneOf<string, Error<string>>` and gates `UpsertTranslationAsync`, `CreateAsync`'s `sourceLocale`, the migration backfill and options validation. `WikiHelpers.NormalizeLocaleOrEmpty` is the permissive read form: a bad `?lang=` is treated as absent and **never** a 400.
- **`UpsertTranslationAsync` ends with `int? expectedRevisionNumber` — a compare-and-swap.** `null` means create-only (an existing translation is an `Error<string>`). A stale value is a conflict returning `Error<string>` that is **never** retried automatically: retrying re-applies the loser's stale markdown, which is the loss the parameter exists to prevent. The single automatic retry applies only to the insert race on `(PageId, Locale)`, where no content can be lost.
- **All three backends must end up with a unique constraint on `(PageId, Locale, RevisionNumber)`, and they disagree today.** Verified in the source: SurrealDB has `wiki_revision_page_rev ON wiki_revision FIELDS pageId, revisionNumber UNIQUE` (`SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs:97`) and would **reject** the first translation revision; ArangoDB has `Fields = ["PageId", "RevisionNumber"]`, `Persistent`, **no** `Unique` (`SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWiki.cs:101-105`); Memgraph has two *separate* non-unique indexes (`SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs:124-125`) and no constraint at all. So a numbering bug fails loudly on one backend and passes silently on two — in CI, a baffling one-of-three red that reads like flakiness. The requirement is not "fix SurrealDB" but **make all three agree**, and Task 6's cross-backend test must assert the constraint *rejects* a duplicate, not merely that the happy path works.
- **`IWikiService` additions are purely additive.** These five existing call sites must keep compiling with zero edits in Phases 1–3: `SharpMUSH.Server/Controllers/WikiController.cs`, `SharpMUSH.Server/Controllers/SeoController.cs`, `SharpMUSH.Server/Middleware/BotPrerenderMiddleware.cs`, `SharpMUSH.Implementation/Commands/WikiCommands.cs` (+ its `WikiCommand/*.cs` helpers), `SharpMUSH.Implementation/Functions/WikiFunctions.cs`.
- **Draft translations must never leak.** The caller filters the candidate locale set by visibility *before* calling `IWikiLocaleResolver.Resolve`. The resolver stays permission-blind.
- **No seeded translations.** `SeedWikiPagesAsync` gains `SourceLocale` on the three English pages it already seeds and nothing else.
- **`?lang=` is the only locale mechanism.** One canonical slug, no locale path prefix, no locale-suffixed slugs, canonical URL unchanged.
- **New resx keys:** PascalCase, no separators (`.`/`_`/`-`). A key whose value equals its name *and* contains a lowercase→uppercase transition **fails** `SharedResourceLocalizationTests.No_resource_value_is_left_as_its_own_camel_case_key`. Add every new key to **both** `SharpMUSH.Client/Resources/SharedResource.resx` and `SharedResource.fr.resx`, `<data>` at 2 spaces, `<value>` at 4 spaces, always `xml:space="preserve"`.
- Component tests install `EchoLocalizer<SharedResource>` (asserts on keys); tests that care about English copy use `PortalLocalizer.Create()`.

## File Structure

**New files**

| File | Responsibility |
|---|---|
| `SharpMUSH.Configuration/Options/WikiOptions.cs` | `Wiki.DefaultLocale` config record + `DefaultLocaleFallback`, the change's only `"en"` literal |
| `SharpMUSH.Library/Models/Wiki/WikiTranslation.cs` | Stored translation overlay row |
| `SharpMUSH.Library/Models/Wiki/WikiTranslationSummary.cs` | Bodyless translation listing row |
| `SharpMUSH.Library/Models/Wiki/LocalizedWikiPage.cs` | Read model; never stored |
| `SharpMUSH.Library/Services/Interfaces/IWikiLocaleResolver.cs` | `LocaleResolution` + resolver contract |
| `SharpMUSH.Library/Services/WikiLocaleResolver.cs` | The five-step fallback chain. No DB, no HTTP |
| `SharpMUSH.Library/Services/Interfaces/IWikiLocalizationService.cs` | Localized-read contract |
| `SharpMUSH.Library/Services/WikiLocalizationService.cs` | Visibility filtering + the only `LocalizedWikiPage` factory |
| `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWikiTranslations.cs` | Collection, indexes, the `SourceLocale` / `WikiRevision.Locale` backfill, and the unique `(PageId, Locale, RevisionNumber)` constraint |
| `SharpMUSH.Client/Models/WikiTranslationInfo.cs` | Client-side translation summary |
| `SharpMUSH.Client/Models/WikiTranslationSaveError.cs` | Save failure + `NeedsReload`, so a 409 is distinguishable from a 400 |
| `SharpMUSH.Client/Resources/PortalLocales.cs` | Shared portal locale list + display names |
| `SharpMUSH.Tests/Wiki/WikiLocaleResolverTests.cs` | Table-driven chain tests |
| `SharpMUSH.Tests/Wiki/WikiLocalizationServiceTests.cs` | Draft visibility, fallback banner, and that a stamped `SourceLocale` survives a change to `wiki_default_locale` |
| `SharpMUSH.Tests/Wiki/WikiHelpersLocaleTests.cs` | `NormalizeLocale` / `NormalizeLocaleOrEmpty`, casing collapse, agreement with startup validation |
| `SharpMUSH.Tests.Integration/Wiki/WikiTranslationIntegrationTests.cs` | Cross-backend CRUD, plus the **negative** constraint cases and the concurrency case |
| `SharpMUSH.Tests.BUnit/Components/WikiDisplayFallbackTests.cs` | Banner renders iff `IsFallback` |
| `SharpMUSH.Tests.BUnit/Components/WikiEditLocaleTests.cs` | Inherited metadata disabled off-source |

**Modified files** — full list with the reason each is touched appears in the task that touches it. The heaviest are `SharpMUSH.Library/Services/Interfaces/IWikiService.cs` (+5 methods), the four `IWikiService` implementations, `SharpMUSH.Server/Controllers/WikiController.cs`, and `SharpMUSH.Client/Services/WikiService.cs`.

## Phase Map

| Phase | Tasks | Deliverable | Verifiable locally? |
|---|---|---|---|
| 1 — Config + pure core | 1–4 | `Wiki.DefaultLocale`, models, resolver | Yes |
| 2 — Storage | 5–9 | 5 CRUD methods across 4 backends, optimistic concurrency, and a unique `(PageId, Locale, RevisionNumber)` constraint the three stores do **not** currently agree on. **Cross-backend test — negative cases included — written before the three hand-written backends** (spec Risks) | Task 5 yes; 6–9 CI only |
| 3 — Resolution | 10 | `IWikiLocalizationService` | Yes |
| 4 — HTTP | 11–13 | `?lang=`, translation endpoints, localized listings | Yes |
| 5 — Portal | 14–18 | Reading banner, authoring, history, admin coverage | Yes (bUnit) |
| 6 — Edges | 19–22 | hreflang, in-game, seeding, docs | 19–20 yes; 21 CI |

---

# Phase 1 — Configuration and pure core

### Task 1: `WikiOptions` with `Wiki.DefaultLocale`

`SharpMUSHOptions` uses `required` init properties, so adding one breaks compilation at **six** construction sites. All six are listed below — miss one and the build fails.

Three things about `DefaultLocale` specifically:

1. **It is a real parameter default (`= "en"`), not a `required` member.** Every other `SharpMUSHOptions` member is `required`; this one deliberately is not, because resolution's terminal step depends on it always having a usable value. A configuration file that omits `wiki_default_locale` must bind to `en`, not to null or empty. The `SharpMUSHOptions.Wiki` *property* stays `required` — the distinction is between the container and the field.
2. **It is validated at startup, not at first use.** `ValidateSharpOptions` rejects a value `CultureInfo.GetCultureInfo` cannot parse, failing startup with the offending value named. Deferring this to first use would surface a typo as a `CultureNotFoundException` inside a page render, long after the admin who made it has moved on.
3. **`ValidationPattern` gives the admin UI a client-side check.** The startup validation is the authority, because a regex cannot know which tags actually exist.

**Files:**
- Create: `SharpMUSH.Configuration/Options/WikiOptions.cs`
- Modify: `SharpMUSH.Configuration/Options/SharpMUSHOptions.cs:26` (add property after `TextFile`)
- Modify: `SharpMUSH.Configuration/ValidateSharpOptions.cs` (append the locale check to the generated validator's result)
- Modify: `SharpMUSH.Server/Startup.cs:388` (register `ValidateSharpOptions` instead of the generated validator directly, so the new check actually runs at boot)
- Modify: `SharpMUSH.Configuration/ReadPennMUSHConfig.cs:307-311` (add `Wiki` block after the `TextFile` block)
- Modify: `SharpMUSH.Library/Services/OptionsService.cs:297-301` (same block in `Default()`)
- Modify: `SharpMUSH.Server/Controllers/ConfigurationController.cs:292` (add line to `CloneRecordWithProperty`)
- Modify: `SharpMUSH.Tests/Server/TestSharpMushOptions.cs:112-114`
- Modify: `SharpMUSH.Tests.BUnit/Controllers/ApplicationsGateTests.cs:206-208`
- Modify: `SharpMUSH.Tests.BUnit/Controllers/ConfigurationControllerTests.cs:115-117`
- Modify: `SharpMUSH.Client/Components/ConfigNavDrawer.razor:26` (add a `SectionLink` in the `Content` group)
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx`, `SharedResource.fr.resx`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `SharpMUSH.Configuration.Options.WikiOptions(string DefaultLocale = WikiOptions.DefaultLocaleFallback)`
  - `WikiOptions.DefaultLocaleFallback` → `const string` = `"en"`. **The only `"en"` literal in the whole change.** Task 4's resolver and Tasks 7–9's backfills all reference it.
  - `SharpMUSHOptions.Wiki` → `WikiOptions` (required init)
  - `TestSharpMushOptions.Create(bool allowBrowserCode = false, string wikiDefaultLocale = WikiOptions.DefaultLocaleFallback)` — the second parameter is new and every later test task uses it.
  - `ValidateSharpOptions` now fails when `Wiki.DefaultLocale` is not a real culture, and `Startup` registers *it* rather than the generated validator.
  - Config attribute name `"wiki_default_locale"` (must be globally unique across all 200+ `SharpConfig.Name` values; `ConfigMetadata_AttributeToPropertyName_IsReverseMapping` enforces bijectivity).

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests/Configuration/ConfigurationTests.cs` (it already has `ParseConfigurationFile`; copy its config-file-path idiom):

```csharp
	[Test]
	public async Task WikiDefaultLocale_DefaultsToEnglish()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		await Assert.That(options.Wiki.DefaultLocale).IsEqualTo("en");
	}

	[Test]
	public async Task WikiDefaultLocale_IsExposedThroughTheSchemaAccessor()
	{
		var configFile = Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst");
		var options = ReadPennMushConfig.Create(configFile);

		var value = ConfigAccessor.GetValue(options, nameof(WikiOptions.DefaultLocale));

		await Assert.That(value).IsEqualTo("en");
		await Assert.That(ConfigAccessor.GetCategoryForProperty(nameof(WikiOptions.DefaultLocale))).IsEqualTo("Wiki");
	}

	[Test]
	public async Task WikiDefaultLocale_IsARealParameterDefaultNotARequiredMember()
	{
		// Constructing WikiOptions with no argument must compile and must yield the documented default.
		// If someone later makes DefaultLocale `required`, this line stops compiling — which is the point.
		await Assert.That(new WikiOptions().DefaultLocale).IsEqualTo(WikiOptions.DefaultLocaleFallback);
		await Assert.That(WikiOptions.DefaultLocaleFallback).IsEqualTo("en");
	}

	[Test]
	public async Task ValidateSharpOptions_RejectsAnUnparseableWikiDefaultLocale()
	{
		var options = TestSharpMushOptions.Create(wikiDefaultLocale: "not a locale");

		var result = new ValidateSharpOptions().Validate(null, options);

		await Assert.That(result.Failed).IsTrue();
		await Assert.That(result.FailureMessage)
			.Contains("not a locale")
			.Because("a startup failure that does not name the offending value is a scavenger hunt");
	}

	[Test]
	public async Task ValidateSharpOptions_AcceptsARegionalWikiDefaultLocale()
	{
		var result = new ValidateSharpOptions().Validate(null, TestSharpMushOptions.Create(wikiDefaultLocale: "pt-BR"));

		await Assert.That(result.Failed).IsFalse();
	}
```

Add `using SharpMUSH.Configuration;`, `using SharpMUSH.Configuration.Options;`, `using SharpMUSH.Configuration.Generated;` and `using SharpMUSH.Tests.Server;` to that file's using block if absent.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConfigurationTests/*"`
Expected: compile error — `'SharpMUSHOptions' has no member 'Wiki'`.

- [ ] **Step 3: Create the options record**

Create `SharpMUSH.Configuration/Options/WikiOptions.cs` (tabs; model exactly on `TextFileOptions.cs`):

```csharp
namespace SharpMUSH.Configuration.Options;

public record WikiOptions(
	[property: SharpConfig(
		Name = "wiki_default_locale",
		Category = "Wiki",
		Description = "Locale wiki pages fall back to when a reader's locale has no translation",
		Group = "Wiki",
		Order = 1,
		Tooltip = "A BCP-47 language tag, e.g. 'en', 'fr' or 'pt-BR'",
		ValidationPattern = @"^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$")]
	string DefaultLocale = WikiOptions.DefaultLocaleFallback
)
{
	/// <summary>
	/// The locale used when nothing else supplies one: the parameter default above, the resolver's
	/// last resort when a configured value is unusable, and what the wiki-translation migration stamps
	/// on rows that predate <c>WikiPage.SourceLocale</c>.
	/// </summary>
	/// <remarks>
	/// One constant so those three cannot drift. A migration in particular <em>cannot</em> read
	/// <c>Wiki.DefaultLocale</c> at runtime — see Task 7 — so it needs a compile-time value, and this is it.
	/// </remarks>
	public const string DefaultLocaleFallback = "en";
}
```

`ValidationPattern` is a syntactic gate only. It accepts `zz-ZZ`, which is well-formed and not a real
culture; the startup validation in Step 4a is what rejects that.

- [ ] **Step 4: Wire it into `SharpMUSHOptions`**

In `SharpMUSH.Configuration/Options/SharpMUSHOptions.cs`, after line 26 (`public required TextFileOptions TextFile { get; init; }`):

```csharp
	public required WikiOptions Wiki { get; init; }
```

- [ ] **Step 4a: Validate the locale at startup**

`SharpMUSH.Configuration/ValidateSharpOptions.cs` today just delegates to the generated validator. Add
the locale check alongside it, so a typo fails the boot rather than a page render:

```csharp
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;
using System.Globalization;

namespace SharpMUSH.Configuration;

/// <summary>
/// Validates SharpMUSH configuration options by delegating to the code-generated validator, plus the
/// hand-written checks the generator cannot express.
/// </summary>
public class ValidateSharpOptions : IValidateOptions<SharpMUSHOptions>
{
	private readonly ValidateSharpMUSHOptions _generatedValidator = new();

	public ValidateOptionsResult Validate(string? name, SharpMUSHOptions options)
	{
		var generated = _generatedValidator.Validate(name, options);

		var failures = new List<string>();
		if (generated.Failed && generated.Failures is not null) failures.AddRange(generated.Failures);

		// wiki_default_locale is the terminal step of wiki locale resolution, so an unusable value would
		// otherwise surface as a CultureNotFoundException inside a page render. The ValidationPattern on
		// the attribute is a client-side syntax check only; a regex cannot know which tags actually exist.
		if (!IsRealCulture(options.Wiki.DefaultLocale))
		{
			failures.Add(
				$"Wiki.DefaultLocale (wiki_default_locale) is '{options.Wiki.DefaultLocale}', which is not a "
				+ "recognised BCP-47 locale. Use a tag such as 'en', 'fr' or 'pt-BR'.");
		}

		return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
	}

	/// <summary>
	/// The same rule as <c>WikiHelpers.NormalizeLocale</c>, restated here because
	/// <c>SharpMUSH.Contracts</c> (where that helper lives) references <em>this</em> project, so the
	/// dependency cannot run the other way. Task 2 adds a test asserting the two agree.
	/// </summary>
	private static bool IsRealCulture(string? locale)
	{
		if (string.IsNullOrWhiteSpace(locale)) return false;

		try
		{
			return CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true).Name.Length > 0;
		}
		catch (CultureNotFoundException)
		{
			return false;
		}
	}
}
```

`SharpMUSH.Server/Startup.cs:388` registers `Configuration.Generated.ValidateSharpMUSHOptions` directly
as the `IValidateOptions<SharpMUSHOptions>`, bypassing this wrapper. Change it to
`services.AddScoped<IValidateOptions<SharpMUSHOptions>, ValidateSharpOptions>();` or the new check never
runs at boot. `AddOptions<SharpMUSHOptions>().ValidateOnStart()` (line 387) is already in place, so that
one-line swap is what turns the check into a startup gate.

- [ ] **Step 5: Fill in all six construction sites**

`SharpMUSH.Configuration/ReadPennMUSHConfig.cs` — after the closing `)` of the `TextFile = new TextFileOptions(...)` block, add a comma then:

```csharp
			Wiki = new WikiOptions(
				DefaultLocale: RequiredString(Get(nameof(WikiOptions.DefaultLocale)), WikiOptions.DefaultLocaleFallback)
			)
```

`SharpMUSH.Library/Services/OptionsService.cs` — inside `Default()`, after the `TextFile` block, add a comma then:

```csharp
			Wiki = new WikiOptions()
```

`SharpMUSH.Server/Controllers/ConfigurationController.cs` — in `CloneRecordWithProperty`, after the `TextFile = …` line add a comma then:

```csharp
			Wiki = prop.Name == nameof(SharpMUSHOptions.Wiki) ? (WikiOptions)newValue! : source.Wiki
```

`SharpMUSH.Tests/Server/TestSharpMushOptions.cs` — change the factory signature and add the block:

```csharp
	public static SharpMUSHOptions Create(
		bool allowBrowserCode = false,
		string wikiDefaultLocale = WikiOptions.DefaultLocaleFallback) => new()
	{
```

and after the `TextFile = new TextFileOptions(...)` block add a comma then:

```csharp
		Wiki = new WikiOptions(DefaultLocale: wikiDefaultLocale)
```

`SharpMUSH.Tests.BUnit/Controllers/ApplicationsGateTests.cs` and `SharpMUSH.Tests.BUnit/Controllers/ConfigurationControllerTests.cs` — after each file's `TextFile = new TextFileOptions(...)` block add a comma then:

```csharp
		Wiki = new WikiOptions()
```

Add `using SharpMUSH.Configuration.Options;` where missing.

- [ ] **Step 6: Add the admin config nav entry and its resx keys**

`SharpMUSH.Client/Components/ConfigNavDrawer.razor`, in the `Content` group (after line 26, the `chat` link):

```razor
    @SectionLink("/admin/config/wiki", Icons.Material.Filled.MenuBook, Loc["Wiki"])
```

`Loc["Wiki"]` already exists in `SharedResource.resx` (no new key needed there). Add the French value only if absent — check with `grep -c 'name="Wiki"' SharpMUSH.Client/Resources/SharedResource.fr.resx`; if `0`, add under the `Common pages` banner:

```xml
  <data name="Wiki" xml:space="preserve">
    <value>Wiki</value>
  </data>
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build`
Expected: 0 errors.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConfigurationTests/*"`
Expected: PASS — including the two `ValidateSharpOptions` cases. If `ValidateSharpOptions_RejectsAnUnparseableWikiDefaultLocale` passes but the server still boots with a bad tag, Step 4a's `Startup.cs` registration swap was missed.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/CodeGenerationTests/*"`
Expected: PASS — the generated metadata/accessor/validator pick the new record up reflectively; this proves the `SharpConfig.Name` is unique and the accessor resolves.

Run: `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 271 passed.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Configuration SharpMUSH.Library/Services/OptionsService.cs \
  SharpMUSH.Server/Controllers/ConfigurationController.cs SharpMUSH.Server/Startup.cs \
  SharpMUSH.Client SharpMUSH.Tests/Configuration SharpMUSH.Tests/Server/TestSharpMushOptions.cs \
  SharpMUSH.Tests.BUnit/Controllers
git commit -m "feat(wiki): add validated Wiki.DefaultLocale configuration option"
```

---

### Task 2: `WikiHelpers.NormalizeLocale`

One place turns arbitrary caller input into a canonical BCP-47 tag. `WikiHelpers` lives in `SharpMUSH.Contracts/Services/WikiHelpers.cs` under namespace `SharpMUSH.Library.Services` (surprising, but keep it) and is referenced by both server and client, which is why the normaliser belongs here alongside `NormalizeCategory`.

**Two entry points, because writes and reads want opposite failure modes.** "Any parseable tag" is a permissive *input* rule, not a licence to persist whatever arrives:

| Helper | Returns | Used by |
|---|---|---|
| `NormalizeLocale` | `OneOf<string, Error<string>>` | every **write** boundary — `UpsertTranslationAsync`, `CreateAsync`'s `sourceLocale`, the migration backfill |
| `NormalizeLocaleOrEmpty` | `string` (empty when unusable) | every **read**/lookup path — the resolver, `?lang=`, `GetTranslationAsync`, `GetRevisionsForLocaleAsync` |

A reader typing a bad `?lang=` must get the default page, not a 400. A *writer* persisting a bad locale is a different thing entirely, because it corrupts the store for every later read. This split is also what upgrades `LocalizedWikiPage.IsFallback`'s no-throw claim from a convention to an invariant: no unparseable locale can be in the database to begin with.

Case canonicalisation closes a quieter hole: without it `pt-BR` and `pt-br` are two rows the unique `(PageId, Locale)` index happily accepts and the resolver treats as unrelated.

`predefinedOnly: true` is load-bearing: without it, .NET's ICU accepts arbitrary well-formed junk like `"qq"` as a pseudo-culture, and an unparseable tag would silently become a "valid" locale instead of being rejected.

**Files:**
- Modify: `SharpMUSH.Contracts/SharpMUSH.Contracts.csproj` (add the `OneOf` package reference)
- Modify: `SharpMUSH.Contracts/Services/WikiHelpers.cs` (append after `NormalizeTags`)
- Test: `SharpMUSH.Tests/Wiki/WikiHelpersLocaleTests.cs` (create)

**Interfaces:**
- Consumes: `WikiOptions.DefaultLocaleFallback` (Task 1) in the agreement test only.
- Produces:
  - `WikiHelpers.NormalizeLocale(string? locale)` → `OneOf<string, Error<string>>` — canonical `CultureInfo.Name` (e.g. `"fr-CA"`), or `Error<string>` for null/blank/unparseable/invariant. **The write boundary.**
  - `WikiHelpers.NormalizeLocaleOrEmpty(string? locale)` → `string` — the same canonicalisation, `string.Empty` instead of an error. **The read path.**
  - `WikiHelpers.NeutralLocale(string? locale)` → `string` — two-letter ISO language (e.g. `"fr"` from `"fr-CA"`), or `string.Empty`.
  - `WikiHelpers.SameLanguage(string? a, string? b)` → `bool` — case-insensitive comparison of neutral languages.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Wiki/WikiHelpersLocaleTests.cs` (tabs):

```csharp
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// <see cref="WikiHelpers.NormalizeLocale"/> is the single gate between caller-supplied locale text and
/// every stored locale in the wiki. A tag that escapes it unparsed would later throw inside
/// <c>CultureInfo.GetCultureInfo</c> on a read path the spec guarantees cannot fail — and casing that
/// escapes it uncanonicalised would put <c>pt-BR</c> and <c>pt-br</c> in the store as two unrelated rows
/// the unique index is happy to accept.
/// </summary>
public class WikiHelpersLocaleTests
{
	[Test]
	[Arguments("en", "en")]
	[Arguments("EN", "en")]
	[Arguments("  fr  ", "fr")]
	[Arguments("fr-ca", "fr-CA")]
	[Arguments("FR-CA", "fr-CA")]
	[Arguments("pt-br", "pt-BR")]
	[Arguments("PT-BR", "pt-BR")]
	[Arguments("pt-BR", "pt-BR")]
	public async Task NormalizeLocale_CanonicalisesRecognisedTags(string input, string expected)
	{
		var result = WikiHelpers.NormalizeLocale(input);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0).IsEqualTo(expected);
	}

	[Test]
	public async Task NormalizeLocale_CollapsesEveryCasingOfATagOntoOneStoredValue()
	{
		// This is the hole case canonicalisation closes: three spellings, one row, one index entry.
		string[] spellings = ["pt-br", "PT-BR", "pt-BR"];

		var canonical = spellings.Select(s => WikiHelpers.NormalizeLocale(s).AsT0).Distinct().ToList();

		await Assert.That(canonical)
			.IsEquivalentTo(new[] { "pt-BR" })
			.Because("otherwise the unique (PageId, Locale) index accepts all three as different locales");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("not a locale")]
	[Arguments("qq")]
	[Arguments("zz-ZZ")]
	public async Task NormalizeLocale_RejectsUnusableTagsWithAnError(string? input)
	{
		var result = WikiHelpers.NormalizeLocale(input);

		await Assert.That(result.IsT1)
			.IsTrue()
			.Because("a write boundary must refuse a non-locale rather than store an empty string");
		await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
	}

	[Test]
	[Arguments("pt-br", "pt-BR")]
	[Arguments(null, "")]
	[Arguments("not a locale", "")]
	[Arguments("zz-ZZ", "")]
	public async Task NormalizeLocaleOrEmpty_IsThePermissiveReadPathForm(string? input, string expected)
	{
		await Assert.That(WikiHelpers.NormalizeLocaleOrEmpty(input))
			.IsEqualTo(expected)
			.Because("a reader typing a bad ?lang= gets the default page, never a 400");
	}

	[Test]
	public async Task NormalizeLocale_AgreesWithTheStartupValidationOfWikiDefaultLocale()
	{
		// ValidateSharpOptions restates this rule because SharpMUSH.Contracts references
		// SharpMUSH.Configuration and the dependency cannot run the other way. This is the test that
		// keeps the restatement honest.
		await Assert.That(WikiHelpers.NormalizeLocale(WikiOptions.DefaultLocaleFallback).IsT0).IsTrue();
		await Assert.That(new ValidateSharpOptions()
				.Validate(null, TestSharpMushOptions.Create(wikiDefaultLocale: "zz-ZZ")).Failed)
			.IsTrue()
			.Because("both must reject a well-formed tag that is not a real culture");
	}

	[Test]
	public async Task NeutralLocale_StripsTheRegion()
	{
		await Assert.That(WikiHelpers.NeutralLocale("fr-CA")).IsEqualTo("fr");
		await Assert.That(WikiHelpers.NeutralLocale("fr")).IsEqualTo("fr");
		await Assert.That(WikiHelpers.NeutralLocale("nonsense")).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task SameLanguage_ComparesLanguagesNotTags()
	{
		await Assert.That(WikiHelpers.SameLanguage("fr-CA", "fr")).IsTrue();
		await Assert.That(WikiHelpers.SameLanguage("fr-CA", "en")).IsFalse();
		await Assert.That(WikiHelpers.SameLanguage("en", "en-GB")).IsTrue();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiHelpersLocaleTests/*"`
Expected: compile error — `'WikiHelpers' does not contain a definition for 'NormalizeLocale'`.

- [ ] **Step 3: Give `SharpMUSH.Contracts` the `OneOf` package**

`NormalizeLocale` returns `OneOf<string, Error<string>>`, and `SharpMUSH.Contracts` does not reference
`OneOf` today. Add it to `SharpMUSH.Contracts/SharpMUSH.Contracts.csproj`, in the existing
`PackageReference` group beside `Markdig`, at the version the rest of the solution pins:

```xml
		<PackageReference Include="OneOf" Version="3.0.271" />
```

This does not violate the csproj's browser-safety comment: `OneOf` is a pure, dependency-free assembly
and `SharpMUSH.Client` already references the same version, so the WASM closure is unchanged. Do **not**
add `OneOf.SourceGenerator` — nothing here declares a custom union.

`WikiHelpers` is a single static class in one assembly, so the OneOf-returning overload cannot live in
`SharpMUSH.Library` instead: a static class cannot be split across assemblies, and the spec is explicit
that this helper belongs beside `NormalizeCategory`.

- [ ] **Step 4: Implement the helpers**

At the top of `SharpMUSH.Contracts/Services/WikiHelpers.cs` add:

```csharp
using OneOf;
using OneOf.Types;
using System.Globalization;
```

Append inside the class, after `NormalizeTags`:

```csharp
	/// <summary>
	/// Canonical form of a locale tag, or <see cref="Error{T}"/> when it is not a locale at all.
	/// Canonical means <see cref="CultureInfo"/>'s own casing — <c>pt-br</c> and <c>PT-BR</c> both become
	/// <c>pt-BR</c> — so the unique (PageId, Locale) index cannot be defeated by casing.
	/// </summary>
	/// <remarks>
	/// This is the <em>write</em> boundary: every point a locale enters storage or configuration goes
	/// through it, so no unparseable tag can be in the database to begin with. Read paths want
	/// <see cref="NormalizeLocaleOrEmpty"/> instead, because a reader typing a bad <c>?lang=</c> should get
	/// the default page rather than an error.
	/// <para>
	/// <c>predefinedOnly: true</c> is required. Without it .NET accepts any well-formed tag as a
	/// pseudo-culture, so junk like <c>qq</c> would become a "valid" locale and get persisted.
	/// </para>
	/// </remarks>
	public static OneOf<string, Error<string>> NormalizeLocale(string? locale)
	{
		var normalized = NormalizeLocaleOrEmpty(locale);
		return normalized.Length == 0
			? new Error<string>($"'{locale}' is not a recognised BCP-47 locale tag.")
			: normalized;
	}

	/// <summary>
	/// The same canonicalisation as <see cref="NormalizeLocale"/>, but returning
	/// <see cref="string.Empty"/> rather than an error when the tag is absent or not a real culture.
	/// </summary>
	/// <remarks>
	/// For read and lookup paths only. A malformed <c>?lang=</c> is treated as absent — never a 400 —
	/// so callers can substitute the configured default without branching on an error.
	/// </remarks>
	public static string NormalizeLocaleOrEmpty(string? locale)
	{
		if (string.IsNullOrWhiteSpace(locale)) return string.Empty;

		try
		{
			var culture = CultureInfo.GetCultureInfo(locale.Trim(), predefinedOnly: true);
			return culture.Name.Length == 0 ? string.Empty : culture.Name;
		}
		catch (CultureNotFoundException)
		{
			return string.Empty;
		}
	}

	/// <summary>
	/// The neutral (language-only) form of a locale tag: <c>fr-CA</c> becomes <c>fr</c>.
	/// Returns <see cref="string.Empty"/> when the tag is unusable.
	/// </summary>
	public static string NeutralLocale(string? locale)
	{
		var normalized = NormalizeLocaleOrEmpty(locale);
		return normalized.Length == 0
			? string.Empty
			: CultureInfo.GetCultureInfo(normalized).TwoLetterISOLanguageName;
	}

	/// <summary>
	/// True when two locale tags name the same language, ignoring region. Serving <c>fr</c> to an
	/// <c>fr-CA</c> reader is not a fallback and must not raise a "showing English" notice.
	/// </summary>
	public static bool SameLanguage(string? a, string? b)
	{
		var left = NeutralLocale(a);
		var right = NeutralLocale(b);
		return left.Length > 0
			&& string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiHelpersLocaleTests/*"`
Expected: PASS (all arguments cases).

If `"qq"` or `"zz-ZZ"` unexpectedly resolves, the `predefinedOnly` overload is not being used — re-check Step 4 rather than weakening the test.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Contracts/SharpMUSH.Contracts.csproj SharpMUSH.Contracts/Services/WikiHelpers.cs \
  SharpMUSH.Tests/Wiki/WikiHelpersLocaleTests.cs
git commit -m "feat(wiki): add locale normalisation helpers to WikiHelpers"
```

---

### Task 3: Data model — `SourceLocale`, revision `Locale`, and the three new records

Additive only: `WikiPage` and `WikiRevision` gain **init-only** properties (the convention their own comments establish for `Category`/`Tags`/`Published`), so every existing construction site and every stored document keeps working untouched.

**`SourceLocale` is materialised once, never re-derived.** The property initializer default is `string.Empty` — a property initializer cannot read configuration, and hardcoding `"en"` would mislabel every page on a non-English game. But empty is a *transient* state, not a documented read-time meaning: `Migration_AddWikiTranslations` (Tasks 7–9) stamps every unstamped row once, and every create path stamps new pages, so a page read back from storage always carries a non-empty canonical tag. Nothing normalises empty → `Wiki.DefaultLocale` on read, because that would let an admin changing `wiki_default_locale` silently reinterpret the authored locale of every pre-existing page: an English page starts claiming to be French, `UpsertTranslationAsync` begins rejecting `fr` as "shadowing the source" while accepting `en`, and existing revision history changes meaning — with no migration, no audit trail and nothing to alert on.

`LocalizedWikiPage` deliberately keeps resolved content on the wrapper and never on `Page`. If `Page.Title` stayed authoritative-looking, a caller would eventually render the English title beside French body text.

**Files:**
- Modify: `SharpMUSH.Library/Models/Wiki/WikiPage.cs` (add `SourceLocale` after `Published`)
- Modify: `SharpMUSH.Library/Models/Wiki/WikiRevision.cs` (add `Locale`)
- Create: `SharpMUSH.Library/Models/Wiki/WikiTranslation.cs`
- Create: `SharpMUSH.Library/Models/Wiki/WikiTranslationSummary.cs`
- Create: `SharpMUSH.Library/Models/Wiki/LocalizedWikiPage.cs`
- Test: `SharpMUSH.Tests/Wiki/LocalizedWikiPageTests.cs` (create)

**Interfaces:**
- Consumes: `WikiHelpers.NormalizeLocale` (Task 2) — used by the *callers* of these records, not by the records themselves.
- Produces:
  - `WikiPage.SourceLocale` → `string` (init-only; initializer default `string.Empty`, stamped non-empty by the migration and every create path)
  - `WikiRevision.Locale` → `string` (init-only, default `string.Empty`; empty means "the source-locale stream")
  - `WikiTranslation(string Id, string PageId, string Locale, string Title, string MarkdownSource, string RenderedHtml, string PlainText, string LastEditorDbref, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Published, int RevisionNumber)`
  - `WikiTranslationSummary(string Locale, string Title, bool Published, DateTimeOffset UpdatedAt, int RevisionNumber)`
  - `LocalizedWikiPage(WikiPage Page, string Locale, string RequestedLocale, string Title, string MarkdownSource, string RenderedHtml, string PlainText, bool Published, int RevisionNumber)` with computed `bool IsFallback`

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Wiki/LocalizedWikiPageTests.cs` (tabs):

```csharp
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// <see cref="LocalizedWikiPage.IsFallback"/> drives a user-visible banner, and it compares
/// <em>languages</em>, not tags. Serving <c>fr</c> to an <c>fr-CA</c> reader must not banner every
/// Canadian visit; serving <c>en</c> to that reader must.
/// </summary>
public class LocalizedWikiPageTests
{
	private static WikiPage BarePage() => new(
		Id: "1", Slug: "dragons", Title: "Dragons", Namespace: "main",
		MarkdownSource: "en body", RenderedHtml: "<p>en body</p>", PlainText: "en body",
		AuthorDbref: "#1", LastEditorDbref: "#1",
		CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
		IsProtected: false, RevisionNumber: 1)
	{
		Category = "general",
		SourceLocale = "en",
	};

	private static LocalizedWikiPage Localized(string served, string requested) => new(
		Page: BarePage(),
		Locale: served,
		RequestedLocale: requested,
		Title: "T", MarkdownSource: "m", RenderedHtml: "<p>m</p>", PlainText: "m",
		Published: true, RevisionNumber: 1);

	[Test]
	[Arguments("fr", "fr", false)]
	[Arguments("fr", "fr-CA", false)]
	[Arguments("fr-CA", "fr", false)]
	[Arguments("en", "en-GB", false)]
	[Arguments("en", "fr", true)]
	[Arguments("en", "fr-CA", true)]
	[Arguments("fr", "en", true)]
	public async Task IsFallback_ComparesLanguagesNotTags(string served, string requested, bool expected)
	{
		await Assert.That(Localized(served, requested).IsFallback).IsEqualTo(expected);
	}

	[Test]
	public async Task ResolvedContentLivesOnTheWrapperNotThePage()
	{
		var localized = Localized("fr", "fr") with { Title = "Dragons (fr)", MarkdownSource = "corps fr" };

		await Assert.That(localized.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(localized.Page.Title)
			.IsEqualTo("Dragons")
			.Because("the source page must keep its own title so nobody renders a mixed-language page");
	}

	[Test]
	public async Task WikiPage_SourceLocaleInitializerDefaultsToEmptyNotEnglish()
	{
		var page = BarePage() with { SourceLocale = string.Empty };

		await Assert.That(page.SourceLocale)
			.IsEqualTo(string.Empty)
			.Because("a property initializer cannot read configuration, and hardcoding 'en' would mislabel "
				+ "every page on a non-English game. Empty is a transient pre-backfill state, NOT a "
				+ "read-time synonym for Wiki.DefaultLocale — see Task 10");
	}

	[Test]
	public async Task WikiRevision_LocaleDefaultsToEmptyMeaningTheSourceStream()
	{
		var revision = new WikiRevision(
			Id: "1:1", PageId: "1", RevisionNumber: 1, MarkdownSource: "v1",
			EditorDbref: "#1", Timestamp: DateTimeOffset.UnixEpoch, EditSummary: null);

		await Assert.That(revision.Locale).IsEqualTo(string.Empty);
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LocalizedWikiPageTests/*"`
Expected: compile errors — `LocalizedWikiPage` not found; `WikiPage` has no `SourceLocale`; `WikiRevision` has no `Locale`.

- [ ] **Step 3: Add `SourceLocale` to `WikiPage`**

In `SharpMUSH.Library/Models/Wiki/WikiPage.cs`, after the `Published` property (line 47):

```csharp

	/// <summary>
	/// Canonical BCP-47 locale the page was authored in. Never empty on a page read back from storage:
	/// <c>Migration_AddWikiTranslations</c> stamps every pre-existing page once, and every create path
	/// stamps new pages.
	/// </summary>
	/// <remarks>
	/// The initializer default is <see cref="string.Empty"/> only because a property initializer cannot
	/// read configuration. It means "not yet stamped", and it is <em>not</em> a read-time synonym for
	/// <c>Wiki.DefaultLocale</c>: re-deriving it would let an admin changing <c>wiki_default_locale</c>
	/// silently change the authored locale of every page that predates the field, with no migration and
	/// nothing to alert on. Once stamped, this field is authoritative and immutable per page.
	/// </remarks>
	public string SourceLocale { get; init; } = string.Empty;
```

- [ ] **Step 4: Add `Locale` to `WikiRevision`**

`SharpMUSH.Library/Models/Wiki/WikiRevision.cs` — change the record from an expression-bodied declaration to one with a body:

```csharp
public record WikiRevision(
	string Id,
	string PageId,
	int RevisionNumber,
	string MarkdownSource,
	string EditorDbref,
	DateTimeOffset Timestamp,
	string? EditSummary)
{
	/// <summary>
	/// The locale this revision belongs to, so history is a stream per <c>(PageId, Locale)</c>.
	/// Init-only with an empty default: existing rows read back as source-locale revisions, and
	/// <see cref="string.Empty"/> is the canonical marker for "the source-locale stream".
	/// </summary>
	public string Locale { get; init; } = string.Empty;
}
```

- [ ] **Step 5: Create `WikiTranslation`**

Create `SharpMUSH.Library/Models/Wiki/WikiTranslation.cs` (tabs):

```csharp
namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A translation of a <see cref="WikiPage"/> into one locale — an overlay row hanging off the page,
/// not a page in its own right.
/// </summary>
/// <remarks>
/// Note what this record deliberately lacks: no <c>Category</c>, no <c>Tags</c>, no <c>IsProtected</c>,
/// no <c>Slug</c>. That absence <em>is</em> the enforcement of "a translation inherits the source
/// page's metadata" — there is nowhere for a translation to store a conflicting category, so no
/// runtime check is needed to keep the two in step.
/// </remarks>
/// <param name="Id">Storage key.</param>
/// <param name="PageId">FK to the parent <see cref="WikiPage.Id"/>.</param>
/// <param name="Locale">Canonical BCP-47 tag. Unique per <paramref name="PageId"/>.</param>
/// <param name="Title">Translated display title.</param>
/// <param name="MarkdownSource">Translated Markdown body — the source of truth for this locale.</param>
/// <param name="RenderedHtml">Cached HTML render of <paramref name="MarkdownSource"/>.</param>
/// <param name="PlainText">Plain text extracted from <paramref name="MarkdownSource"/>.</param>
/// <param name="LastEditorDbref">DBRef string of the player who last edited this translation.</param>
/// <param name="CreatedAt">UTC timestamp the translation was first written.</param>
/// <param name="UpdatedAt">UTC timestamp of the last edit to this translation.</param>
/// <param name="Published">When false this translation is a draft: invisible to ordinary readers,
/// who fall back exactly as if it did not exist.</param>
/// <param name="RevisionNumber">Per-locale revision counter, starting at 1.</param>
public record WikiTranslation(
	string Id,
	string PageId,
	string Locale,
	string Title,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	string LastEditorDbref,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	bool Published,
	int RevisionNumber);
```

- [ ] **Step 6: Create `WikiTranslationSummary`**

Create `SharpMUSH.Library/Models/Wiki/WikiTranslationSummary.cs` (tabs):

```csharp
namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// A translation without its body — enough for the editor's locale list, the reader's language chips
/// and <c>hreflang</c> generation without loading Markdown or HTML for every language.
/// </summary>
public record WikiTranslationSummary(
	string Locale,
	string Title,
	bool Published,
	DateTimeOffset UpdatedAt,
	int RevisionNumber);
```

- [ ] **Step 7: Create `LocalizedWikiPage`**

Create `SharpMUSH.Library/Models/Wiki/LocalizedWikiPage.cs` (tabs):

```csharp
using System.Globalization;

namespace SharpMUSH.Library.Models.Wiki;

/// <summary>
/// The read model a localized wiki request resolves to. Never stored.
/// </summary>
/// <remarks>
/// Resolved content sits on this wrapper and never on <see cref="Page"/>. If <c>Page.Title</c> stayed
/// authoritative-looking, a caller would eventually render the English title beside French body text
/// and nobody would notice for months.
/// <para>
/// <see cref="Locale"/> and <see cref="RequestedLocale"/> are guaranteed to be already-normalised,
/// parseable tags, so the <see cref="CultureInfo.GetCultureInfo(string)"/> calls in
/// <see cref="IsFallback"/> cannot throw. That rests on two things, not one:
/// <c>IWikiLocalizationService</c> is the only thing that constructs this record and it normalises the
/// <em>requested</em> tag first, <b>and</b> no unparseable locale can be in the store to begin with
/// because every write boundary goes through <c>WikiHelpers.NormalizeLocale</c>. The second half is what
/// makes this an invariant rather than a convention a future caller can break.
/// </para>
/// </remarks>
/// <param name="Page">Identity and inherited metadata ONLY — never a content source.</param>
/// <param name="Locale">The locale actually served.</param>
/// <param name="RequestedLocale">The locale the reader asked for, after normalisation.</param>
/// <param name="Title">Resolved title.</param>
/// <param name="MarkdownSource">Resolved Markdown body.</param>
/// <param name="RenderedHtml">Resolved HTML.</param>
/// <param name="PlainText">Resolved plain text.</param>
/// <param name="Published">The <em>served</em> row's flag — the translation's when a translation is
/// served, the page's when the source is served.</param>
/// <param name="RevisionNumber">The served row's revision counter.</param>
public sealed record LocalizedWikiPage(
	WikiPage Page,
	string Locale,
	string RequestedLocale,
	string Title,
	string MarkdownSource,
	string RenderedHtml,
	string PlainText,
	bool Published,
	int RevisionNumber)
{
	/// <summary>
	/// True when the served locale is a different <em>language</em> from the requested one, which is
	/// what the reader-facing notice keys off. Compares languages rather than tags so that serving
	/// <c>fr</c> to an <c>fr-CA</c> reader does not banner every Canadian visit.
	/// </summary>
	public bool IsFallback =>
		!string.Equals(
			CultureInfo.GetCultureInfo(Locale).TwoLetterISOLanguageName,
			CultureInfo.GetCultureInfo(RequestedLocale).TwoLetterISOLanguageName,
			StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 errors — the new properties are init-only, so no existing construction site changed.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/LocalizedWikiPageTests/*"`
Expected: PASS.

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed.

- [ ] **Step 9: Commit**

```bash
git add SharpMUSH.Library/Models/Wiki SharpMUSH.Tests/Wiki/LocalizedWikiPageTests.cs
git commit -m "feat(wiki): add translation overlay models and the localized read model"
```

---

### Task 4: `IWikiLocaleResolver` — the five-step fallback chain

This is the *only* place fallback rules live. It has no DB and no HTTP access, and it is permission-blind: it receives an already-visibility-filtered candidate set. That is what makes the chain unit-testable without an auth graph, and it is why a draft translation falls through to step 4 or 5 for an ordinary reader, banner included, exactly as if it did not exist.

Step 5 is the terminal guarantee. The spec names the configured default as the fallback target, but a page authored only in French on an `en`-default game would then have nothing to serve — and a read can never fail for locale reasons.

**Files:**
- Create: `SharpMUSH.Library/Services/Interfaces/IWikiLocaleResolver.cs`
- Create: `SharpMUSH.Library/Services/WikiLocaleResolver.cs`
- Test: `SharpMUSH.Tests/Wiki/WikiLocaleResolverTests.cs` (create)

**Interfaces:**
- Consumes: `WikiHelpers.NormalizeLocaleOrEmpty` / `NeutralLocale` / `SameLanguage` (Task 2); `WikiOptions.DefaultLocaleFallback` and `SharpMUSHOptions.Wiki.DefaultLocale` (Task 1); `TestSharpMushOptions.Create(allowBrowserCode, wikiDefaultLocale)` (Task 1) in tests.
- Produces:
  - `LocaleResolution(string Locale, bool IsFallback)` — record in namespace `SharpMUSH.Library.Services.Interfaces`
  - `IWikiLocaleResolver.Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available)` → `LocaleResolution`. **`sourceLocale` is a precondition, not an input to normalise:** it must already be a non-empty canonical tag. The resolver never substitutes `DefaultLocale` for it — that substitution is exactly the re-derivation bug the spec removes. `IWikiLocalizationService` (Task 10) is the one place that deals with an unstamped row, and it logs a warning when it has to.
  - `IWikiLocaleResolver.NormalizeRequested(string? requested)` → `string` — the normalised requested tag (falls to `Wiki.DefaultLocale`); `IWikiLocalizationService` needs it to fill `LocalizedWikiPage.RequestedLocale`.
  - `IWikiLocaleResolver.DefaultLocale` → `string` — the normalised configured default.
  - `WikiLocaleResolver(IOptionsMonitor<SharpMUSHOptions> options)` — registered as a singleton in Task 10.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Wiki/WikiLocaleResolverTests.cs` (tabs):

```csharp
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// Table-driven cover of every step in the fallback chain. The resolver is deliberately permission-blind:
/// callers hand it an already-visibility-filtered candidate set, which is what lets these tests exercise
/// the rules with no database and no auth graph.
/// </summary>
public class WikiLocaleResolverTests
{
	private static IWikiLocaleResolver BuildResolver(string defaultLocale = "en")
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: defaultLocale));
		return new WikiLocaleResolver(monitor);
	}

	[Test]
	public async Task Step2_ExactMatchWins()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: ["fr", "de"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step2_ExactMatchIsCaseInsensitive()
	{
		var result = BuildResolver().Resolve("FR", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step3_RegionFindsItsNeutralParent()
	{
		var result = BuildResolver().Resolve("fr-CA", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback)
			.IsFalse()
			.Because("serving fr to an fr-CA reader is the same language, not a fallback");
	}

	[Test]
	public async Task Step3_NeutralFindsARegionalTranslation()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: ["fr-CA"]);

		await Assert.That(result.Locale).IsEqualTo("fr-CA");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task Step4_FallsToTheConfiguredDefaultWhenATranslationExistsForIt()
	{
		var result = BuildResolver("de").Resolve("fr", sourceLocale: "en", available: ["de"]);

		await Assert.That(result.Locale).IsEqualTo("de");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task Step5_FallsToTheSourceLocaleAsTheTerminalGuarantee()
	{
		var result = BuildResolver().Resolve("fr", sourceLocale: "en", available: []);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task Step5_SourceLocaleWinsEvenWhenItIsNotTheConfiguredDefault()
	{
		// A page authored only in French on an en-default game still has something to serve.
		var result = BuildResolver("en").Resolve("de", sourceLocale: "fr", available: []);

		await Assert.That(result.Locale).IsEqualTo("fr");
		await Assert.That(result.IsFallback).IsTrue();
	}

	[Test]
	public async Task StampedSourceLocaleIsNotReinterpretedWhenTheConfiguredDefaultChanges()
	{
		// The regression test for the bug the design fixes. Same page, two different configured defaults:
		// the served locale must be the page's own stamped SourceLocale both times. If the resolver ever
		// re-derives an "effective" source locale from configuration, this fails.
		var onEnglishGame = BuildResolver("en").Resolve("de", sourceLocale: "fr", available: []);
		var onGermanGame = BuildResolver("de").Resolve("es", sourceLocale: "fr", available: []);

		await Assert.That(onEnglishGame.Locale).IsEqualTo("fr");
		await Assert.That(onGermanGame.Locale)
			.IsEqualTo("fr")
			.Because("SourceLocale is materialised per page; changing wiki_default_locale must not "
				+ "reinterpret what language an existing page was authored in");
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("not a locale")]
	public async Task UnparseableOrAbsentRequestBecomesTheConfiguredDefault(string? requested)
	{
		var result = BuildResolver("en").Resolve(requested, sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback)
			.IsFalse()
			.Because("a reader who asked for nothing is not being shown a fallback");
	}

	[Test]
	public async Task RequestingTheSourceLocaleIsNeverAFallback()
	{
		var result = BuildResolver().Resolve("en", sourceLocale: "en", available: ["fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task SourceLocaleIsPreferredOverATranslationThatShadowsIt()
	{
		// No row may shadow the source; if a stale one exists, the page still wins.
		var result = BuildResolver().Resolve("en", sourceLocale: "en", available: ["en", "fr"]);

		await Assert.That(result.Locale).IsEqualTo("en");
		await Assert.That(result.IsFallback).IsFalse();
	}

	[Test]
	public async Task NormalizeRequested_FallsToTheConfiguredDefault()
	{
		var resolver = BuildResolver("fr");

		await Assert.That(resolver.NormalizeRequested("junk")).IsEqualTo("fr");
		await Assert.That(resolver.NormalizeRequested("pt-br")).IsEqualTo("pt-BR");
		await Assert.That(resolver.DefaultLocale).IsEqualTo("fr");
	}

	[Test]
	public async Task DefaultLocale_FallsBackToEnglishWhenConfigurationIsGarbage()
	{
		// Unreachable in production: ValidateSharpOptions (Task 1) fails startup on an unparseable
		// wiki_default_locale. Kept as belt-and-braces so a bad value from a hand-edited stored config
		// degrades to a readable page instead of throwing inside a render.
		await Assert.That(BuildResolver("not a locale").DefaultLocale)
			.IsEqualTo(WikiOptions.DefaultLocaleFallback)
			.Because("a misconfigured default must not break every wiki read");
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiLocaleResolverTests/*"`
Expected: compile error — `IWikiLocaleResolver` / `WikiLocaleResolver` not found.

- [ ] **Step 3: Create the contract**

Create `SharpMUSH.Library/Services/Interfaces/IWikiLocaleResolver.cs` (tabs):

```csharp
namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>The outcome of resolving a reader's requested locale against a page's available content.</summary>
/// <param name="Locale">The locale to serve. Always a canonical, parseable tag.</param>
/// <param name="IsFallback">True when the served locale is a different language from the requested one.</param>
public sealed record LocaleResolution(string Locale, bool IsFallback);

/// <summary>
/// The one place wiki locale-fallback rules live. No database, no HTTP, and no permission awareness —
/// callers hand it a candidate set they have already filtered by visibility, which is what keeps draft
/// translations from leaking without teaching the rules about an auth graph.
/// </summary>
public interface IWikiLocaleResolver
{
	/// <summary>The normalised, game-wide configured fallback locale (<c>Wiki.DefaultLocale</c>).</summary>
	string DefaultLocale { get; }

	/// <summary>
	/// Normalises a caller-supplied locale tag, substituting <see cref="DefaultLocale"/> when it is
	/// absent, blank or unparseable. Never throws and never returns empty.
	/// </summary>
	string NormalizeRequested(string? requested);

	/// <summary>
	/// Resolves which locale to serve, in order:
	/// <list type="number">
	///   <item>Normalise <paramref name="requested"/> — null, blank or unparseable becomes <see cref="DefaultLocale"/>.</item>
	///   <item>The page's own <paramref name="sourceLocale"/>, if it is the requested language.</item>
	///   <item>Exact match against <paramref name="available"/>, case-insensitive.</item>
	///   <item>Neutral-language match: <c>fr-CA</c> finds an <c>fr</c> translation and vice versa.</item>
	///   <item><see cref="DefaultLocale"/>, if a translation exists for it.</item>
	///   <item><paramref name="sourceLocale"/> — the <c>WikiPage</c> row itself, which always exists.</item>
	/// </list>
	/// </summary>
	/// <param name="requested">The reader's locale, unvalidated.</param>
	/// <param name="sourceLocale">
	/// The page's stamped <c>SourceLocale</c>: a non-empty canonical tag. This is a precondition, not
	/// something this method normalises — the configured default must never be substituted for it, or
	/// changing <c>wiki_default_locale</c> would reinterpret the authored locale of every existing page.
	/// <c>IWikiLocalizationService</c> is the single place that copes with an unstamped row.
	/// </param>
	/// <param name="available">Locales with content the caller has decided this reader may see.</param>
	LocaleResolution Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available);
}
```

- [ ] **Step 4: Implement the resolver**

Create `SharpMUSH.Library/Services/WikiLocaleResolver.cs` (tabs):

```csharp
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiLocaleResolver"/>
public sealed class WikiLocaleResolver(IOptionsMonitor<SharpMUSHOptions> options) : IWikiLocaleResolver
{
	public string DefaultLocale
	{
		get
		{
			// Last resort when Wiki.DefaultLocale itself is unparseable. ValidateSharpOptions rejects that
			// at startup, so this branch exists only so a hand-edited stored config degrades to a readable
			// page rather than throwing inside a render.
			var configured = WikiHelpers.NormalizeLocaleOrEmpty(options.CurrentValue.Wiki.DefaultLocale);
			return configured.Length == 0 ? WikiOptions.DefaultLocaleFallback : configured;
		}
	}

	public string NormalizeRequested(string? requested)
	{
		// Deliberately the permissive form: a reader's bad ?lang= becomes the default, never an error.
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(requested);
		return normalized.Length == 0 ? DefaultLocale : normalized;
	}

	public LocaleResolution Resolve(string? requested, string sourceLocale, IReadOnlyCollection<string> available)
	{
		var want = NormalizeRequested(requested);

		// sourceLocale is the page's materialised SourceLocale and is authoritative. Canonicalise the
		// casing, but do NOT substitute DefaultLocale for an empty value: that re-derivation is what let
		// a change to wiki_default_locale silently relabel every pre-existing page. Task 10 handles the
		// unstamped-row case once, loudly.
		var source = WikiHelpers.NormalizeLocaleOrEmpty(sourceLocale);

		// The source row always exists, so prefer it whenever it is the requested language. This also
		// makes a stale translation row that shadows the source unreachable rather than authoritative.
		if (WikiHelpers.SameLanguage(want, source))
			return new LocaleResolution(source, IsFallback: false);

		if (Match(available, c => string.Equals(c, want, StringComparison.OrdinalIgnoreCase)) is { } exact)
			return new LocaleResolution(exact, IsFallback: false);

		if (Match(available, c => WikiHelpers.SameLanguage(c, want)) is { } neutral)
			return new LocaleResolution(neutral, IsFallback: false);

		if (Match(available, c => WikiHelpers.SameLanguage(c, DefaultLocale)) is { } fallbackDefault)
			return new LocaleResolution(fallbackDefault, IsFallback: true);

		return new LocaleResolution(source, IsFallback: true);
	}

	/// <summary>
	/// First candidate satisfying <paramref name="predicate"/>, ordered so the result does not depend on
	/// the caller's collection order: exact-length tags before regional variants, then alphabetical.
	/// </summary>
	private static string? Match(IReadOnlyCollection<string> available, Func<string, bool> predicate) =>
		available
			.Where(c => WikiHelpers.NormalizeLocaleOrEmpty(c).Length > 0)
			.Where(predicate)
			.OrderBy(c => c.Length)
			.ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
			.Select(WikiHelpers.NormalizeLocaleOrEmpty)
			.FirstOrDefault();
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiLocaleResolverTests/*"`
Expected: PASS (all 14 tests, including `StampedSourceLocaleIsNotReinterpretedWhenTheConfiguredDefaultChanges`).

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Library/Services/Interfaces/IWikiLocaleResolver.cs \
  SharpMUSH.Library/Services/WikiLocaleResolver.cs \
  SharpMUSH.Tests/Wiki/WikiLocaleResolverTests.cs
git commit -m "feat(wiki): add the locale fallback resolver"
```

---

# Phase 2 — Storage

**Sequencing note (spec Risks).** The five new CRUD methods are mechanical, but the three stores' revision indexes disagree *today* — unique on SurrealDB, non-unique on ArangoDB, absent on Memgraph — so a numbering bug fails loudly on one and passes silently on two, surfacing in CI as a one-of-three red that reads like flakiness. **The cross-backend integration test is therefore written in Task 6, before the three hand-written backends in Tasks 7–9, and it asserts the constraint rejects duplicates rather than only exercising the happy path.** A test that only writes valid data cannot tell a real constraint from a missing one, which is precisely how these three drifted apart. Because C# requires every implementer to satisfy the interface, Task 5 lands `NotSupportedException` stubs in the three DB providers so the solution compiles and the new test can be written and shown red. Tasks 7, 8 and 9 each delete their own stub. **After Task 9 no stub remains** — grep for `WikiTranslationsNotImplemented` to confirm.

**Verification reality.** `SharpMUSH.Tests.Integration` runs locally under Podman (see Global Constraints), so Tasks 6–9 are verified by (a) `dotnet build` clean, (b) the mirrored unit tests in `SharpMUSH.Tests` staying green, (c) **`SHARPMUSH_DATABASE_PROVIDER=<db> dotnet run --project SharpMUSH.Tests.Integration` green for each of `arangodb`, `memgraph` and `surrealdb` locally**, and (d) the CI `test-integration` job green on all three. Do not mark 7–9 done on a local build alone — but equally, do not defer to CI what you can prove in seconds here.

### Task 5: `IWikiService` translation CRUD + `InMemoryWikiService` reference implementation

The five methods are purely additive, so `WikiController`, `SeoController`, `BotPrerenderMiddleware`, `WikiCommands` and `WikiFunctions` keep compiling with zero edits. `CreateAsync` gains one **optional trailing** parameter, which is source-compatible for the same reason — every existing call site keeps working, and seeding needs a way to stamp `SourceLocale`.

Four conventions this task establishes and the three DB backends must copy exactly:

1. **Revision streams are keyed by `(PageId, Locale)` with `Locale = ""` meaning the source stream.** `GetRevisionsAsync(pageId, …)` therefore filters to empty-`Locale` rows so its five existing callers see exactly what they see today, and `GetRevisionsForLocaleAsync(pageId, locale, …)` filters on a non-empty locale. A distinct name rather than an overload: an overload differing only by an inserted `string?` invites a silent mis-bind at a call site passing positional ints, and the compiler would not complain.
2. **Translation revision IDs are `{PageId}:{Locale}:{RevisionNumber}`**, so a translation revision can never collide with a source revision (`{PageId}:{RevisionNumber}`).
3. **`UpsertTranslationAsync` ends with a required `int? expectedRevisionNumber` — a compare-and-swap.** The unique `(PageId, Locale)` index protects concurrent *inserts* and nothing else: two translators editing the same French page both read `RevisionNumber = 4`, both compute 5, and both write it. One translator's prose is silently lost and the index is perfectly happy, because it is the same row either way. So:
   - **`expectedRevisionNumber` is the revision the editor loaded.** The update applies only if the stored `RevisionNumber` still matches, and the revision append happens in the same transaction as the row update. A backend that cannot span both in one transaction must instead make the update conditional on the expected value and treat "zero rows affected" as the conflict signal.
   - **`null` means create-only.** If a translation already exists, that is an `Error<string>` rather than a blind overwrite — which is what a caller who believed it was creating a new translation should get.
   - **A detected conflict returns `Error<string>` and is never retried automatically.** Retrying re-applies the loser's stale markdown on top of the winner's, which is exactly the data loss this exists to prevent. The editor reloads and the human decides. The single automatic retry belongs to the *insert* race only, where no content can be lost.

   The parameter is deliberately **not** optional. A default of `null` would make every existing-translation update silently become a create-only call, and the compiler would not complain.
4. **Every locale entering storage goes through `WikiHelpers.NormalizeLocale` (the `OneOf` form).** That means `UpsertTranslationAsync`'s `locale` and `CreateAsync`'s `sourceLocale`: an unparseable tag is an `Error<string>` and nothing is written. Lookups (`GetTranslationAsync`, `GetRevisionsForLocaleAsync`) use `NormalizeLocaleOrEmpty` and treat an unusable tag as "no such locale", never an error.

**Files:**
- Modify: `SharpMUSH.Library/Services/Interfaces/IWikiService.cs` (add 5 methods; add `sourceLocale` to `CreateAsync`; amend `GetRevisionsAsync` and `DeleteAsync` doc comments)
- Modify: `SharpMUSH.Library/Services/InMemoryWikiService.cs` (full implementation)
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Wiki.cs` (stubs + `CreateAsync` parameter)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Wiki.cs` (stubs + `CreateAsync` parameter)
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs` (stubs + `CreateAsync` parameter — **4-space indentation in this file**)
- Test: `SharpMUSH.Tests/Wiki/InMemoryWikiServiceTests.cs` (append)

**Interfaces:**
- Consumes: `WikiTranslation`, `WikiTranslationSummary`, `WikiPage.SourceLocale`, `WikiRevision.Locale` (Task 3); `WikiHelpers.NormalizeLocale` / `NormalizeLocaleOrEmpty` (Task 2).
- Produces, all on `IWikiService`:
  - `Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)`
  - `Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)`
  - `Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(string pageId, string locale, string title, string markdown, string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber)`
  - `Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)`
  - `Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take)`
  - `CreateAsync(string title, string markdown, string authorDbref, WikiNamespace ns = WikiNamespace.Main, string? category = null, string? sourceLocale = null)` — an unparseable `sourceLocale` is an `Error<string>` and no page is created. `null` stores `string.Empty`, i.e. "not yet stamped"; Task 12 and Task 20 make the two real create paths pass `IWikiLocalizationService.DefaultLocale`, and the Tasks 7–9 backfill stamps anything that predates them.
- Also produces the sentinel the later backend tasks delete: `InMemoryWikiService` has no stub; each DB provider gains `private static NotSupportedException WikiTranslationsNotImplemented(string provider)`.

- [ ] **Step 1: Write the failing tests**

Append to `SharpMUSH.Tests/Wiki/InMemoryWikiServiceTests.cs`, inside the existing class (tabs; reuse the file's existing `BuildService()` and `CreatePageAsync(...)` helpers):

```csharp
	// ---- Translations -------------------------------------------------------

	[Test]
	public async Task UpsertTranslationAsync_CreatesTheFirstRevisionOfATranslation()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.UpsertTranslationAsync(
			page.Id, "fr", "Dragons (fr)", "corps **fr**", "#2", "première traduction",
			published: true, expectedRevisionNumber: null);

		await Assert.That(result.IsT0).IsTrue();
		var translation = result.AsT0;
		await Assert.That(translation.Locale).IsEqualTo("fr");
		await Assert.That(translation.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(translation.RevisionNumber).IsEqualTo(1);
		await Assert.That(translation.RenderedHtml).Contains("<strong>fr</strong>");
		await Assert.That(translation.PlainText).Contains("corps");
		await Assert.That(translation.Published).IsTrue();
	}

	[Test]
	public async Task UpsertTranslationAsync_NormalisesTheLocaleTag()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.UpsertTranslationAsync(page.Id, "FR-ca", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.AsT0.Locale).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task UpsertTranslationAsync_SecondCallBumpsTheRevisionInsteadOfDuplicating()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var second = await svc.UpsertTranslationAsync(
			page.Id, "fr", "v2", "corps v2", "#3", "révision", true, expectedRevisionNumber: 1);

		await Assert.That(second.AsT0.RevisionNumber).IsEqualTo(2);
		await Assert.That(second.AsT0.MarkdownSource).IsEqualTo("corps v2");
		await Assert.That(second.AsT0.LastEditorDbref).IsEqualTo("#3");
		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count)
			.IsEqualTo(1)
			.Because("upsert must not create a second row for the same (PageId, Locale)");
	}

	[Test]
	public async Task UpsertTranslationAsync_NullExpectedRevisionMeansCreateOnly()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var again = await svc.UpsertTranslationAsync(
			page.Id, "fr", "écrasé", "corps écrasé", "#3", null, true, expectedRevisionNumber: null);

		await Assert.That(again.IsT1)
			.IsTrue()
			.Because("a caller who passed null believed it was creating a translation, not overwriting one");
		await Assert.That((await svc.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v1");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAStaleExpectedRevision()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "fr", "v2", "corps v2", "#3", null, true, expectedRevisionNumber: 1);

		// A second translator who loaded revision 1 and is only now saving.
		var stale = await svc.UpsertTranslationAsync(
			page.Id, "fr", "perdu", "corps perdu", "#4", null, true, expectedRevisionNumber: 1);

		await Assert.That(stale.IsT1).IsTrue();
		await Assert.That((await svc.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v2")
			.Because("the winner's prose must survive; the loser reloads and the human decides");
		var revisions = await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		await Assert.That(revisions.Count).IsEqualTo(2);
		await Assert.That(revisions.Select(r => r.MarkdownSource))
			.DoesNotContain("corps perdu")
			.Because("a rejected write must leave no revision behind");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnparseableLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.UpsertTranslationAsync(page.Id, "not a locale", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsShadowingTheSourceLocale()
	{
		var svc = BuildService();
		var createResult = await svc.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");
		var page = createResult.AsT0;

		var result = await svc.UpsertTranslationAsync(page.Id, "en", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT1)
			.IsTrue()
			.Because("no row may shadow the source; the page itself is edited instead");
	}

	[Test]
	public async Task UpsertTranslationAsync_RejectsAnUnknownPage()
	{
		var svc = BuildService();

		var result = await svc.UpsertTranslationAsync("ghost", "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		var result = await svc.GetTranslationAsync(page.Id, "de");

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task GetTranslationsAsync_ReturnsBodylessSummariesIncludingDrafts()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "de", "Drachen", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var summaries = await svc.GetTranslationsAsync(page.Id);

		await Assert.That(summaries.Count).IsEqualTo(2);
		await Assert.That(summaries.Select(s => s.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
		await Assert.That(summaries.Single(s => s.Locale == "de").Published)
			.IsFalse()
			.Because("storage returns every row; visibility filtering is the caller's job");
	}

	[Test]
	public async Task GetRevisionsForLocaleAsync_IsASeparateStreamFromTheSource()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons", markdown: "v1");
		await svc.UpdateAsync(page.Id, "v2", "#1");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

		var french = await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
		var source = await svc.GetRevisionsAsync(page.Id);

		await Assert.That(french.Count).IsEqualTo(2);
		await Assert.That(french.All(r => r.Locale == "fr")).IsTrue();
		await Assert.That(source.Count)
			.IsEqualTo(2)
			.Because("GetRevisionsAsync must keep returning only the source stream for its five existing callers");
		await Assert.That(source.All(r => r.Locale.Length == 0)).IsTrue();
	}

	[Test]
	public async Task DeleteTranslationAsync_RemovesTheRowAndItsRevisions()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		var deleted = await svc.DeleteTranslationAsync(page.Id, "fr", "#2");

		await Assert.That(deleted.IsT0).IsTrue();
		await Assert.That((await svc.GetTranslationAsync(page.Id, "fr")).IsT1).IsTrue();
		await Assert.That((await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task DeleteTranslationAsync_DeletingTheLastTranslationIsAllowed()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await svc.DeleteTranslationAsync(page.Id, "fr", "#2");

		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetBySlugAsync(page.Slug, page.Category, WikiNamespace.Main)).IsT0)
			.IsTrue()
			.Because("removing the last translation must not remove the page");
	}

	[Test]
	public async Task DeleteTranslationAsync_ReturnsNotFoundForAMissingLocale()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");

		await Assert.That((await svc.DeleteTranslationAsync(page.Id, "fr", "#2")).IsT1).IsTrue();
	}

	[Test]
	public async Task DeleteAsync_CascadesToTranslationsAndTheirRevisions()
	{
		var svc = BuildService();
		var page = await CreatePageAsync(svc, "Dragons");
		await svc.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
		await svc.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		await svc.DeleteAsync(page.Id, "#1");

		await Assert.That((await svc.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
		await Assert.That((await svc.GetRevisionsAsync(page.Id)).Count).IsEqualTo(0);
	}

	[Test]
	public async Task CreateAsync_StampsTheSourceLocaleWhenSupplied()
	{
		var svc = BuildService();

		var result = await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "general", "fr-CA");

		await Assert.That(result.AsT0.SourceLocale).IsEqualTo("fr-CA");
	}

	[Test]
	public async Task CreateAsync_RejectsAnUnparseableSourceLocale()
	{
		var svc = BuildService();

		var result = await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "general", "not a locale");

		await Assert.That(result.IsT1)
			.IsTrue()
			.Because("SourceLocale is materialised and authoritative, so a junk tag must not reach storage");
	}

	[Test]
	public async Task CreateAsync_CanonicalisesTheSourceLocaleItStores()
	{
		var svc = BuildService();

		var result = await svc.CreateAsync("Dragons", "body", "#1", WikiNamespace.Main, "general", "PT-br");

		await Assert.That(result.AsT0.SourceLocale).IsEqualTo("pt-BR");
	}

	[Test]
	public async Task CreateAsync_LeavesSourceLocaleUnstampedWhenNotSupplied()
	{
		var svc = BuildService();

		var result = await svc.CreateAsync("Dragons", "body", "#1");

		await Assert.That(result.AsT0.SourceLocale)
			.IsEqualTo(string.Empty)
			.Because("null means 'not stamped', a transient state the Tasks 7-9 backfill closes. It is NOT a "
				+ "read-time synonym for Wiki.DefaultLocale — the two real create paths (Tasks 12 and 20) "
				+ "pass IWikiLocalizationService.DefaultLocale so this branch is only reached by tests");
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/InMemoryWikiServiceTests/*"`
Expected: compile errors — `'IWikiService' does not contain a definition for 'UpsertTranslationAsync'`, and `CreateAsync` takes no sixth argument.

- [ ] **Step 3: Extend the `IWikiService` contract**

In `SharpMUSH.Library/Services/Interfaces/IWikiService.cs`:

Change `CreateAsync` (line 74, doc from line 64) to add the trailing parameter and document it:

```csharp
	/// <summary>
	/// Creates a new wiki page. The (namespace, category, slug) identity must be unique.
	/// <paramref name="category"/> is normalised (null/blank → <c>general</c>) and is part of
	/// the page's identity, so it is fixed at creation. Renders the Markdown to HTML and extracts
	/// plain text at creation time.
	/// <paramref name="sourceLocale"/> records the locale the body is authored in, canonicalised through
	/// <c>WikiHelpers.NormalizeLocale</c>. It is materialised once here and immutable thereafter — nothing
	/// re-derives it on read. Null or blank stores <see cref="string.Empty"/>, meaning "not yet stamped";
	/// the wiki-translations migration backfills those, and both real create paths supply
	/// <c>IWikiLocalizationService.DefaultLocale</c>.
	/// Returns <c>Error&lt;string&gt;</c> when a page with the same (namespace, category, slug) already
	/// exists, or when <paramref name="sourceLocale"/> is non-blank and not a recognised locale tag.
	/// </summary>
	Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null);
```

Amend the `GetRevisionsAsync` doc (lines 117–120, signature at 121) to state the new filter, and the `DeleteAsync` doc (lines 92–95, signature at 96) to state the cascade:

```csharp
	/// <summary>
	/// Deletes a wiki page, all its revisions, all its translations and those translations' revisions.
	/// Returns <c>None</c> if a page was found and deleted; <c>NotFound</c> if not found.
	/// </summary>
	Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref);
```

```csharp
	/// <summary>
	/// Returns the <em>source-locale</em> revision history for a page, ordered by revision number
	/// descending, with skip/take pagination. Translation revisions are a separate stream — see
	/// <see cref="GetRevisionsForLocaleAsync"/>.
	/// </summary>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20);
```

Append the five new methods at the end of the interface, before the closing brace:

```csharp

	/// <summary>
	/// Lists every translation of a page as a bodyless summary, including unpublished drafts.
	/// Visibility filtering is the caller's responsibility — see <c>IWikiLocalizationService</c>.
	/// </summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId);

	/// <summary>
	/// Retrieves one translation by its <c>(pageId, locale)</c> identity. <paramref name="locale"/> is
	/// matched case-insensitively after normalisation.
	/// Returns <c>NotFound</c> when no translation exists for that locale.
	/// </summary>
	Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale);

	/// <summary>
	/// Creates or updates a translation. Mirrors <see cref="UpdateAsync"/>: bumps the per-locale
	/// <c>RevisionNumber</c>, writes a <see cref="WikiRevision"/> carrying the locale, and re-renders
	/// HTML and plain text through the same <c>WikiMarkdigPipeline</c>.
	/// Returns <c>Error&lt;string&gt;</c> when the page does not exist, when
	/// <paramref name="locale"/> is unparseable, when it would shadow the page's own
	/// <c>SourceLocale</c>, when a concurrent write loses the unique-index race, or when
	/// <paramref name="expectedRevisionNumber"/> does not match what is stored.
	/// </summary>
	/// <param name="expectedRevisionNumber">
	/// The <c>RevisionNumber</c> the caller loaded, making this a compare-and-swap. The update applies only
	/// if the stored value still matches, and the revision append happens in the same transaction as the row
	/// update (or the update is made conditional and "zero rows affected" is the conflict signal).
	/// <para>
	/// <see langword="null"/> means <em>create-only</em>: an existing translation is an
	/// <c>Error&lt;string&gt;</c> rather than a blind overwrite.
	/// </para>
	/// <para>
	/// A conflict is <b>never</b> retried automatically. Retrying re-applies the loser's stale markdown on
	/// top of the winner's, which is exactly the data loss this parameter exists to prevent — the editor
	/// reloads and the human decides. The one automatic retry in this contract belongs to the insert race on
	/// <c>(pageId, locale)</c>, where no content can be lost.
	/// </para>
	/// </param>
	Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId,
		string locale,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber);

	/// <summary>
	/// Deletes one translation and its revision stream, leaving the page and every other translation
	/// alone. Deleting the last translation is allowed.
	/// Returns <c>None</c> on success; <c>NotFound</c> when that locale has no translation.
	/// </summary>
	Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref);

	/// <summary>
	/// Returns the revision history for one <c>(pageId, locale)</c> stream, newest first.
	/// Pass <see cref="string.Empty"/> for the source-locale stream, which is what
	/// <see cref="GetRevisionsAsync"/> returns.
	/// </summary>
	/// <remarks>
	/// A distinct name rather than an overload of <see cref="GetRevisionsAsync"/>: an overload differing
	/// only by an inserted <c>string</c> invites a silent mis-bind at a call site that passes positional
	/// ints, and the compiler would not complain.
	/// </remarks>
	Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take);
```

- [ ] **Step 4: Implement in `InMemoryWikiService`**

In `SharpMUSH.Library/Services/InMemoryWikiService.cs`:

Add the storage field after `_revisions` (line 22):

```csharp
	private readonly ConcurrentDictionary<(string PageId, string Locale), WikiTranslation> _translations = new();
```

Replace the stale class-header remark (lines 11–12, "An ArangoDB-backed implementation will replace the persistence layer in a later phase") — three DB backends now exist:

```csharp
/// Intended for unit tests and development use; the three database providers implement the same
/// contract for production.
```

Change `CreateAsync`'s signature and stamp the locale. Signature (line 122–127):

```csharp
	public Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null)
```

Reject an unparseable `sourceLocale` before anything is written — put this beside the existing
duplicate-identity check:

```csharp
		// SourceLocale is materialised once and never re-derived, so a junk tag must not reach storage.
		// Null or blank is the "not stamped" case, left to the migration backfill rather than an error;
		// a non-blank tag that is not a locale is an error, because storing it would corrupt every later read.
		var stampedLocale = string.Empty;
		if (!string.IsNullOrWhiteSpace(sourceLocale))
		{
			var normalizedSource = WikiHelpers.NormalizeLocale(sourceLocale);
			if (normalizedSource.IsT1)
				return Task.FromResult<OneOf<WikiPage, Error<string>>>(normalizedSource.AsT1);

			stampedLocale = normalizedSource.AsT0;
		}
```

and in the `new WikiPage(...)` initializer (line 152–154):

```csharp
		{
			Category = cat,
			SourceLocale = stampedLocale,
		};
```

Extend `DeleteAsync` (after line 207, `_revisions.TryRemove(id, out _);`) to cascade:

```csharp
		foreach (var key in _translations.Keys.Where(k => k.PageId == id).ToList())
			_translations.TryRemove(key, out _);
```

(The single `_revisions[id]` list already holds both source and translation revisions, so removing it clears both streams.)

Change the `GetRevisionsAsync` ordering block (in `InMemoryWikiService`, the `result = list` chain) to filter to the source stream:

```csharp
			result = list
				.Where(r => r.Locale.Length == 0)
				.OrderByDescending(r => r.RevisionNumber)
				.Skip(skip)
				.Take(take)
				.ToList();
```

Add a locale-aware overload of `SaveRevisionSnapshot` and the five methods, before the closing brace:

```csharp

	// ---- Translations -------------------------------------------------------

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		IReadOnlyList<WikiTranslationSummary> result = _translations
			.Where(kv => kv.Key.PageId == pageId)
			.Select(kv => new WikiTranslationSummary(
				kv.Value.Locale, kv.Value.Title, kv.Value.Published, kv.Value.UpdatedAt, kv.Value.RevisionNumber))
			.OrderBy(s => s.Locale, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return Task.FromResult(result);
	}

	public Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var key = TranslationKey(pageId, locale);
		if (key is not null && _translations.TryGetValue(key.Value, out var translation))
			return Task.FromResult<OneOf<WikiTranslation, NotFound>>(translation);
		return Task.FromResult<OneOf<WikiTranslation, NotFound>>(new NotFound());
	}

	public Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId,
		string locale,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber)
	{
		var normalizedLocale = WikiHelpers.NormalizeLocale(locale);
		if (normalizedLocale.IsT1)
			return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(normalizedLocale.AsT1);

		var normalized = normalizedLocale.AsT0;

		if (!_pagesById.TryGetValue(pageId, out var page))
			return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
				new Error<string>($"No wiki page with id '{pageId}'."));

		if (page.SourceLocale.Length > 0
			&& string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
				new Error<string>(
					$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation."));

		var key = (PageId: pageId, Locale: normalized);
		var now = DateTimeOffset.UtcNow;
		var html = _renderer.RenderToHtml(markdown);
		var plain = _renderer.ExtractPlainText(markdown);

		// Compare-and-swap, not AddOrUpdate. AddOrUpdate would happily fold two writers that both loaded
		// revision 4 into one revision 5 and lose one translator's prose.
		WikiTranslation updated;
		if (expectedRevisionNumber is null)
		{
			updated = new WikiTranslation(
				Id: $"{pageId}:{normalized}",
				PageId: pageId,
				Locale: normalized,
				Title: title,
				MarkdownSource: markdown,
				RenderedHtml: html,
				PlainText: plain,
				LastEditorDbref: editorDbref,
				CreatedAt: now,
				UpdatedAt: now,
				Published: published,
				RevisionNumber: 1);

			// Create-only: an existing row is a conflict, not something to overwrite.
			if (!_translations.TryAdd(key, updated))
				return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
					new Error<string>(
						$"A '{normalized}' translation already exists for page '{pageId}'. "
						+ "Pass its current revision number to update it."));
		}
		else
		{
			if (!_translations.TryGetValue(key, out var existing))
				return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
					new Error<string>($"No '{normalized}' translation exists for page '{pageId}' to update."));

			if (existing.RevisionNumber != expectedRevisionNumber.Value)
				return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
					new Error<string>(
						$"The '{normalized}' translation changed while you were editing "
						+ $"(expected revision {expectedRevisionNumber.Value}, found {existing.RevisionNumber}). "
						+ "Reload and re-apply your changes."));

			updated = existing with
			{
				Title = title,
				MarkdownSource = markdown,
				RenderedHtml = html,
				PlainText = plain,
				LastEditorDbref = editorDbref,
				UpdatedAt = now,
				Published = published,
				RevisionNumber = existing.RevisionNumber + 1,
			};

			// TryUpdate's comparison value is the CAS: a writer who won the race between TryGetValue and
			// here has already replaced `existing`, so this fails and no revision is appended.
			if (!_translations.TryUpdate(key, updated, existing))
				return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(
					new Error<string>(
						$"The '{normalized}' translation changed while you were editing. "
						+ "Reload and re-apply your changes."));
		}

		SaveTranslationRevisionSnapshot(updated, editorDbref, editSummary);

		return Task.FromResult<OneOf<WikiTranslation, Error<string>>>(updated);
	}

	public Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var key = TranslationKey(pageId, locale);
		if (key is null || !_translations.TryRemove(key.Value, out var removed))
			return Task.FromResult<OneOf<None, NotFound>>(new NotFound());

		if (_revisions.TryGetValue(pageId, out var list))
		{
			lock (list)
			{
				list.RemoveAll(r => string.Equals(r.Locale, removed.Locale, StringComparison.OrdinalIgnoreCase));
			}
		}

		return Task.FromResult<OneOf<None, NotFound>>(new None());
	}

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take)
	{
		if (!_revisions.TryGetValue(pageId, out var list))
			return Task.FromResult<IReadOnlyList<WikiRevision>>([]);

		// Empty means the source-locale stream; anything else is matched after normalisation. This is a
		// read, so an unusable tag yields "no such stream" rather than an error.
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		IReadOnlyList<WikiRevision> result;
		lock (list)
		{
			result = list
				.Where(r => string.Equals(r.Locale, wanted, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(r => r.RevisionNumber)
				.Skip(skip)
				.Take(take)
				.ToList();
		}
		return Task.FromResult(result);
	}

	/// <summary>The dictionary key for a translation, or null when the locale tag is unusable.</summary>
	private static (string PageId, string Locale)? TranslationKey(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		return normalized.Length == 0 ? null : (pageId, normalized);
	}

	/// <summary>
	/// Appends a full snapshot revision for a translation. The id carries the locale so a translation
	/// revision can never collide with the source page's <c>{PageId}:{RevisionNumber}</c> keys.
	/// </summary>
	private void SaveTranslationRevisionSnapshot(WikiTranslation translation, string editorDbref, string? editSummary)
	{
		var revList = _revisions.GetOrAdd(translation.PageId, _ => []);
		var rev = new WikiRevision(
			Id: $"{translation.PageId}:{translation.Locale}:{translation.RevisionNumber}",
			PageId: translation.PageId,
			RevisionNumber: translation.RevisionNumber,
			MarkdownSource: translation.MarkdownSource,
			EditorDbref: editorDbref,
			Timestamp: translation.UpdatedAt,
			EditSummary: editSummary)
		{
			Locale = translation.Locale,
		};

		lock (revList)
		{
			revList.Add(rev);
		}
	}
```

`_translations` is keyed on a value tuple, whose default comparer is ordinal, so normalise before every lookup — `TranslationKey` is the only way in.

- [ ] **Step 5: Add temporary stubs to the three DB providers**

These exist solely so the solution compiles and Task 6's test can be written red. Each is deleted by its own backend task.

`SharpMUSH.Database.ArangoDB/ArangoDatabase.Wiki.cs` — change `CreateAsync`'s signature to add `string? sourceLocale = null`, add the same "reject an unparseable tag, then stamp" block as `InMemoryWikiService` and put `SourceLocale = stampedLocale` in the anonymous `doc` (line ~205, after `Published = true`), then append inside the `#region Wiki`, before `#endregion`:

```csharp

	// ---- Translations (Task 7 replaces these) -------------------------------

	private static NotSupportedException WikiTranslationsNotImplemented(string provider) =>
		new($"Wiki translations are not yet implemented for the {provider} provider.");

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId) =>
		throw WikiTranslationsNotImplemented("ArangoDB");

	public Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale) =>
		throw WikiTranslationsNotImplemented("ArangoDB");

	public Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId, string locale, string title, string markdown,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber) =>
		throw WikiTranslationsNotImplemented("ArangoDB");

	public Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref) =>
		throw WikiTranslationsNotImplemented("ArangoDB");

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
		string pageId, string locale, int skip, int take) =>
		throw WikiTranslationsNotImplemented("ArangoDB");
```

`SharpMUSH.Database.Memgraph/MemgraphDatabase.Wiki.cs` — the same block with `"Memgraph"` in place of `"ArangoDB"`, plus `CreateAsync` gaining `string? sourceLocale = null`, the same reject-then-stamp block, and `sourceLocale = stampedLocale` added to the `CREATE (p:WikiPage {...})` property map and its parameter object.

`SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs` — the same block with `"SurrealDB"`, **indented with 4 spaces**, and note this file's `None` alias: the delete stub returns `Task<OneOf<OkNone, NotFound>>`. Also add `sourceLocale` to `WikiPageFields`, to `WikiPageDbRecord` (as `public string? sourceLocale { get; set; }`), to `MapToWikiPage`, and to `CreateAsync`'s `CONTENT { … }`.

For all three: the `WikiPage` deserialisers (`WikiPageFromJson`, `NodeToWikiPage`, `MapToWikiPage`) must read `SourceLocale` defensively, yielding `string.Empty` for a stored document predating the field. That empty value means "not yet stamped" and is closed by the Tasks 7–9 backfill; **no deserialiser and no reader substitutes `Wiki.DefaultLocale` for it.**

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 errors.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/InMemoryWikiServiceTests/*"`
Expected: PASS, including all 19 new tests (15 CRUD + the two compare-and-swap cases + the two `CreateAsync` locale cases).

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed. If a Wiki controller or command test now fails, `GetRevisionsAsync`'s new source-stream filter is the first suspect — it must be a no-op today because every existing revision row has `Locale == ""`.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Library SharpMUSH.Database.ArangoDB SharpMUSH.Database.Memgraph \
  SharpMUSH.Database.SurrealDB SharpMUSH.Tests/Wiki/InMemoryWikiServiceTests.cs
git commit -m "feat(wiki): add translation CRUD to IWikiService with the in-memory reference implementation"
```

---

### Task 6: Cross-backend integration test — written *before* the three backends

This is the spec's named mitigation for the three-hand-written-backends risk. It goes red on all three providers (`NotSupportedException` from the Task 5 stubs) and turns green one provider at a time across Tasks 7–9.

There is no per-test parameterisation over backends in this codebase: the provider is chosen by the `SHARPMUSH_DATABASE_PROVIDER` environment variable and CI runs the whole assembly three times. One test class is therefore all three backends' contract.

**The negative cases are the point of this file, not a bonus.** The three stores' revision indexes disagree today — unique on SurrealDB, non-unique on ArangoDB, absent on Memgraph — and *a test that only writes valid data cannot tell a real constraint from a missing one.* That is precisely how they drifted apart. So this class must assert two things no happy-path test can:

- a translation revision numbered 1 is **accepted** alongside a source revision numbered 1 (today's SurrealDB `wiki_revision_page_rev` UNIQUE index rejects this);
- a duplicate `(PageId, Locale, RevisionNumber)` is **rejected** — exactly one of two writers claiming the same next revision number succeeds, and the loser leaves no revision row behind (a store with no constraint and a read-then-write upsert produces two rows numbered the same and fails here).

Both are expressed through the public `IWikiService` surface rather than raw provider queries, because a cross-backend file cannot hand-write AQL, Cypher and SurrealQL. That is sufficient: whether the loser is stopped by the unique index or by the conditional update's "zero rows affected" is a backend's choice, and the observable outcome is what the spec pins down.

Two conventions this file must follow or it will be flaky: the integration DB is shared across the whole test session and never reset, so **every title is uniquified with `Guid.NewGuid().ToString("N")[..8]`**, and counts use `IsGreaterThanOrEqualTo` unless scoped to a page this test created.

**Files:**
- Create: `SharpMUSH.Tests.Integration/Wiki/WikiTranslationIntegrationTests.cs` (**4-space indentation**, matching its siblings)

**Interfaces:**
- Consumes: every method from Task 5; `ServerWebAppFactory` (`SharpMUSH.Tests` namespace, `[ClassDataSource(Shared = SharedType.PerTestSession)]`).
- Produces: nothing consumed by later code — it is the acceptance gate for Tasks 7–9.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.Integration/Wiki/WikiTranslationIntegrationTests.cs` (4 spaces):

```csharp
using Microsoft.Extensions.DependencyInjection;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// The translation overlay's CRUD and index semantics against the configured DB backend. The backend is
/// selected by <c>SHARPMUSH_DATABASE_PROVIDER</c> (arangodb / memgraph / surrealdb) and CI runs this
/// assembly once per provider, so this one class is all three providers' contract.
///
/// Written deliberately before the three hand-written backend implementations: the five CRUD methods are
/// mechanical, but the existing revision indexes differ per store — unique on SurrealDB, non-unique on
/// ArangoDB, absent on Memgraph — and this file is what catches that.
///
/// The <b>negative</b> cases at the bottom carry the weight. A suite that only writes valid data cannot
/// distinguish a real unique constraint from a missing one, which is exactly how these three drifted
/// apart, so "rejects a duplicate (PageId, Locale, RevisionNumber)" and "accepts a translation revision 1
/// beside a source revision 1" are asserted explicitly.
///
/// The session database is shared and never reset, so every page title is uniquified.
/// </summary>
[NotInParallel]
public class WikiTranslationIntegrationTests
{
    [ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
    public required ServerWebAppFactory WebAppFactory { get; init; }

    private IWikiService Wiki => WebAppFactory.Services.GetRequiredService<ISharpDatabase>() as IWikiService
        ?? throw new InvalidOperationException("ISharpDatabase does not implement IWikiService in this configuration.");

    /// <summary>Creates a uniquely-named English source page and returns it.</summary>
    private async Task<WikiPage> CreateSourcePageAsync(string label, string sourceLocale = "en")
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var result = await Wiki.CreateAsync(
            $"{label} {uid}", "en **body**", "#1", WikiNamespace.Main, "general", sourceLocale);
        await Assert.That(result.IsT0).IsTrue();
        return result.AsT0;
    }

    [Test]
    public async Task CreateAsync_PersistsSourceLocale()
    {
        var page = await CreateSourcePageAsync("SrcLocale", sourceLocale: "fr-CA");

        var reread = await Wiki.GetBySlugAsync(page.Slug, page.Category, WikiNamespace.Main);

        await Assert.That(reread.IsT0).IsTrue();
        await Assert.That(reread.AsT0.SourceLocale)
            .IsEqualTo("fr-CA")
            .Because("SourceLocale must round-trip through the provider's serializer");
    }

    [Test]
    public async Task UpsertTranslationAsync_RoundTripsThroughTheProvider()
    {
        var page = await CreateSourcePageAsync("Upsert");

        var created = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "Titre fr", "corps **fr**", "#2", "première",
            published: true, expectedRevisionNumber: null);

        await Assert.That(created.IsT0).IsTrue();
        var fetched = await Wiki.GetTranslationAsync(page.Id, "fr");
        await Assert.That(fetched.IsT0).IsTrue();
        await Assert.That(fetched.AsT0.Title).IsEqualTo("Titre fr");
        await Assert.That(fetched.AsT0.MarkdownSource).IsEqualTo("corps **fr**");
        await Assert.That(fetched.AsT0.RenderedHtml).Contains("<strong>fr</strong>");
        await Assert.That(fetched.AsT0.RevisionNumber).IsEqualTo(1);
        await Assert.That(fetched.AsT0.Published).IsTrue();
    }

    [Test]
    public async Task UpsertTranslationAsync_IsAnUpsertNotAnInsert()
    {
        // The (PageId, Locale) unique index is what this asserts. A provider whose index is missing or
        // non-unique will produce two rows here and fail on the count, which is the whole point of
        // running this file against every store.
        var page = await CreateSourcePageAsync("UpsertTwice");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

        var second = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "v2", "corps v2", "#3", "révision", true, expectedRevisionNumber: 1);

        await Assert.That(second.IsT0).IsTrue();
        await Assert.That(second.AsT0.RevisionNumber).IsEqualTo(2);
        await Assert.That(second.AsT0.MarkdownSource).IsEqualTo("corps v2");
        var summaries = await Wiki.GetTranslationsAsync(page.Id);
        await Assert.That(summaries.Count).IsEqualTo(1);
    }

    [Test]
    public async Task UpsertTranslationAsync_TwoLocalesOnOnePageAreDistinctRows()
    {
        var page = await CreateSourcePageAsync("TwoLocales");

        await Wiki.UpsertTranslationAsync(page.Id, "fr", "Titre fr", "fr", "#2", null, true, expectedRevisionNumber: null);
        await Wiki.UpsertTranslationAsync(page.Id, "de", "Titel de", "de", "#2", null, true, expectedRevisionNumber: null);

        var summaries = await Wiki.GetTranslationsAsync(page.Id);

        await Assert.That(summaries.Count).IsEqualTo(2);
        await Assert.That(summaries.Select(s => s.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
    }

    [Test]
    public async Task UpsertTranslationAsync_SameLocaleOnTwoPagesAreDistinctRows()
    {
        var first = await CreateSourcePageAsync("SameLocaleA");
        var second = await CreateSourcePageAsync("SameLocaleB");

        await Wiki.UpsertTranslationAsync(first.Id, "fr", "A fr", "a", "#2", null, true, expectedRevisionNumber: null);
        await Wiki.UpsertTranslationAsync(second.Id, "fr", "B fr", "b", "#2", null, true, expectedRevisionNumber: null);

        await Assert.That((await Wiki.GetTranslationAsync(first.Id, "fr")).AsT0.Title).IsEqualTo("A fr");
        await Assert.That((await Wiki.GetTranslationAsync(second.Id, "fr")).AsT0.Title)
            .IsEqualTo("B fr")
            .Because("the unique index is on (PageId, Locale), not on Locale alone");
    }

    [Test]
    public async Task UpsertTranslationAsync_NormalisesTheLocaleAndFindsItByEitherCase()
    {
        var page = await CreateSourcePageAsync("LocaleCase");

        var created = await Wiki.UpsertTranslationAsync(page.Id, "FR-ca", "T", "m", "#2", null, true, expectedRevisionNumber: null);

        await Assert.That(created.AsT0.Locale).IsEqualTo("fr-CA");
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr-ca")).IsT0).IsTrue();
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "FR-CA")).IsT0).IsTrue();
    }

    [Test]
    public async Task UpsertTranslationAsync_RejectsShadowingTheSourceLocale()
    {
        var page = await CreateSourcePageAsync("Shadow", sourceLocale: "en");

        var result = await Wiki.UpsertTranslationAsync(page.Id, "en", "T", "m", "#2", null, true, expectedRevisionNumber: null);

        await Assert.That(result.IsT1).IsTrue();
        await Assert.That(result.AsT1).IsTypeOf<Error<string>>();
    }

    [Test]
    public async Task UpsertTranslationAsync_RejectsAnUnknownPage()
    {
        var ghost = $"node_wiki_pages/ghost_{Guid.NewGuid():N}";

        var result = await Wiki.UpsertTranslationAsync(ghost, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

        await Assert.That(result.IsT1).IsTrue();
    }

    [Test]
    public async Task GetTranslationAsync_ReturnsNotFoundForAMissingLocale()
    {
        var page = await CreateSourcePageAsync("MissingLocale");

        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "de")).IsT1).IsTrue();
    }

    [Test]
    public async Task GetTranslationsAsync_IncludesUnpublishedDrafts()
    {
        var page = await CreateSourcePageAsync("DraftListing");
        await Wiki.UpsertTranslationAsync(page.Id, "de", "Entwurf", "m", "#2", null, published: false, expectedRevisionNumber: null);

        var summaries = await Wiki.GetTranslationsAsync(page.Id);

        await Assert.That(summaries.Single().Published)
            .IsFalse()
            .Because("storage returns every row; visibility filtering happens above the DB layer");
    }

    [Test]
    public async Task GetRevisionsForLocaleAsync_IsASeparateStreamFromTheSource()
    {
        var page = await CreateSourcePageAsync("RevStreams");
        await Wiki.UpdateAsync(page.Id, "en v2", "#1");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

        var french = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
        var source = await Wiki.GetRevisionsAsync(page.Id);

        await Assert.That(french.Count).IsEqualTo(2);
        await Assert.That(french.All(r => r.Locale == "fr")).IsTrue();
        await Assert.That(french.Select(r => r.MarkdownSource)).Contains("fr2");
        await Assert.That(source.Count).IsEqualTo(2);
        await Assert.That(source.All(r => r.Locale.Length == 0))
            .IsTrue()
            .Because("GetRevisionsAsync must stay the source-locale stream for its existing callers");
    }

    [Test]
    public async Task DeleteTranslationAsync_RemovesOnlyThatLocale()
    {
        var page = await CreateSourcePageAsync("DeleteOne");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
        await Wiki.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

        var deleted = await Wiki.DeleteTranslationAsync(page.Id, "fr", "#2");

        await Assert.That(deleted.IsT0).IsTrue();
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).IsT1).IsTrue();
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "de")).IsT0).IsTrue();
        await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
        await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "de", 0, 20)).Count).IsEqualTo(1);
    }

    [Test]
    public async Task DeleteTranslationAsync_ReturnsNotFoundForAMissingLocale()
    {
        var page = await CreateSourcePageAsync("DeleteMissing");

        await Assert.That((await Wiki.DeleteTranslationAsync(page.Id, "fr", "#2")).IsT1).IsTrue();
    }

    [Test]
    public async Task DeleteAsync_CascadesToTranslationsAndTheirRevisions()
    {
        var page = await CreateSourcePageAsync("Cascade");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);
        await Wiki.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, true, expectedRevisionNumber: null);

        await Wiki.DeleteAsync(page.Id, "#1");

        await Assert.That((await Wiki.GetTranslationsAsync(page.Id)).Count).IsEqualTo(0);
        await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20)).Count).IsEqualTo(0);
        await Assert.That((await Wiki.GetRevisionsForLocaleAsync(page.Id, "de", 0, 20)).Count).IsEqualTo(0);
        await Assert.That((await Wiki.GetRevisionsAsync(page.Id)).Count).IsEqualTo(0);
    }

    // ---- Negative cases: the revision constraint itself ---------------------
    //
    // Everything above would pass on a store with no revision constraint at all. These four will not.

    [Test]
    public async Task RevisionIndex_AcceptsATranslationRevisionOneBesideASourceRevisionOne()
    {
        // (PageId, RevisionNumber) is NOT unique any more: a translation's stream restarts at 1 while the
        // source page already has a revision 1. SurrealDB's pre-existing wiki_revision_page_rev UNIQUE
        // index rejects this outright, which is the whole reason Task 9 must redefine it.
        var page = await CreateSourcePageAsync("RevOneTwice");

        var created = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "Titre fr", "corps fr", "#2", null, true, expectedRevisionNumber: null);

        await Assert.That(created.IsT0)
            .IsTrue()
            .Because("a translation revision 1 must coexist with the source's revision 1");
        var french = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
        var source = await Wiki.GetRevisionsAsync(page.Id);
        await Assert.That(french.Single().RevisionNumber).IsEqualTo(1);
        await Assert.That(source.Single().RevisionNumber).IsEqualTo(1);
        await Assert.That(source.Single().Locale)
            .IsEqualTo(string.Empty)
            .Because("the two rows are distinguished by Locale, which is why it is in the constraint");
    }

    [Test]
    public async Task RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber()
    {
        // Two writers both loaded revision 1 and both compute revision 2. Exactly one may land. A store
        // with no constraint and a read-then-write upsert writes both and fails the count below, which is
        // the assertion that tells a real constraint from a missing one.
        var page = await CreateSourcePageAsync("DupRevision");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

        var winner = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "v2", "corps v2", "#3", null, true, expectedRevisionNumber: 1);
        var loser = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "perdu", "corps perdu", "#4", null, true, expectedRevisionNumber: 1);

        await Assert.That(winner.IsT0).IsTrue();
        await Assert.That(loser.IsT1)
            .IsTrue()
            .Because("a second revision 2 for (PageId, Locale) must be refused, never silently accepted");
        await Assert.That(loser.AsT1).IsTypeOf<Error<string>>();

        var revisions = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
        await Assert.That(revisions.Count(r => r.RevisionNumber == 2))
            .IsEqualTo(1)
            .Because("two rows numbered 2 is the exact corruption the unique constraint exists to stop");
        await Assert.That(revisions.Select(r => r.MarkdownSource))
            .DoesNotContain("corps perdu")
            .Because("a rejected write must leave no revision behind");
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
            .IsEqualTo("corps v2");
    }

    [Test]
    public async Task UpsertTranslationAsync_CreateOnlyRefusesAnExistingTranslation()
    {
        var page = await CreateSourcePageAsync("CreateOnly");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

        var again = await Wiki.UpsertTranslationAsync(
            page.Id, "fr", "écrasé", "corps écrasé", "#3", null, true, expectedRevisionNumber: null);

        await Assert.That(again.IsT1).IsTrue();
        await Assert.That((await Wiki.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
            .IsEqualTo("corps v1");
    }

    [Test]
    public async Task ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse()
    {
        // The spec's concurrency case. Needs a real backend: the in-memory dictionary cannot reproduce the
        // race. Whichever ordering the store picks, exactly one writer wins, the other gets Error<string>,
        // and the loser's markdown appears in no revision.
        var page = await CreateSourcePageAsync("Concurrent");
        await Wiki.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

        var results = await Task.WhenAll(
            Wiki.UpsertTranslationAsync(page.Id, "fr", "A", "corps a", "#2", null, true, expectedRevisionNumber: 1),
            Wiki.UpsertTranslationAsync(page.Id, "fr", "B", "corps b", "#3", null, true, expectedRevisionNumber: 1));

        await Assert.That(results.Count(r => r.IsT0))
            .IsEqualTo(1)
            .Because("exactly one compare-and-swap on the same expected revision may succeed");
        await Assert.That(results.Count(r => r.IsT1)).IsEqualTo(1);

        var revisions = await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr", 0, 20);
        await Assert.That(revisions.Count).IsEqualTo(2);
        var winnerMarkdown = results.Single(r => r.IsT0).AsT0.MarkdownSource;
        var loserMarkdown = winnerMarkdown == "corps a" ? "corps b" : "corps a";
        await Assert.That(revisions.Select(r => r.MarkdownSource))
            .DoesNotContain(loserMarkdown)
            .Because("the loser is never retried, so its prose must not reach the store at all");
        await Assert.That((await Wiki.GetTranslationsAsync(page.Id)).Count).IsEqualTo(1);
    }
}
```

- [ ] **Step 2: Verify it compiles and is red for the right reason**

Run: `dotnet build SharpMUSH.Tests.Integration`
Expected: 0 errors. The file compiles against the Task 5 stubs.

**Run this suite locally — it works under Podman.** Its red state at the end of this task is the `NotSupportedException` the Task 5 stubs throw, and you should *see* that rather than assume it: run `SHARPMUSH_DATABASE_PROVIDER=arangodb dotnet run --project SharpMUSH.Tests.Integration -- --treenode-filter "/*/*/WikiTranslationIntegrationTests/*"` and confirm the failures name `WikiTranslationsNotImplemented`. A test that fails for the wrong reason looks identical to one that fails for the right reason, and this is the task where that distinction decides whether Phase 2 means anything.

`ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse` is the one assertion whose value depends on a real store: the in-memory dictionary cannot reproduce the race, so it proves nothing against `InMemoryWikiService` — but it does run for real against each of the three providers.

- [ ] **Step 3: Commit**

```bash
git add SharpMUSH.Tests.Integration/Wiki/WikiTranslationIntegrationTests.cs
git commit -m "test(wiki): add the cross-backend translation contract ahead of the backend implementations"
```

- [ ] **Step 4: Record the acceptance gate**

Note in the PR description that CI job `test-integration` will fail on all three matrix entries until Task 9 lands, and that this is intended. Tasks 7, 8 and 9 each flip one entry green.

---

### Task 7: ArangoDB translation storage

`Migration_AddWiki.Up` guards its whole index block behind `if (!await …ExistAsync(WikiPages))`, so on any existing database that migration is a no-op — a **new** migration file is mandatory. Migrations are auto-discovered by assembly scan (`migrator.AddMigrations(typeof(ArangoDatabase).Assembly)` in `ArangoDatabase.Migration.cs:83`), ordered by `Id`, and tracked in the `MigrationHistory` collection. The highest existing engine `Id` is `20260714_001` (`add_sessions`), so the new one is `20260726_001`.

This task carries **three** things beyond the new collection, and their order inside `Up` is load-bearing:

1. the idempotent backfill that stamps `WikiPage.SourceLocale` and `WikiRevision.Locale`;
2. **then** the unique constraint on `(PageId, Locale, RevisionNumber)` — the deployed Arango index is non-unique (`Migration_AddWiki.cs:101-105`), which is why a numbering bug passes silently on this backend while failing loudly on SurrealDB;
3. the compare-and-swap in `UpsertTranslationAsync`.

**Files:**
- Modify: `SharpMUSH.Database/DatabaseConstants.cs:25` (add `WikiTranslations` after `WikiRevisions`)
- Create: `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWikiTranslations.cs` (collection, indexes, **backfill**, unique revision constraint)
- Modify: `SharpMUSH.Database.ArangoDB/ArangoDatabase.Wiki.cs` (replace the Task 5 stubs; extend `DeleteAsync`; filter `GetRevisionsAsync`; add `WikiTranslationFromJson`)

**Interfaces:**
- Consumes: the Task 5 contract; `WikiHelpers.NormalizeLocale` / `NormalizeLocaleOrEmpty`; `WikiOptions.DefaultLocaleFallback` (Task 1); `ExtractKey` (`ArangoDatabase.Accounts.cs:238`).
- Produces:
  - `DatabaseConstants.WikiTranslations` = `"node_wiki_translations"`
  - `Migration_AddWikiTranslations` — `Id => 20260726_001`, `Name => "add_wiki_translations"`
  - Index `wiki_revision_page_locale_rev` on `node_wiki_revisions`, `UNIQUE (PageId, Locale, RevisionNumber)`
  - `ArangoDatabase.WikiTranslationFromJson(JsonElement)` → `WikiTranslation` (private)

- [ ] **Step 1: Add the collection constant**

`SharpMUSH.Database/DatabaseConstants.cs`, after line 25:

```csharp
	public const string WikiTranslations = "node_wiki_translations";
```

- [ ] **Step 2: Write the migration**

Create `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWikiTranslations.cs` (tabs; modelled on `Migration_AddWiki.cs`):

```csharp
using Core.Arango;
using Core.Arango.Migration;
using Core.Arango.Protocol;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Database.ArangoDB.Migrations;

/// <summary>
/// Adds <c>node_wiki_translations</c> — per-locale overlay rows hanging off a wiki page — backfills
/// <c>WikiPage.SourceLocale</c> and <c>WikiRevision.Locale</c>, and replaces the revision index with a
/// unique one over <c>(PageId, Locale, RevisionNumber)</c>.
/// </summary>
/// <remarks>
/// A separate migration rather than an edit to <see cref="Migration_AddWiki"/>: that migration guards
/// its entire index block behind a collection-existence check, so on any database created before today
/// an edit there would silently never run.
/// <para>
/// The backfill must run <b>before</b> the unique index is created, or creation fails on rows whose
/// <c>Locale</c> is null.
/// </para>
/// </remarks>
public class Migration_AddWikiTranslations : IArangoMigration
{
	public long Id => 20260726_001;

	public string Name => "add_wiki_translations";

	public async Task Up(IArangoMigrator migrator, ArangoHandle handle)
	{
		if (!await migrator.Context.Collection.ExistAsync(handle, DatabaseConstants.WikiTranslations))
		{
			await migrator.Context.Collection.CreateAsync(handle, new ArangoCollection
			{
				Name = DatabaseConstants.WikiTranslations,
				Type = ArangoCollectionType.Document,
				WaitForSync = true,
				Schema = new ArangoSchema
				{
					Rule = new
					{
						type = DatabaseConstants.TypeObject,
						properties = new
						{
							PageId = new { type = DatabaseConstants.TypeString },
							Locale = new { type = DatabaseConstants.TypeString },
							Title = new { type = DatabaseConstants.TypeString },
							MarkdownSource = new { type = DatabaseConstants.TypeString },
							RenderedHtml = new { type = DatabaseConstants.TypeString },
							PlainText = new { type = DatabaseConstants.TypeString },
							LastEditorDbref = new { type = DatabaseConstants.TypeString },
							Published = new { type = DatabaseConstants.TypeBoolean },
							RevisionNumber = new { type = DatabaseConstants.TypeNumber }
						},
						required = (string[])["PageId", "Locale", "Title", "MarkdownSource"],
						additionalProperties = true
					}
				}
			});

			// One translation per (page, locale). This is the constraint that makes
			// UpsertTranslationAsync an upsert rather than an append.
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiTranslations, new ArangoIndex
			{
				Fields = ["PageId", "Locale"],
				Unique = true,
				Type = ArangoIndexType.Persistent
			});

			// Non-unique, for "which pages have a French translation?" listings and admin coverage.
			await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiTranslations, new ArangoIndex
			{
				Fields = ["Locale"],
				Type = ArangoIndexType.Persistent
			});
		}

		// ---- Backfill, BEFORE the unique index ------------------------------
		//
		// Order matters: a unique index over (PageId, Locale, RevisionNumber) cannot be created while
		// pre-existing revision rows have no Locale at all.

		// Every page that predates the field is stamped once. After this the value is authoritative and
		// immutable per page; nothing re-derives it on read, because an admin later changing
		// wiki_default_locale must not relabel the authored locale of pages that already exist.
		var stampedPages = await migrator.Context.Query.ExecuteAsync<string>(handle,
			"""
			FOR p IN @@c
				FILTER p.SourceLocale == null OR p.SourceLocale == ""
				UPDATE p WITH { SourceLocale: @locale } IN @@c
				RETURN NEW._key
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiPages },
				{ "locale", WikiOptions.DefaultLocaleFallback }
			});

		// Pre-existing revisions are all source-locale revisions, and the source stream's marker is the
		// empty string (Task 5, convention 1) — NOT the default locale. Stamping it explicitly rather than
		// leaving null is what lets the unique index cover the column.
		var stampedRevisions = await migrator.Context.Query.ExecuteAsync<string>(handle,
			"""
			FOR r IN @@c
				FILTER r.Locale == null
				UPDATE r WITH { Locale: "" } IN @@c
				RETURN NEW._key
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions }
			});

		// The migration logs the locale it stamped and the row counts. That is the whole mitigation: there
		// is deliberately no rollback path, no language detection and no per-page override, because
		// SharpMUSH is pre-production and wiping + reseeding is acceptable recovery. Revisit only if a live
		// game with existing wiki content ever adopts SharpMUSH.
		Console.WriteLine(
			$"[{Name}] stamped SourceLocale='{WikiOptions.DefaultLocaleFallback}' on "
			+ $"{stampedPages.Count} page(s); stamped Locale='' on "
			+ $"{stampedRevisions.Count} revision(s).");

		// ---- Revision constraint --------------------------------------------
		//
		// The deployed index is Fields = ["PageId", "RevisionNumber"], Persistent, NOT unique
		// (Migration_AddWiki.cs:101-105). Translation revisions restart numbering at 1, so that pair is no
		// longer unique — but a *non*-unique index means a numbering bug passes silently here while failing
		// loudly on SurrealDB. Both halves matter: add the unique three-field index, then drop the old pair
		// so nothing keeps writing against a constraint-free lookup.
		await migrator.Context.Index.CreateAsync(handle, DatabaseConstants.WikiRevisions, new ArangoIndex
		{
			Name = "wiki_revision_page_locale_rev",
			Fields = ["PageId", "Locale", "RevisionNumber"],
			Unique = true,
			Type = ArangoIndexType.Persistent
		});

		// Optional cleanup, not load-bearing: the new unique index is what enforces correctness. Confirm
		// the shape Core.Arango 3.12's IArangoIndexModule.ListAsync actually returns before relying on the
		// property names below; if it does not expose Fields/Id conveniently, delete this loop and leave the
		// old index in place. A redundant non-unique lookup index costs write throughput, not correctness.
		var existingIndexes = await migrator.Context.Index.ListAsync(handle, DatabaseConstants.WikiRevisions);
		foreach (var stale in existingIndexes.Where(i =>
			i.Type == ArangoIndexType.Persistent
			&& i.Fields is ["PageId", "RevisionNumber"]))
		{
			await migrator.Context.Index.DropAsync(handle, stale.Id!);
		}
	}

	public Task Down(IArangoMigrator migrator, ArangoHandle handle) => Task.CompletedTask;
}
```

The backfill and the revision-index work sit **outside** the collection-existence guard on purpose: `node_wiki_pages` and `node_wiki_revisions` already exist on every deployed database, so a guarded call would never run. `Index.CreateAsync` on an identical existing index is idempotent in Core.Arango, and the backfill's `FILTER` makes it a no-op on a second pass, so the whole migration is safe to re-run.

**Why the backfill hardcodes `WikiOptions.DefaultLocaleFallback` instead of reading `Wiki.DefaultLocale`.** It cannot read it: `OptionsService` is an `IOptionsFactory<SharpMUSHOptions>` over `ISharpDatabase` (`SharpMUSH.Library/Services/OptionsService.cs:7`), so the configured value lives *in* the database this migration is preparing, and Core.Arango instantiates `IArangoMigration` types reflectively with no DI. It does not need to, either: this migration ships in the same release that introduces `wiki_default_locale`, and an admin cannot have changed a setting that did not exist yet — so at the moment the backfill first runs, `Wiki.DefaultLocale` is necessarily its parameter default. A game whose existing content is not English sets `wiki_default_locale` and re-stamps, or wipes and reseeds.

If `Index.CreateAsync` fails with a uniqueness violation, the database already contains duplicate `(PageId, RevisionNumber)` rows that the old non-unique index tolerated. Pre-production: wipe and reseed. Do not weaken the index to make the migration pass.

- [ ] **Step 3: Replace the stubs with real AQL**

In `SharpMUSH.Database.ArangoDB/ArangoDatabase.Wiki.cs`, delete the whole `// ---- Translations (Task 7 replaces these) ----` block from Task 5 (including `WikiTranslationsNotImplemented`) and put this in its place:

```csharp

	// ---- Translations -------------------------------------------------------

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId SORT t.Locale ASC RETURN t",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", pageId }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiTranslationFromJson)
			.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId AND t.Locale == @locale RETURN t",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", pageId },
				{ "locale", normalized }
			});

		return result.FirstOrDefault() is { ValueKind: not JsonValueKind.Undefined } elem
			? OneOf<WikiTranslation, NotFound>.FromT0(WikiTranslationFromJson(elem))
			: new NotFound();
	}

	public async Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId, string locale, string title, string markdown,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber)
	{
		var normalizedLocale = WikiHelpers.NormalizeLocale(locale);
		if (normalizedLocale.IsT1) return normalizedLocale.AsT1;

		var normalized = normalizedLocale.AsT0;

		var pageLookup = await GetByIdAsync(pageId);
		if (pageLookup.IsT1)
			return new Error<string>($"No wiki page with id '{pageId}'.");

		var page = pageLookup.AsT0;
		if (page.SourceLocale.Length > 0
			&& string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			return new Error<string>(
				$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");

		var now = DateTimeOffset.UtcNow;
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		var bindVars = new Dictionary<string, object>
		{
			{ "@c", DatabaseConstants.WikiTranslations },
			{ "pageId", pageId },
			{ "locale", normalized },
			{ "title", title },
			{ "markdown", markdown },
			{ "html", html },
			{ "plain", plain },
			{ "editor", editorDbref },
			{ "now", now },
			{ "published", published }
		};

		try
		{
			if (expectedRevisionNumber is null)
			{
				// Create-only. A plain INSERT so the (PageId, Locale) unique index — not a read-then-write
				// race in C# — arbitrates two writers who both believe they are creating the translation.
				var inserted = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
					"""
					INSERT {
						PageId: @pageId, Locale: @locale, Title: @title,
						MarkdownSource: @markdown, RenderedHtml: @html, PlainText: @plain,
						LastEditorDbref: @editor, CreatedAt: @now, UpdatedAt: @now,
						Published: @published, RevisionNumber: 1
					}
					IN @@c
					RETURN NEW
					""",
					bindVars: bindVars);

				if (inserted.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } created)
					return new Error<string>($"Insert of translation '{normalized}' returned no document.");

				var newTranslation = WikiTranslationFromJson(created);
				await SaveWikiTranslationRevisionAsync(newTranslation, editorDbref, editSummary);
				return newTranslation;
			}

			// Compare-and-swap: the FILTER on RevisionNumber is the condition, and "no document returned"
			// is the conflict signal. Never fold this back into an UPSERT — an unconditional UPDATE lets two
			// translators who both loaded revision 4 both write 5, and one loses their prose silently.
			bindVars["expected"] = expectedRevisionNumber.Value;
			var updatedRows = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
				"""
				FOR t IN @@c
					FILTER t.PageId == @pageId AND t.Locale == @locale AND t.RevisionNumber == @expected
					UPDATE t WITH {
						Title: @title, MarkdownSource: @markdown, RenderedHtml: @html, PlainText: @plain,
						LastEditorDbref: @editor, UpdatedAt: @now, Published: @published,
						RevisionNumber: t.RevisionNumber + 1
					}
					IN @@c
					RETURN NEW
				""",
				bindVars: bindVars);

			if (updatedRows.FirstOrDefault() is not { ValueKind: not JsonValueKind.Undefined } row)
			{
				// Zero rows affected. Either the translation is gone or somebody else already bumped it.
				// Do NOT retry: re-reading and re-applying would overwrite the winner with stale markdown,
				// which is precisely the loss expectedRevisionNumber exists to prevent.
				var current = await GetTranslationAsync(pageId, normalized);
				return current.IsT0
					? new Error<string>(
						$"The '{normalized}' translation changed while you were editing "
						+ $"(expected revision {expectedRevisionNumber.Value}, found {current.AsT0.RevisionNumber}). "
						+ "Reload and re-apply your changes.")
					: new Error<string>($"No '{normalized}' translation exists for page '{pageId}' to update.");
			}

			var translation = WikiTranslationFromJson(row);
			await SaveWikiTranslationRevisionAsync(translation, editorDbref, editSummary);
			return translation;
		}
		catch (Exception ex)
		{
			// A lost unique-index race on the create path surfaces as a driver conflict. Reading the winner
			// back is safe there because nothing of this caller's content was meant to land. It is NOT safe
			// on the update path, which is why that branch returns above without touching this handler.
			if (expectedRevisionNumber is null)
			{
				var retry = await GetTranslationAsync(pageId, normalized);
				if (retry.IsT0)
					return new Error<string>(
						$"A '{normalized}' translation already exists for page '{pageId}'. "
						+ "Pass its current revision number to update it.");
			}

			return new Error<string>($"Could not write translation '{normalized}': {ex.Message}");
		}
	}

	public async Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var lookup = await GetTranslationAsync(pageId, normalized);
		if (lookup.IsT1) return new NotFound();

		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR r IN @@c FILTER r.PageId == @pageId AND r.Locale == @locale REMOVE r IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "locale", normalized }
			});

		await arangoDb.Document.DeleteAsync<JsonElement>(
			handle, DatabaseConstants.WikiTranslations, ExtractKey(lookup.AsT0.Id));

		return new None();
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
		string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"""
			FOR r IN @@c
				FILTER r.PageId == @pageId AND (r.Locale == @locale OR (@locale == "" AND r.Locale == null))
				SORT r.RevisionNumber DESC
				LIMIT @skip, @take
				RETURN r
			""",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiRevisions },
				{ "pageId", pageId },
				{ "locale", wanted },
				{ "skip", skip },
				{ "take", take }
			});

		return result
			.Where(e => e.ValueKind != JsonValueKind.Undefined)
			.Select(WikiRevisionFromJson)
			.ToList()
			.AsReadOnly();
	}

	/// <summary>
	/// Appends a revision snapshot for a translation. The document carries <c>Locale</c>, which is what
	/// splits history into a stream per (PageId, Locale).
	/// </summary>
	private async Task SaveWikiTranslationRevisionAsync(
		WikiTranslation translation, string editorDbref, string? editSummary)
	{
		var doc = new
		{
			PageId = translation.PageId,
			Locale = translation.Locale,
			RevisionNumber = translation.RevisionNumber,
			MarkdownSource = translation.MarkdownSource,
			EditorDbref = editorDbref,
			Timestamp = translation.UpdatedAt,
			EditSummary = editSummary
		};

		await arangoDb.Document.CreateAsync(handle, DatabaseConstants.WikiRevisions, doc);
	}

	private static WikiTranslation WikiTranslationFromJson(JsonElement elem) => new(
		Id: elem.TryGetProperty("_id", out var id) ? id.GetString() ?? "" : "",
		PageId: elem.TryGetProperty("PageId", out var pageId) ? pageId.GetString() ?? "" : "",
		Locale: elem.TryGetProperty("Locale", out var locale) ? locale.GetString() ?? "" : "",
		Title: elem.TryGetProperty("Title", out var title) ? title.GetString() ?? "" : "",
		MarkdownSource: elem.TryGetProperty("MarkdownSource", out var md) ? md.GetString() ?? "" : "",
		RenderedHtml: elem.TryGetProperty("RenderedHtml", out var html) ? html.GetString() ?? "" : "",
		PlainText: elem.TryGetProperty("PlainText", out var plain) ? plain.GetString() ?? "" : "",
		LastEditorDbref: elem.TryGetProperty("LastEditorDbref", out var editor) ? editor.GetString() ?? "" : "",
		CreatedAt: elem.TryGetProperty("CreatedAt", out var created) && created.ValueKind != JsonValueKind.Null
			? created.GetDateTimeOffset() : DateTimeOffset.MinValue,
		UpdatedAt: elem.TryGetProperty("UpdatedAt", out var updated) && updated.ValueKind != JsonValueKind.Null
			? updated.GetDateTimeOffset() : DateTimeOffset.MinValue,
		Published: !elem.TryGetProperty("Published", out var published)
			|| published.ValueKind != JsonValueKind.False,
		RevisionNumber: elem.TryGetProperty("RevisionNumber", out var rev) && rev.TryGetInt32(out var revNum)
			? revNum : 1);
```

**On the transaction boundary.** The row update and the revision append are two AQL statements here, not one transaction — `IArangoQueryModule` has no ambient transaction in this codebase. The spec allows exactly that, provided the update is conditional and "zero rows affected" is the conflict signal, which is what the `FILTER … t.RevisionNumber == @expected` above gives. The residual window is narrow and benign: if the process dies between the two statements the translation row is at revision *n+1* with no matching revision row, so history has a gap. It cannot silently overwrite a winner's prose, which is the failure mode that matters. If Core.Arango's stream-transaction API is later wired into this provider, wrap both statements and delete this note.

- [ ] **Step 4: Extend `DeleteAsync`, filter `GetRevisionsAsync`, and read `SourceLocale`**

In the same file:

`DeleteAsync` — before the existing revision-removal query, add the translation sweep (the revision query already removes every row for the page, translations included, so only the translation documents themselves need adding):

```csharp
		await arangoDb.Query.ExecuteAsync<ArangoVoid>(handle,
			"FOR t IN @@c FILTER t.PageId == @pageId REMOVE t IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.WikiTranslations },
				{ "pageId", id }
			});
```

`GetRevisionsAsync` — add the source-stream filter so its five existing callers keep seeing only source revisions:

```csharp
			"FOR r IN @@c FILTER r.PageId == @pageId AND (r.Locale == null OR r.Locale == \"\") SORT r.RevisionNumber DESC LIMIT @skip, @take RETURN r",
```

`WikiPageFromJson` — add the `SourceLocale` init-property to the returned object, defensively so a document the backfill has not reached yields empty. Read it straight through; do **not** substitute the configured default here or anywhere else on the read path:

```csharp
			SourceLocale = elem.TryGetProperty("SourceLocale", out var srcLoc) ? srcLoc.GetString() ?? "" : "",
```

`WikiRevisionFromJson` — same treatment:

```csharp
			Locale = elem.TryGetProperty("Locale", out var revLoc) ? revLoc.GetString() ?? "" : "",
```

- [ ] **Step 5: Verify locally as far as possible**

Run: `dotnet build`
Expected: 0 errors, and `grep -n WikiTranslationsNotImplemented SharpMUSH.Database.ArangoDB/ArangoDatabase.Wiki.cs` returns nothing.

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SurrealMigrationIdempotencyTests/*"`
Expected: PASS — confirms nothing asserts an exact index count.

**Verify locally, then in CI.** Run `SHARPMUSH_DATABASE_PROVIDER=arangodb dotnet run --project SharpMUSH.Tests.Integration` and require it green before committing; CI job `test-integration` with the same provider is the confirming gate. Green on Arango is also what proves the migration applies to a fresh database, that the backfill runs before the unique index, and that `WikiTranslationIntegrationTests` — including `RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber` — passes on Arango. Never mark this task done on a local build alone. In the CI log, look for the migration's `stamped SourceLocale=…` line: absent means the migration did not run, and the negative revision test will then be passing for the wrong reason on a fresh database with nothing to stamp.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database/DatabaseConstants.cs SharpMUSH.Database.ArangoDB
git commit -m "feat(wiki): implement translation storage for ArangoDB"
```

---

### Task 8: Memgraph translation storage

Memgraph has no migration-id bookkeeping — schema is a list of idempotent statements in `MemgraphDatabase.Migration.cs`, each wrapped in a try/catch that swallows "already exists". DDL must run auto-commit on the shared `indexSession`, never inside a managed transaction. Property names in this provider are **camelCase**; node labels are PascalCase. IDs are client-assigned `Guid.NewGuid().ToString("N")`.

**Memgraph is the backend with no revision constraint at all today.** `MemgraphDatabase.Migration.cs:124-125` declares two *separate* non-unique indexes, on `:WikiRevision(pageId)` and `:WikiRevision(revisionNumber)`. Two independent indexes are not a composite constraint, so a duplicate `(pageId, locale, revisionNumber)` is accepted silently — the same numbering bug that SurrealDB rejects outright. This task is where Memgraph gains a real one, and the statement list's ordering is load-bearing: the backfill statements must precede the constraint, because `ASSERT … IS UNIQUE` cannot be created over a property that does not exist on the existing nodes.

**Files:**
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs:113-135` (extend `wikiIndexQueries` — backfill, then constraints)
- Modify: `SharpMUSH.Database.Memgraph/MemgraphDatabase.Wiki.cs` (replace stubs; extend `DeleteAsync`; filter `GetRevisionsAsync`; add `NodeToWikiTranslation`)

**Interfaces:**
- Consumes: the Task 5 contract; `WikiHelpers.NormalizeLocale` / `NormalizeLocaleOrEmpty`; `WikiOptions.DefaultLocaleFallback` (Task 1).
- Produces: `:WikiTranslation` node label with camelCase properties `translationId, pageId, locale, title, markdownSource, renderedHtml, plainText, lastEditorDbref, createdAt, updatedAt, published, revisionNumber`; a uniqueness constraint on `(:WikiRevision pageId, locale, revisionNumber)`; `MemgraphDatabase.NodeToWikiTranslation(INode)` → `WikiTranslation` (private static).

- [ ] **Step 1: Extend the schema statement list**

`SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs` — append to the `wikiIndexQueries` array **in this order** (keep the file's existing indentation inside that array). The array is executed sequentially on the auto-commit `indexSession`, so ordering here *is* the ordering guarantee the spec asks for:

```csharp
					// Backfill FIRST. A composite uniqueness constraint cannot be created over a property the
					// existing nodes do not carry, and pre-existing revisions have no locale at all.
					// Pages get the configured default; revisions get the empty source-stream marker, because
					// every pre-existing revision belongs to the page's own locale stream.
					"MATCH (p:WikiPage) WHERE p.sourceLocale IS NULL OR p.sourceLocale = '' SET p.sourceLocale = 'en'",
					"MATCH (r:WikiRevision) WHERE r.locale IS NULL SET r.locale = ''",

					"CREATE INDEX ON :WikiTranslation(pageId)",
					"CREATE INDEX ON :WikiTranslation(locale)",
					"CREATE CONSTRAINT ON (t:WikiTranslation) ASSERT t.pageId, t.locale IS UNIQUE",
					"CREATE INDEX ON :WikiRevision(locale)",

					// The real constraint, replacing two independent non-unique indexes that enforced nothing.
					// Same syntax as the existing (namespace, category, slug) page constraint above.
					"CREATE CONSTRAINT ON (r:WikiRevision) ASSERT r.pageId, r.locale, r.revisionNumber IS UNIQUE",

					// The standalone revisionNumber index indexed a column nothing queries on its own, and its
					// presence is part of why this backend looked constrained when it was not.
					"DROP INDEX ON :WikiRevision(revisionNumber)"
```

The `'en'` literal in the backfill is `WikiOptions.DefaultLocaleFallback`. These statements are Cypher string literals in a `string[]`, so use an interpolated string (`$"… SET p.sourceLocale = '{WikiOptions.DefaultLocaleFallback}'"`) rather than retyping it — Task 1 exists so there is one literal. The same reasoning as Task 7 applies to *why* it is a compile-time constant rather than `Wiki.DefaultLocale`: options are stored in the database and read through `ISharpDatabase`, so migration-time access would be circular, and this migration ships in the release that introduces the setting, so the setting cannot yet have been changed.

Both backfill statements are idempotent by their `WHERE` clause, which matters because this array runs on **every** start-up, not once. `DROP INDEX` on an absent index throws "not found" rather than "already exists", so confirm the surrounding try/catch swallows it too — the existing handlers only match `already exists`. If it does not, add a `catch` for it rather than removing the statement.

- [ ] **Step 2: Replace the stubs**

In `SharpMUSH.Database.Memgraph/MemgraphDatabase.Wiki.cs`, delete the Task 5 stub block and add (tabs):

```csharp

	// ---- Translations -------------------------------------------------------

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
	{
		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (t:WikiTranslation {pageId: $pageId}) RETURN t ORDER BY t.locale ASC",
			new { pageId });

		var records = await result.ToListAsync();
		return records
			.Select(r => NodeToWikiTranslation(r["t"].As<INode>()))
			.Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
			.ToList()
			.AsReadOnly();
	}

	public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync(
			"MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale}) RETURN t",
			new { pageId, locale = normalized });

		var records = await result.ToListAsync();
		if (records.Count == 0) return new NotFound();
		return NodeToWikiTranslation(records[0]["t"].As<INode>());
	}

	public async Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
		string pageId, string locale, string title, string markdown,
		string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber)
	{
		var normalizedLocale = WikiHelpers.NormalizeLocale(locale);
		if (normalizedLocale.IsT1) return normalizedLocale.AsT1;

		var normalized = normalizedLocale.AsT0;

		var pageLookup = await GetByIdAsync(pageId);
		if (pageLookup.IsT1)
			return new Error<string>($"No wiki page with id '{pageId}'.");

		var page = pageLookup.AsT0;
		if (page.SourceLocale.Length > 0
			&& string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
			return new Error<string>(
				$"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");

		var now = DateTimeOffset.UtcNow;
		var html = _wikiRenderer.RenderToHtml(markdown);
		var plain = _wikiRenderer.ExtractPlainText(markdown);

		try
		{
			await using var session = driver.AsyncSession();
			// ExecuteWriteAsync gives one managed transaction, so here the row write and the revision append
			// really are atomic — the spec's preferred shape rather than the conditional-update fallback.
			return await session.ExecuteWriteAsync<OneOf<WikiTranslation, Error<string>>>(async tx =>
			{
				// No MERGE. MERGE + ON MATCH SET revisionNumber = revisionNumber + 1 is an unconditional
				// bump: two translators who both loaded revision 4 both produce a 5 and one loses their
				// prose. The compare-and-swap has to be expressed as a MATCH on the expected value.
				var cypher = expectedRevisionNumber is null
					? """
						CREATE (t:WikiTranslation {
							translationId: $translationId, pageId: $pageId, locale: $locale,
							title: $title, markdownSource: $markdown, renderedHtml: $html, plainText: $plain,
							lastEditorDbref: $editorDbref, createdAt: $now, updatedAt: $now,
							published: $published, revisionNumber: 1
						})
						RETURN t
						"""
					: """
						MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale})
						WHERE t.revisionNumber = $expected
						SET t.title = $title,
						    t.markdownSource = $markdown,
						    t.renderedHtml = $html,
						    t.plainText = $plain,
						    t.lastEditorDbref = $editorDbref,
						    t.updatedAt = $now,
						    t.published = $published,
						    t.revisionNumber = t.revisionNumber + 1
						RETURN t
						""";

				var result = await tx.RunAsync(cypher,
					new
					{
						pageId,
						locale = normalized,
						translationId = Guid.NewGuid().ToString("N"),
						title,
						markdown,
						html,
						plain,
						editorDbref,
						now = now.ToString("O"),
						published,
						expected = expectedRevisionNumber ?? 0
					});

				var records = await result.ToListAsync();
				if (records.Count == 0)
				{
					// Zero rows matched: somebody else already bumped it, or it does not exist. Not retried.
					return new Error<string>(
						$"The '{normalized}' translation changed while you were editing, or does not exist "
						+ $"(expected revision {expectedRevisionNumber}). Reload and re-apply your changes.");
				}

				var translation = NodeToWikiTranslation(records[0]["t"].As<INode>());

				await SaveMemgraphTranslationRevisionAsync(tx, translation, editorDbref, editSummary, now);
				return translation;
			});
		}
		catch (Exception ex)
		{
			// On the create path a lost race against the (pageId, locale) uniqueness constraint lands here,
			// and there is nothing of this caller's to preserve. On the update path a conflict has already
			// returned above, so reaching this handler with a non-null expected revision is a real fault —
			// report it, never re-read and re-apply, which would overwrite the winner with stale markdown.
			if (expectedRevisionNumber is null)
			{
				var existing = await GetTranslationAsync(pageId, normalized);
				if (existing.IsT0)
					return new Error<string>(
						$"A '{normalized}' translation already exists for page '{pageId}'. "
						+ "Pass its current revision number to update it.");
			}

			return new Error<string>($"Could not write translation '{normalized}': {ex.Message}");
		}
	}

	public async Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
	{
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
		if (normalized.Length == 0) return new NotFound();

		var lookup = await GetTranslationAsync(pageId, normalized);
		if (lookup.IsT1) return new NotFound();

		await using var session = driver.AsyncSession();
		await session.ExecuteWriteAsync(async tx =>
		{
			await tx.RunAsync(
				"MATCH (r:WikiRevision {pageId: $pageId, locale: $locale}) DELETE r",
				new { pageId, locale = normalized });

			await tx.RunAsync(
				"MATCH (t:WikiTranslation {pageId: $pageId, locale: $locale}) DELETE t",
				new { pageId, locale = normalized });
		});

		return new None();
	}

	public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
		string pageId, string locale, int skip, int take)
	{
		var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

		await using var session = driver.AsyncSession();
		var result = await session.RunAsync("""
			MATCH (r:WikiRevision {pageId: $pageId})
			WHERE coalesce(r.locale, '') = $locale
			RETURN r ORDER BY r.revisionNumber DESC SKIP $skip LIMIT $take
			""",
			new { pageId, locale = wanted, skip, take });

		var records = await result.ToListAsync();
		return records.Select(r => NodeToWikiRevision(r["r"].As<INode>())).ToList().AsReadOnly();
	}

	/// <summary>
	/// Appends a revision node for a translation. <c>revisionId</c> carries the locale so it cannot
	/// collide with the source page's <c>{pageId}:{revisionNumber}</c> keys.
	/// </summary>
	private static async Task SaveMemgraphTranslationRevisionAsync(
		IAsyncQueryRunner runner,
		WikiTranslation translation,
		string editorDbref,
		string? editSummary,
		DateTimeOffset timestamp)
	{
		await runner.RunAsync("""
			CREATE (r:WikiRevision {
				revisionId: $revisionId,
				pageId: $pageId,
				locale: $locale,
				revisionNumber: $revisionNumber,
				markdownSource: $markdownSource,
				editorDbref: $editorDbref,
				timestamp: $timestamp,
				editSummary: $editSummary
			})
			""",
			new
			{
				revisionId = $"{translation.PageId}:{translation.Locale}:{translation.RevisionNumber}",
				pageId = translation.PageId,
				locale = translation.Locale,
				revisionNumber = translation.RevisionNumber,
				markdownSource = translation.MarkdownSource,
				editorDbref,
				timestamp = timestamp.ToString("O"),
				editSummary = editSummary ?? ""
			});
	}

	private static WikiTranslation NodeToWikiTranslation(INode node) => new(
		Id: node.Properties.TryGetValue("translationId", out var id) ? id?.ToString() ?? "" : "",
		PageId: node.Properties.TryGetValue("pageId", out var pageId) ? pageId?.ToString() ?? "" : "",
		Locale: node.Properties.TryGetValue("locale", out var locale) ? locale?.ToString() ?? "" : "",
		Title: node.Properties.TryGetValue("title", out var title) ? title?.ToString() ?? "" : "",
		MarkdownSource: node.Properties.TryGetValue("markdownSource", out var md) ? md?.ToString() ?? "" : "",
		RenderedHtml: node.Properties.TryGetValue("renderedHtml", out var html) ? html?.ToString() ?? "" : "",
		PlainText: node.Properties.TryGetValue("plainText", out var plain) ? plain?.ToString() ?? "" : "",
		LastEditorDbref: node.Properties.TryGetValue("lastEditorDbref", out var editor) ? editor?.ToString() ?? "" : "",
		CreatedAt: node.Properties.TryGetValue("createdAt", out var created)
			&& DateTimeOffset.TryParse(created?.ToString(), out var createdAt) ? createdAt : DateTimeOffset.MinValue,
		UpdatedAt: node.Properties.TryGetValue("updatedAt", out var updated)
			&& DateTimeOffset.TryParse(updated?.ToString(), out var updatedAt) ? updatedAt : DateTimeOffset.MinValue,
		Published: !node.Properties.TryGetValue("published", out var published) || published is not false,
		RevisionNumber: node.Properties.TryGetValue("revisionNumber", out var rev)
			&& int.TryParse(rev?.ToString(), out var revNum) ? revNum : 1);
```

- [ ] **Step 3: Extend `DeleteAsync`, filter `GetRevisionsAsync`, and read `SourceLocale`**

`DeleteAsync` — inside the existing `ExecuteWriteAsync`, before the `WikiPage` delete (the `WikiRevision {pageId: $id}` delete already covers translation revisions):

```csharp
			await tx.RunAsync(
				"MATCH (t:WikiTranslation {pageId: $id}) DELETE t",
				new { id });
```

`GetRevisionsAsync` — restrict to the source stream:

```csharp
		var result = await session.RunAsync("""
			MATCH (r:WikiRevision {pageId: $pageId})
			WHERE coalesce(r.locale, '') = ''
			RETURN r ORDER BY r.revisionNumber DESC SKIP $skip LIMIT $take
			""",
			new { pageId, skip, take });
```

`NodeToWikiPage` — add `SourceLocale = node.Properties.TryGetValue("sourceLocale", out var srcLoc) ? srcLoc?.ToString() ?? "" : ""` to the init block, read straight through with no substitution of the configured default; `NodeToWikiRevision` — add `Locale` the same way.

- [ ] **Step 4: Verify locally as far as possible**

Run: `dotnet build`
Expected: 0 errors, and `grep -n WikiTranslationsNotImplemented SharpMUSH.Database.Memgraph/MemgraphDatabase.Wiki.cs` returns nothing.

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed.

**Verify locally, then in CI.** Run `SHARPMUSH_DATABASE_PROVIDER=memgraph dotnet run --project SharpMUSH.Tests.Integration` and require it green before committing; CI with the same provider is the confirming gate. Either way it must include `RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber` — which is *the* test for this backend, since it is the one that had no revision constraint at all. Watch specifically for a constraint-creation failure at startup: Memgraph rejects DDL inside managed transactions, so the new statements must be in `wikiIndexQueries` (auto-commit `indexSession`) and nowhere else, and `ASSERT … IS UNIQUE` fails if the backfill statements were placed after it rather than before.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Database.Memgraph
git commit -m "feat(wiki): implement translation storage for Memgraph"
```

---

### Task 9: SurrealDB translation storage — the last stub goes

**This file uses 4-space indentation.** Two more traps specific to this provider: the CBOR serializer ignores `[JsonPropertyName]`, so DB-record property names must match SurrealDB field names **exactly** (lower camelCase); and `None` is aliased as `OkNone` at the top of the file. Pagination is `LIMIT $take START $skip`, not `SKIP`/`LIMIT`. Index DDL lives in an always-run `indexQueries` array guarded only by `IF NOT EXISTS`, so no new migration id is needed.

**Files:**
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs:90-97` (extend `indexQueries`: backfill, drop the offending unique index, redefine it over three fields)
- Modify: `SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs` (add `WikiTranslationDbRecord`; replace stubs; extend `DeleteAsync`; filter `GetRevisionsAsync`)

**Interfaces:**
- Consumes: the Task 5 contract; `WikiOptions.DefaultLocaleFallback` (Task 1); `NormalizeSurrealId` (`SurrealDatabase.Accounts.cs:223`); `ExecuteAsync` (`SurrealDatabase.cs:142`).
- Produces: table `wiki_translation`; index `wiki_revision_page_locale_rev` `UNIQUE (pageId, locale, revisionNumber)` replacing `wiki_revision_page_rev`; `WikiTranslationDbRecord`; `SurrealDatabase.WikiTranslationFields` const; `MapToWikiTranslation`; `NormalizeWikiTranslationId`.

- [ ] **Step 1: Extend the index list — backfill, then replace the revision index**

SurrealDB is the backend whose *current* index actively breaks translations: `SurrealDatabase.Migration.cs:97` declares `wiki_revision_page_rev ON wiki_revision FIELDS pageId, revisionNumber UNIQUE`. **Translation revisions share `pageId` with source revisions and restart numbering at 1, so that index rejects the very first translation revision.** The array is executed in order, so the statements go in this order and no other: backfill, drop, redefine.

`SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs` — **delete** the existing `"DEFINE INDEX IF NOT EXISTS wiki_revision_page_rev …"` line at 97 and put this in its place (the surrounding array already uses this shape for the `wiki_page` category backfill at line 91, so it is a familiar idiom in this file):

```csharp
				// Backfill BEFORE the new unique index, or the DEFINE fails on rows with no locale.
				// Pages get the configured default; revisions get the empty source-stream marker, because
				// every pre-existing revision belongs to its page's own locale stream.
				$"UPDATE wiki_page SET sourceLocale = '{WikiOptions.DefaultLocaleFallback}' WHERE sourceLocale = NONE OR sourceLocale = ''",
				"UPDATE wiki_revision SET locale = '' WHERE locale = NONE",

				// The old index is UNIQUE on (pageId, revisionNumber) and would reject a translation's
				// revision 1 outright. DEFINE INDEX IF NOT EXISTS will not alter an existing index, so the
				// drop is mandatory rather than tidiness.
				"REMOVE INDEX IF EXISTS wiki_revision_page_rev ON wiki_revision",
				"DEFINE INDEX IF NOT EXISTS wiki_revision_page_locale_rev ON wiki_revision FIELDS pageId, locale, revisionNumber UNIQUE",

				"DEFINE INDEX IF NOT EXISTS wiki_translation_page_locale ON wiki_translation FIELDS pageId, locale UNIQUE",
				"DEFINE INDEX IF NOT EXISTS wiki_translation_locale ON wiki_translation FIELDS locale",
```

`WikiOptions.DefaultLocaleFallback` rather than a bare `'en'` for the reason Task 1 gives: one literal. And as in Tasks 7 and 8, the backfill uses that compile-time constant rather than `Wiki.DefaultLocale` because options are stored in the database and read through `ISharpDatabase`, so migration-time access would be circular — and this migration ships in the release that introduces the setting, so it cannot yet have been changed.

There is no separate `(pageId, locale, revisionNumber)` non-unique index: the unique one above serves the same lookups. Both `UPDATE` statements are idempotent by their `WHERE` clause, which matters because this array runs on every start-up rather than once; `SurrealMigrationIdempotencyTests` is what holds that.

If the `DEFINE … UNIQUE` fails on an existing database, it has duplicate `(pageId, locale, revisionNumber)` rows. Pre-production: wipe and reseed. Do not drop the `UNIQUE` keyword to make the migration pass — that is exactly how ArangoDB and Memgraph ended up unconstrained.

- [ ] **Step 2: Add the DB record and field list**

In `SharpMUSH.Database.SurrealDB/SurrealDatabase.Wiki.cs`, after `WikiRevisionDbRecord` (file scope, before the partial class; 4 spaces):

```csharp
internal class WikiTranslationDbRecord : Record
{
    public string? pageId { get; set; }
    public string? locale { get; set; }
    public string? title { get; set; }
    public string? markdownSource { get; set; }
    public string? renderedHtml { get; set; }
    public string? plainText { get; set; }
    public string? lastEditorDbref { get; set; }
    public string? createdAt { get; set; }
    public string? updatedAt { get; set; }
    public bool? published { get; set; }
    public int? revisionNumber { get; set; }
}
```

and next to the existing field-list constants:

```csharp
    private const string WikiTranslationFields =
        "id, pageId, locale, title, markdownSource, renderedHtml, plainText, " +
        "lastEditorDbref, createdAt, updatedAt, published, revisionNumber";
```

- [ ] **Step 3: Replace the stubs**

Delete the Task 5 stub block and add (4 spaces):

```csharp

    // ---- Translations -------------------------------------------------------

    public async Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
    {
        var parameters = new Dictionary<string, object?> { ["pageId"] = pageId };
        var response = await ExecuteAsync(
            $"SELECT {WikiTranslationFields} FROM wiki_translation WHERE pageId = $pageId ORDER BY locale ASC",
            parameters);
        var results = response.GetValue<List<WikiTranslationDbRecord>>(0);
        return (results?
                .Select(MapToWikiTranslation)
                .Select(t => new WikiTranslationSummary(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber))
                .ToList() ?? [])
            .AsReadOnly();
    }

    public async Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
    {
        var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
        if (normalized.Length == 0) return new NotFound();

        var parameters = new Dictionary<string, object?> { ["pageId"] = pageId, ["locale"] = normalized };
        var response = await ExecuteAsync(
            $"SELECT {WikiTranslationFields} FROM wiki_translation WHERE pageId = $pageId AND locale = $locale",
            parameters);
        var results = response.GetValue<List<WikiTranslationDbRecord>>(0);
        if (results is null or { Count: 0 }) return new NotFound();
        return MapToWikiTranslation(results[0]);
    }

    public async Task<OneOf<WikiTranslation, Error<string>>> UpsertTranslationAsync(
        string pageId, string locale, string title, string markdown,
        string editorDbref, string? editSummary, bool published, int? expectedRevisionNumber)
    {
        var normalizedLocale = WikiHelpers.NormalizeLocale(locale);
        if (normalizedLocale.IsT1) return normalizedLocale.AsT1;

        var normalized = normalizedLocale.AsT0;

        var pageLookup = await GetByIdAsync(pageId);
        if (pageLookup.IsT1)
            return new Error<string>($"No wiki page with id '{pageId}'.");

        var page = pageLookup.AsT0;
        if (page.SourceLocale.Length > 0
            && string.Equals(page.SourceLocale, normalized, StringComparison.OrdinalIgnoreCase))
            return new Error<string>(
                $"'{normalized}' is the page's source locale; edit the page itself rather than adding a translation.");

        var now = DateTimeOffset.UtcNow;
        var html = _wikiRenderer.RenderToHtml(markdown);
        var plain = _wikiRenderer.ExtractPlainText(markdown);

        var parameters = new Dictionary<string, object?>
        {
            ["pageId"] = pageId,
            ["locale"] = normalized,
            ["title"] = title,
            ["markdown"] = markdown,
            ["html"] = html,
            ["plain"] = plain,
            ["editorDbref"] = editorDbref,
            ["now"] = now.ToString("O"),
            ["published"] = published
        };

        try
        {
            if (expectedRevisionNumber is null)
            {
                // Create-only. A bare CREATE so the (pageId, locale) unique index arbitrates two writers who
                // both believe they are creating the translation, rather than a read-then-write race here.
                parameters["created"] = now.ToString("O");
                parameters["rev"] = 1;
                await ExecuteAsync("""
                    CREATE wiki_translation CONTENT {
                    	pageId: $pageId,
                    	locale: $locale,
                    	title: $title,
                    	markdownSource: $markdown,
                    	renderedHtml: $html,
                    	plainText: $plain,
                    	lastEditorDbref: $editorDbref,
                    	createdAt: $created,
                    	updatedAt: $now,
                    	published: $published,
                    	revisionNumber: $rev
                    }
                    """,
                    parameters);
            }
            else
            {
                // Compare-and-swap. The WHERE clause on revisionNumber is the condition and "no rows
                // returned" is the conflict signal — this provider has no ambient transaction spanning the
                // row update and the revision append, which is the fallback the spec permits.
                //
                // Never make this an unconditional UPDATE: two translators who both loaded revision 4 would
                // both write 5 and one would lose their prose with the index none the wiser.
                parameters["expected"] = expectedRevisionNumber.Value;
                parameters["rev"] = expectedRevisionNumber.Value + 1;
                var updateResponse = await ExecuteAsync(
                    "UPDATE wiki_translation MERGE { title: $title, markdownSource: $markdown, " +
                    "renderedHtml: $html, plainText: $plain, lastEditorDbref: $editorDbref, " +
                    "updatedAt: $now, published: $published, revisionNumber: $rev } " +
                    "WHERE pageId = $pageId AND locale = $locale AND revisionNumber = $expected " +
                    "RETURN AFTER",
                    parameters);

                var updated = updateResponse.GetValue<List<WikiTranslationDbRecord>>(0);
                if (updated is null or { Count: 0 })
                {
                    // Zero rows affected. Do NOT re-read and re-apply: that overwrites the winner with this
                    // caller's stale markdown, which is the loss expectedRevisionNumber exists to prevent.
                    var current = await GetTranslationAsync(pageId, normalized);
                    return current.IsT0
                        ? new Error<string>(
                            $"The '{normalized}' translation changed while you were editing "
                            + $"(expected revision {expectedRevisionNumber.Value}, found {current.AsT0.RevisionNumber}). "
                            + "Reload and re-apply your changes.")
                        : new Error<string>($"No '{normalized}' translation exists for page '{pageId}' to update.");
                }
            }
        }
        catch (Exception ex)
        {
            // Only the create path can legitimately land here, via the unique-index rejection.
            if (expectedRevisionNumber is null)
            {
                var existing = await GetTranslationAsync(pageId, normalized);
                if (existing.IsT0)
                    return new Error<string>(
                        $"A '{normalized}' translation already exists for page '{pageId}'. "
                        + "Pass its current revision number to update it.");
            }

            return new Error<string>($"Could not write translation '{normalized}': {ex.Message}");
        }

        var written = await GetTranslationAsync(pageId, normalized);
        if (written.IsT1)
            return new Error<string>($"Upsert of translation '{normalized}' returned no document.");

        await SaveSurrealTranslationRevisionAsync(written.AsT0, editorDbref, editSummary, now);
        return written.AsT0;
    }

    public async Task<OneOf<OkNone, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
    {
        var normalized = WikiHelpers.NormalizeLocaleOrEmpty(locale);
        if (normalized.Length == 0) return new NotFound();

        var lookup = await GetTranslationAsync(pageId, normalized);
        if (lookup.IsT1) return new NotFound();

        var revParams = new Dictionary<string, object?> { ["pageId"] = pageId, ["locale"] = normalized };
        await ExecuteAsync("DELETE wiki_revision WHERE pageId = $pageId AND locale = $locale", revParams);

        var key = NormalizeSurrealId(lookup.AsT0.Id, "wiki_translation");
        var delParams = new Dictionary<string, object?> { ["id"] = new StringRecordId(key) };
        await ExecuteAsync("DELETE $id", delParams);

        return new OkNone();
    }

    public async Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(
        string pageId, string locale, int skip, int take)
    {
        var wanted = locale.Length == 0 ? string.Empty : WikiHelpers.NormalizeLocaleOrEmpty(locale);

        var parameters = new Dictionary<string, object?>
        {
            ["pageId"] = pageId, ["locale"] = wanted, ["skip"] = skip, ["take"] = take
        };
        var response = await ExecuteAsync(
            $"SELECT {WikiRevisionFields} FROM wiki_revision " +
            "WHERE pageId = $pageId AND (locale ?? '') = $locale " +
            "ORDER BY revisionNumber DESC LIMIT $take START $skip",
            parameters);
        var results = response.GetValue<List<WikiRevisionDbRecord>>(0);
        return (results?.Select(MapToWikiRevision).ToList() ?? []).AsReadOnly();
    }

    /// <summary>
    /// Appends a revision row for a translation, carrying <c>locale</c> so the per-locale stream and the
    /// (pageId, locale, revisionNumber) unique index both work.
    /// </summary>
    private async Task SaveSurrealTranslationRevisionAsync(
        WikiTranslation translation,
        string editorDbref,
        string? editSummary,
        DateTimeOffset timestamp)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["pageId"] = translation.PageId,
            ["locale"] = translation.Locale,
            ["rev"] = translation.RevisionNumber,
            ["markdown"] = translation.MarkdownSource,
            ["editorDbref"] = editorDbref,
            ["timestamp"] = timestamp.ToString("O"),
            ["editSummary"] = editSummary
        };

        await ExecuteAsync("""
            CREATE wiki_revision CONTENT {
            	pageId: $pageId,
            	locale: $locale,
            	revisionNumber: $rev,
            	markdownSource: $markdown,
            	editorDbref: $editorDbref,
            	timestamp: $timestamp,
            	editSummary: $editSummary
            }
            """,
            parameters);
    }

    private static WikiTranslation MapToWikiTranslation(WikiTranslationDbRecord record) => new(
        Id: NormalizeWikiTranslationId(record.Id),
        PageId: record.pageId ?? "",
        Locale: record.locale ?? "",
        Title: record.title ?? "",
        MarkdownSource: record.markdownSource ?? "",
        RenderedHtml: record.renderedHtml ?? "",
        PlainText: record.plainText ?? "",
        LastEditorDbref: record.lastEditorDbref ?? "",
        CreatedAt: DateTimeOffset.TryParse(record.createdAt, out var created) ? created : DateTimeOffset.MinValue,
        UpdatedAt: DateTimeOffset.TryParse(record.updatedAt, out var updated) ? updated : DateTimeOffset.MinValue,
        Published: record.published ?? true,
        RevisionNumber: record.revisionNumber ?? 1);

    private static string NormalizeWikiTranslationId(RecordId? id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.TryDeserializeId<string>(out var stringId))
            return $"wiki_translation/{stringId}";
        if (id.TryDeserializeId<long>(out var longId))
            return $"wiki_translation/{longId}";
        if (id.TryDeserializeId<int>(out var intId))
            return $"wiki_translation/{intId}";
        throw new InvalidOperationException($"Unsupported SurrealDB wiki_translation record ID type for table '{id.Table}'.");
    }
```

- [ ] **Step 4: Extend `DeleteAsync`, filter `GetRevisionsAsync`, and read `SourceLocale`**

`DeleteAsync` — after the existing `DELETE wiki_revision WHERE pageId = $id`:

```csharp
        await ExecuteAsync("DELETE wiki_translation WHERE pageId = $id", parameters);
```

`GetRevisionsAsync` — restrict to the source stream:

```csharp
        var response = await ExecuteAsync(
            $"SELECT {WikiRevisionFields} FROM wiki_revision WHERE pageId = $pageId AND (locale ?? '') = '' " +
            $"ORDER BY revisionNumber DESC LIMIT $take START $skip",
            parameters);
```

`WikiPageDbRecord` — add `public string? sourceLocale { get; set; }`; add `sourceLocale` to `WikiPageFields`; set `SourceLocale = record.sourceLocale ?? ""` in `MapToWikiPage` (straight through — no substitution of the configured default); write it in `CreateAsync`'s `CONTENT`. `WikiRevisionDbRecord` — add `public string? locale { get; set; }`, add `locale` to `WikiRevisionFields`, set `Locale = record.locale ?? ""` in `MapToWikiRevision`.

- [ ] **Step 5: Verify locally as far as possible**

Run: `dotnet build`
Expected: 0 errors.

Run: `grep -rn "WikiTranslationsNotImplemented" SharpMUSH.Database.*/`
Expected: **no output**. Every stub from Task 5 is gone.

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/SurrealMigrationIdempotencyTests/*"`
Expected: PASS — the backfill `UPDATE`s and the `REMOVE INDEX IF EXISTS` + re-`DEFINE` pair must all stay idempotent across repeated migrations. This is the closest thing to local evidence that Step 1's ordering is safe; it is not a substitute for the CI run.

- [ ] **Step 5a: Confirm all three backends now agree**

```bash
grep -rn "revisionNumber UNIQUE\|RevisionNumber\"\]\|IS UNIQUE" \
  SharpMUSH.Database.SurrealDB/SurrealDatabase.Migration.cs \
  SharpMUSH.Database.ArangoDB/Migrations/ \
  SharpMUSH.Database.Memgraph/MemgraphDatabase.Migration.cs
```

Expected: each of the three declares a unique constraint over `(PageId/pageId, Locale/locale, RevisionNumber/revisionNumber)`, and **no** store still has a constraint over the two-field `(pageId, revisionNumber)` pair. If one backend is missing its constraint the Task 6 negative test will simply pass there, silently — that asymmetry is the whole defect this task set exists to close, so check it by reading, not by test colour.

**Verify all three locally, then in CI.** Loop `SHARPMUSH_DATABASE_PROVIDER` over `arangodb`, `memgraph` and `surrealdb` locally and require all three green — this is the task where the three stores' disagreement either resolves or is exposed, and you can now see that here rather than inferring it from a CI matrix. CI `test-integration` green on all three entries remains the acceptance gate. Phase 2 is not complete until that is true, and specifically not until `RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber` and `RevisionIndex_AcceptsATranslationRevisionOneBesideASourceRevisionOne` are green on all three rather than one.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Database.SurrealDB
git commit -m "feat(wiki): implement translation storage for SurrealDB"
```

---

# Phase 3 — Resolution

### Task 10: `IWikiLocalizationService` — the only `LocalizedWikiPage` factory

One implementation, no per-backend variants. It is the single place that:

1. normalises the requested locale,
2. **filters the candidate translation set by visibility before calling the resolver** — a reader without edit permission sees only `Published == true` translations, so an unpublished French translation falls through to step 4 or 5 exactly as if it did not exist, banner included,
3. constructs `LocalizedWikiPage`, which is what makes "resolved content lives on the wrapper" a single enforcement point rather than a convention.

Draft visibility is a first-class test case here, not an afterthought: it is the regression most likely to leak unfinished content.

The service takes `includeDrafts` as a plain `bool` rather than a `ClaimsPrincipal` so that `SharpMUSH.Library` keeps no ASP.NET dependency. The controller computes it in Task 11.

**This is also the one place that copes with an unstamped `SourceLocale`, and it does so as a diagnostic rather than a design.** An earlier draft of this plan had the service normalise empty → `Wiki.DefaultLocale` on every read. That was a data-integrity bug: an admin changing `wiki_default_locale` would silently change the authored locale of every page predating the field. The field is now materialised once by the Tasks 7–9 backfill and by every create path, so `SourceLocaleOf` reads `page.SourceLocale` straight through. If it is nevertheless empty, the backfill has not run — the service **logs a warning naming the page** and uses the configured default *for that single read* so the page still renders, because a read can never fail for locale reasons. That is graceful degradation over a broken row, not a documented meaning for empty, and it is expected never to fire.

**Files:**
- Create: `SharpMUSH.Library/Services/Interfaces/IWikiLocalizationService.cs`
- Create: `SharpMUSH.Library/Services/WikiLocalizationService.cs`
- Modify: `SharpMUSH.Server/Startup.cs:315-317` (register both new singletons next to `IWikiService`)
- Test: `SharpMUSH.Tests/Wiki/WikiLocalizationServiceTests.cs` (create)

**Interfaces:**
- Consumes: `IWikiService` (Task 5), `IWikiLocaleResolver` (Task 4), `LocalizedWikiPage` / `WikiTranslationSummary` (Task 3), `ILogger<WikiLocalizationService>`.
- Produces, all on `IWikiLocalizationService`:
  - `string DefaultLocale { get; }`
  - `string SourceLocaleOf(WikiPage page)` — the page's materialised source locale, canonicalised. The one accessor every caller uses instead of re-deriving it (Task 12's `GetRevisions` in particular).
  - `Task<OneOf<LocalizedWikiPage, NotFound>> GetLocalizedBySlugAsync(string slug, string? category, WikiNamespace ns, string? requestedLocale, bool includeDrafts)`
  - `Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? requestedLocale, bool includeDrafts)`
  - `Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(IReadOnlyList<WikiPage> pages, string? requestedLocale, bool includeDrafts)`
  - `Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(string pageId, bool includeDrafts)`
  - `Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool includeDrafts)` — source locale plus every visible translation locale, for `hreflang` and the language chip row.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Wiki/WikiLocalizationServiceTests.cs` (tabs):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Wiki;

/// <summary>
/// The service that owns visibility filtering and is the only thing allowed to construct a
/// <see cref="LocalizedWikiPage"/>. The draft-leak cases below are the ones most likely to catch a
/// regression that ships unfinished translations to the public, so they are first-class, not an
/// afterthought.
/// </summary>
public class WikiLocalizationServiceTests
{
	private static (IWikiService Storage, IWikiLocalizationService Service) Build(string defaultLocale = "en")
	{
		var monitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		monitor.CurrentValue.Returns(TestSharpMushOptions.Create(wikiDefaultLocale: defaultLocale));
		var storage = new InMemoryWikiService(new WikiMarkdigPipeline());
		var resolver = new WikiLocaleResolver(monitor);
		return (storage, new WikiLocalizationService(
			storage, resolver, NullLogger<WikiLocalizationService>.Instance));
	}

	private static async Task<WikiPage> SeedAsync(IWikiService storage, string? sourceLocale = "en") =>
		(await storage.CreateAsync("Dragons", "en **body**", "#1", WikiNamespace.Main, "general", sourceLocale)).AsT0;

	[Test]
	public async Task RequestedSourceLocale_ServesThePageWithNoBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "en", false);

		await Assert.That(result.IsT0).IsTrue();
		var localized = result.AsT0;
		await Assert.That(localized.Locale).IsEqualTo("en");
		await Assert.That(localized.IsFallback).IsFalse();
		await Assert.That(localized.Title).IsEqualTo(page.Title);
		await Assert.That(localized.MarkdownSource).IsEqualTo("en **body**");
	}

	[Test]
	public async Task PublishedTranslation_IsServedWithNoBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false);

		var localized = result.AsT0;
		await Assert.That(localized.Locale).IsEqualTo("fr");
		await Assert.That(localized.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(localized.MarkdownSource).IsEqualTo("corps fr");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task UnpublishedTranslation_IsInvisibleToAnOrdinaryReaderWhoGetsTheFallbackAndBanner()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "fr", includeDrafts: false);

		var localized = result.AsT0;
		await Assert.That(localized.Locale)
			.IsEqualTo("en")
			.Because("a draft translation must fall through exactly as if it did not exist");
		await Assert.That(localized.MarkdownSource).IsEqualTo("en **body**");
		await Assert.That(localized.MarkdownSource).DoesNotContain("brouillon");
		await Assert.That(localized.IsFallback)
			.IsTrue()
			.Because("the reader asked for French and got English, so the notice must show");
	}

	[Test]
	public async Task UnpublishedTranslation_IsVisibleToAnEditorPreviewingTheirOwnDraft()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "fr", includeDrafts: true);

		var localized = result.AsT0;
		await Assert.That(localized.Locale).IsEqualTo("fr");
		await Assert.That(localized.MarkdownSource).IsEqualTo("corps brouillon");
		await Assert.That(localized.Published)
			.IsFalse()
			.Because("Published is the served row's flag, so the editor can see it is still a draft");
		await Assert.That(localized.IsFallback).IsFalse();
	}

	[Test]
	public async Task GetVisibleTranslationsAsync_HidesDraftsFromOrdinaryReaders()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var forReader = await service.GetVisibleTranslationsAsync(page.Id, includeDrafts: false);
		var forEditor = await service.GetVisibleTranslationsAsync(page.Id, includeDrafts: true);

		await Assert.That(forReader.Select(t => t.Locale)).IsEquivalentTo(new[] { "fr" });
		await Assert.That(forEditor.Select(t => t.Locale).Order()).IsEquivalentTo(new[] { "de", "fr" });
	}

	[Test]
	public async Task GetVisibleLocalesAsync_IncludesTheSourceLocaleFirst()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var locales = await service.GetVisibleLocalesAsync(page, includeDrafts: false);

		await Assert.That(locales).IsEquivalentTo(new[] { "en", "fr" });
	}

	[Test]
	public async Task StampedSourceLocale_IsNotReinterpretedWhenTheConfiguredDefaultChanges()
	{
		// The regression test for the bug the design fixes. A page authored in French keeps being a French
		// page whatever wiki_default_locale later says, so an admin flipping that setting cannot silently
		// relabel existing content, start rejecting `fr` as "shadowing the source", or change what the
		// revision history means.
		var (storageA, serviceA) = Build("en");
		await SeedAsync(storageA, sourceLocale: "fr");
		var (storageB, serviceB) = Build("de");
		await SeedAsync(storageB, sourceLocale: "fr");

		var onEnglishGame = await serviceA.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "es", false);
		var onGermanGame = await serviceB.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, "es", false);

		await Assert.That(onEnglishGame.AsT0.Locale).IsEqualTo("fr");
		await Assert.That(onGermanGame.AsT0.Locale)
			.IsEqualTo("fr")
			.Because("SourceLocale is materialised once by the migration, never re-derived from configuration");
	}

	[Test]
	public async Task UnstampedSourceLocale_StillRendersAndIsTreatedAsABrokenRow()
	{
		// Reachable only if the Tasks 7-9 backfill has not run. A read can never fail for locale reasons, so
		// the page still renders using the configured default — but this is graceful degradation over a
		// broken row, logged at Warning, NOT a documented meaning for empty. Nothing may depend on it.
		var (storage, service) = Build("fr");
		await SeedAsync(storage, sourceLocale: null);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false);

		await Assert.That(result.IsT0)
			.IsTrue()
			.Because("an unmigrated row must not turn every read of that page into an error");
		await Assert.That(result.AsT0.Locale).IsEqualTo("fr");
	}

	[Test]
	public async Task RegionalRequest_FindsTheNeutralTranslationWithoutBannering()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var result = await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr-CA", false);

		await Assert.That(result.AsT0.Locale).IsEqualTo("fr");
		await Assert.That(result.AsT0.RequestedLocale).IsEqualTo("fr-CA");
		await Assert.That(result.AsT0.IsFallback).IsFalse();
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("not a locale")]
	public async Task MalformedOrAbsentLocale_IsTreatedAsAbsentAndNeverFails(string? requested)
	{
		var (storage, service) = Build();
		await SeedAsync(storage);

		var result = await service.GetLocalizedBySlugAsync(
			"dragons", "general", WikiNamespace.Main, requested, false);

		await Assert.That(result.IsT0)
			.IsTrue()
			.Because("a read can never fail for locale reasons");
		await Assert.That(result.AsT0.Locale).IsEqualTo("en");
		await Assert.That(result.AsT0.IsFallback).IsFalse();
	}

	[Test]
	public async Task MissingPage_StillReturnsNotFound()
	{
		var (_, service) = Build();

		var result = await service.GetLocalizedBySlugAsync("ghost", "general", WikiNamespace.Main, "fr", false);

		await Assert.That(result.IsT1).IsTrue();
	}

	[Test]
	public async Task LocalizeAllAsync_LocalizesEveryPageAndReturnsOneRowPerPage()
	{
		var (storage, service) = Build();
		var first = (await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		var second = (await storage.CreateAsync("Beta", "b", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(first.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, true, expectedRevisionNumber: null);

		var localized = await service.LocalizeAllAsync([first, second], "fr", includeDrafts: false);

		await Assert.That(localized.Count)
			.IsEqualTo(2)
			.Because("listings must still return one row per page, not N rows per locale");
		await Assert.That(localized.Single(p => p.Page.Id == first.Id).Title).IsEqualTo("Alpha (fr)");
		await Assert.That(localized.Single(p => p.Page.Id == second.Id).Title).IsEqualTo("Beta");
		await Assert.That(localized.Single(p => p.Page.Id == second.Id).IsFallback).IsTrue();
	}

	[Test]
	public async Task ResolvedContentNeverLeaksOntoThePage()
	{
		var (storage, service) = Build();
		var page = await SeedAsync(storage);
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, true, expectedRevisionNumber: null);

		var localized = (await service.GetLocalizedBySlugAsync("dragons", "general", WikiNamespace.Main, "fr", false)).AsT0;

		await Assert.That(localized.Page.Title).IsEqualTo("Dragons");
		await Assert.That(localized.Page.MarkdownSource)
			.IsEqualTo("en **body**")
			.Because("Page carries identity and inherited metadata only — never content");
		await Assert.That(localized.Page.Category).IsEqualTo("general");
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiLocalizationServiceTests/*"`
Expected: compile error — `WikiLocalizationService` not found.

- [ ] **Step 3: Create the contract**

Create `SharpMUSH.Library/Services/Interfaces/IWikiLocalizationService.cs` (tabs):

```csharp
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Resolves wiki content for a reader's locale. Controllers, middleware and softcode inject this rather
/// than <see cref="IWikiService"/> when they want content a human will read.
/// </summary>
/// <remarks>
/// This is the only type allowed to construct a <see cref="LocalizedWikiPage"/>, which is what gives the
/// "resolved content lives on the wrapper, never on the page" invariant exactly one enforcement point.
/// It is also where the visibility decision lives: every method takes <c>includeDrafts</c> and filters the
/// candidate set before <see cref="IWikiLocaleResolver.Resolve"/> ever sees it, so the resolver stays
/// permission-blind and an unpublished translation is unreachable rather than merely un-rendered.
/// </remarks>
public interface IWikiLocalizationService
{
	/// <summary>The normalised, game-wide configured fallback locale (<c>Wiki.DefaultLocale</c>).</summary>
	string DefaultLocale { get; }

	/// <summary>
	/// The locale a page was authored in: its materialised <see cref="WikiPage.SourceLocale"/>, canonicalised.
	/// </summary>
	/// <remarks>
	/// Exists so no caller re-derives this. The field is stamped once by <c>Migration_AddWikiTranslations</c>
	/// and by every create path, and is immutable thereafter — the configured default affects new pages and
	/// fallback resolution, never the interpretation of an existing one. An empty value means the backfill has
	/// not run: this method logs a warning and returns <see cref="DefaultLocale"/> so the page still renders,
	/// which is degradation over a broken row rather than a meaning callers may rely on.
	/// </remarks>
	string SourceLocaleOf(WikiPage page);

	/// <summary>
	/// Looks a page up by identity and resolves it into <paramref name="requestedLocale"/>.
	/// Returns <c>NotFound</c> only when the page itself does not exist — never for locale reasons.
	/// </summary>
	/// <param name="includeDrafts">True when the caller may see unpublished translations, i.e. may edit
	/// the page. Ordinary readers pass false and fall back as though drafts were absent.</param>
	Task<OneOf<LocalizedWikiPage, NotFound>> GetLocalizedBySlugAsync(
		string slug, string? category, WikiNamespace ns, string? requestedLocale, bool includeDrafts);

	/// <summary>Resolves an already-loaded page. Never fails.</summary>
	Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? requestedLocale, bool includeDrafts);

	/// <summary>
	/// Resolves a whole listing. Returns exactly one row per input page — localized listings show
	/// localized titles, not N rows per locale.
	/// </summary>
	Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(
		IReadOnlyList<WikiPage> pages, string? requestedLocale, bool includeDrafts);

	/// <summary>Translations of a page this reader may see, ordered by locale.</summary>
	Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(string pageId, bool includeDrafts);

	/// <summary>
	/// Every locale this reader can actually read the page in: the page's source locale first, then each
	/// visible translation's locale. Drives the language chip row and <c>hreflang</c>.
	/// </summary>
	Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool includeDrafts);
}
```

- [ ] **Step 4: Implement the service**

Create `SharpMUSH.Library/Services/WikiLocalizationService.cs` (tabs):

```csharp
using Microsoft.Extensions.Logging;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IWikiLocalizationService"/>
public sealed class WikiLocalizationService(
	IWikiService wikiService,
	IWikiLocaleResolver resolver,
	ILogger<WikiLocalizationService> logger) : IWikiLocalizationService
{
	public string DefaultLocale => resolver.DefaultLocale;

	public async Task<OneOf<LocalizedWikiPage, NotFound>> GetLocalizedBySlugAsync(
		string slug, string? category, WikiNamespace ns, string? requestedLocale, bool includeDrafts)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
		if (lookup.IsT1) return new NotFound();

		return await LocalizeAsync(lookup.AsT0, requestedLocale, includeDrafts);
	}

	public async Task<LocalizedWikiPage> LocalizeAsync(WikiPage page, string? requestedLocale, bool includeDrafts)
	{
		var visible = await VisibleTranslationsAsync(page.Id, includeDrafts);
		return Build(page, requestedLocale, visible);
	}

	public async Task<IReadOnlyList<LocalizedWikiPage>> LocalizeAllAsync(
		IReadOnlyList<WikiPage> pages, string? requestedLocale, bool includeDrafts)
	{
		var results = new List<LocalizedWikiPage>(pages.Count);
		foreach (var page in pages)
		{
			results.Add(await LocalizeAsync(page, requestedLocale, includeDrafts));
		}

		return results.AsReadOnly();
	}

	public async Task<IReadOnlyList<WikiTranslationSummary>> GetVisibleTranslationsAsync(
		string pageId, bool includeDrafts)
	{
		var all = await wikiService.GetTranslationsAsync(pageId);
		return all
			.Where(t => includeDrafts || t.Published)
			.OrderBy(t => t.Locale, StringComparer.OrdinalIgnoreCase)
			.ToList()
			.AsReadOnly();
	}

	public async Task<IReadOnlyList<string>> GetVisibleLocalesAsync(WikiPage page, bool includeDrafts)
	{
		var visible = await GetVisibleTranslationsAsync(page.Id, includeDrafts);
		var source = SourceLocaleOf(page);

		return new[] { source }
			.Concat(visible.Select(t => t.Locale))
			.Where(l => l.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList()
			.AsReadOnly();
	}

	/// <inheritdoc/>
	public string SourceLocaleOf(WikiPage page)
	{
		// Read the materialised value straight through. Substituting the configured default for a stamped
		// value would mean an admin changing wiki_default_locale silently relabels the authored locale of
		// every existing page — an English page starts claiming to be French, UpsertTranslationAsync begins
		// rejecting `fr` as "shadowing the source", and revision history changes meaning, with no migration
		// and nothing to alert on.
		var normalized = WikiHelpers.NormalizeLocaleOrEmpty(page.SourceLocale);
		if (normalized.Length > 0) return normalized;

		// Unreachable once Migration_AddWikiTranslations has run, which is why it is a Warning rather than a
		// branch anything is allowed to depend on. A read can never fail for locale reasons, so the page
		// still renders — but the row is broken, and pre-production the fix is to re-run migrations or wipe
		// and reseed, not to make this substitution part of the design.
		logger.LogWarning(
			"Wiki page {PageId} ({Slug}) has no SourceLocale. The Migration_AddWikiTranslations backfill has "
			+ "not run on this database; serving it as '{DefaultLocale}' for this read only.",
			page.Id, page.Slug, resolver.DefaultLocale);

		return resolver.DefaultLocale;
	}

	/// <summary>The translations this reader may see, as full rows so the body is available if one wins.</summary>
	private async Task<IReadOnlyList<WikiTranslation>> VisibleTranslationsAsync(string pageId, bool includeDrafts)
	{
		var summaries = await wikiService.GetTranslationsAsync(pageId);
		var wanted = summaries.Where(t => includeDrafts || t.Published).Select(t => t.Locale).ToList();

		var rows = new List<WikiTranslation>(wanted.Count);
		foreach (var locale in wanted)
		{
			var row = await wikiService.GetTranslationAsync(pageId, locale);
			if (row.IsT0) rows.Add(row.AsT0);
		}

		return rows;
	}

	/// <summary>
	/// The single construction site for <see cref="LocalizedWikiPage"/>. The requested tag is normalised
	/// here and the source tag comes from <see cref="SourceLocaleOf"/>; together with every write boundary
	/// rejecting an unparseable locale, that is what guarantees the <c>CultureInfo</c> calls in
	/// <c>IsFallback</c> cannot throw.
	/// </summary>
	private LocalizedWikiPage Build(
		WikiPage page, string? requestedLocale, IReadOnlyList<WikiTranslation> visible)
	{
		var source = SourceLocaleOf(page);
		var requested = resolver.NormalizeRequested(requestedLocale);
		var resolution = resolver.Resolve(requested, source, visible.Select(t => t.Locale).ToList());

		var served = visible.FirstOrDefault(
			t => string.Equals(t.Locale, resolution.Locale, StringComparison.OrdinalIgnoreCase));

		return served is null
			? new LocalizedWikiPage(
				Page: page,
				Locale: source,
				RequestedLocale: requested,
				Title: page.Title,
				MarkdownSource: page.MarkdownSource,
				RenderedHtml: page.RenderedHtml,
				PlainText: page.PlainText,
				Published: page.Published,
				RevisionNumber: page.RevisionNumber)
			: new LocalizedWikiPage(
				Page: page,
				Locale: served.Locale,
				RequestedLocale: requested,
				Title: served.Title,
				MarkdownSource: served.MarkdownSource,
				RenderedHtml: served.RenderedHtml,
				PlainText: served.PlainText,
				Published: served.Published,
				RevisionNumber: served.RevisionNumber);
	}
}
```

`LocalizeAllAsync` is a straightforward per-page loop. The spec's listing-performance risk says to **measure before adding a denormalized title cache and not to add one pre-emptively** — do not optimise this here.

- [ ] **Step 5: Register both services**

`SharpMUSH.Server/Startup.cs`, replacing the wiki block at lines 315–317:

```csharp
		// Wiki subsystem — backed by whichever ISharpDatabase is active (all three DB backends implement IWikiService).
		services.AddSingleton<WikiMarkdigPipeline>();
		services.AddSingleton<IWikiService>(sp => (IWikiService)sp.GetRequiredService<ISharpDatabase>());

		// Locale fallback rules (pure) and the one localized-read service every reader path goes through.
		services.AddSingleton<IWikiLocaleResolver, WikiLocaleResolver>();
		services.AddSingleton<IWikiLocalizationService, WikiLocalizationService>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiLocalizationServiceTests/*"`
Expected: PASS — all 15 tests, including both draft-visibility cases and `StampedSourceLocale_IsNotReinterpretedWhenTheConfiguredDefaultChanges`.

Run: `grep -rn "EffectiveSourceLocale" SharpMUSH.Library SharpMUSH.Server`
Expected: **no output**. The re-derive-on-read helper must be gone, not merely unused — `SourceLocaleOf` replaces it and Task 12 consumes it.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Library/Services SharpMUSH.Server/Startup.cs \
  SharpMUSH.Tests/Wiki/WikiLocalizationServiceTests.cs
git commit -m "feat(wiki): add the localization service that owns draft visibility and fallback"
```

---

# Phase 4 — HTTP surface

### Task 11: `?lang=` on the canonical page read

The API already threads `?ns=` and `?category=` as query params, so `?lang=` follows the existing convention. A malformed or unknown tag is treated as absent and falls to the configured default — **never a 400**, logged at Debug.

`WikiPageDto` gains four **trailing init-only** properties rather than positional parameters, so `ToDto(WikiPage)` and every listing endpoint keep compiling unchanged while a new `ToDto(LocalizedWikiPage, …)` overload serves the localized paths.

**Files:**
- Modify: `SharpMUSH.Server/Controllers/WikiController.cs` (constructor gains `IWikiLocalizationService`; DTO gains four properties; `GetPage`, `GetCharacterPage` gain `?lang=`; new `ToDto` overload; new `IncludeDrafts` helper)
- Test: `SharpMUSH.Tests/Server/Controllers/WikiControllerLocaleTests.cs` (create)

**Interfaces:**
- Consumes: `IWikiLocalizationService` (Task 10).
- Produces:
  - `WikiController.WikiPageDto.Locale` / `.RequestedLocale` / `.IsFallback` / `.AvailableLocales` (init-only, defaulted)
  - `GET /api/wiki/ns/{ns}/{category}/{slug}?lang=` and `GET /api/wiki/character/{name}?lang=`
  - `WikiController.IncludeDrafts` → `bool` (private) — `CanSeeUnpublished || User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit)`

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Server/Controllers/WikiControllerLocaleTests.cs` (tabs). Copy the controller-construction and claims-principal setup from the neighbouring `WikiControllerVisibilityTests.cs` — it already builds a `WikiController` over `InMemoryWikiService` with a substituted `IPrerenderCacheService` and a `ClaimsPrincipal` carrying `PortalPermission` claims. Mirror that file's helper names exactly.

```csharp
	[Test]
	public async Task GetPage_WithNoLangParameter_ServesTheSourceLocaleWithoutABanner()
	{
		var (controller, storage) = BuildAnonymous();
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.GetPage("main", "general", "dragons", lang: null);

		var dto = OkDto(result);
		await Assert.That(dto.Locale).IsEqualTo("en");
		await Assert.That(dto.RequestedLocale).IsEqualTo("en");
		await Assert.That(dto.IsFallback).IsFalse();
		await Assert.That(dto.MarkdownSource).IsEqualTo("en body");
	}

	[Test]
	public async Task GetPage_WithLangServesAPublishedTranslation()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(dto.MarkdownSource).IsEqualTo("corps fr");
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.IsFallback).IsFalse();
		await Assert.That(dto.AvailableLocales.Order()).IsEquivalentTo(new[] { "en", "fr" });
	}

	[Test]
	public async Task GetPage_DraftTranslationDoesNotLeakToAnAnonymousReader()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.MarkdownSource).DoesNotContain("brouillon");
		await Assert.That(dto.Locale).IsEqualTo("en");
		await Assert.That(dto.IsFallback).IsTrue();
		await Assert.That(dto.AvailableLocales)
			.IsEquivalentTo(new[] { "en" })
			.Because("advertising a language the reader cannot see would be a dead chip and an hreflang lie");
	}

	[Test]
	public async Task GetPage_DraftTranslationIsVisibleToAnEditor()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetPage("main", "general", "dragons", lang: "fr");

		var dto = OkDto(result);
		await Assert.That(dto.MarkdownSource).IsEqualTo("corps brouillon");
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.Published).IsFalse();
	}

	[Test]
	[Arguments("not a locale")]
	[Arguments("")]
	[Arguments("zz-ZZ")]
	public async Task GetPage_MalformedLangIsNeverA400(string lang)
	{
		var (controller, storage) = BuildAnonymous();
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.GetPage("main", "general", "dragons", lang);

		var dto = OkDto(result);
		await Assert.That(dto.Locale)
			.IsEqualTo("en")
			.Because("a malformed lang tag is treated as absent, never rejected");
	}

	[Test]
	public async Task GetPage_UnpublishedPageStillReturns404ForAnonymousReaders()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Secret", "hidden", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.SetMetadataAsync(page.Id, "general", [], published: false);

		var result = await controller.GetPage("main", "general", "secret", lang: "fr");

		await Assert.That(result).IsTypeOf<NotFoundResult>()
			.Because("localization must not weaken the existing page-level visibility gate");
	}
```

Add a local helper in the same class:

```csharp
	private static WikiController.WikiPageDto OkDto(IActionResult result) =>
		(WikiController.WikiPageDto)((OkObjectResult)result).Value!;
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerLocaleTests/*"`
Expected: compile error — `GetPage` takes three arguments; `WikiPageDto` has no `Locale`.

- [ ] **Step 3: Extend the DTO and inject the service**

In `SharpMUSH.Server/Controllers/WikiController.cs`, change the constructor (lines 44–47):

```csharp
public class WikiController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	IPrerenderCacheService prerenderCache,
	ILogger<WikiController> logger) : ControllerBase
```

Change `WikiPageDto` (lines 50–64) to add trailing init-only properties, leaving every positional parameter alone:

```csharp
	/// <summary>Page data returned by the API. Includes MarkdownSource so the editor can round-trip.</summary>
	public record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber,
		string? Category,
		IReadOnlyList<string> Tags,
		bool Published)
	{
		// Localization fields are init-only with defaults so that ToDto(WikiPage) — used by every
		// endpoint that has not been localized yet — keeps compiling and keeps its current shape.

		/// <summary>The locale actually served.</summary>
		public string Locale { get; init; } = string.Empty;

		/// <summary>The normalised locale the reader asked for.</summary>
		public string RequestedLocale { get; init; } = string.Empty;

		/// <summary>True when a different language was served than requested — drives the reader's notice.</summary>
		public bool IsFallback { get; init; }

		/// <summary>Locales this reader can actually read the page in, source locale first.</summary>
		public IReadOnlyList<string> AvailableLocales { get; init; } = [];
	}
```

Add the localized mapper next to the existing `ToDto` methods:

```csharp
	private static WikiPageDto ToDto(LocalizedWikiPage p, IReadOnlyList<string> availableLocales) => new(
		p.Page.Id, p.Page.Slug, p.Title, p.Page.Namespace, p.MarkdownSource, p.RenderedHtml, p.PlainText,
		p.Page.CreatedAt, p.Page.UpdatedAt, p.Page.IsProtected, p.RevisionNumber,
		p.Page.Category, p.Page.Tags, p.Published)
	{
		Locale = p.Locale,
		RequestedLocale = p.RequestedLocale,
		IsFallback = p.IsFallback,
		AvailableLocales = availableLocales,
	};
```

Add the visibility helper next to `CanSeeUnpublished`:

```csharp
	/// <summary>
	/// True when the caller may see unpublished <em>translations</em>: they can already see drafts, or they
	/// hold the edit scope and so may be previewing their own translation at <c>?lang=</c>.
	/// </summary>
	private bool IncludeDrafts =>
		CanSeeUnpublished || User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiEdit);
```

- [ ] **Step 4: Localize the two page-read endpoints**

Replace `GetPage` (lines 136–143):

```csharp
	/// <summary>
	/// GET /api/wiki/ns/{namespace}/{category}/{slug}?lang=fr
	/// Returns JSON page data for a page identified by (namespace, category, slug), resolved into the
	/// reader's locale, or 404 when the page doesn't exist. This is the canonical page route.
	/// <c>lang</c> is advisory: a malformed or unknown tag is treated as absent and falls to the
	/// configured default rather than producing a 400.
	/// </summary>
	[HttpGet("ns/{ns}/{category}/{slug}")]
	public async Task<IActionResult> GetPage(string ns, string category, string slug, [FromQuery] string? lang = null)
	{
		var result = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (result.IsT1 || !CanSee(result.AsT0)) return NotFound();

		return Ok(await LocalizedDtoAsync(result.AsT0, lang));
	}
```

Replace `GetCharacterPage` (lines 149–156):

```csharp
	[HttpGet("character/{name}")]
	public async Task<IActionResult> GetCharacterPage(string name, [FromQuery] string? lang = null)
	{
		var result = await wikiService.GetBySlugAsync(name, WikiHelpers.DefaultCategory, WikiNamespace.Character);
		if (result.IsT1 || !CanSee(result.AsT0)) return NotFound();

		return Ok(await LocalizedDtoAsync(result.AsT0, lang));
	}
```

and add the shared helper near the other private helpers:

```csharp
	/// <summary>
	/// Resolves a page into the reader's locale and packages it with the locales that reader may see.
	/// A requested tag that does not resolve is logged at Debug — it is a client-side hint, not an error.
	/// </summary>
	private async Task<WikiPageDto> LocalizedDtoAsync(WikiPage page, string? lang)
	{
		var includeDrafts = IncludeDrafts;
		var localized = await localization.LocalizeAsync(page, lang, includeDrafts);
		var available = await localization.GetVisibleLocalesAsync(page, includeDrafts);

		// Read path: a bad tag is a client-side hint, never a 400. NormalizeLocaleOrEmpty is the permissive
		// form for exactly this reason — the OneOf-returning NormalizeLocale belongs at write boundaries.
		if (!string.IsNullOrWhiteSpace(lang) && WikiHelpers.NormalizeLocaleOrEmpty(lang).Length == 0)
			logger.LogDebug("Unrecognised wiki lang tag ignored: {Lang}", LogSanitizer.Sanitize(lang));

		return ToDto(localized, available);
	}
```

Add `using SharpMUSH.Library.Models.Wiki;` if the `LocalizedWikiPage` reference needs it (the file already imports that namespace at line 5).

- [ ] **Step 5: Fix the existing controller-test constructions**

Every existing test that news up a `WikiController` now needs the extra argument. Find them and add a real `WikiLocalizationService` over the same storage instance (a substitute would silently return nulls):

Run: `grep -rn "new WikiController(" SharpMUSH.Tests SharpMUSH.Tests.BUnit SharpMUSH.Tests.Integration`

For each hit, construct as:

```csharp
		var resolver = new WikiLocaleResolver(optionsMonitor);
		var controller = new WikiController(
			storage,
			new WikiLocalizationService(storage, resolver, NullLogger<WikiLocalizationService>.Instance),
			Substitute.For<IPrerenderCacheService>(),
			Substitute.For<ILogger<WikiController>>());
```

where `optionsMonitor` is the `IOptionsMonitor<SharpMUSHOptions>` substitute pattern from Task 4 Step 1.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerLocaleTests/*"`
Expected: PASS.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed. `WikiControllerVisibilityTests`, `WikiControllerProtectionTests` and `WikiControllerHtmlTests` must all still pass unchanged in behaviour.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Server/Controllers/WikiController.cs SharpMUSH.Tests
git commit -m "feat(wiki): serve localized page reads through ?lang="
```

---

### Task 12: Translation CRUD endpoints and per-locale revisions

Writes are gated on the same permission as editing the page (`PortalPermission.WikiEdit`) **and** on the *source* page's `IsProtected`, mirroring `UpdatePage` exactly. Deleting a translation is an edit, not a page deletion, so it is `WikiEdit` rather than `WikiDelete`.

The upsert request carries the client's **`ExpectedRevisionNumber`** and the controller passes it straight through. A stale value comes back as an `Error<string>` that this endpoint surfaces as **409 Conflict**, not 400: the request was well-formed and the client's correct response is to reload, which is a different instruction from "you sent something invalid". Nothing here retries.

This task also gives `WikiController.CreatePage` its `sourceLocale`, so pages created through the API are stamped at birth rather than waiting for the next migration pass.

**Files:**
- Modify: `SharpMUSH.Server/Controllers/WikiController.cs` (four new actions; `sourceLocale` on `CreatePage`; `?lang=` on `GetRevisions`; new DTO + request records; update the routes doc block at lines 21–41)
- Test: `SharpMUSH.Tests/Server/Controllers/WikiControllerTranslationTests.cs` (create)

**Interfaces:**
- Consumes: `IWikiLocalizationService` (including `SourceLocaleOf` and `DefaultLocale`), `IWikiService` translation methods, `IncludeDrafts` (Task 11).
- Produces:
  - `GET /api/wiki/{slug}/translations?ns=&category=` → `WikiTranslationSummaryDto[]`
  - `PUT /api/wiki/{slug}/translations/{locale}?ns=&category=` → `WikiTranslationSummaryDto`, or **409** on a revision conflict
  - `DELETE /api/wiki/{slug}/translations/{locale}?ns=&category=` → 204
  - `GET /api/wiki/{slug}/revisions?lang=` → that locale's stream
  - `WikiController.WikiTranslationSummaryDto(string Locale, string Title, bool Published, DateTimeOffset UpdatedAt, int RevisionNumber)`
  - `WikiController.UpsertTranslationRequest(string Title, string Markdown, string? EditSummary, bool Published, int? ExpectedRevisionNumber)`

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Server/Controllers/WikiControllerTranslationTests.cs` (tabs), reusing the same `BuildAnonymous` / `BuildWithClaims` helpers introduced in Task 11:

```csharp
	[Test]
	public async Task PutTranslation_CreatesTheTranslationForAnEditor()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.PutTranslation(
			"dragons", "fr",
			new WikiController.UpsertTranslationRequest(
				"Dragons (fr)", "corps fr", "première", Published: true, ExpectedRevisionNumber: null),
			ns: "main", category: "general");

		var dto = (WikiController.WikiTranslationSummaryDto)((OkObjectResult)result).Value!;
		await Assert.That(dto.Locale).IsEqualTo("fr");
		await Assert.That(dto.Title).IsEqualTo("Dragons (fr)");
		await Assert.That(dto.RevisionNumber).IsEqualTo(1);
	}

	[Test]
	public async Task PutTranslation_RejectsShadowingTheSourceLocaleWithBadRequest()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.PutTranslation(
			"dragons", "en",
			new WikiController.UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}

	[Test]
	public async Task PutTranslation_OnAProtectedPageRequiresWikiAdmin()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.SetProtectionAsync(page.Id, isProtected: true);

		var result = await controller.PutTranslation(
			"dragons", "fr",
			new WikiController.UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<ForbidResult>()
			.Because("a translation write is gated on the source page's IsProtected, same as a page edit");
	}

	[Test]
	public async Task PutTranslation_OnAMissingPageIs404()
	{
		var (controller, _) = BuildWithClaims(PortalPermission.WikiEdit);

		var result = await controller.PutTranslation(
			"ghost", "fr",
			new WikiController.UpsertTranslationRequest("T", "m", null, true, null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NotFoundResult>();
	}

	[Test]
	public async Task PutTranslation_ReturnsConflictOnAStaleExpectedRevision()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "fr", "v2", "corps v2", "#2", null, true, expectedRevisionNumber: 1);

		var result = await controller.PutTranslation(
			"dragons", "fr",
			new WikiController.UpsertTranslationRequest("perdu", "corps perdu", null, true, ExpectedRevisionNumber: 1),
			ns: "main", category: "general");

		await Assert.That(result)
			.IsTypeOf<ConflictObjectResult>()
			.Because("the request was well-formed; the client's correct response is to reload, not to fix its body");
		await Assert.That((await storage.GetTranslationAsync(page.Id, "fr")).AsT0.MarkdownSource)
			.IsEqualTo("corps v2")
			.Because("the endpoint must never retry a conflict — that re-applies the loser's stale markdown");
	}

	[Test]
	public async Task PutTranslation_ReturnsConflictWhenCreateOnlyHitsAnExistingTranslation()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "v1", "corps v1", "#2", null, true, expectedRevisionNumber: null);

		var result = await controller.PutTranslation(
			"dragons", "fr",
			new WikiController.UpsertTranslationRequest("écrasé", "corps écrasé", null, true, ExpectedRevisionNumber: null),
			ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<ConflictObjectResult>();
	}

	[Test]
	public async Task CreatePage_StampsTheConfiguredDefaultAsTheSourceLocale()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);

		await controller.CreatePage(
			new WikiController.CreatePageRequest("Dragons", "en body", Namespace: "main", Category: "general"));

		var created = await storage.GetBySlugAsync("dragons", "general", WikiNamespace.Main);
		await Assert.That(created.AsT0.SourceLocale)
			.IsEqualTo("en")
			.Because("SourceLocale is materialised at creation, not re-derived on every later read");
	}

	[Test]
	public async Task GetTranslations_HidesDraftsFromAnonymousReaders()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, published: true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "de", "T", "m", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.GetTranslations("dragons", ns: "main", category: "general");

		var dtos = (IEnumerable<WikiController.WikiTranslationSummaryDto>)((OkObjectResult)result).Value!;
		await Assert.That(dtos.Select(d => d.Locale)).IsEquivalentTo(new[] { "fr" });
	}

	[Test]
	public async Task DeleteTranslation_RemovesOneLocaleAndReturns204()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiEdit);
		var page = (await storage.CreateAsync("Dragons", "en body", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "m", "#2", null, true, expectedRevisionNumber: null);

		var result = await controller.DeleteTranslation("dragons", "fr", ns: "main", category: "general");

		await Assert.That(result).IsTypeOf<NoContentResult>();
		await Assert.That((await storage.GetTranslationAsync(page.Id, "fr")).IsT1).IsTrue();
	}

	[Test]
	public async Task GetRevisions_WithLangReturnsThatLocaleStream()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Dragons", "v1", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.UpdateAsync(page.Id, "v2", "#1");
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "fr1", "#2", null, true, expectedRevisionNumber: null);
		await storage.UpsertTranslationAsync(page.Id, "fr", "T", "fr2", "#2", null, true, expectedRevisionNumber: 1);

		var french = await controller.GetRevisions("dragons", 0, 20, "main", "general", lang: "fr");
		var source = await controller.GetRevisions("dragons", 0, 20, "main", "general", lang: null);

		var frenchDtos = (IEnumerable<WikiController.WikiRevisionDto>)((OkObjectResult)french).Value!;
		var sourceDtos = (IEnumerable<WikiController.WikiRevisionDto>)((OkObjectResult)source).Value!;
		await Assert.That(frenchDtos.Count()).IsEqualTo(2);
		await Assert.That(frenchDtos.Select(d => d.MarkdownSource)).Contains("fr2");
		await Assert.That(sourceDtos.Count())
			.IsEqualTo(2)
			.Because("omitting lang must keep returning the source stream the history page already shows");
	}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerTranslationTests/*"`
Expected: compile error — `PutTranslation` / `GetTranslations` / `DeleteTranslation` not found.

- [ ] **Step 3: Add the DTO and request records**

In `WikiController`, next to the other nested records:

```csharp
	/// <summary>A translation without its body — enough for locale lists and hreflang.</summary>
	public record WikiTranslationSummaryDto(
		string Locale,
		string Title,
		bool Published,
		DateTimeOffset UpdatedAt,
		int RevisionNumber);

	/// <summary>Request body for creating or updating one locale's translation of a page.</summary>
	/// <param name="ExpectedRevisionNumber">
	/// The <c>RevisionNumber</c> the editor loaded, for optimistic concurrency. Null means create-only.
	/// A stale value is answered with 409 and must not be retried — see <see cref="PutTranslation"/>.
	/// </param>
	public record UpsertTranslationRequest(
		string Title,
		string Markdown,
		string? EditSummary,
		bool Published,
		int? ExpectedRevisionNumber);
```

and the mapper next to the other `ToDto` methods:

```csharp
	private static WikiTranslationSummaryDto ToDto(WikiTranslationSummary t) =>
		new(t.Locale, t.Title, t.Published, t.UpdatedAt, t.RevisionNumber);
```

- [ ] **Step 4: Add the four actions**

Append to `WikiController` (before the static prerender generators):

```csharp
	/// <summary>
	/// GET /api/wiki/{slug}/translations?ns=&amp;category=
	/// Lists the translations of a page that this reader may see. Drafts are omitted for readers without
	/// the edit scope, so the language chips and <c>hreflang</c> never advertise a page nobody can open.
	/// </summary>
	[HttpGet("{slug}/translations")]
	public async Task<IActionResult> GetTranslations(
		string slug, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1 || !CanSee(lookup.AsT0)) return NotFound();

		var summaries = await localization.GetVisibleTranslationsAsync(lookup.AsT0.Id, IncludeDrafts);
		return Ok(summaries.Select(ToDto));
	}

	/// <summary>
	/// PUT /api/wiki/{slug}/translations/{locale}?ns=&amp;category=
	/// Creates or updates one locale's translation. Gated on the page-edit scope and on the source page's
	/// <c>IsProtected</c>, exactly as <see cref="UpdatePage"/> is: a translation is an edit to the page.
	/// </summary>
	[HttpPut("{slug}/translations/{locale}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> PutTranslation(
		string slug, string locale, [FromBody] UpsertTranslationRequest request,
		[FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");

		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await wikiService.UpsertTranslationAsync(
			lookup.AsT0.Id, locale, request.Title, request.Markdown,
			editorDbref, request.EditSummary, request.Published, request.ExpectedRevisionNumber);

		return result.Match<IActionResult>(
			translation =>
			{
				logger.LogInformation(
					"Wiki translation saved: slug={Slug} locale={Locale} rev={Rev} by={Editor}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(translation.Locale),
					translation.RevisionNumber, LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return Ok(new WikiTranslationSummaryDto(
					translation.Locale, translation.Title, translation.Published,
					translation.UpdatedAt, translation.RevisionNumber));
			},
			// A revision conflict is 409, not 400: the request was well-formed and the client's correct
			// response is to reload, which is a different instruction from "your body was invalid". The
			// endpoint does not retry — retrying would re-apply this caller's stale markdown over the
			// winner's, which is the loss expectedRevisionNumber exists to prevent.
			err => IsRevisionConflict(err.Value)
				? Conflict(new { error = err.Value, reload = true })
				: BadRequest(new { error = err.Value }));
	}

	/// <summary>
	/// True when an upsert error is an optimistic-concurrency conflict rather than a bad request.
	/// </summary>
	/// <remarks>
	/// Matched on the storage layer's wording because <c>OneOf&lt;T, Error&lt;string&gt;&gt;</c> carries no
	/// error code. If a third conflict phrasing is ever added, promote this to a typed error rather than
	/// growing the string list — this is the one place that would silently start answering 400.
	/// </remarks>
	private static bool IsRevisionConflict(string error) =>
		error.Contains("changed while you were editing", StringComparison.OrdinalIgnoreCase)
		|| error.Contains("already exists for page", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// DELETE /api/wiki/{slug}/translations/{locale}?ns=&amp;category=
	/// Removes one locale's translation and its revision stream. The page and every other translation are
	/// untouched, and deleting the last translation is allowed. Gated as an edit, not a page deletion.
	/// </summary>
	[HttpDelete("{slug}/translations/{locale}")]
	[Authorize(Policy = PortalPermission.WikiEdit)]
	public async Task<IActionResult> DeleteTranslation(
		string slug, string locale, [FromQuery] string? ns = null, [FromQuery] string? category = null)
	{
		var editorDbref = CallerDbref;
		if (string.IsNullOrEmpty(editorDbref))
			return Unauthorized("Missing character identity.");

		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();

		if (lookup.AsT0.IsProtected && !User.HasClaim(PortalPermission.ClaimType, PortalPermission.WikiAdmin))
			return Forbid();

		var result = await wikiService.DeleteTranslationAsync(lookup.AsT0.Id, locale, editorDbref);

		return result.Match<IActionResult>(
			_ =>
			{
				logger.LogInformation(
					"Wiki translation deleted: slug={Slug} locale={Locale} by={Editor}",
					LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(locale), LogSanitizer.Sanitize(editorDbref));
				prerenderCache.InvalidatePrefix($"/wiki/");
				return NoContent();
			},
			_ => NotFound());
	}
```

- [ ] **Step 5: Add `?lang=` to `GetRevisions`**

Replace `GetRevisions` (lines 225–235). The existing signature's trailing optional params stay in place so no current caller breaks:

```csharp
	/// <summary>
	/// GET /api/wiki/{slug}/revisions?skip=&amp;take=&amp;ns=&amp;category=&amp;lang=
	/// Revision history, newest first. Omitting <c>lang</c> (or naming the page's source locale) returns
	/// the source-locale stream, which is exactly what this route returned before translations existed.
	/// </summary>
	[HttpGet("{slug}/revisions")]
	public async Task<IActionResult> GetRevisions(
		string slug, [FromQuery] int skip = 0, [FromQuery] int take = 20,
		[FromQuery] string? ns = null, [FromQuery] string? category = null,
		[FromQuery] string? lang = null)
	{
		var lookup = await wikiService.GetBySlugAsync(slug, category, ParseNamespace(ns));
		if (lookup.IsT1) return NotFound();
		if (!CanSee(lookup.AsT0)) return NotFound();

		var page = lookup.AsT0;
		var localized = await localization.LocalizeAsync(page, lang, IncludeDrafts);

		// The source page's revisions are stored with an empty Locale; a translation's carry its tag.
		// SourceLocaleOf, not a local re-derivation: SourceLocale is materialised once and there is exactly
		// one accessor for it, so a controller cannot start disagreeing with the resolver about what
		// language a page was authored in.
		var stream = string.Equals(
			localized.Locale, localization.SourceLocaleOf(page), StringComparison.OrdinalIgnoreCase)
			? string.Empty
			: localized.Locale;

		var revisions = await wikiService.GetRevisionsForLocaleAsync(page.Id, stream, skip, take);
		return Ok(revisions.Select(ToDto));
	}
```

- [ ] **Step 5a: Stamp `SourceLocale` on API-created pages**

`CreatePage` (line 276) creates a page without a source locale today, which would leave it unstamped until the next migration pass. Pass the configured default — the one place that legitimately turns "no locale supplied" into a concrete one, at **write** time:

```csharp
		// SourceLocale is materialised at creation. The configured default affects new pages and fallback
		// resolution only; it never reinterprets a page that already exists.
		var result = await wikiService.CreateAsync(
			request.Title, request.Markdown, authorDbref, ns, request.Category, localization.DefaultLocale);
```

- [ ] **Step 6: Update the routes doc block**

In the `<summary>` at lines 21–41, add after the `{slug}/revisions/{n}` line:

```
///   GET    /api/wiki/{slug}/translations — locales this reader may read the page in
///   PUT    /api/wiki/{slug}/translations/{locale} — create/update one locale (authenticated)
///   DELETE /api/wiki/{slug}/translations/{locale} — remove one locale (authenticated)
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerTranslationTests/*"`
Expected: PASS, including both 409 cases and `CreatePage_StampsTheConfiguredDefaultAsTheSourceLocale`.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Server/Controllers/WikiController.cs SharpMUSH.Tests/Server/Controllers
git commit -m "feat(wiki): add translation CRUD endpoints with optimistic concurrency"
```

---

### Task 13: Localized listings

The five listing endpoints gain `?lang=` and return localized titles. **They still return one row per page** — that constraint is the whole reason the overlay shape was chosen over a `Locale` identity column.

Do **not** add a denormalized title cache. The spec's listing-performance risk explicitly says to measure first and not to pre-empt.

**Files:**
- Modify: `SharpMUSH.Server/Controllers/WikiController.cs` (`GetRecentChanges`, `ListNamespacePages`, `ListAllPages`, `ListCategoryPages`, `ListTagPages`)
- Test: `SharpMUSH.Tests/Server/Controllers/WikiControllerLocaleTests.cs` (append)

**Interfaces:**
- Consumes: `IWikiLocalizationService.LocalizeAllAsync` (Task 10); `ToDto(LocalizedWikiPage, IReadOnlyList<string>)` (Task 11).
- Produces: `?lang=` on all five listing routes.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests/Server/Controllers/WikiControllerLocaleTests.cs`:

```csharp
	[Test]
	public async Task GetRecentChanges_WithLangReturnsLocalizedTitlesOneRowPerPage()
	{
		var (controller, storage) = BuildAnonymous();
		var alpha = (await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en")).AsT0;
		await storage.CreateAsync("Beta", "b", "#1", WikiNamespace.Main, "general", "en");
		await storage.UpsertTranslationAsync(alpha.Id, "fr", "Alpha (fr)", "a-fr", "#2", null, published: true, expectedRevisionNumber: null);

		var result = await controller.GetRecentChanges(count: 20, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Count)
			.IsEqualTo(2)
			.Because("a localized listing must not return N rows per page");
		await Assert.That(dtos.Single(d => d.Slug == "alpha").Title).IsEqualTo("Alpha (fr)");
		await Assert.That(dtos.Single(d => d.Slug == "alpha").IsFallback).IsFalse();
		await Assert.That(dtos.Single(d => d.Slug == "beta").Title).IsEqualTo("Beta");
		await Assert.That(dtos.Single(d => d.Slug == "beta").IsFallback).IsTrue();
	}

	[Test]
	public async Task ListAllPages_WithLangKeepsTheTotalCountHeaderSemantics()
	{
		var (controller, storage) = BuildWithClaims(PortalPermission.WikiRead);
		await storage.CreateAsync("Alpha", "a", "#1", WikiNamespace.Main, "general", "en");
		await storage.CreateAsync("Beta", "b", "#1", WikiNamespace.Main, "general", "en");

		var result = await controller.ListAllPages(skip: 0, take: 50, ns: null, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Count).IsEqualTo(2);
		await Assert.That(controller.Response.Headers["X-Total-Count"].ToString()).IsEqualTo("2");
	}

	[Test]
	public async Task ListNamespacePages_DraftTranslationDoesNotChangeAListedTitle()
	{
		var (controller, storage) = BuildAnonymous();
		var page = (await storage.CreateAsync("Help Intro", "h", "#1", WikiNamespace.Help, "general", "en")).AsT0;
		await storage.UpsertTranslationAsync(page.Id, "fr", "Intro (brouillon)", "h-fr", "#2", null, published: false, expectedRevisionNumber: null);

		var result = await controller.ListNamespacePages("help", skip: 0, take: 50, lang: "fr");

		var dtos = ((IEnumerable<WikiController.WikiPageDto>)((OkObjectResult)result).Value!).ToList();
		await Assert.That(dtos.Single().Title)
			.IsEqualTo("Help Intro")
			.Because("an unpublished translation must not surface its title in a public listing");
	}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerLocaleTests/*"`
Expected: compile error — `GetRecentChanges` takes one argument.

- [ ] **Step 3: Add a shared listing helper**

In `WikiController`, next to `LocalizedDtoAsync`:

```csharp
	/// <summary>
	/// Localizes a listing into the reader's locale, one DTO per page. <c>AvailableLocales</c> is left
	/// empty here on purpose: a listing does not drive language chips or hreflang, and loading every
	/// page's translation set to fill it would be N extra queries for data nothing reads.
	/// </summary>
	private async Task<IEnumerable<WikiPageDto>> LocalizedListAsync(IEnumerable<WikiPage> pages, string? lang)
	{
		var visible = FilterVisible(pages).ToList();
		var localized = await localization.LocalizeAllAsync(visible, lang, IncludeDrafts);
		return localized.Select(p => ToDto(p, []));
	}
```

- [ ] **Step 4: Localize the five listing actions**

`GetRecentChanges` (lines 162–171):

```csharp
	/// <summary>
	/// GET /api/wiki/recent?count=20&amp;lang=fr
	/// Returns recently updated pages with titles resolved into the reader's locale, one row per page.
	/// </summary>
	[HttpGet("recent")]
	public async Task<IActionResult> GetRecentChanges([FromQuery] int count = 20, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetRecentChangesAsync(count);
		return Ok(await LocalizedListAsync(pages, lang));
	}
```

`ListNamespacePages` (lines 173–184):

```csharp
	[HttpGet("ns/{ns}")]
	public async Task<IActionResult> ListNamespacePages(
		string ns, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByNamespaceAsync(ParseNamespace(ns), skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}
```

`ListAllPages` (lines 186–201) — keep the existing `X-Total-Count` logic exactly as it is, only swapping the projection:

```csharp
	[HttpGet("pages")]
	public async Task<IActionResult> ListAllPages(
		[FromQuery] int skip = 0, [FromQuery] int take = 50,
		[FromQuery] string? ns = null, [FromQuery] string? lang = null)
	{
		var parsed = ParseOptionalNamespace(ns);
		var pages = await wikiService.GetAllPagesAsync(skip, take, parsed);

		if (CanSeeUnpublished)
			Response.Headers["X-Total-Count"] = (await wikiService.CountPagesAsync(parsed)).ToString();

		return Ok(await LocalizedListAsync(pages, lang));
	}
```

Preserve whatever the current body does around the header — read it before editing and change only the final projection; the header is gated on `CanSeeUnpublished` today and that must not change.

`ListCategoryPages` (lines 203–212) and `ListTagPages` (lines 214–223) get the same treatment:

```csharp
	[HttpGet("category/{category}")]
	public async Task<IActionResult> ListCategoryPages(
		string category, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByCategoryAsync(category, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}

	[HttpGet("tag/{tag}")]
	public async Task<IActionResult> ListTagPages(
		string tag, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] string? lang = null)
	{
		var pages = await wikiService.GetByTagAsync(tag, skip, take);
		return Ok(await LocalizedListAsync(pages, lang));
	}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiControllerLocaleTests/*"`
Expected: PASS.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed. `WikiControllerVisibilityTests` covers the listing visibility gate and must stay green — `LocalizedListAsync` calls `FilterVisible` first, exactly as the old code did.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Server/Controllers/WikiController.cs SharpMUSH.Tests/Server/Controllers
git commit -m "feat(wiki): localize the listing endpoints without multiplying rows"
```

---

# Phase 5 — Portal

**Correction to one spec line, recorded so nobody hunts for a control that does not exist.** The spec says `WikiEdit.razor`'s "category/tags/protection fields render visible but disabled" on a non-source locale. `WikiEdit.razor` has **no protection control** — `grep -rn IsProtected SharpMUSH.Client/` shows protection lives only in `AdminWiki.razor`'s batch actions and is page-level, so there is nothing there to disable. The editable metadata in `WikiEdit.razor` is Title, Content, Category, Tags and Published. Of those: **Category and Tags are inherited and get disabled**; **Title, Content and Published belong to the translation and stay editable** (a translation owns its own `Published` flag — that is the whole point of letting a translator draft French while English stays live). This is the spec's decision applied to the actual UI, not a change to it.

### Task 14: Client plumbing — `lang` on every request, locale on every model

`GetWikiArticle` encodes ns/category in the **path**, while mutation and revision routes use the `KeyQuery` helper for query params. `lang` is a query param in both cases, appended after whatever each URL already builds.

`LanguagePicker.razor` currently owns the portal's locale list as a private static array. Task 16 needs the same list for the editor's dropdown, so it moves to a shared `PortalLocales` and the picker consumes it — one list, two readers.

**Files:**
- Create: `SharpMUSH.Client/Models/WikiTranslationInfo.cs`
- Create: `SharpMUSH.Client/Resources/PortalLocales.cs`
- Modify: `SharpMUSH.Client/Components/LanguagePicker.razor` (consume `PortalLocales`)
- Modify: `SharpMUSH.Client/Models/WikiArticle.cs` (four locale properties + `RevisionNumber`)
- Create: `SharpMUSH.Client/Models/WikiTranslationSaveError.cs` (message + `NeedsReload`, so a 409 is distinguishable from a 400)
- Modify: `SharpMUSH.Client/Models/WikiPageSummary.cs` (`Locale`, `IsFallback`)
- Modify: `SharpMUSH.Client/Services/WikiService.cs` (DTO mirror, `lang` params, three new methods, `LangQuery` helper, 409 handling on the upsert)
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx`, `SharedResource.fr.resx`
- Test: `SharpMUSH.Tests.BUnit/Resources/PortalLocalesTests.cs` (create)
- Test: `SharpMUSH.Tests.BUnit/Resources/SharedResourceLocalizationTests.cs` (append a guard)

**Interfaces:**
- Consumes: the Task 11–13 endpoints.
- Produces:
  - `SharpMUSH.Client.Resources.PortalLocales.Supported` → `IReadOnlyList<(string Code, string Flag)>`
  - `PortalLocales.Codes` → `IReadOnlyList<string>`
  - `PortalLocales.DisplayName(string code)` → `string` (native name, first letter upper-cased)
  - `SharpMUSH.Client.Models.WikiTranslationInfo(string Locale, string Title, bool Published, DateTimeOffset UpdatedAt, int RevisionNumber)`
  - `WikiArticle.Locale` / `.RequestedLocale` / `.IsFallback` / `.AvailableLocales` (`List<string>`) / `.RevisionNumber` (`int`) — the **served** row's revision number, which Task 16 passes back as `expectedRevisionNumber`
  - `WikiPageSummary.Locale` / `.IsFallback` (init-only)
  - `WikiService.GetWikiArticle(string slug, string? category = null, string? ns = null, string? lang = null)`
  - `WikiService.GetTranslationsAsync(string slug, string? ns = null, string? category = null)` → `ValueTask<IReadOnlyList<WikiTranslationInfo>>`
  - `WikiService.UpsertTranslationAsync(string slug, string locale, string title, string markdown, bool published, int? expectedRevisionNumber, string? editSummary = null, string? ns = null, string? category = null)` → `ValueTask<OneOf<WikiTranslationInfo, WikiTranslationSaveError>>`
  - `SharpMUSH.Client.Models.WikiTranslationSaveError(string Message, bool NeedsReload)` — `NeedsReload` is set from a 409 so `WikiEdit` can offer a reload instead of showing a plain failure toast
  - `WikiService.DeleteTranslationAsync(string slug, string locale, string? ns = null, string? category = null)` → `ValueTask<OneOf<None, string>>`
  - `lang` as a trailing optional parameter on `GetRecentChangesAsync`, `GetNamespacePagesAsync`, `GetAllPagesAsync`, `GetByCategoryAsync`, `GetByTagAsync`, `GetRevisionsAsync`
- New resx keys produced here (both files): `WikiFallbackNotice`, `WikiFallbackCreateTranslation`, `WikiAvailableTranslations`, `WkLocaleSelector`, `WkAddTranslation`, `WkSourceLocaleLabel`, `WkInheritedFromSource`, `WkTranslationSaved`, `WkTranslationSaveFailed`, `WkDeleteTranslation`, `WkTranslationDeleteConfirmTitle`, `WkTranslationDeleteConfirmText`, `WkCustomLocalePlaceholder`, `WkHistoryLocale`, `WkTranslationConflict`, `WkTranslationReload`, `WikiTranslations`, `WikiLocaleFilter`, `WikiAllLocales`, `WikiUntranslatedOnly`, `ResWikiStatTranslations`.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Resources/PortalLocalesTests.cs` (tabs):

```csharp
using System.Globalization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The portal's locale list moved out of <c>LanguagePicker.razor</c> so the wiki editor's locale dropdown
/// reads the same list. If the two ever diverge, a language appears in one place and not the other.
/// </summary>
public class PortalLocalesTests
{
	[Test]
	public async Task Supported_ContainsTheTwoLocalesWithSatelliteResources()
	{
		await Assert.That(PortalLocales.Codes).IsEquivalentTo(new[] { "en", "fr" });
	}

	[Test]
	public async Task Every_supported_code_is_a_real_culture()
	{
		foreach (var code in PortalLocales.Codes)
		{
			await Assert.That(CultureInfo.GetCultureInfo(code, predefinedOnly: true).Name).IsEqualTo(code);
		}
	}

	[Test]
	public async Task DisplayName_UsesTheNativeNameCapitalised()
	{
		await Assert.That(PortalLocales.DisplayName("fr")).IsEqualTo("Français");
		await Assert.That(PortalLocales.DisplayName("en")).IsEqualTo("English");
	}

	[Test]
	public async Task DisplayName_FallsBackToTheTagForAnUnknownLocale()
	{
		await Assert.That(PortalLocales.DisplayName("zz-ZZ"))
			.IsEqualTo("zz-ZZ")
			.Because("a game may translate into a locale the portal chrome has no resx for");
	}

	[Test]
	public async Task Flag_IsPresentForEverySupportedLocale()
	{
		foreach (var (code, flag) in PortalLocales.Supported)
		{
			await Assert.That(flag).IsNotEmpty().Because($"{code} has no flag emoji");
		}
	}
}
```

Append to `SharpMUSH.Tests.BUnit/Resources/SharedResourceLocalizationTests.cs` (add `using SharpMUSH.Client.Resources;` if absent — it is already there):

```csharp
	[Test]
	public async Task Every_wiki_localization_string_is_in_the_resx()
	{
		var loc = PortalLocalizer.Create();

		string[] keys =
		[
			"WikiFallbackNotice", "WikiFallbackCreateTranslation", "WikiAvailableTranslations",
			"WkLocaleSelector", "WkAddTranslation", "WkSourceLocaleLabel", "WkInheritedFromSource",
			"WkTranslationSaved", "WkTranslationSaveFailed", "WkDeleteTranslation",
			"WkTranslationDeleteConfirmTitle", "WkTranslationDeleteConfirmText",
			"WkCustomLocalePlaceholder", "WkHistoryLocale",
			"WkTranslationConflict", "WkTranslationReload",
			"WikiTranslations", "WikiLocaleFilter", "WikiAllLocales", "WikiUntranslatedOnly",
			"ResWikiStatTranslations",
		];

		var missing = keys.Where(k => loc[k].ResourceNotFound).ToList();

		await Assert.That(missing).IsEmpty();
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/PortalLocalesTests/*"`
Expected: compile error — `PortalLocales` not found.

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/SharedResourceLocalizationTests/Every_wiki_localization_string_is_in_the_resx"`
Expected: FAIL listing all 19 keys.

- [ ] **Step 3: Create `PortalLocales` and refactor the picker onto it**

Create `SharpMUSH.Client/Resources/PortalLocales.cs` (tabs):

```csharp
using System.Globalization;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// The locales the portal chrome ships translations for. One list, read by the nav language picker and by
/// the wiki editor's locale dropdown, so a language can never appear in one and not the other.
/// </summary>
/// <remarks>
/// This is deliberately <em>not</em> the set of locales wiki content may be translated into. A game may
/// translate its wiki into any locale <see cref="CultureInfo.GetCultureInfo(string, bool)"/> accepts — the
/// chrome falls back to English, the content does not — so the editor offers this list plus whatever
/// translations already exist plus a free-text field.
/// <para>To add a language: add its code and flag here, and add the matching
/// <c>SharedResource.{code}.resx</c>. Display names come from the framework.</para>
/// </remarks>
public static class PortalLocales
{
	/// <summary>Supported locale codes with their flag emoji, in display order.</summary>
	public static IReadOnlyList<(string Code, string Flag)> Supported { get; } =
	[
		("en", "\U0001F1FA\U0001F1F8"),
		("fr", "\U0001F1EB\U0001F1F7"),
	];

	/// <summary>Just the codes, for membership tests and dropdown population.</summary>
	public static IReadOnlyList<string> Codes { get; } = Supported.Select(l => l.Code).ToArray();

	/// <summary>
	/// A locale's name in its own language, first character upper-cased ("Français"). Falls back to the
	/// tag itself for a locale this runtime's ICU data does not know, which is expected: the WASM build
	/// ships a sharded ICU covering only the locales in <see cref="Supported"/>.
	/// </summary>
	public static string DisplayName(string code)
	{
		try
		{
			var culture = CultureInfo.GetCultureInfo(code, predefinedOnly: true);
			var name = culture.NativeName;
			if (name.Length > 0 && char.IsLower(name[0]))
				name = char.ToUpper(name[0], culture) + name[1..];
			return name;
		}
		catch (CultureNotFoundException)
		{
			return code;
		}
	}

	/// <summary>The flag emoji for a supported locale, or a globe for anything else.</summary>
	public static string Flag(string code) =>
		Supported.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Flag
		?? "\U0001F310";
}
```

`SharpMUSH.Client/Components/LanguagePicker.razor` — delete the private `SupportedLocales` array and the `_languages` projection (lines 24–49) and read the shared list instead. Add `@using SharpMUSH.Client.Resources` at the top if absent, then replace the `@code` block's list members with:

```csharp
    private readonly record struct LanguageOption(string Code, string DisplayName, string Flag);

    private static readonly LanguageOption[] _languages = PortalLocales.Supported
        .Select(l => new LanguageOption(l.Code, PortalLocales.DisplayName(l.Code), l.Flag))
        .ToArray();
```

Leave `OnInitializedAsync` and `SetLanguageAsync` exactly as they are — the `localStorage` `"locale"` key and the force-reload are the mechanism `?lang=` layers on top of, not something to change.

- [ ] **Step 4: Add the client models**

Create `SharpMUSH.Client/Models/WikiTranslationInfo.cs` (tabs):

```csharp
namespace SharpMUSH.Client.Models;

/// <summary>
/// One locale's translation of a wiki page, without its body — enough for the language chip row and the
/// editor's locale dropdown.
/// </summary>
public record WikiTranslationInfo(
	string Locale,
	string Title,
	bool Published,
	DateTimeOffset UpdatedAt,
	int RevisionNumber);
```

`SharpMUSH.Client/Models/WikiArticle.cs` — append four properties (this is a mutable class, so plain `get; set;` matching its style):

```csharp

	/// <summary>The locale whose content this article actually carries.</summary>
	public string Locale { get; set; } = string.Empty;

	/// <summary>The locale the reader asked for, after server-side normalisation.</summary>
	public string RequestedLocale { get; set; } = string.Empty;

	/// <summary>True when a different language was served than requested — drives the fallback notice.</summary>
	public bool IsFallback { get; set; }

	/// <summary>Locales this reader can read the page in, source locale first.</summary>
	public List<string> AvailableLocales { get; set; } = [];

	/// <summary>
	/// Revision number of the row that was actually served — the translation's when a translation was
	/// served, the page's otherwise. <c>WikiView</c> passes it back as <c>expectedRevisionNumber</c> so a
	/// concurrent save is detected instead of silently overwritten.
	/// </summary>
	public int RevisionNumber { get; set; }
```

Create `SharpMUSH.Client/Models/WikiTranslationSaveError.cs` (tabs):

```csharp
namespace SharpMUSH.Client.Models;

/// <summary>Why a translation save failed, and whether the fix is a reload rather than a correction.</summary>
/// <param name="Message">Server-supplied text, safe to show.</param>
/// <param name="NeedsReload">
/// True when the server answered 409: somebody else saved first. The editor must offer to reload and must
/// **not** retry — a retry re-sends this editor's stale markdown over the winner's.
/// </param>
public record WikiTranslationSaveError(string Message, bool NeedsReload);
```

`SharpMUSH.Client/Models/WikiPageSummary.cs` — append two init-only properties inside the record body:

```csharp

	/// <summary>The locale this row's title came from.</summary>
	public string Locale { get; init; } = string.Empty;

	/// <summary>True when this row's title is a fallback rather than the requested language.</summary>
	public bool IsFallback { get; init; }
```

- [ ] **Step 5: Thread `lang` through `WikiService`**

In `SharpMUSH.Client/Services/WikiService.cs`:

Extend the private `WikiPageDto` mirror (lines 15–29) with the four trailing fields the server now sends. They must be nullable or defaulted so a response from an older server still deserialises:

```csharp
	private record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber,
		string? Category,
		IReadOnlyList<string>? Tags,
		bool Published,
		string? Locale,
		string? RequestedLocale,
		bool IsFallback,
		IReadOnlyList<string>? AvailableLocales);
```

Add the translation DTO and request records next to the existing private records:

```csharp
	private record WikiTranslationSummaryDto(
		string Locale, string Title, bool Published, DateTimeOffset UpdatedAt, int RevisionNumber);

	private record UpsertTranslationRequest(
		string Title, string Markdown, string? EditSummary, bool Published, int? ExpectedRevisionNumber);
```

Add the query helper next to `NsQuery` and `KeyQuery`:

```csharp
	/// <summary>
	/// Builds the optional <c>lang</c> query suffix. Null or blank sends nothing at all, which the server
	/// reads as "use the configured default" — sending an empty <c>lang=</c> would mean the same thing but
	/// makes the prerender cache key and the browser history noisier for no gain.
	/// </summary>
	private static string LangQuery(string? lang, bool first) =>
		string.IsNullOrWhiteSpace(lang)
			? string.Empty
			: $"{(first ? '?' : '&')}lang={Uri.EscapeDataString(lang)}";
```

Change `GetWikiArticle` (lines 49–67) — the URL is path-based, so `lang` is the first query param:

```csharp
	public async ValueTask<OneOf<WikiArticle, None>> GetWikiArticle(
		string slug, string? category = null, string? ns = null, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var url = $"api/wiki/ns/{Uri.EscapeDataString(ns ?? "main")}/{Uri.EscapeDataString(category ?? "general")}/{Uri.EscapeDataString(slug)}{LangQuery(lang, first: true)}";
			var dto = await http.GetFromJsonAsync<WikiPageDto>(url);
			return dto is null ? new None() : ToArticle(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return new None();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetWikiArticle failed for slug={Slug} lang={Lang}", slug, lang);
			return new None();
		}
	}
```

Add `string? lang = null` as the **last** parameter of `GetRecentChangesAsync`, `GetNamespacePagesAsync`, `GetAllPagesAsync`, `GetByCategoryAsync`, `GetByTagAsync` and `GetRevisionsAsync`, and append `{LangQuery(lang, first: false)}` to each of their URLs (every one of those URLs already has at least one query param, so `first: false` is correct in all six).

Extend the two mappers so the new fields reach the models:

```csharp
	private static WikiArticle ToArticle(WikiPageDto dto) =>
		new(
			title: dto.Title,
			content: dto.MarkdownSource,
			image: null,
			renderedHtml: dto.RenderedHtml
		)
		{
			Id = dto.Id,
			Slug = dto.Slug,
			Category = dto.Category,
			Tags = dto.Tags?.ToList() ?? [],
			Published = dto.Published,
			Locale = dto.Locale ?? string.Empty,
			RequestedLocale = dto.RequestedLocale ?? string.Empty,
			IsFallback = dto.IsFallback,
			AvailableLocales = dto.AvailableLocales?.ToList() ?? [],
			// The SERVED row's revision number — the translation's when a translation was served, the page's
			// otherwise. Task 16 passes it back as expectedRevisionNumber, which is why it must come from the
			// same DTO field the server resolved rather than from a separate page lookup.
			RevisionNumber = dto.RevisionNumber,
		};
```

and in `ToSummary`, add:

```csharp
			Locale = dto.Locale ?? string.Empty,
			IsFallback = dto.IsFallback,
```

Add the three translation methods (place them after `GetRevisionAsync`, matching the file's `try`/`catch`/`logger.LogError` shape exactly):

```csharp
	/// <summary>Locales this reader can read the page in, excluding drafts they may not see.</summary>
	public async ValueTask<IReadOnlyList<WikiTranslationInfo>> GetTranslationsAsync(
		string slug, string? ns = null, string? category = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiTranslationSummaryDto>>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations{KeyQuery(ns, category)}");
			return dtos?
				.Select(d => new WikiTranslationInfo(d.Locale, d.Title, d.Published, d.UpdatedAt, d.RevisionNumber))
				.ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetTranslationsAsync failed for slug={Slug}", slug);
			return [];
		}
	}

	/// <summary>
	/// Creates or updates one locale's translation. <paramref name="expectedRevisionNumber"/> is the revision
	/// the editor loaded; null means create-only.
	/// </summary>
	/// <remarks>
	/// A 409 from the server means somebody else saved first. It comes back as a
	/// <see cref="WikiTranslationSaveError"/> with <c>NeedsReload</c> set, and the caller must offer a reload
	/// rather than retrying — a retry re-sends this editor's stale markdown over the winner's, which is the
	/// data loss the whole compare-and-swap exists to prevent.
	/// </remarks>
	public async ValueTask<OneOf<WikiTranslationInfo, WikiTranslationSaveError>> UpsertTranslationAsync(
		string slug, string locale, string title, string markdown, bool published,
		int? expectedRevisionNumber, string? editSummary = null, string? ns = null, string? category = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations/{Uri.EscapeDataString(locale)}{KeyQuery(ns, category)}",
				new UpsertTranslationRequest(title, markdown, editSummary, published, expectedRevisionNumber));

			if (!response.IsSuccessStatusCode)
				return new WikiTranslationSaveError(
					await response.Content.ReadAsStringAsync(),
					NeedsReload: response.StatusCode == System.Net.HttpStatusCode.Conflict);

			var dto = await response.Content.ReadFromJsonAsync<WikiTranslationSummaryDto>();
			return dto is null
				? new WikiTranslationSaveError("The server returned no translation.", NeedsReload: false)
				: new WikiTranslationInfo(dto.Locale, dto.Title, dto.Published, dto.UpdatedAt, dto.RevisionNumber);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "UpsertTranslationAsync failed for slug={Slug} locale={Locale}", slug, locale);
			return new WikiTranslationSaveError(ex.Message, NeedsReload: false);
		}
	}

	/// <summary>Removes one locale's translation. The page and every other locale are untouched.</summary>
	public async ValueTask<OneOf<None, string>> DeleteTranslationAsync(
		string slug, string locale, string? ns = null, string? category = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations/{Uri.EscapeDataString(locale)}{KeyQuery(ns, category)}");

			return response.IsSuccessStatusCode
				? new None()
				: await response.Content.ReadAsStringAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "DeleteTranslationAsync failed for slug={Slug} locale={Locale}", slug, locale);
			return ex.Message;
		}
	}
```

- [ ] **Step 6: Add the 21 resx keys to both files**

In `SharpMUSH.Client/Resources/SharedResource.resx`, append a new banner-delimited section at the end (matching the file's 3-line `====` banner style, 2-space `<data>`, 4-space `<value>`):

```xml

  <!-- ======================== -->
  <!-- Wiki localization        -->
  <!-- ======================== -->
  <data name="WikiFallbackNotice" xml:space="preserve">
    <value>This page is not available in {0} yet — showing the {1} version.</value>
  </data>
  <data name="WikiFallbackCreateTranslation" xml:space="preserve">
    <value>Translate this page</value>
  </data>
  <data name="WikiAvailableTranslations" xml:space="preserve">
    <value>Also available in</value>
  </data>
  <data name="WkLocaleSelector" xml:space="preserve">
    <value>Language</value>
  </data>
  <data name="WkAddTranslation" xml:space="preserve">
    <value>Add a translation…</value>
  </data>
  <data name="WkSourceLocaleLabel" xml:space="preserve">
    <value>{0} (source)</value>
  </data>
  <data name="WkInheritedFromSource" xml:space="preserve">
    <value>Inherited from the source language — edit it on the source page.</value>
  </data>
  <data name="WkTranslationSaved" xml:space="preserve">
    <value>Translation saved.</value>
  </data>
  <data name="WkTranslationSaveFailed" xml:space="preserve">
    <value>Could not save the translation: {0}</value>
  </data>
  <data name="WkDeleteTranslation" xml:space="preserve">
    <value>Delete this translation</value>
  </data>
  <data name="WkTranslationDeleteConfirmTitle" xml:space="preserve">
    <value>Delete translation?</value>
  </data>
  <data name="WkTranslationDeleteConfirmText" xml:space="preserve">
    <value>The {0} translation and its history will be removed. The page and its other languages are unaffected.</value>
  </data>
  <data name="WkCustomLocalePlaceholder" xml:space="preserve">
    <value>Language tag, e.g. es or pt-BR</value>
  </data>
  <data name="WkHistoryLocale" xml:space="preserve">
    <value>Language</value>
  </data>
  <data name="WkTranslationConflict" xml:space="preserve">
    <value>This translation changed while you were editing. Reload it to see the current version — your unsaved text is still in the editor.</value>
  </data>
  <data name="WkTranslationReload" xml:space="preserve">
    <value>Reload the translation</value>
  </data>
  <data name="WikiTranslations" xml:space="preserve">
    <value>Translations</value>
  </data>
  <data name="WikiLocaleFilter" xml:space="preserve">
    <value>Language</value>
  </data>
  <data name="WikiAllLocales" xml:space="preserve">
    <value>All languages</value>
  </data>
  <data name="WikiUntranslatedOnly" xml:space="preserve">
    <value>Missing translations only</value>
  </data>
  <data name="ResWikiStatTranslations" xml:space="preserve">
    <value>Translations</value>
  </data>
```

In `SharpMUSH.Client/Resources/SharedResource.fr.resx`, append the same 21 keys under that file's short one-line banner style:

```xml

  <!-- Wiki localization -->
  <data name="WikiFallbackNotice" xml:space="preserve">
    <value>Cette page n'est pas encore disponible en {0} — voici la version en {1}.</value>
  </data>
  <data name="WikiFallbackCreateTranslation" xml:space="preserve">
    <value>Traduire cette page</value>
  </data>
  <data name="WikiAvailableTranslations" xml:space="preserve">
    <value>Également disponible en</value>
  </data>
  <data name="WkLocaleSelector" xml:space="preserve">
    <value>Langue</value>
  </data>
  <data name="WkAddTranslation" xml:space="preserve">
    <value>Ajouter une traduction…</value>
  </data>
  <data name="WkSourceLocaleLabel" xml:space="preserve">
    <value>{0} (source)</value>
  </data>
  <data name="WkInheritedFromSource" xml:space="preserve">
    <value>Hérité de la langue source — modifiable sur la page source.</value>
  </data>
  <data name="WkTranslationSaved" xml:space="preserve">
    <value>Traduction enregistrée.</value>
  </data>
  <data name="WkTranslationSaveFailed" xml:space="preserve">
    <value>Impossible d'enregistrer la traduction : {0}</value>
  </data>
  <data name="WkDeleteTranslation" xml:space="preserve">
    <value>Supprimer cette traduction</value>
  </data>
  <data name="WkTranslationDeleteConfirmTitle" xml:space="preserve">
    <value>Supprimer la traduction ?</value>
  </data>
  <data name="WkTranslationDeleteConfirmText" xml:space="preserve">
    <value>La traduction en {0} et son historique seront supprimés. La page et ses autres langues ne sont pas affectées.</value>
  </data>
  <data name="WkCustomLocalePlaceholder" xml:space="preserve">
    <value>Étiquette de langue, par ex. es ou pt-BR</value>
  </data>
  <data name="WkHistoryLocale" xml:space="preserve">
    <value>Langue</value>
  </data>
  <data name="WkTranslationConflict" xml:space="preserve">
    <value>Cette traduction a été modifiée pendant votre édition. Rechargez-la pour voir la version actuelle — votre texte non enregistré reste dans l'éditeur.</value>
  </data>
  <data name="WkTranslationReload" xml:space="preserve">
    <value>Recharger la traduction</value>
  </data>
  <data name="WikiTranslations" xml:space="preserve">
    <value>Traductions</value>
  </data>
  <data name="WikiLocaleFilter" xml:space="preserve">
    <value>Langue</value>
  </data>
  <data name="WikiAllLocales" xml:space="preserve">
    <value>Toutes les langues</value>
  </data>
  <data name="WikiUntranslatedOnly" xml:space="preserve">
    <value>Traductions manquantes uniquement</value>
  </data>
  <data name="ResWikiStatTranslations" xml:space="preserve">
    <value>Traductions</value>
  </data>
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet build`
Expected: 0 errors.

Run: `dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 271 + 5 (`PortalLocalesTests`) + 1 (the new resx guard) passing, 0 failed. In particular `No_resource_value_is_left_as_its_own_camel_case_key` must still pass — every new key has a real human value.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiServiceTests/*"`
Expected: PASS — `SharpMUSH.Tests/Client/Services/WikiServiceTests.cs` exercises the client service and must survive the new optional parameters.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Client SharpMUSH.Tests.BUnit/Resources
git commit -m "feat(wiki): thread lang through the client service and share the portal locale list"
```

---

### Task 15: `WikiDisplay` — the fallback notice and the language chip row

Dismissal is **per-session only**: a translation gap should keep nagging, so it is component state, not `localStorage`.

`WikiDisplay.razor` has two body branches — the hero branch (`DisplayAsHero`, slug == "home") at lines 33–41 and the normal grid at lines 68–76. **Both need the notice**, or the home page silently never shows it.

**Files:**
- Modify: `SharpMUSH.Client/Components/WikiDisplay.razor` (notice + chips in both branches; `Locale` parameter)
- Modify: `SharpMUSH.Client/Components/WikiDisplay.razor.css` (chip row styling)
- Modify: `SharpMUSH.Client/Components/WikiView.razor` (accept and forward `Locale`; pass it to the fetch)
- Modify: `SharpMUSH.Client/Pages/WikiPage.razor` (`[SupplyParameterFromQuery(Name = "lang")]`, include it in `@key`)
- Test: `SharpMUSH.Tests.BUnit/Components/WikiDisplayFallbackTests.cs` (create)

**Interfaces:**
- Consumes: `WikiArticle.IsFallback` / `.Locale` / `.RequestedLocale` / `.AvailableLocales` (Task 14); `PortalLocales.DisplayName` (Task 14).
- Produces:
  - `WikiDisplay.Locale` → `string?` parameter — the locale from `?lang=`. Load-bearing: it is what the "Translate this page" link targets, so that a reader who asked for `de` and got English is offered a *German* editor, not a re-edit of whatever `Article.RequestedLocale` normalised to.
  - `WikiView.Locale` → `string?` parameter
  - `WikiPage.Lang` → `string?` (`[SupplyParameterFromQuery(Name = "lang")]`)
  - CSS class `wiki-lang-chips`

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Components/WikiDisplayFallbackTests.cs` (tabs). `SharpMUSH.Tests.BUnit` does not set `TreatWarningsAsErrors`, but keep it warning-clean anyway. Model the setup on `SharpMUSH.Tests.BUnit/Components/WikiIndexWidgetTests.cs`, which already builds a `BunitContext` with MudBlazor and an echo localizer:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// The fallback notice is the whole user-visible payoff of "fallback, not 404": without it a French
/// reader silently gets English and never learns a translation is missing. These tests assert it renders
/// exactly when <see cref="WikiArticle.IsFallback"/> is set — including on the hero (home) branch, which
/// has its own body markup and is easy to forget.
/// </summary>
public class WikiDisplayFallbackTests : BunitContext
{
	public WikiDisplayFallbackTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<WikiMarkdigPipeline>();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static WikiArticle Article(bool isFallback, string locale = "en", string requested = "fr") =>
		new("Dragons", "body", null, "<p>body</p>")
		{
			Id = "1",
			Slug = "dragons",
			Category = "general",
			Locale = locale,
			RequestedLocale = requested,
			IsFallback = isFallback,
			AvailableLocales = ["en", "fr"],
		};

	private IRenderedComponent<WikiDisplay> Render(WikiArticle article, string slug = "dragons") =>
		Render<WikiDisplay>(p => p
			.Add(c => c.Slug, slug)
			.Add(c => c.Namespace, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Locale, "fr")
			.Add(c => c.Article, article)
			.Add(c => c.ActivateEditMode, () => Task.CompletedTask));

	[Test]
	public async Task Notice_rendersWhenTheArticleIsAFallback()
	{
		var cut = Render(Article(isFallback: true));

		await Assert.That(cut.Markup).Contains("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_isAbsentWhenTheRequestedLanguageWasServed()
	{
		var cut = Render(Article(isFallback: false, locale: "fr", requested: "fr"));

		await Assert.That(cut.Markup).DoesNotContain("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_rendersOnTheHeroBranchToo()
	{
		// DisplayAsHero is slug == "home"; it has its own body markup, so it needs its own notice.
		var cut = Render(Article(isFallback: true), slug: "home");

		await Assert.That(cut.Markup).Contains("WikiFallbackNotice");
	}

	[Test]
	public async Task Notice_canBeDismissedForTheSession()
	{
		var cut = Render(Article(isFallback: true));

		cut.Find(".wiki-fallback-dismiss").Click();

		await Assert.That(cut.Markup).DoesNotContain("WikiFallbackNotice");
	}

	[Test]
	public async Task LanguageChips_listEveryAvailableLocaleExceptTheOneBeingServed()
	{
		var cut = Render(Article(isFallback: true, locale: "en", requested: "fr"));

		var hrefs = cut.FindAll(".wiki-lang-chips a").Select(a => a.GetAttribute("href")).ToList();

		await Assert.That(hrefs).Contains("/wiki/main/general/dragons?lang=fr");
		await Assert.That(hrefs.Any(h => h!.EndsWith("lang=en")))
			.IsFalse()
			.Because("the locale already on screen is not a link to somewhere else");
	}

	[Test]
	public async Task LanguageChips_areAbsentWhenOnlyOneLocaleExists()
	{
		var article = Article(isFallback: false, locale: "en", requested: "en");
		article.AvailableLocales = ["en"];

		var cut = Render(article);

		await Assert.That(cut.FindAll(".wiki-lang-chips")).IsEmpty();
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiDisplayFallbackTests/*"`
Expected: compile error — `WikiDisplay` has no `Locale` parameter.

- [ ] **Step 3: Add the notice and chips to `WikiDisplay.razor`**

Add the parameter to the `@code` block (4-space indentation in Razor), after `Category`:

```csharp
    /// <summary>The locale the reader asked for via <c>?lang=</c>; null means their stored preference.</summary>
    [Parameter]
    public string? Locale { get; init; }
```

and the dismissal state plus two helpers:

```csharp
    /// <summary>
    /// Per-session only, deliberately: a translation gap should keep nagging on the next page view, so this
    /// is component state rather than anything persisted.
    /// </summary>
    private bool _fallbackDismissed;

    private bool ShowFallbackNotice => Article?.IsFallback == true && !_fallbackDismissed;

    /// <summary>Locales worth offering as a link — everything available except the one on screen.</summary>
    private IEnumerable<string> OtherLocales =>
        Article?.AvailableLocales
            .Where(l => !string.Equals(l, Article.Locale, StringComparison.OrdinalIgnoreCase))
        ?? [];

    private string LocaleHref(string locale) =>
        $"/wiki/{Namespace ?? "main"}/{Category ?? "general"}/{Slug}?lang={Uri.EscapeDataString(locale)}";

    /// <summary>
    /// The locale a "Translate this page" link should target. The raw <c>?lang=</c> wins over
    /// <see cref="WikiArticle.RequestedLocale"/>: a reader who asked for a locale the server normalised
    /// away must still land in an editor for the locale <em>they</em> asked for.
    /// </summary>
    private string TargetLocale =>
        string.IsNullOrWhiteSpace(Locale) ? Article?.RequestedLocale ?? string.Empty : Locale;
```

Add a `RenderFragment` for the block so both body branches share one definition:

```csharp
    /// <summary>
    /// The fallback notice and language chips. Defined once and rendered in both the hero and grid
    /// branches — duplicating the markup is how the home page ends up silently never showing it.
    /// </summary>
    private RenderFragment LocaleBanner => @<div>
        @if (ShowFallbackNotice)
        {
            <MudAlert Severity="Severity.Info" Variant="Variant.Outlined" Dense="true" Class="mb-2">
                <div style="display:flex;align-items:center;gap:0.5rem;flex-wrap:wrap;">
                    <span>
                        @string.Format(Loc["WikiFallbackNotice"],
                            PortalLocales.DisplayName(Article!.RequestedLocale),
                            PortalLocales.DisplayName(Article!.Locale))
                    </span>
                    <AuthorizeView Policy="wiki.edit">
                        <Authorized>
                            <MudLink Href="@($"/wiki/{Namespace ?? "main"}/{Category ?? "general"}/{Slug}/edit?lang={Uri.EscapeDataString(TargetLocale)}")"
                                     Typo="Typo.body2">
                                @Loc["WikiFallbackCreateTranslation"]
                            </MudLink>
                        </Authorized>
                    </AuthorizeView>
                    <div style="flex:1;"></div>
                    <MudIconButton Class="wiki-fallback-dismiss"
                                   Icon="@Icons.Material.Filled.Close"
                                   Size="Size.Small"
                                   OnClick="@(() => _fallbackDismissed = true)" />
                </div>
            </MudAlert>
        }
        @if (OtherLocales.Any())
        {
            <div class="wiki-lang-chips">
                <span class="wiki-lang-chips-label">@Loc["WikiAvailableTranslations"]</span>
                @foreach (var locale in OtherLocales)
                {
                    <a href="@LocaleHref(locale)">@PortalLocales.Flag(locale) @PortalLocales.DisplayName(locale)</a>
                }
            </div>
        }
    </div>;
```

Add `@using SharpMUSH.Client.Resources` to the top of the file if absent, then render `@LocaleBanner` at the start of both bodies:

- hero branch — immediately inside the body wrapper at line 33, before its existing content;
- grid branch — immediately before `<div class="WikiContent wiki-article-body">` at line 68.

- [ ] **Step 4: Style the chip row**

Append to `SharpMUSH.Client/Components/WikiDisplay.razor.css`:

```css
.wiki-lang-chips {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 0.5rem;
    margin: 0 0 0.75rem;
    font-size: 0.75rem;
}

.wiki-lang-chips-label {
    color: var(--text-faint);
    text-transform: uppercase;
    letter-spacing: 0.04em;
}

.wiki-lang-chips a {
    display: inline-flex;
    align-items: center;
    gap: 0.25rem;
    padding: 0.125rem 0.5rem;
    border: 1px solid var(--mud-palette-lines-default);
    border-radius: 999px;
    color: var(--text-dim);
    text-decoration: none;
}

.wiki-lang-chips a:hover {
    color: var(--mud-palette-primary);
    border-color: var(--mud-palette-primary);
}
```

- [ ] **Step 5: Thread `lang` from the route down**

`SharpMUSH.Client/Pages/WikiPage.razor` — add the query parameter and put it in the `@key`, or a language switch will not re-fetch:

```razor
<WikiView @key="@($"{Ns}:{Category}:{Slug}:{Lang}")" Slug="@Slug" Namespace="@Ns" Category="@Category"
          Locale="@Lang" Mode="WikiView.WikiMode.View" />
```

```csharp
    [SupplyParameterFromQuery(Name = "lang")]
    public string? Lang { get; set; }
```

`SharpMUSH.Client/Components/WikiView.razor` — add the parameter, forward it to `WikiDisplay`, and use it in the fetch:

```csharp
    /// <summary>The locale requested via <c>?lang=</c>; null means the reader's stored preference.</summary>
    [Parameter] public string? Locale { get; init; }
```

```razor
    <WikiDisplay Slug="@Slug" Namespace="@Namespace" Category="@Category" Locale="@Locale" Article="@_article"
                 ActivateEditMode="ActivateEditMode" OnArticleRestored="HandleArticleRestored" />
```

and in `OnInitializedAsync` (line 131):

```csharp
        var result = await Wiki.GetWikiArticle(Slug, Category, Namespace, Locale);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiDisplayFallbackTests/*"`
Expected: PASS (6 tests).

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 0 errors; all previously-passing tests still green, including `WikiRoutePageTests` (which renders the real route components) and `SharpMUSH.Tests/Client/Components/WikiDisplayTests.cs`.

- [ ] **Step 7: Commit**

```bash
git add SharpMUSH.Client/Components SharpMUSH.Client/Pages/WikiPage.razor \
  SharpMUSH.Tests.BUnit/Components/WikiDisplayFallbackTests.cs
git commit -m "feat(wiki): show the fallback notice and language chips on wiki reads"
```

---

### Task 16: `WikiEdit` — locale selector with inherited metadata visibly disabled

Decision 4 made legible rather than mysterious: on a non-source locale, Category and Tags render **visible but disabled** with an "inherited from source" hint. Title, body and Published stay editable — a translation owns those. There is no protection control in this component (see the Phase 5 preamble).

`/wiki/{ns}/{category}/{slug}/edit?lang=fr` is the deep link the fallback notice's "Translate this page" link points at.

**Files:**
- Modify: `SharpMUSH.Client/Components/WikiEdit.razor` (locale selector; disable inherited fields; `SelectedLocale` + `AvailableLocales` + `SourceLocale` parameters)
- Modify: `SharpMUSH.Client/Components/WikiEdit.razor.css` (selector row + disabled hint)
- Modify: `SharpMUSH.Client/Components/WikiView.razor` (route the save through the translation endpoint on a non-source locale; load the translation for editing; hold the loaded revision number; conflict banner + reload action; gains `@inject IStringLocalizer<SharedResource> Loc`, which it does not have today)
- Modify: `SharpMUSH.Client/Pages/WikiPageEdit.razor` (`?lang=` → `WikiView.Locale`)
- Test: `SharpMUSH.Tests.BUnit/Components/WikiEditLocaleTests.cs` (create)

**Interfaces:**
- Consumes: `WikiService.GetTranslationsAsync` / `.UpsertTranslationAsync` / `.DeleteTranslationAsync` (Task 14); `PortalLocales` (Task 14); `WikiArticle.RevisionNumber` and `WikiTranslationSaveError` (Task 14).
- Produces:
  - `WikiEdit.SourceLocale` → `string` parameter
  - `WikiEdit.SelectedLocale` → `string` parameter (the locale being edited; equals `SourceLocale` for the source page)
  - `WikiEdit.AvailableLocales` → `IReadOnlyList<string>` parameter
  - `WikiEdit.OnLocaleChanged` → `EventCallback<string>` parameter
  - `WikiEdit.IsTranslation` → `bool` (private computed: `!SameLanguage(SelectedLocale, SourceLocale)`)
  - `WikiView.ExpectedTranslationRevision` → `int?` (private computed) and a conflict banner offering a reload
  - CSS classes `wiki-edit-localerow`, `wiki-edit-inherited`

**Optimistic concurrency lands here on the client side.** The editor holds the `RevisionNumber` of the row it loaded and passes it on save. On a conflict it shows `WkTranslationConflict` with a `WkTranslationReload` action and **does not retry** — a retry re-applies this editor's stale markdown over whatever the other translator just saved, which is exactly the loss the parameter prevents. The unsaved text stays in the textarea so the human can copy from it before reloading.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Components/WikiEditLocaleTests.cs` (tabs), same `BunitContext` setup as Task 15:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// A translation inherits the source page's category and tags structurally — <c>WikiTranslation</c> has
/// nowhere to store its own. These tests assert the editor makes that legible (visible but disabled with a
/// hint) rather than mysterious, and that the fields a translation <em>does</em> own stay editable.
/// </summary>
public class WikiEditLocaleTests : BunitContext
{
	public WikiEditLocaleTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<WikiMarkdigPipeline>();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static WikiArticle Draft() => new("Dragons", "body", null, "<p>body</p>")
	{
		Id = "1",
		Slug = "dragons",
		Category = "lore",
		Tags = ["myth"],
		Published = true,
	};

	private IRenderedComponent<WikiEdit> Render(string selectedLocale) =>
		Render<WikiEdit>(p => p
			.Add(c => c.Article, Draft())
			.Add(c => c.SourceLocale, "en")
			.Add(c => c.SelectedLocale, selectedLocale)
			.Add(c => c.AvailableLocales, new[] { "en", "fr" })
			.Add(c => c.OnLocaleChanged, EventCallback.Factory.Create<string>(this, _ => { })));

	[Test]
	public async Task Category_and_tags_are_enabled_on_the_source_locale()
	{
		var cut = Render("en");

		await Assert.That(cut.Find(".wiki-edit-cat input").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-taginput").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.FindAll(".wiki-edit-inherited")).IsEmpty();
	}

	[Test]
	public async Task Category_and_tags_are_disabled_on_a_translation()
	{
		var cut = Render("fr");

		await Assert.That(cut.Find(".wiki-edit-cat input").HasAttribute("disabled"))
			.IsTrue()
			.Because("a translation has nowhere to store its own category");
		await Assert.That(cut.Find(".wiki-edit-taginput").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task Inherited_hint_explains_why_the_fields_are_disabled()
	{
		var cut = Render("fr");

		await Assert.That(cut.Markup).Contains("WkInheritedFromSource");
	}

	[Test]
	public async Task Title_body_and_published_stay_editable_on_a_translation()
	{
		var cut = Render("fr");

		await Assert.That(cut.Find(".wiki-edit-title").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-textarea").HasAttribute("disabled")).IsFalse();
		await Assert.That(cut.Find(".wiki-edit-pub input").HasAttribute("disabled"))
			.IsFalse()
			.Because("a translation owns its own Published flag — that is how a translator drafts French while English stays live");
	}

	[Test]
	public async Task Locale_selector_offers_the_source_every_translation_and_an_add_option()
	{
		var cut = Render("en");

		await Assert.That(cut.Markup).Contains("WkLocaleSelector");
		await Assert.That(cut.Markup).Contains("WkAddTranslation");
	}

	[Test]
	public async Task Changing_the_locale_raises_OnLocaleChanged()
	{
		var raised = new List<string>();
		var cut = Render<WikiEdit>(p => p
			.Add(c => c.Article, Draft())
			.Add(c => c.SourceLocale, "en")
			.Add(c => c.SelectedLocale, "en")
			.Add(c => c.AvailableLocales, new[] { "en", "fr" })
			.Add(c => c.OnLocaleChanged, EventCallback.Factory.Create<string>(this, raised.Add)));

		await cut.Instance.SelectLocaleAsync("fr");

		await Assert.That(raised).IsEquivalentTo(new[] { "fr" });
	}

	[Test]
	public async Task Source_locale_edit_does_not_send_a_translation_expected_revision()
	{
		// Editing the source locale goes through UpdatePageAsync, not the translation endpoint, so there is
		// no translation revision to compare against. Asserted here because getting it wrong the other way —
		// sending the page's revision number as a translation's — would make every first save look stale.
		var cut = Render("en");

		await Assert.That(cut.Instance.IsTranslationEdit).IsFalse();
	}
}
```

`WikiView`'s conflict handling has no bUnit test in this task: the component owns the HTTP call, and the
existing `WikiView` tests do not stub `WikiService`. The behaviour is covered where it is actually
decidable — `PutTranslation_ReturnsConflictOnAStaleExpectedRevision` (Task 12) for the 409, and
`ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse` (Task 6) for the storage guarantee. Do **not**
add a `WikiService` mock here just to assert a banner; state that in the PR instead of leaving it silent.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiEditLocaleTests/*"`
Expected: compile error — `WikiEdit` has no `SourceLocale` parameter.

- [ ] **Step 3: Add the parameters and the selector**

In `SharpMUSH.Client/Components/WikiEdit.razor`, add `@using SharpMUSH.Client.Resources` at the top, then extend the `@code` parameter block:

```csharp
    /// <summary>The page's stamped source locale. Editing this locale edits the page itself.</summary>
    [Parameter] public string SourceLocale { get; set; } = "en";

    /// <summary>The locale currently being edited. Equal to <see cref="SourceLocale"/> for the source page.</summary>
    [Parameter] public string SelectedLocale { get; set; } = "en";

    /// <summary>Source locale plus every existing translation's locale.</summary>
    [Parameter] public IReadOnlyList<string> AvailableLocales { get; set; } = [];

    /// <summary>Raised when the editor switches to a different locale, so the host can load its content.</summary>
    [Parameter] public EventCallback<string> OnLocaleChanged { get; set; }

    /// <summary>Free-text tag for "Add a translation…", so a game can translate into any real locale.</summary>
    private string _customLocale = string.Empty;
    private bool _addingLocale;

    /// <summary>
    /// True when the selected locale is a different language from the source, i.e. we are editing an
    /// overlay row rather than the page. Category, tags and protection are inherited in that case, and
    /// <c>WikiTranslation</c> has no field for them — the disabled inputs make that visible.
    /// </summary>
    private bool IsTranslation => !WikiHelpers.SameLanguage(SelectedLocale, SourceLocale);

    /// <summary>
    /// Public mirror of <see cref="IsTranslation"/> so the host and component tests can tell whether a save
    /// goes to the translation endpoint (and therefore carries an expected revision number) or to the
    /// page-update endpoint.
    /// </summary>
    public bool IsTranslationEdit => IsTranslation;

    /// <summary>Switches the editor to another locale. Public so component tests can drive it directly.</summary>
    public async Task SelectLocaleAsync(string locale)
    {
        _addingLocale = false;
        if (string.Equals(locale, SelectedLocale, StringComparison.OrdinalIgnoreCase)) return;
        await OnLocaleChanged.InvokeAsync(locale);
    }

    private async Task AddCustomLocaleAsync()
    {
        // The free-text field is a write path in spirit: whatever is typed here ends up as a stored Locale,
        // so it is canonicalised and rejected here rather than being sent for the server to refuse.
        // NormalizeLocale's Error case is silently ignored rather than surfaced, because the field is
        // validated as-you-type by the same rule and an empty selection is the visible feedback.
        var normalized = WikiHelpers.NormalizeLocale(_customLocale);
        if (normalized.IsT1) return;
        _customLocale = string.Empty;
        await SelectLocaleAsync(normalized.AsT0);
    }

    private string LocaleLabel(string locale) =>
        WikiHelpers.SameLanguage(locale, SourceLocale)
            ? string.Format(Loc["WkSourceLocaleLabel"], PortalLocales.DisplayName(locale))
            : PortalLocales.DisplayName(locale);
```

Add `@using SharpMUSH.Library.Services` so `WikiHelpers` resolves (it lives in `SharpMUSH.Contracts` under that namespace and the Client already references the project).

Insert the selector row immediately above the title row (before line 20's `<div class="wiki-edit-titlerow">`):

```razor
        <div class="wiki-edit-localerow">
            <MudIcon Icon="@Icons.Material.Filled.Language" Style="font-size:0.875rem;color:var(--text-faint);" />
            <MudSelect T="string"
                       Value="@SelectedLocale"
                       ValueChanged="@SelectLocaleAsync"
                       Label="@Loc["WkLocaleSelector"].Value"
                       Dense="true"
                       Margin="Margin.Dense"
                       Variant="Variant.Outlined"
                       Style="max-width:220px;">
                @foreach (var locale in AvailableLocales)
                {
                    <MudSelectItem T="string" Value="@locale">@LocaleLabel(locale)</MudSelectItem>
                }
            </MudSelect>
            @if (_addingLocale)
            {
                <input class="wiki-edit-localeinput" value="@_customLocale"
                       @oninput="@(e => _customLocale = e.Value?.ToString() ?? string.Empty)"
                       @onkeydown="@(async e => { if (e.Key == "Enter") await AddCustomLocaleAsync(); })"
                       placeholder="@Loc["WkCustomLocalePlaceholder"]" />
            }
            else
            {
                <MudButton Variant="Variant.Text" Size="Size.Small" Style="text-transform:none;"
                           StartIcon="@Icons.Material.Filled.Add"
                           OnClick="@(() => _addingLocale = true)">@Loc["WkAddTranslation"]</MudButton>
            }
        </div>
```

- [ ] **Step 4: Disable the inherited fields**

Category input (line 26–28) — add `disabled` and the hint:

```razor
            <div class="wiki-edit-cat">
                <MudIcon Icon="@Icons.Material.Filled.Folder" Style="font-size:0.875rem;color:var(--text-faint);" />
                <input value="@Article.Category"
                       disabled="@IsTranslation"
                       @oninput="@(e => Article.Category = e.Value?.ToString())"
                       placeholder="@Loc["WikiCategory"]" />
            </div>
```

Tag input (line 80–82) — same treatment, and disable the per-tag remove buttons so a translator cannot half-edit the inherited set:

```razor
                <span class="wiki-edit-tag">@t
                    <button type="button" disabled="@IsTranslation" @onclick="@(() => RemoveTag(t))">×</button>
                </span>
```

```razor
            <input class="wiki-edit-taginput" value="@_tagEntry"
                   disabled="@IsTranslation"
                   @oninput="@(e => _tagEntry = e.Value?.ToString() ?? string.Empty)"
                   @onkeydown="OnTagKeyDown" placeholder="@Loc["WkAddTagPlaceholder"]" />
```

Add the hint after the tag row's closing `</div>` (line 89):

```razor
        @if (IsTranslation)
        {
            <div class="wiki-edit-inherited">
                <MudIcon Icon="@Icons.Material.Filled.Info" Style="font-size:0.75rem;" />
                <span>@Loc["WkInheritedFromSource"]</span>
            </div>
        }
```

Leave the title input, the textarea and the Published checkbox untouched.

- [ ] **Step 5: Style the new rows**

Append to `SharpMUSH.Client/Components/WikiEdit.razor.css`:

```css
.wiki-edit-localerow {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    margin-bottom: 0.5rem;
}

.wiki-edit-localeinput {
    background: transparent;
    border: 1px solid var(--mud-palette-lines-default);
    border-radius: 4px;
    color: var(--text-primary);
    font-size: 0.8125rem;
    padding: 0.25rem 0.5rem;
}

.wiki-edit-inherited {
    display: flex;
    align-items: center;
    gap: 0.375rem;
    margin-top: 0.25rem;
    color: var(--text-faint);
    font-size: 0.6875rem;
}

.wiki-edit-cat input:disabled,
.wiki-edit-taginput:disabled {
    opacity: 0.55;
    cursor: not-allowed;
}
```

- [ ] **Step 6: Route the save through the right endpoint**

`SharpMUSH.Client/Components/WikiView.razor` — the host owns the HTTP work, so it decides page-vs-translation. Add state and parameters:

```csharp
    /// <summary>The locale requested via <c>?lang=</c>; null means the reader's stored preference.</summary>
    [Parameter] public string? Locale { get; init; }

    private string _sourceLocale = "en";
    private string _selectedLocale = "en";
    private IReadOnlyList<string> _availableLocales = [];

    /// <summary>
    /// Revision number of the row currently loaded, held for optimistic concurrency on save.
    /// </summary>
    private int _loadedRevision;

    /// <summary>Set when the server answered 409; drives the reload prompt instead of a plain error toast.</summary>
    private bool _saveConflict;
```

In `OnInitializedAsync`, after the existing fetch, record what came back and load the locale list:

```csharp
    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        var result = await Wiki.GetWikiArticle(Slug, Category, Namespace, Locale);
        _article = result.IsT0 ? result.AsT0 : null;

        if (_article is not null)
        {
            // The served locale is the source locale unless a translation won, in which case the source is
            // whichever available locale is not the served one. AvailableLocales is source-first.
            _availableLocales = _article.AvailableLocales;
            _sourceLocale = _article.AvailableLocales.FirstOrDefault() ?? _article.Locale;
            _selectedLocale = _article.Locale;
            _loadedRevision = _article.RevisionNumber;
        }

        _loading = false;
        await base.OnInitializedAsync();
    }
```

Pass the new parameters into `WikiEdit`:

```razor
    <WikiEdit Article="@_editDraft"
              SourceLocale="@_sourceLocale"
              SelectedLocale="@_selectedLocale"
              AvailableLocales="@_availableLocales"
              OnLocaleChanged="@HandleLocaleChanged"
              OnSaved="@HandleSave"
              OnCancel="@HandleCancel" />
```

Add the locale-switch handler — it navigates rather than mutating in place, so the URL stays the source of truth and a browser back button works:

```csharp
    /// <summary>
    /// Switching locale in the editor is a navigation, not local state: <c>?lang=</c> stays the single
    /// source of truth for which locale is on screen, so a refresh or a back button lands where expected.
    /// </summary>
    private void HandleLocaleChanged(string locale)
    {
        var ns = Namespace ?? "main";
        var cat = Category ?? "general";
        Nav.NavigateTo($"/wiki/{ns}/{cat}/{Slug}/edit?lang={Uri.EscapeDataString(locale)}");
    }
```

Rewrite `HandleSave` so a non-source locale writes a translation. Keep the existing create/update/metadata path untouched for the source locale:

```csharp
    /// <summary>
    /// The revision number to compare against on a translation save, or null for create-only.
    /// </summary>
    /// <remarks>
    /// Null when the loaded article is not this locale's own row — the reader was served a fallback because
    /// no translation exists yet, so its <c>RevisionNumber</c> belongs to a different stream and passing it
    /// would make the very first save look stale.
    /// </remarks>
    private int? ExpectedTranslationRevision =>
        _article is not null && WikiHelpers.SameLanguage(_article.Locale, _selectedLocale)
            ? _loadedRevision
            : null;

    private async Task HandleSave(WikiArticle draft)
    {
        _saveError = null;
        _saveConflict = false;

        if (!WikiHelpers.SameLanguage(_selectedLocale, _sourceLocale))
        {
            // A translation owns Title, body and Published; Category, Tags and IsProtected are inherited
            // structurally, so there is no metadata call to make here.
            var translationResult = await Wiki.UpsertTranslationAsync(
                draft.Slug, _selectedLocale, draft.Title, draft.Content, draft.Published,
                ExpectedTranslationRevision, editSummary: null, ns: Namespace, category: Category);

            translationResult.Switch(
                _ =>
                {
                    _editDraft = null;
                    Mode = WikiMode.View;
                    Nav.NavigateTo(
                        $"/wiki/{Namespace ?? "main"}/{Category ?? "general"}/{Slug}?lang={Uri.EscapeDataString(_selectedLocale)}",
                        forceLoad: true);
                },
                err =>
                {
                    // On a conflict, prompt for a reload and STOP. Retrying — or silently re-reading the
                    // current revision and saving again — would re-apply this editor's stale markdown over
                    // the other translator's, which is precisely the loss expectedRevisionNumber prevents.
                    // The draft stays in the editor so its text can be copied out first.
                    _saveConflict = err.NeedsReload;
                    _saveError = err.NeedsReload ? Loc["WkTranslationConflict"] : err.Message;
                });

            StateHasChanged();
            return;
        }

        OneOf.OneOf<WikiArticle, string> result;

        if (string.IsNullOrEmpty(draft.Slug))
        {
            // New page — category is part of identity, so it's fixed at create from the draft.
            result = await Wiki.CreatePageAsync(draft.Title, draft.Content, Namespace, draft.Category);
        }
        else
        {
            // The page is identified by its current (route) category.
            result = await Wiki.UpdatePageAsync(draft.Slug, draft.Content, ns: Namespace, category: Category);
        }

        if (result.IsT0 && MetadataChanged(result.AsT0, draft))
        {
            result = await Wiki.SetMetadataAsync(
                result.AsT0.Slug, draft.Category, draft.Tags, draft.Published, Namespace,
                currentCategory: result.AsT0.Category);
        }

        await result.Match<Task>(
            updated =>
            {
                _article = updated;
                _editDraft = null;
                Mode = WikiMode.View;
                StateHasChanged();
                return Task.CompletedTask;
            },
            err =>
            {
                _saveError = err;
                StateHasChanged();
                return Task.CompletedTask;
            });
    }
```

Render the reload prompt wherever `_saveError` is already shown, adding the action only for a conflict —
a reload button next to "you typed something invalid" would be nonsense:

```razor
    @if (_saveError is not null)
    {
        <MudAlert Severity="@(_saveConflict ? Severity.Warning : Severity.Error)" Dense="true">
            @_saveError
            @if (_saveConflict)
            {
                <MudButton Variant="Variant.Text" Size="Size.Small"
                           OnClick="@(() => Nav.NavigateTo(Nav.Uri, forceLoad: true))">
                    @Loc["WkTranslationReload"]
                </MudButton>
            }
        </MudAlert>
    }
```

Reloading is a `forceLoad` navigation to the same URL rather than a re-fetch into the existing draft: it
brings back the winner's text *and* resets `_loadedRevision`, so the next save compares against the right
revision. A partial refresh that updated the body but not the revision number would conflict forever.

Add `@using SharpMUSH.Library.Services` to `WikiView.razor` for `WikiHelpers`, and — this one is easy to miss — `@inject IStringLocalizer<SharedResource> Loc` (plus `@using Microsoft.Extensions.Localization` and `@using SharpMUSH.Client.Resources`). `WikiView.razor` currently injects only `WikiService` and `NavigationManager` (lines 3–4); `WikiEdit.razor:5` is the pattern to copy. Without it the conflict banner's `Loc[...]` does not compile.

**Note the pre-existing bug you will see while editing `HandleSave`:** the editor collects `_summary` and `_minor` in `WikiEdit.razor` (lines 94, 97) and never sends them — `UpdatePageAsync` is called without `editSummary`. That is out of scope here (it predates this work and touching it would widen the diff into the page-edit path this plan must not disturb). Open a follow-up issue titled "WikiEdit collects an edit summary and discards it" and reference it in the PR rather than fixing it inline.

`SharpMUSH.Client/Pages/WikiPageEdit.razor` — add the query parameter and forward it:

```csharp
    [SupplyParameterFromQuery(Name = "lang")]
    public string? Lang { get; set; }
```

```razor
<WikiView @key="@($"{Ns}:{Category}:{Slug}:{Lang}")" Slug="@Slug" Namespace="@Ns" Category="@Category"
          Locale="@Lang" Mode="WikiView.WikiMode.Edit" />
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiEditLocaleTests/*"`
Expected: PASS (7 tests).

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 0 errors; every previously-passing test still green.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Client/Components SharpMUSH.Client/Pages/WikiPageEdit.razor \
  SharpMUSH.Tests.BUnit/Components/WikiEditLocaleTests.cs
git commit -m "feat(wiki): add a locale selector to the editor with inherited metadata disabled"
```

---

### Task 17: History and diff take `?lang=`

`WikiPageHistory.razor` already emits a `?rev=` link that nothing reads (`WikiPageHistory.razor:134`) — a dead precedent. Do not copy it: `?lang=` must be read where it is emitted.

**Files:**
- Modify: `SharpMUSH.Client/Pages/WikiPageHistory.razor` (`?lang=`, locale chip row, thread into the fetch, carry into diff links)
- Modify: `SharpMUSH.Client/Pages/WikiPageDiff.razor` (`?lang=`, thread into both revision fetches)
- Test: `SharpMUSH.Tests.BUnit/Pages/WikiRoutePageTests.cs` (append)

**Interfaces:**
- Consumes: `WikiService.GetRevisionsAsync(..., string? lang)` and `GetRevisionAsync` (Task 14 — add `lang` to `GetRevisionAsync` here if Task 14 did not).
- Produces: `WikiPageHistory.Lang` and `WikiPageDiff.Lang`, both `[SupplyParameterFromQuery(Name = "lang")] string?`.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests.BUnit/Pages/WikiRoutePageTests.cs` (this file already renders real route components against a fake `WikiService`; follow its existing fake-registration idiom):

```csharp
	[Test]
	public async Task History_page_requests_the_revisions_for_the_lang_query_parameter()
	{
		var wiki = RegisterRecordingWikiService();

		Render<SharpMUSH.Client.Pages.WikiPageHistory>(p => p
			.Add(c => c.Ns, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Slug, "dragons")
			.Add(c => c.Lang, "fr"));

		await Assert.That(wiki.LastRevisionsLang)
			.IsEqualTo("fr")
			.Because("the history page must show the locale's own stream, not always the source's");
	}

	[Test]
	public async Task History_page_carries_lang_into_its_diff_links()
	{
		var wiki = RegisterRecordingWikiService();
		wiki.Revisions = [new WikiRevisionInfo(2, "#1", DateTimeOffset.UnixEpoch, null, "v2"),
			new WikiRevisionInfo(1, "#1", DateTimeOffset.UnixEpoch, null, "v1")];

		var cut = Render<SharpMUSH.Client.Pages.WikiPageHistory>(p => p
			.Add(c => c.Ns, "main")
			.Add(c => c.Category, "general")
			.Add(c => c.Slug, "dragons")
			.Add(c => c.Lang, "fr"));

		var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).Where(h => h is not null).ToList();

		await Assert.That(hrefs.Any(h => h!.Contains("/diff?") && h.Contains("lang=fr")))
			.IsTrue()
			.Because("dropping lang on the way to the diff would diff the wrong locale's revisions");
	}
```

Add a `RegisterRecordingWikiService()` helper to that file if one does not already exist, recording the `lang` argument it was last called with and returning a settable `Revisions` list.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiRoutePageTests/*"`
Expected: compile error — `WikiPageHistory` has no `Lang` parameter.

- [ ] **Step 3: Add `?lang=` to the history page**

`SharpMUSH.Client/Pages/WikiPageHistory.razor` — add the query parameter next to the existing route parameters:

```csharp
    [SupplyParameterFromQuery(Name = "lang")]
    public string? Lang { get; set; }
```

Thread it into the revisions fetch (find the `Wiki.GetRevisionsAsync(...)` call and add the trailing argument):

```csharp
        _revisions = await Wiki.GetRevisionsAsync(Slug, skip: 0, take: 50, ns: Ns, category: Category, lang: Lang);
```

Carry it into every generated link. Add a helper and use it for the diff link, the back-to-page link and the per-revision view link:

```csharp
    /// <summary>Appends the current <c>?lang=</c> so navigating within history never silently changes locale.</summary>
    private string WithLang(string url) =>
        string.IsNullOrWhiteSpace(Lang)
            ? url
            : $"{url}{(url.Contains('?') ? '&' : '?')}lang={Uri.EscapeDataString(Lang)}";
```

Replace the existing `?rev=` link at line 134 — it is dead (nothing reads `rev`), so point it at the page in the current locale instead of inventing another unread parameter:

```razor
                    <MudLink Href="@WithLang($"/wiki/{Ns}/{Category}/{Slug}")">…</MudLink>
```

and wrap the diff link:

```razor
                    <MudLink Href="@WithLang($"/wiki/{Ns}/{Category}/{Slug}/diff?from={from}&to={to}")">…</MudLink>
```

Add a locale chip row above the revision list so a reader can switch streams. Load the locales in `OnInitializedAsync`:

```csharp
    private IReadOnlyList<WikiTranslationInfo> _translations = [];
```

```csharp
        _translations = await Wiki.GetTranslationsAsync(Slug, Ns, Category);
```

```razor
    @if (_translations.Count > 0)
    {
        <div class="wiki-lang-chips">
            <span class="wiki-lang-chips-label">@Loc["WkHistoryLocale"]</span>
            <a href="@($"/wiki/{Ns}/{Category}/{Slug}/history")">@Loc["WkSourceLocaleLabel"]</a>
            @foreach (var t in _translations)
            {
                <a href="@($"/wiki/{Ns}/{Category}/{Slug}/history?lang={Uri.EscapeDataString(t.Locale)}")">
                    @PortalLocales.Flag(t.Locale) @PortalLocales.DisplayName(t.Locale)
                </a>
            }
        </div>
    }
```

Add `@using SharpMUSH.Client.Resources` and `@using SharpMUSH.Client.Models` if absent. The `.wiki-lang-chips` CSS from Task 15 lives in `WikiDisplay.razor.css`, which is component-scoped — copy those two rules into `WikiPageHistory.razor.css` rather than reaching across the scope boundary.

- [ ] **Step 4: Add `?lang=` to the diff page**

`SharpMUSH.Client/Pages/WikiPageDiff.razor` — add the parameter next to the existing `FromRev`/`ToRev` (lines 123–127):

```csharp
    [SupplyParameterFromQuery(Name = "lang")]
    public string? Lang { get; set; }
```

and add `lang: Lang` to both `Wiki.GetRevisionAsync(...)` calls inside `OnParametersSetAsync` (lines 141–161). If `GetRevisionAsync` does not yet take `lang`, add it in `SharpMUSH.Client/Services/WikiService.cs` the same way Task 14 handled the others — it uses `KeyQuery`, so append `{LangQuery(lang, first: false)}`:

```csharp
	public async ValueTask<OneOf<WikiRevisionInfo, None>> GetRevisionAsync(
		string slug, int revisionNumber, string? ns = null, string? category = null, string? lang = null)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/WikiRoutePageTests/*"`
Expected: PASS.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 0 errors; all tests green.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/Pages SharpMUSH.Client/Services/WikiService.cs \
  SharpMUSH.Tests.BUnit/Pages/WikiRoutePageTests.cs
git commit -m "feat(wiki): show per-locale history and diff streams"
```

---

### Task 18: `/admin/wiki` translation coverage and locale filter

This is what makes untranslated Help pages findable, and it is the difference between a feature people use and one they forget exists.

`WikiPageSummary` does not carry coverage data, and the listing endpoint deliberately does not fill `AvailableLocales` (Task 13). The admin page therefore fetches translations per row after paging — acceptable because the grid pages at 25–50 rows, and honest about the spec's "measure before caching" instruction.

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/AdminWiki.razor` (coverage column, locale filter, stat tile)
- Test: `SharpMUSH.Tests.BUnit/Pages/AdminWikiCoverageTests.cs` (create)

**Interfaces:**
- Consumes: `WikiService.GetTranslationsAsync` (Task 14); `PortalLocales` (Task 14).
- Produces: `AdminWiki` locale filter state `_localeFilter` (`string`, `""` = all, `"__missing"` = missing-only); `AdminWiki.CoverageFor(WikiPageSummary)` → `IReadOnlyList<string>`.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Pages/AdminWikiCoverageTests.cs` (tabs), modelled on the existing `SharpMUSH.Tests.BUnit/Pages/AdminAccountsPageTests.cs` grid-rendering setup:

```csharp
	[Test]
	public async Task Coverage_column_lists_each_pages_locales()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("dragons", "Dragons")],
			translations: new() { ["dragons"] = ["fr", "de"] });

		await Assert.That(cut.Markup).Contains("WikiTranslations");
		var coverage = cut.Find(".admin-wiki-coverage").TextContent;
		await Assert.That(coverage).Contains("fr");
		await Assert.That(coverage).Contains("de");
	}

	[Test]
	public async Task Coverage_column_marks_a_page_with_no_translations()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("lonely", "Lonely")],
			translations: new());

		await Assert.That(cut.Find(".admin-wiki-coverage").ClassList)
			.Contains("admin-wiki-coverage-none")
			.Because("an untranslated page is the thing staff came here to find");
	}

	[Test]
	public async Task Locale_filter_offers_every_portal_locale_and_a_missing_only_option()
	{
		var cut = RenderAdminWikiWith(pages: [Summary("dragons", "Dragons")], translations: new());

		await Assert.That(cut.Markup).Contains("WikiLocaleFilter");
		await Assert.That(cut.Markup).Contains("WikiAllLocales");
		await Assert.That(cut.Markup).Contains("WikiUntranslatedOnly");
	}

	[Test]
	public async Task Missing_only_filter_hides_pages_that_already_have_that_locale()
	{
		var cut = RenderAdminWikiWith(
			pages: [Summary("done", "Done"), Summary("todo", "Todo")],
			translations: new() { ["done"] = ["fr"] });

		await cut.Instance.SetLocaleFilterAsync("fr:missing");

		await Assert.That(cut.Markup).Contains("Todo");
		await Assert.That(cut.Markup).DoesNotContain("Done");
	}
```

Add the `Summary(...)` factory and `RenderAdminWikiWith(...)` helper to the same file, registering a fake `WikiService` whose `GetTranslationsAsync` returns the supplied dictionary.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AdminWikiCoverageTests/*"`
Expected: FAIL — no `admin-wiki-coverage` element; `SetLocaleFilterAsync` not found.

- [ ] **Step 3: Add the coverage state and filter**

In `SharpMUSH.Client/Pages/Admin/AdminWiki.razor`, add `@using SharpMUSH.Client.Resources` and extend the `@code` fields (around lines 225–243):

```csharp
    /// <summary>
    /// Translation locales per page ref, loaded after paging. One extra request per visible row: the grid
    /// pages at 25–50, and the spec is explicit that a denormalized coverage cache must be measured for
    /// before it is added.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>> _coverage = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Empty = every page; "fr" = pages having fr; "fr:missing" = pages lacking fr.</summary>
    private string _localeFilter = string.Empty;
```

Add the filter handler and accessors:

```csharp
    /// <summary>Public so component tests can drive the filter without simulating MudSelect internals.</summary>
    public async Task SetLocaleFilterAsync(string value)
    {
        _localeFilter = value;
        await ReloadAsync();
    }

    private IReadOnlyList<string> CoverageFor(WikiPageSummary page) =>
        _coverage.TryGetValue(RefFor(page), out var locales) ? locales : [];

    /// <summary>Applies the locale filter to an already-paged set of rows.</summary>
    private IEnumerable<WikiPageSummary> ApplyLocaleFilter(IEnumerable<WikiPageSummary> pages)
    {
        if (_localeFilter.Length == 0) return pages;

        var missing = _localeFilter.EndsWith(":missing", StringComparison.Ordinal);
        var locale = missing ? _localeFilter[..^":missing".Length] : _localeFilter;

        return pages.Where(p =>
        {
            var has = CoverageFor(p).Any(l => WikiHelpers.SameLanguage(l, locale));
            return missing ? !has : has;
        });
    }

    /// <summary>Fills <see cref="_coverage"/> for the rows currently on screen.</summary>
    private async Task LoadCoverageAsync(IEnumerable<WikiPageSummary> pages)
    {
        foreach (var page in pages)
        {
            var reference = RefFor(page);
            if (_coverage.ContainsKey(reference)) continue;
            var translations = await Wiki.GetTranslationsAsync(page.Slug, page.Namespace, page.Category);
            _coverage[reference] = translations.Select(t => t.Locale).ToList();
        }
    }
```

Add `@using SharpMUSH.Library.Services` for `WikiHelpers`.

In `LoadServerData` (lines 260–277), call `LoadCoverageAsync` on the paged rows and then `ApplyLocaleFilter`, keeping the existing `_searchText` filter in place:

```csharp
        await LoadCoverageAsync(items);
        items = ApplyLocaleFilter(items).ToList();
```

- [ ] **Step 4: Add the filter control, the column and the stat tile**

In `<ToolBarContent>`, after the namespace filter (line 63), add the locale filter — same shape as the namespace one so the toolbar stays consistent:

```razor
                <MudSelect T="string"
                           Value="@_localeFilter"
                           ValueChanged="@SetLocaleFilterAsync"
                           Label="@Loc["WikiLocaleFilter"]"
                           Dense="true"
                           Margin="Margin.Dense"
                           Variant="Variant.Outlined"
                           Style="max-width:200px;">
                    <MudSelectItem T="string" Value="@(string.Empty)">@Loc["WikiAllLocales"]</MudSelectItem>
                    @foreach (var code in PortalLocales.Codes)
                    {
                        <MudSelectItem T="string" Value="@code">@PortalLocales.DisplayName(code)</MudSelectItem>
                        <MudSelectItem T="string" Value="@($"{code}:missing")">
                            @($"{PortalLocales.DisplayName(code)} — {Loc["WikiUntranslatedOnly"]}")
                        </MudSelectItem>
                    }
                </MudSelect>
```

Add the coverage column to `<Columns>`, after the Tags column (line 136) and before the actions column:

```razor
            <TemplateColumn Title="@Loc["WikiTranslations"]" Sortable="false">
                <CellTemplate>
                    @{
                        var locales = CoverageFor(context.Item);
                    }
                    <span class="@($"admin-wiki-coverage{(locales.Count == 0 ? " admin-wiki-coverage-none" : "")}")">
                        @if (locales.Count == 0)
                        {
                            <MudIcon Icon="@Icons.Material.Filled.TranslateOutlined"
                                     Size="Size.Small" Color="Color.Warning"
                                     Style="font-size:0.8125rem;" />
                        }
                        else
                        {
                            @string.Join(" · ", locales)
                        }
                    </span>
                </CellTemplate>
            </TemplateColumn>
```

Add a stat tile to the tuple array at lines 18–36 (match whatever that array's element shape is — `(value, resourceKey, icon, warn)`):

```csharp
            (_coverage.Values.Sum(v => v.Count), "ResWikiStatTranslations", Icons.Material.Filled.Translate, false),
```

Append to `SharpMUSH.Client/Pages/Admin/AdminWiki.razor.css`:

```css
.admin-wiki-coverage {
    font-size: 0.6875rem;
    color: var(--text-dim);
    letter-spacing: 0.02em;
}

.admin-wiki-coverage-none {
    color: var(--mud-palette-warning);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/AdminWikiCoverageTests/*"`
Expected: PASS (4 tests).

Run: `dotnet build && dotnet run --project SharpMUSH.Tests.BUnit`
Expected: 0 errors; all tests green.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client/Pages/Admin SharpMUSH.Tests.BUnit/Pages/AdminWikiCoverageTests.cs
git commit -m "feat(wiki): add translation coverage and a locale filter to the wiki admin grid"
```

---

# Phase 6 — SEO, in-game, seeding, docs

### Task 19: `hreflang` in the bot prerender

The canonical stays at the unsuffixed slug — `?lang=` never changes it. `<html lang="en">` is hardcoded in two places (`WikiController.cs:575` and `:627`) and must reflect the served locale, or a translated page tells every crawler and screen reader it is English.

`BotPrerenderMiddleware` caches on the raw path only (`prerenderCache.Get(path)`), so a locale-varying prerender **must** fold the locale into the cache key or the first bot to request French poisons the cache for everyone.

**Files:**
- Modify: `SharpMUSH.Server/Controllers/WikiController.cs` (both static generators gain locale + alternates parameters; `BuildArticleJsonLd` gains `inLanguage`)
- Modify: `SharpMUSH.Server/Middleware/BotPrerenderMiddleware.cs` (resolve through `IWikiLocalizationService`; locale-aware cache key)
- Modify: `SharpMUSH.Server/Controllers/SeoController.cs` (`xhtml:link` alternates in the sitemap)
- Test: `SharpMUSH.Tests/Server/Controllers/WikiPrerenderLocaleTests.cs` (create)

**Interfaces:**
- Consumes: `IWikiLocalizationService` (Task 10).
- Produces:
  - `WikiController.GeneratePrerenderHtml(LocalizedWikiPage page, string canonicalUrl, IReadOnlyList<string> alternateLocales, string defaultLocale, string siteName = "SharpMUSH")`
  - `WikiController.GenerateCharacterPrerenderHtml(LocalizedWikiPage page, string canonicalUrl, IReadOnlyList<string> alternateLocales, string defaultLocale, string siteName = "SharpMUSH")`

The old `WikiPage`-taking overloads are **replaced**, not kept alongside — two generators differing only in parameter type is exactly the mis-bind trap the spec warns about for `GetRevisionsAsync`. Update `SeoControllerTests` / `WikiControllerHtmlTests` call sites accordingly.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests/Server/Controllers/WikiPrerenderLocaleTests.cs` (tabs):

```csharp
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The prerendered HTML is what crawlers and link unfurlers see. A translated page that still declares
/// <c>&lt;html lang="en"&gt;</c> or omits its alternates is invisible as a translation no matter how good it is.
/// </summary>
public class WikiPrerenderLocaleTests
{
	private static LocalizedWikiPage Localized(string locale, string title) => new(
		Page: new WikiPage(
			Id: "1", Slug: "dragons", Title: "Dragons", Namespace: "main",
			MarkdownSource: "en", RenderedHtml: "<p>en</p>", PlainText: "en",
			AuthorDbref: "#1", LastEditorDbref: "#1",
			CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
			IsProtected: false, RevisionNumber: 1)
		{
			Category = "general",
			SourceLocale = "en",
		},
		Locale: locale,
		RequestedLocale: locale,
		Title: title,
		MarkdownSource: "corps",
		RenderedHtml: "<p>corps</p>",
		PlainText: "corps",
		Published: true,
		RevisionNumber: 1);

	[Test]
	public async Task HtmlLangAttribute_ReflectsTheServedLocale()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("fr", "Dragons (fr)"), "https://x/wiki/main/general/dragons", ["en", "fr"], "en");

		await Assert.That(html).Contains("<html lang=\"fr\">");
		await Assert.That(html).DoesNotContain("<html lang=\"en\">");
	}

	[Test]
	public async Task AlternateLinks_AreEmittedForEveryAvailableLocale()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("en", "Dragons"), "https://x/wiki/main/general/dragons", ["en", "fr"], "en");

		await Assert.That(html).Contains("hreflang=\"en\"");
		await Assert.That(html).Contains("hreflang=\"fr\"");
		await Assert.That(html).Contains("?lang=fr");
	}

	[Test]
	public async Task XDefaultAlternate_PointsAtTheConfiguredDefault()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("en", "Dragons"), "https://x/wiki/main/general/dragons", ["en", "fr"], "en");

		await Assert.That(html).Contains("hreflang=\"x-default\"");
	}

	[Test]
	public async Task Canonical_StaysAtTheUnsuffixedSlug()
	{
		var canonical = "https://x/wiki/main/general/dragons";

		var html = WikiController.GeneratePrerenderHtml(
			Localized("fr", "Dragons (fr)"), canonical, ["en", "fr"], "en");

		await Assert.That(html).Contains($"<link rel=\"canonical\" href=\"{canonical}\" />");
		await Assert.That(html)
			.DoesNotContain($"rel=\"canonical\" href=\"{canonical}?lang=")
			.Because("?lang= is a view of one canonical page, never a page of its own");
	}

	[Test]
	public async Task NoAlternateLinks_WhenOnlyOneLocaleExists()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("en", "Dragons"), "https://x/wiki/main/general/dragons", ["en"], "en");

		await Assert.That(html)
			.DoesNotContain("rel=\"alternate\"")
			.Because("a single-locale page has nothing to alternate to");
	}

	[Test]
	public async Task JsonLd_DeclaresTheServedLanguageAndTheTranslatedTitle()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("fr", "Dragons (fr)"), "https://x/wiki/main/general/dragons", ["en", "fr"], "en");

		await Assert.That(html).Contains("\"inLanguage\":\"fr\"");
		await Assert.That(html).Contains("Dragons (fr)");
	}

	[Test]
	public async Task Title_UsesTheResolvedTitleNotThePageTitle()
	{
		var html = WikiController.GeneratePrerenderHtml(
			Localized("fr", "Dragons (fr)"), "https://x/wiki/main/general/dragons", ["en", "fr"], "en");

		await Assert.That(html).Contains("Dragons (fr)");
	}
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiPrerenderLocaleTests/*"`
Expected: compile error — `GeneratePrerenderHtml` does not accept a `LocalizedWikiPage`.

- [ ] **Step 3: Rewrite the two generators**

In `SharpMUSH.Server/Controllers/WikiController.cs`, change `GeneratePrerenderHtml` (lines 564–592). Keep every existing tag; add the `lang` attribute, the alternates and `inLanguage`:

```csharp
	/// <summary>
	/// Bot-facing static HTML for a wiki page, resolved into one locale.
	/// </summary>
	/// <param name="page">The resolved page. Its <c>Title</c> and body are the served locale's, not the source's.</param>
	/// <param name="canonicalUrl">The unsuffixed canonical URL. <c>?lang=</c> never changes it: every locale
	/// is a view of one canonical page, and a per-locale canonical would split its ranking.</param>
	/// <param name="alternateLocales">Every locale a reader can read this page in, for <c>hreflang</c>.</param>
	/// <param name="defaultLocale">The configured default, emitted as <c>hreflang="x-default"</c>.</param>
	public static string GeneratePrerenderHtml(
		LocalizedWikiPage page,
		string canonicalUrl,
		IReadOnlyList<string> alternateLocales,
		string defaultLocale,
		string siteName = "SharpMUSH")
	{
		var title = HttpUtility.HtmlEncode($"{page.Title} - {siteName}");
		var ogTitle = HttpUtility.HtmlEncode(page.Title);
		var ogDesc = HttpUtility.HtmlEncode(Summarise(page.PlainText));
		var canonical = HttpUtility.HtmlEncode(canonicalUrl);
		var ogUrl = canonical;
		var lang = HttpUtility.HtmlEncode(page.Locale);

		var sb = new StringBuilder();
		sb.AppendLine("<!DOCTYPE html>");
		sb.AppendLine($"<html lang=\"{lang}\">");
		sb.AppendLine("<head>");
		sb.AppendLine($"  <meta charset=\"utf-8\" />");
		sb.AppendLine($"  <title>{title}</title>");
		sb.AppendLine($"  <link rel=\"canonical\" href=\"{canonical}\" />");
		AppendAlternates(sb, canonicalUrl, alternateLocales, defaultLocale);
		sb.AppendLine($"  <meta property=\"og:title\" content=\"{ogTitle}\" />");
		sb.AppendLine($"  <meta property=\"og:description\" content=\"{ogDesc}\" />");
		sb.AppendLine($"  <meta property=\"og:type\" content=\"article\" />");
		sb.AppendLine($"  <meta property=\"og:url\" content=\"{ogUrl}\" />");
		sb.AppendLine($"  <meta property=\"og:locale\" content=\"{lang}\" />");
		sb.AppendLine($"  <script type=\"application/ld+json\">{BuildArticleJsonLd(page, canonicalUrl)}</script>");
		sb.AppendLine("</head>");
		// … keep the existing <body> emission verbatim, but read page.RenderedHtml (the resolved body)
		// rather than a WikiPage's.
```

Read the current body-emission lines before editing and reproduce them exactly, substituting `page.RenderedHtml` / `page.Title` (which now come from the wrapper). `Summarise` is whatever the current code uses to trim `PlainText` for `og:description` — reuse it unchanged; if it is inline, leave it inline.

Add the shared alternates helper next to the generators:

```csharp
	/// <summary>
	/// Emits one <c>&lt;link rel="alternate" hreflang="…"&gt;</c> per available locale plus
	/// <c>x-default</c> at the configured default. Nothing is emitted for a single-locale page: a lone
	/// self-referential alternate is noise, and advertising a locale nobody can read would be a lie.
	/// </summary>
	private static void AppendAlternates(
		StringBuilder sb, string canonicalUrl, IReadOnlyList<string> alternateLocales, string defaultLocale)
	{
		if (alternateLocales.Count < 2) return;

		var separator = canonicalUrl.Contains('?') ? '&' : '?';

		foreach (var locale in alternateLocales)
		{
			var href = HttpUtility.HtmlEncode(
				$"{canonicalUrl}{separator}lang={Uri.EscapeDataString(locale)}");
			sb.AppendLine(
				$"  <link rel=\"alternate\" hreflang=\"{HttpUtility.HtmlEncode(locale)}\" href=\"{href}\" />");
		}

		sb.AppendLine(
			$"  <link rel=\"alternate\" hreflang=\"x-default\" href=\"{HttpUtility.HtmlEncode(canonicalUrl)}\" />");
	}
```

`BuildArticleJsonLd` (lines 599–611) — change the parameter type and add `inLanguage`:

```csharp
	private static string BuildArticleJsonLd(LocalizedWikiPage page, string canonicalUrl)
```

with `headline` reading `page.Title`, `datePublished` / `dateModified` reading `page.Page.CreatedAt` / `page.Page.UpdatedAt`, and a new entry:

```csharp
			["inLanguage"] = page.Locale,
```

`GenerateCharacterPrerenderHtml` (lines 616–643) — same signature change, same `lang` attribute, same `AppendAlternates` call after its canonical link. It has no JSON-LD, so nothing else changes.

- [ ] **Step 4: Resolve through the localization service in the middleware**

`SharpMUSH.Server/Middleware/BotPrerenderMiddleware.cs` — resolve the scoped service alongside `IWikiService` (line 60):

```csharp
		await using var scope = scopeFactory.CreateAsyncScope();
		var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();
		var localization = scope.ServiceProvider.GetRequiredService<IWikiLocalizationService>();
```

Read `?lang=` from the query and fold it into the cache key. Replace the `prerenderCache.Get(path)` line (line 49) region:

```csharp
		// A bot may request any locale, so the locale is part of the cache identity. Keying on path alone
		// would let the first French crawler poison the entry every other reader gets.
		// Read path, so the permissive form: an unparseable ?lang= keys the default entry rather than a 400.
		var requestedLang = context.Request.Query["lang"].FirstOrDefault();
		var normalizedLang = WikiHelpers.NormalizeLocaleOrEmpty(requestedLang);
		var cacheKey = normalizedLang.Length == 0 ? path : $"{path}#{normalizedLang}";

		if (prerenderCache.Get(cacheKey) is { } cached)
		{
			await WriteHtmlResponse(context, cached);
			return;
		}
```

and the `Set` at line 108:

```csharp
			prerenderCache.Set(cacheKey, html);
```

Prerendered HTML is bot-facing and unauthenticated, so **`includeDrafts` is always `false`** here — a crawler must never see an unpublished translation.

Replace each of the three resolution branches' `GeneratePrerenderHtml` calls. The `/wiki/` branch (lines 64–80):

```csharp
			var lookup = await wikiService.GetBySlugAsync(slug, category, ns);
			if (lookup.IsT0 && lookup.AsT0.Published)
			{
				var page = lookup.AsT0;
				// Bot-facing and unauthenticated: never surface an unpublished translation.
				var localized = await localization.LocalizeAsync(page, normalizedLang, includeDrafts: false);
				var alternates = await localization.GetVisibleLocalesAsync(page, includeDrafts: false);
				html = WikiController.GeneratePrerenderHtml(
					localized,
					$"{canonicalBase}/wiki/{page.Namespace}/{page.Category}/{page.Slug}",
					alternates,
					localization.DefaultLocale);
			}
```

Preserve whatever `Published` / visibility condition each branch currently has — read them before editing and change only the generator call and the resolution. Apply the same shape to the `/character/` branch (using `GenerateCharacterPrerenderHtml`) and the `/help/` branch.

Add `using SharpMUSH.Library.Services;` for `WikiHelpers` if absent.

- [ ] **Step 5: Add sitemap alternates**

`SharpMUSH.Server/Controllers/SeoController.cs` — inject the localization service and declare the xhtml namespace. Constructor:

```csharp
public class SeoController(
	IWikiService wikiService,
	IWikiLocalizationService localization,
	ILogger<SeoController> logger) : ControllerBase
```

Change the `urlset` opening tag (line 35):

```csharp
		sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">");
```

Extend `AppendUrl` with an optional alternates list, keeping its current two-argument behaviour for the non-page entries:

```csharp
	private static void AppendUrl(
		StringBuilder sb, string loc, string lastmod, IReadOnlyList<string>? alternateLocales = null)
	{
		sb.AppendLine("  <url>");
		sb.AppendLine($"    <loc>{SecurityElement.Escape(loc)}</loc>");
		sb.AppendLine($"    <lastmod>{lastmod}</lastmod>");

		// Only worth emitting when there is more than one locale to point at.
		if (alternateLocales is { Count: > 1 })
		{
			foreach (var locale in alternateLocales)
			{
				var href = $"{loc}{(loc.Contains('?') ? '&' : '?')}lang={Uri.EscapeDataString(locale)}";
				sb.AppendLine(
					$"    <xhtml:link rel=\"alternate\" hreflang=\"{SecurityElement.Escape(locale)}\" href=\"{SecurityElement.Escape(href)}\" />");
			}
		}

		sb.AppendLine("  </url>");
	}
```

In the paging loop, pass the locales (bot-facing, so `includeDrafts: false`):

```csharp
				AppendUrl(
					sb,
					baseUrl + PathFor(page),
					page.UpdatedAt.ToString("yyyy-MM-dd"),
					await localization.GetVisibleLocalesAsync(page, includeDrafts: false));
```

- [ ] **Step 6: Fix the existing generator call sites**

Run: `grep -rn "GeneratePrerenderHtml\|GenerateCharacterPrerenderHtml\|new SeoController(" SharpMUSH.Tests SharpMUSH.Tests.Integration SharpMUSH.Server`

Update each: production call sites are the middleware (done in Step 4); tests (`SeoControllerTests.cs`, `WikiControllerHtmlTests.cs`, `SeoEndpointTests.cs`) need a `LocalizedWikiPage` and an alternates list. Build the `LocalizedWikiPage` through a real `WikiLocalizationService` over the test's `InMemoryWikiService` rather than by hand — that keeps the tests honest about the "only one type constructs this record" invariant.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiPrerenderLocaleTests/*"`
Expected: PASS (7 tests).

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed. `SeoControllerTests` and `WikiControllerHtmlTests` must both be green.

`SeoEndpointTests` lives in `SharpMUSH.Tests.Integration` and needs Docker — **CI-verified**, job `test-integration`, all three matrix entries.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Server SharpMUSH.Tests/Server/Controllers
git commit -m "feat(wiki): emit hreflang alternates and the served locale in bot prerenders"
```

---

### Task 20: In-game `@WIKI` and the `wiki()` family

`@WIKI` reads the executor's existing `LOCALE` attribute rather than inventing a switch — the exact pattern is at `SharpMUSH.Implementation/Commands/MoreCommands.cs:2976-3005` (connection metadata `"Locale"` first, persisted `LOCALE` attribute as the `@force`-context fallback). A new `/SOURCE` switch forces the source locale.

**The switch-dispatch trap:** `WikiCommands.cs:34` computes `actions = switches.Where(s => s != "NOEVAL")` and errors with `TooManySwitches` when more than one remains. `SOURCE` is a modifier, not an action, so it must be excluded the same way or `@wiki/view/source` errors out.

**Files:**
- Modify: `SharpMUSH.Implementation/Commands/WikiCommands.cs` (add `SOURCE`; exclude it from `actions`; resolve the locale; pass it down)
- Modify: `SharpMUSH.Implementation/Commands/WikiCommand/WikiCommandHelper.cs` (add `ResolveExecutorLocaleAsync`)
- Modify: `SharpMUSH.Implementation/Commands/WikiCommand/ViewWiki.cs` (`Handle` and `History` take a locale)
- Modify: `SharpMUSH.Implementation/Commands/WikiCommand/ListWiki.cs` (`List` and `Recent` take a locale)
- Modify: `SharpMUSH.Implementation/Commands/WikiCommand/EditWiki.cs:45` (`CreateAsync` gains `sourceLocale`, so an in-game page is stamped at creation — the second and last create path in the codebase)
- Modify: `SharpMUSH.Implementation/Functions/WikiFunctions.cs` (`wiki()` `MaxArgs` 2 → 3; `locale` field; `wikilist()` / `wikirecent()` localized titles)
- Modify: `SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpwiki.md` (document `/SOURCE` and the third `wiki()` argument)
- Test: `SharpMUSH.Tests/Commands/WikiCommandTests.cs` (append)
- Test: `SharpMUSH.Tests/Functions/WikiFunctionUnitTests.cs` (append)

**Interfaces:**
- Consumes: `IWikiLocalizationService` via `parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>()` (the same idiom the existing code uses for `IWikiService`).
- Produces:
  - `WikiCommandHelper.ResolveExecutorLocaleAsync(IMUSHCodeParser parser, AnySharpObject executor)` → `ValueTask<string?>` — null when nothing is set, which the localization service reads as "use the configured default".
  - `WikiCommandHelper.FormatPageLine(LocalizedWikiPage page)` overload alongside the existing `WikiPage` one.
  - `wiki()` field `locale`; `wiki()` third positional argument.

- [ ] **Step 1: Write the failing tests**

Append to `SharpMUSH.Tests/Commands/WikiCommandTests.cs` (follow the file's existing parser-invocation idiom exactly — it already drives `@wiki/create` and `@wiki/view` end to end):

```csharp
	[Test]
	public async Task WikiView_ServesTheExecutorsLocaleWhenATranslationExists()
	{
		// Seeded by the harness: @locale fr, then a French translation via the service.
		await SetExecutorLocaleAsync("fr");
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", published: true);

		var output = await RunAsync("@wiki/view dragons");

		await Assert.That(output).Contains("corps fr");
		await Assert.That(output).DoesNotContain("en body");
	}

	[Test]
	public async Task WikiView_WithSourceSwitchForcesTheSourceLocale()
	{
		await SetExecutorLocaleAsync("fr");
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", published: true);

		var output = await RunAsync("@wiki/view/source dragons");

		await Assert.That(output)
			.Contains("en body")
			.Because("/SOURCE is how staff read what a translator is translating from");
	}

	[Test]
	public async Task WikiView_SourceSwitchDoesNotCountAsASecondAction()
	{
		await CreateWikiPageAsync("Dragons", "en body");

		var output = await RunAsync("@wiki/view/source dragons");

		await Assert.That(output)
			.DoesNotContain("switch")
			.Because("SOURCE is a modifier like NOEVAL, not an action, so it must not trip TooManySwitches");
	}

	[Test]
	public async Task WikiView_FallsBackToTheSourceWhenTheLocaleHasNoTranslation()
	{
		await SetExecutorLocaleAsync("de");
		await CreateWikiPageAsync("Dragons", "en body");

		var output = await RunAsync("@wiki/view dragons");

		await Assert.That(output).Contains("en body");
	}

	[Test]
	public async Task WikiView_DraftTranslationDoesNotLeakInGame()
	{
		await SetExecutorLocaleAsync("fr");
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Brouillon", "corps brouillon", published: false);

		var output = await RunAsync("@wiki/view dragons");

		await Assert.That(output).DoesNotContain("brouillon");
		await Assert.That(output).Contains("en body");
	}

	[Test]
	public async Task WikiList_ShowsLocalizedTitles()
	{
		await SetExecutorLocaleAsync("fr");
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", published: true);

		var output = await RunAsync("@wiki/list");

		await Assert.That(output).Contains("Dragons (fr)");
	}
```

Add the three helpers (`SetExecutorLocaleAsync`, `CreateWikiPageAsync`, `UpsertTranslationAsync`) to the same class if it does not already have equivalents, resolving `IWikiService` from the test harness's service provider the way the file's existing tests do.

Two things the helpers must get right, both from Task 5's contract:

- `CreateWikiPageAsync` passes an explicit `sourceLocale` (`"en"` for these fixtures), so the pages under test are stamped rather than relying on a migration these tests never run.
- the test-local `UpsertTranslationAsync(pageId, locale, title, markdown, published)` wrapper forwards `expectedRevisionNumber: null` — every call above is a first write for its locale, so create-only is correct and there is no revision to compare. If a later test needs a second write to the same locale, that call passes the loaded number explicitly rather than the helper defaulting it.

Also give `EditWiki.cs`'s `CreateAsync` call (`SharpMUSH.Implementation/Commands/WikiCommand/EditWiki.cs:45`) a source locale, so a page created in-game is stamped at birth exactly as the API path is (Task 12, Step 5a):

```csharp
		var result = await wikiService.CreateAsync(
			title, markdown, executor.Object().DBRef.ToString(), ns, category, localization.DefaultLocale);
```

That is the second and last create path in the codebase; after it, nothing writes an unstamped `WikiPage`. Match the file's existing argument names and dbref expression rather than copying the placeholders above verbatim.

Append to `SharpMUSH.Tests/Functions/WikiFunctionUnitTests.cs`:

```csharp
	[Test]
	public async Task Wiki_ThirdArgumentSelectsTheLocale()
	{
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", published: true);

		await Assert.That(await EvaluateAsync("wiki(dragons,title,fr)")).IsEqualTo("Dragons (fr)");
		await Assert.That(await EvaluateAsync("wiki(dragons,text,fr)")).Contains("corps fr");
	}

	[Test]
	public async Task Wiki_LocaleFieldReturnsTheServedLocale()
	{
		var page = await CreateWikiPageAsync("Dragons", "en body");
		await UpsertTranslationAsync(page.Id, "fr", "Dragons (fr)", "corps fr", published: true);

		await Assert.That(await EvaluateAsync("wiki(dragons,locale,fr)")).IsEqualTo("fr");
		await Assert.That(await EvaluateAsync("wiki(dragons,locale,de)"))
			.IsEqualTo("en")
			.Because("the locale field reports what was served, which is how softcode detects a fallback");
	}

	[Test]
	public async Task Wiki_UnparseableLocaleFallsBackRatherThanErroring()
	{
		await CreateWikiPageAsync("Dragons", "en body");

		await Assert.That(await EvaluateAsync("wiki(dragons,text,not a locale)")).Contains("en body");
	}

	[Test]
	public async Task Wiki_FourArgumentsIsStillAnArgumentCountError()
	{
		await Assert.That(await EvaluateAsync("wiki(dragons,text,fr,extra)")).Contains("#-1");
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiCommandTests/*"`
Expected: FAIL — `@wiki/view` serves English, and `/source` is rejected as an unknown switch.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiFunctionUnitTests/*"`
Expected: FAIL — `wiki()` rejects three arguments (`MaxArgs = 2`).

- [ ] **Step 3: Add the locale resolver helper**

Append to `SharpMUSH.Implementation/Commands/WikiCommand/WikiCommandHelper.cs`:

```csharp
	/// <summary>
	/// The executor's locale for wiki reads: the connection's <c>Locale</c> metadata when the command came
	/// from a real connection, otherwise the persisted <c>LOCALE</c> attribute (the <c>@force</c> case).
	/// Returns null when neither is set, which <c>IWikiLocalizationService</c> reads as "use the configured
	/// default" — the same contract <c>?lang=</c> has on the web side.
	/// </summary>
	/// <remarks>
	/// Mirrors the read in <c>Commands.SetLocale</c> (<c>MoreCommands.cs</c>) rather than inventing a second
	/// source of truth for what locale a player is on.
	/// </remarks>
	public static async ValueTask<string?> ResolveExecutorLocaleAsync(
		IMUSHCodeParser parser, AnySharpObject executor)
	{
		var handle = parser.CurrentState.Handle;
		if (handle.HasValue)
		{
			var connectionService = parser.ServiceProvider.GetRequiredService<IConnectionService>();
			var conn = connectionService.Get(handle.Value);
			if (conn is not null
				&& conn.Metadata.TryGetValue("Locale", out var stored)
				&& !string.IsNullOrEmpty(stored))
				return stored;
		}

		var database = parser.ServiceProvider.GetRequiredService<ISharpDatabase>();
		var localeAttrs = database.GetAttributeAsync(executor.Object().DBRef, ["LOCALE"], CancellationToken.None);
		await foreach (var attr in localeAttrs)
		{
			var saved = attr.Value.ToPlainText();
			if (!string.IsNullOrEmpty(saved)) return saved;
		}

		return null;
	}

	/// <summary>
	/// One listing line for a page resolved into a locale. Identical to the
	/// <see cref="FormatPageLine(WikiPage)"/> overload except that the title is the served locale's.
	/// </summary>
	public static string FormatPageLine(LocalizedWikiPage page)
	{
		var markers = $"{(page.Published ? "" : " (draft)")}{(page.Page.IsProtected ? " (protected)" : "")}";
		return $"{DisplayReference(page.Page),-30} {page.Title} (rev {page.RevisionNumber}, {page.Page.UpdatedAt:yyyy-MM-dd}){markers}";
	}
```

Add whatever `using` directives that file needs for `IConnectionService`, `ISharpDatabase` and `LocalizedWikiPage` — copy them from `MoreCommands.cs` and `WikiCommands.cs`.

- [ ] **Step 4: Add `/SOURCE` and thread the locale through the command**

`SharpMUSH.Implementation/Commands/WikiCommands.cs` — add `"SOURCE"` to the `Switches` array:

```csharp
[SharpCommand(Name = "@WIKI",
	Switches =
	[
		"VIEW", "LIST", "SEARCH", "RECENT", "HISTORY", "CREATE", "EDIT", "APPEND", "ROLLBACK",
		"DELETE", "PROTECT", "UNPROTECT", "CATEGORY", "TAG", "PUBLISH", "UNPUBLISH", "NOEVAL", "SOURCE"
	],
	Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2,
	ParameterNames = ["page", "content"])]
```

Change the `actions` computation (line 34) so `SOURCE` is a modifier, not an action:

```csharp
		// NOEVAL and SOURCE are modifiers, not actions: leaving either in this set would make
		// "@wiki/view/source foo" look like two actions and trip TooManySwitches.
		var actions = switches.Where(s => s is not ("NOEVAL" or "SOURCE")).ToArray();
		var forceSource = switches.Contains("SOURCE");
```

Resolve the locale once, before the dispatch switch, next to the existing `wikiService` resolution:

```csharp
		var wikiService = parser.ServiceProvider.GetRequiredService<IWikiService>();
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();

		// /SOURCE forces the page's own locale by asking for nothing the resolver can match; otherwise the
		// executor's LOCALE decides, exactly as ?lang= does on the web.
		var locale = forceSource
			? null
			: await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor);
```

For `/SOURCE`, passing `null` is not enough — `null` means "configured default", which on an `en` game is the source anyway but on a `fr`-default game is not. Add an explicit flag instead: pass `forceSource` down and have the read helpers skip localization entirely when it is set. Update the four affected dispatch arms to pass both:

```csharp
			"VIEW" when hasArg0 => await ViewWiki.Handle(
				parser, Mediator!, wikiService, localization, NotifyService!, arg0!, locale, forceSource),
			"HISTORY" when hasArg0 => await ViewWiki.History(
				parser, Mediator!, wikiService, NotifyService!, arg0!, locale, forceSource),
			"LIST" => await ListWiki.List(
				parser, Mediator!, wikiService, localization, NotifyService!, arg0, locale, forceSource),
			"RECENT" => await ListWiki.Recent(
				parser, Mediator!, wikiService, localization, NotifyService!, arg0, locale, forceSource),
```

Leave every other arm's signature untouched — write paths do not localize.

- [ ] **Step 5: Localize the read helpers**

`ViewWiki.Handle` — after the existing `GetBySlugAsync` lookup and not-found notify, resolve and render the localized body:

```csharp
		var page = lookup.AsT0;

		// In-game readers see drafts only if they could edit the page; @wiki has no per-locale permission
		// of its own, so reuse the page-edit gate the write paths already use.
		var includeDrafts = await WikiCommandHelper.CanEdit(executor, page);
		var localized = forceSource
			? null
			: await localization.LocalizeAsync(page, locale, includeDrafts);

		var title = localized?.Title ?? page.Title;
		var markdown = localized?.MarkdownSource ?? page.MarkdownSource;
		var body = RecursiveMarkdownHelper.RenderMarkdown(markdown, RenderWidth, parser);
```

and use `title` / `body` in the existing header-and-body emission, plus append a fallback marker to the header line when `localized?.IsFallback == true`:

```csharp
		var localeMarker = localized is { IsFallback: true } ? $" [{localized.Locale}]" : string.Empty;
```

Splice `localeMarker` into the existing header block next to the revision/date info — read the current header construction (lines 45–46) and add it there rather than restructuring.

`ViewWiki.History` — pick the stream the same way the controller does:

```csharp
		var revisions = forceSource || locale is null
			? await wikiService.GetRevisionsAsync(page.Id)
			: await wikiService.GetRevisionsForLocaleAsync(
				page.Id, WikiHelpers.NormalizeLocaleOrEmpty(locale), 0, 20);
```

`ListWiki.List` and `ListWiki.Recent` — after fetching pages, localize and format:

```csharp
		var lines = forceSource
			? pages.Select(WikiCommandHelper.FormatPageLine)
			: (await localization.LocalizeAllAsync(pages.ToList(), locale, includeDrafts: false))
				.Select(WikiCommandHelper.FormatPageLine);
```

Keep `ListWiki.Search` on the un-localized path: it matches against `PlainText`, which is the source body, and making search locale-aware is a separate piece of work the spec does not scope.

- [ ] **Step 6: Extend the `wiki()` family**

`SharpMUSH.Implementation/Functions/WikiFunctions.cs` — change `wiki()`'s attribute and add the locale plumbing:

```csharp
	[SharpFunction(Name = "wiki", MinArgs = 1, MaxArgs = 3,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["page", "field", "locale"])]
```

In the body, after resolving `field`, read the optional third argument and default it to the executor's `LOCALE`:

```csharp
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();

		// Third argument wins; otherwise the executor's LOCALE, exactly as @wiki does. An unparseable tag
		// is treated as absent by the localization service — a bad locale must not turn a read into #-1.
		var explicitLocale = parser.CurrentState.Arguments.Count > 2
			? parser.CurrentState.Arguments["2"].Message!.ToPlainText()
			: null;
		var locale = string.IsNullOrWhiteSpace(explicitLocale)
			? await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor)
			: explicitLocale;

		var localized = await localization.LocalizeAsync(lookup.AsT0, locale, includeDrafts: false);
```

Change the field switch (lines 39–51) so content fields read the wrapper and metadata fields read the page:

```csharp
		return field switch
		{
			"text" => new CallState(localized.PlainText),
			"markdown" or "source" => new CallState(localized.MarkdownSource),
			"title" => new CallState(localized.Title),
			"locale" => new CallState(localized.Locale),
			"category" => new CallState(localized.Page.Category ?? string.Empty),
			"tags" => new CallState(string.Join(" ", localized.Page.Tags)),
			"namespace" => new CallState(localized.Page.Namespace),
			"revision" => new CallState(localized.RevisionNumber.ToString()),
			"updated" => new CallState(localized.Page.UpdatedAt.ToUnixTimeSeconds().ToString()),
			"author" => new CallState(localized.Page.AuthorDbref),
			_ => new CallState("#-1 UNKNOWN WIKI FIELD"),
		};
```

Reproduce the exact existing arm names and return shapes when you edit — the list above matches what the current code returns; verify against lines 39–51 before replacing, and keep any arm this plan has not named.

`wikilist()` and `wikirecent()` — localize their titles the same way `ListWiki` does:

```csharp
		var localization = parser.ServiceProvider.GetRequiredService<IWikiLocalizationService>();
		var locale = await WikiCommandHelper.ResolveExecutorLocaleAsync(parser, executor);
		var localized = await localization.LocalizeAllAsync(pages.ToList(), locale, includeDrafts: false);
		return new CallState(string.Join(" ", localized.Select(p => WikiCommandHelper.DisplayReference(p.Page))));
```

`DisplayReference` returns a slug-based reference, not a title, so these two functions' *output* does not actually change — but resolving through the service keeps them honest if the reference format ever grows a title. Leave `wikisearch()` alone for the same reason `ListWiki.Search` is left alone.

- [ ] **Step 7: Document the new surface**

`SharpMUSH.Documentation/Helpfiles/SharpMUSH/sharpwiki.md` — add to the `@wiki` switch list and the `wiki()` description:

```markdown
### Locale

`@wiki` reads pages in your locale, set with `@locale`. When your locale has no
translation you get the fallback version with its locale shown in brackets on the
header line.

- `@wiki/view/source <page>` — read the page in the locale it was written in,
  ignoring your own. Useful when translating.
- `@wiki/history <page>` shows your locale's revision stream;
  `@wiki/history/source <page>` shows the source locale's.

`wiki(<page>, <field>, [<locale>])` takes an optional third argument naming a
locale; it defaults to your `LOCALE`. The `locale` field returns the locale
actually served, which is how softcode detects that it got a fallback:

```
think wiki(markdown_guide, locale, fr)
```

Unpublished translations are invisible unless you could edit the page.
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiCommandTests/*"`
Expected: PASS.

Run: `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WikiFunctionUnitTests/*"`
Expected: PASS.

Run: `dotnet build && dotnet run --project SharpMUSH.Tests`
Expected: 0 errors; 4927 total / 0 failed. `WikiSyntaxInGameRenderingTests` must stay green — the rendering path is unchanged, only which Markdown reaches it.

- [ ] **Step 9: Commit**

```bash
git add SharpMUSH.Implementation SharpMUSH.Documentation SharpMUSH.Tests
git commit -m "feat(wiki): read the executor's locale in @wiki and the wiki() family"
```

---

### Task 21: Seeded pages get a `SourceLocale`

The three seeded pages are English, so stamping `"en"` on them is correct and is not the same as defaulting the property to `"en"`. Stamping the *seeds* records a fact about their content; a game's own pages are stamped with that game's configured default by the create paths in Tasks 12 and 20, and anything predating all of that is stamped once by the Tasks 7–9 backfill. In no case is the value re-derived on read.

**No translations are seeded.** Machine-quality French help is worse than a visible gap, and the fallback notice makes the gap actionable. Translating the seeded Help pages is content work with native review, tracked separately.

**Files:**
- Modify: `SharpMUSH.Server/StartupHandler.cs:493-548` (three `CreateAsync` calls gain `sourceLocale: "en"`)
- Test: `SharpMUSH.Tests.Integration/Wiki/WikiStartupSeedingTests.cs` (append — including the backfill assertions, which need a real migrated database and so belong here rather than in the unit suite)

**Interfaces:**
- Consumes: `CreateAsync(..., string? sourceLocale = null)` (Task 5); the Tasks 7–9 backfill.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

Append to `SharpMUSH.Tests.Integration/Wiki/WikiStartupSeedingTests.cs` (tabs, matching that file):

```csharp
	[Test]
	public async Task SeededPagesCarryAnExplicitSourceLocale()
	{
		var result = await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.SourceLocale)
			.IsEqualTo("en")
			.Because("the seeded pages are English, and labelling them keeps a non-English game from mislabelling them");
	}

	[Test]
	public async Task SeededHelpPagesCarryAnExplicitSourceLocale()
	{
		var result = await Wiki.GetBySlugAsync("markdown_guide", "general", WikiNamespace.Help);

		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.SourceLocale).IsEqualTo("en");
	}

	[Test]
	public async Task NoTranslationsAreSeeded()
	{
		var result = await Wiki.GetBySlugAsync("markdown_guide", "general", WikiNamespace.Help);
		await Assert.That(result.IsT0).IsTrue();

		var translations = await Wiki.GetTranslationsAsync(result.AsT0.Id);

		await Assert.That(translations)
			.IsEmpty()
			.Because("machine-quality translated help is worse than a visible gap the fallback notice makes actionable");
	}

	[Test]
	public async Task SeedingStaysIdempotentWithSourceLocalePresent()
	{
		// StartupHandler ran once before this suite. A second pass must be a no-op, not a duplicate or an
		// error, and must not change the recorded source locale.
		var before = (await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main)).AsT0;

		var second = await Wiki.CreateAsync("Home", "different body", "#1", WikiNamespace.Main, "general", "en");

		await Assert.That(second.IsT1)
			.IsTrue()
			.Because("CreateAsync rejects a duplicate identity, which is what makes seeding idempotent");
		var after = (await Wiki.GetBySlugAsync("home", "general", WikiNamespace.Main)).AsT0;
		await Assert.That(after.SourceLocale).IsEqualTo(before.SourceLocale);
		await Assert.That(after.MarkdownSource).IsEqualTo(before.MarkdownSource);
	}

	[Test]
	public async Task BackfillIsANoOpOnASecondPassAndLeavesNoUnstampedRow()
	{
		// The migration ran during harness startup and runs again on every start, so "idempotent" has to be
		// true rather than assumed. Two assertions: nothing on this database is unstamped, and no revision
		// row lacks the Locale the new unique constraint covers.
		var pages = await Wiki.GetAllPagesAsync(0, 200);

		await Assert.That(pages.All(p => p.SourceLocale.Length > 0))
			.IsTrue()
			.Because("Migration_AddWikiTranslations stamps every page once; a read-time default would hide "
				+ "an unstamped row instead of the migration fixing it");

		foreach (var page in pages)
		{
			var revisions = await Wiki.GetRevisionsAsync(page.Id, 0, 50);
			await Assert.That(revisions.All(r => r.Locale.Length == 0))
				.IsTrue()
				.Because("source-locale revisions carry the empty stream marker, stamped by the backfill "
					+ "before the unique (PageId, Locale, RevisionNumber) constraint was created");
		}
	}
```

- [ ] **Step 2: Verify it compiles and is red for the right reason**

Run: `dotnet build SharpMUSH.Tests.Integration`
Expected: 0 errors.

**This file runs locally under Podman.** Confirm its red state rather than assuming it: `SourceLocale` must come back *empty* before the fix, because `SeedWikiPagesAsync` does not set it yet. Then verify green against every provider.

- [ ] **Step 3: Stamp the locale on all three seeded pages**

`SharpMUSH.Server/StartupHandler.cs`, in `SeedWikiPagesAsync` — add the argument to each of the three `wikiService.CreateAsync(...)` calls (Home at ~line 500, Markdown Guide at ~line 526, Application Schema Guide at ~line 539):

```csharp
		var homeResult = await wikiService.CreateAsync(
			title: "Home",
			markdown: """…""",
			authorDbref: "#1",
			ns: WikiNamespace.Main,
			category: "general",
			sourceLocale: "en");
```

Do the same for the other two. Leave the markdown bodies, the `Switch(...)` logging and `LogSeedSkip` exactly as they are.

Update the method's doc comment to record the deliberate absence:

```csharp
	/// <summary>
	/// Seeds the default Home, Markdown Guide, and Application Schema Guide wiki pages (idempotent — no-op
	/// if present). The pages are English, so they are stamped with an explicit <c>SourceLocale</c>.
	/// No translations are seeded: machine-quality translated help is worse than a gap the reader's
	/// fallback notice makes actionable, so translating these is content work tracked separately.
	/// </summary>
```

- [ ] **Step 4: Verify locally as far as possible**

Run: `dotnet build`
Expected: 0 errors.

Run: `dotnet run --project SharpMUSH.Tests`
Expected: 4927 total / 0 failed.

**Acceptance gate:** CI `test-integration` green on all three matrix entries, which is what actually exercises `WikiStartupSeedingTests`.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Server/StartupHandler.cs SharpMUSH.Tests.Integration/Wiki/WikiStartupSeedingTests.cs
git commit -m "feat(wiki): stamp SourceLocale on the seeded pages"
```

---

### Task 22: Documentation

Three docs record what was built, and one of them (`url-strategy.md`) the spec names explicitly. While in `url-strategy.md`, its wiki route examples are stale — they show the pre-category `/wiki/Page_Name` form the code stopped using — so fix them in the same pass rather than adding a Locale section next to wrong neighbours.

**Files:**
- Modify: `docs/design/url-strategy.md` (new `### Locale` under `## URL Conventions`; fix the stale wiki route examples; note the hreflang emission and the unchanged canonical)
- Modify: `Localization.md` (new `## Wiki Content Localization` section; add the new files to `## File Reference`)
- Modify: `docs/todo/area-05-wiki.md` (new Localization block; move the relevant "Remaining" bullets)

**Interfaces:**
- Consumes: everything above.
- Produces: nothing consumed by code.

- [ ] **Step 1: Fix and extend `docs/design/url-strategy.md`**

Replace the stale `### Wiki Pages` body (lines 71–77) so it matches the code's actual `/wiki/{ns}/{category}/{slug}` shape:

```markdown
### Wiki Pages

- `/wiki/{namespace}/{category}/{slug}` — the canonical page route. All three
  segments are part of a page's identity.
- Underscores replace spaces in the slug; lookup is case-insensitive
- Display always shows the page's canonical title (original case)
- Special characters in slugs are percent-encoded
- `/help/{topic}` and `/character/{name}` are aliases resolving to the `help`
  and `character` namespaces at the default `general` category
```

Add the new subsection immediately after it:

```markdown
### Locale

`?lang=<BCP-47 tag>` is the **only** locale mechanism for wiki content. There is
no locale path prefix and no locale-suffixed slug.

- One canonical slug per page, whatever locale is being read. `?lang=` selects a
  view of that page; it never creates a second page, and it never changes the
  `<link rel="canonical">` the prerender emits.
- A malformed or unknown tag is treated as absent and falls back to the
  configured `Wiki.DefaultLocale`. It is never a 400 — a read cannot fail for
  locale reasons.
- Absent `?lang=`, the portal sends the locale from the `locale` localStorage key
  the language picker writes. An explicit `?lang=` wins over that preference.
- `?lang=` is accepted on the page read, the listing endpoints
  (`recent`, `ns/{ns}`, `pages`, `category/{c}`, `tag/{t}`) and
  `{slug}/revisions`. Listings return localized titles and still return **one row
  per page**.
- `[[WikiLink]]` targets and the unique slug index are unaffected: neither has a
  locale dimension.

Because `?lang=` does not change page identity, it also does not change any
permalink: a link someone copies out of the address bar keeps working when the
reader's locale differs.
```

In `## SEO / Pre-rendering`, after `### OpenGraph Tags`, add:

```markdown
### hreflang

Prerendered wiki pages emit one `<link rel="alternate" hreflang="…">` per locale
the page can actually be read in, plus `hreflang="x-default"` at
`Wiki.DefaultLocale`, and set `<html lang>` to the locale actually served. The
sitemap carries the same alternates as `xhtml:link` entries.

Nothing is emitted for a single-locale page, and unpublished translations are
never advertised — the prerender path resolves with drafts excluded, since it is
unauthenticated by definition.
```

In `## Canonical URLs`, append:

```markdown
- `?lang=` is never canonical. Every locale of a page shares the unsuffixed
  canonical URL, so translations consolidate ranking signals instead of
  competing with each other.
```

- [ ] **Step 2: Extend `Localization.md`**

Add a new section after `## Blazor Admin UI Localization` (which ends around line 217, before `## Adding a New Language`):

```markdown
---

## Wiki Content Localization

Portal chrome and engine notifications are localized through resx files. Wiki
*content* is not — it lives in the database, so it has its own mechanism.

### Shape

A `WikiPage` keeps identity, metadata and the body in the locale it was authored
in (`SourceLocale`), which is stamped once — at creation for new pages, by
`Migration_AddWikiTranslations` for pages predating the field — and never
re-derived on read, so changing `wiki_default_locale` cannot relabel existing
content. Each translation is an overlay row, `WikiTranslation`, keyed
by `(PageId, Locale)`. A translation owns its `Title`, `MarkdownSource`,
`Published` flag and revision history, and **inherits** `Category`, `Tags` and
`IsProtected` from the source page — structurally, because `WikiTranslation` has
no field for them.

### Reading

`?lang=<tag>` selects a locale; absent it, the portal sends the `locale`
localStorage key the language picker writes. Resolution order:

1. Requested locale, normalised; unparseable becomes `Wiki.DefaultLocale` — a bad
   `?lang=` is never an error, only a write of a bad locale is
2. The page's own stamped source locale, if it is the requested language
3. Exact match against the visible translations
4. Neutral-language match (`fr-CA` finds `fr`, and vice versa)
5. `Wiki.DefaultLocale`, if a translation exists for it
6. The source locale — always available, so a read never fails

When the served language differs from the requested one, the reader gets the
fallback page plus a dismissible notice. Dismissal is per-session on purpose.

### Which locales are allowed?

**Any tag `CultureInfo.GetCultureInfo` accepts** — not only the locales the
portal chrome has a resx for. A game can translate its wiki into Spanish while
the chrome falls back to English. The editor's locale dropdown offers
`PortalLocales.Codes` ∪ existing translations, plus a free-text field.

### Drafts do not leak

`IWikiLocalizationService` filters the candidate translation set by visibility
*before* the resolver sees it, so an unpublished translation is unreachable
rather than merely un-rendered: an ordinary reader falls through to the next
step exactly as if it did not exist. `IWikiLocaleResolver` is permission-blind by
design, which is what keeps the fallback rules unit-testable with no auth graph.

### Adding a translation

- Portal: `/wiki/{ns}/{category}/{slug}/edit?lang=fr`
- API: `PUT /api/wiki/{slug}/translations/{locale}?ns=&category=`, carrying the
  `expectedRevisionNumber` the editor loaded. A concurrent save answers **409**;
  the editor offers a reload and never retries, because retrying would re-apply
  the loser's stale markdown over the winner's.
- In-game: read with `@wiki` in your `@locale`; `@wiki/view/source` reads the
  source. `wiki(<page>, <field>, [<locale>])` takes an explicit locale, and its
  `locale` field returns what was actually served.

Configuration: `wiki_default_locale` (`Wiki.DefaultLocale`, default `en`) in
`/admin/config/wiki`.

### Not localized

- The on-disk helpfiles under `SharpMUSH.Documentation/Helpfiles/` served to the
  telnet `help` command. They never touch `IWikiService`.
- `mush-defs.json`, generated from `[SharpFunction]`/`[SharpCommand]` attributes.
- Category *names*. Category is part of page identity, so a translation cannot
  carry its own; localized category display names are a separate concern.
```

Add to `## File Reference`:

```markdown
- `SharpMUSH.Library/Services/WikiLocaleResolver.cs` — the fallback chain (pure)
- `SharpMUSH.Library/Services/WikiLocalizationService.cs` — visibility filtering; the only
  `LocalizedWikiPage` factory
- `SharpMUSH.Library/Models/Wiki/WikiTranslation.cs` — the overlay row
- `SharpMUSH.Contracts/Services/WikiHelpers.cs` — `NormalizeLocale` (write boundary, returns `OneOf`) /
  `NormalizeLocaleOrEmpty` (permissive read path) / `NeutralLocale` / `SameLanguage`
- `SharpMUSH.Database.ArangoDB/Migrations/Migration_AddWikiTranslations.cs` — the `SourceLocale` /
  `WikiRevision.Locale` backfill and the unique revision constraint (equivalents in the SurrealDB and
  Memgraph migration statement lists)
- `SharpMUSH.Client/Resources/PortalLocales.cs` — the portal's locale list, shared by the language
  picker and the wiki editor
- `SharpMUSH.Configuration/Options/WikiOptions.cs` — `wiki_default_locale`
```

- [ ] **Step 3: Update `docs/todo/area-05-wiki.md`**

Add a block before `## Testing`:

```markdown
## Localization
- [x] Per-locale content via `WikiTranslation` overlay rows keyed `(PageId, Locale)` — a translation owns Title / MarkdownSource / Published / revisions and inherits Category / Tags / IsProtected structurally; no schema migration and no content rewrite (one additive-column backfill)
- [x] `Wiki.DefaultLocale` (`wiki_default_locale`, default `en`, validated at startup) in `/admin/config/wiki`; `WikiPage.SourceLocale` is materialised once by the migration and never re-derived, so changing the default cannot relabel existing pages
- [x] Fallback, never 404 — `IWikiLocaleResolver` (pure, 5-step chain) + `IWikiLocalizationService` (visibility filtering, the only `LocalizedWikiPage` factory)
- [x] Drafts do not leak — the candidate set is filtered before resolution; an unpublished translation is unreachable for readers without edit permission
- [x] `?lang=` on the page read, all five listings and `{slug}/revisions`; translation CRUD at `/api/wiki/{slug}/translations[/{locale}]`, with `expectedRevisionNumber` optimistic concurrency answering 409 on a conflict (never retried)
- [x] Unique `(PageId, Locale, RevisionNumber)` constraint on all three DB backends, which disagreed before this change; asserted by a cross-backend test that checks the constraint *rejects* duplicates
- [x] Reader UI — dismissible fallback notice (per-session) + language chip row in `WikiDisplay.razor`
- [x] Authoring — locale selector in `WikiEdit.razor` with inherited Category/Tags visibly disabled; `/wiki/{ns}/{cat}/{slug}/edit?lang=`
- [x] Per-locale history and diff (`?lang=` on `WikiPageHistory` / `WikiPageDiff`)
- [x] Staff — translation-coverage column and locale filter (incl. "missing only") on `/admin/wiki`
- [x] SEO — `hreflang` alternates + `x-default` + `<html lang>` in the bot prerender, `xhtml:link` in the sitemap; canonical unchanged
- [x] In-game — `@wiki` reads the executor's `LOCALE`, `/SOURCE` forces the source; `wiki()` takes an optional third locale argument and a `locale` field
- [x] Tests — `WikiLocaleResolverTests`, `WikiLocalizationServiceTests` (draft visibility first-class), `WikiHelpersLocaleTests`, `WikiTranslationIntegrationTests` (cross-backend, including the negative constraint and concurrency cases), `WikiDisplayFallbackTests` / `WikiEditLocaleTests` (bUnit), seeding and backfill idempotency
```

and add to `## Remaining (out of portal scope or follow-up)`:

```markdown
- Translating the seeded Help pages — content work needing native review, deliberately not machine-translated
- Locale-aware wiki search (`@wiki/search`, `wikisearch()`, the omnisearch box) — matches source `PlainText` today
- Localized category *display* names — Category is part of page identity, so it cannot be translated through the overlay
- Listing performance: localized listings resolve per row. Measure before adding a denormalized title cache
- `WikiEdit` collects an edit summary and a minor-edit flag and discards both (predates localization)
- The `SourceLocale` backfill carries no rollback path, no language detection and no per-page override. That is deliberate while SharpMUSH is pre-production, because wiping and reseeding is acceptable recovery; the migration logs the locale it stamped and the row count, which is enough to notice a wrong default. **Revisit this first if a live game with existing wiki content ever adopts SharpMUSH.**
```

- [ ] **Step 4: Verify**

Run: `dotnet build`
Expected: 0 errors (docs only, but confirms nothing was edited by accident).

Read back each changed section and confirm no claim in it is aspirational — every `[x]` above must correspond to a task that actually landed. If any task was cut, cut its bullet too.

- [ ] **Step 5: Commit**

```bash
git add docs/design/url-strategy.md Localization.md docs/todo/area-05-wiki.md
git commit -m "docs(wiki): record the localization mechanism, ?lang= URL policy and hreflang emission"
```

---

## Definition of Done

- [ ] `dotnet build` — 0 errors, and no new warnings in any project with `TreatWarningsAsErrors`.
- [ ] `dotnet format whitespace --folder <each touched project>` reports no changes (run twice — it needs two passes to converge).
- [ ] `dotnet run --project SharpMUSH.Tests` — 0 failed, total ≥ 4927 (new tests added, none removed).
- [ ] `dotnet run --project SharpMUSH.Tests.BUnit` — 0 failed, total ≥ 271.
- [ ] `grep -rn "WikiTranslationsNotImplemented" SharpMUSH.Database.*/` — no output.
- [ ] `grep -rn "EffectiveSourceLocale" SharpMUSH.Library SharpMUSH.Server` — no output. Nothing re-derives `SourceLocale` on read; `IWikiLocalizationService.SourceLocaleOf` is the only accessor.
- [ ] All three backends declare a unique constraint over `(PageId, Locale, RevisionNumber)` and none still constrains the two-field `(pageId, revisionNumber)` pair. Verified by reading the three migration files, not by test colour — a backend missing its constraint makes the negative test pass silently, which is how the three drifted apart in the first place.
- [ ] CI `test-integration` green on **all three** matrix entries (`arangodb`, `memgraph`, `surrealdb`), specifically including `RevisionIndex_RejectsADuplicatePageLocaleRevisionNumber`, `RevisionIndex_AcceptsATranslationRevisionOneBesideASourceRevisionOne` and `ConcurrentUpsertsWithTheSameExpectedRevisionLoseNoProse`. CI is the confirming gate, not the only evidence: every one of these must also have been run locally per provider via `SHARPMUSH_DATABASE_PROVIDER`, which Podman makes possible. Never claim these passed without having actually run them somewhere.
- [ ] CI `format` job green.
- [ ] The five pre-existing `IWikiService` call sites still compile without behavioural change: `WikiController`, `SeoController`, `BotPrerenderMiddleware`, `WikiCommands`, `WikiFunctions`. (`WikiController` and `BotPrerenderMiddleware` gain the localization service by design; `SeoController` gains it for sitemap alternates; `WikiCommands` and `WikiFunctions` gain it for reads. What must not change is any *existing* method's behaviour when no locale is requested.)
- [ ] No seeded translations exist: `WikiStartupSeedingTests.NoTranslationsAreSeeded` passes.
- [ ] A bad `wiki_default_locale` fails startup naming the value, and `Startup.cs` registers `ValidateSharpOptions` rather than the generated validator directly (otherwise the check never runs).
- [ ] Follow-up issues opened for the five items added to `area-05-wiki.md`'s Remaining list.

## Self-Review Notes

Checked against the spec section by section:

| Spec section | Covered by |
|---|---|
| Data model — `WikiPage.SourceLocale`, `WikiTranslation`, `WikiRevision.Locale`, `WikiTranslationSummary`, `LocalizedWikiPage` | Task 3 |
| `SourceLocale` is materialised once, never re-derived | Tasks 3 (doc + initializer), 7–9 (backfill), 10 (`SourceLocaleOf`, and the removal of read-time normalisation), 12 + 20 (both create paths stamp it), 21 (seeds + backfill assertions) |
| Storage — `DatabaseConstants.WikiTranslations`, `Migration_AddWikiTranslations`, revision index, per-backend equivalents, in-memory dictionary | Tasks 5, 7, 8, 9 |
| The revision index must be corrected across all three backends | Task 6 (negative tests first), 7 (Arango: non-unique → unique 3-field), 8 (Memgraph: two loose indexes → a real composite constraint), 9 (SurrealDB: drop the 2-field UNIQUE, redefine over 3) |
| Configuration — `WikiOptions`, real `"en"` default, `ValidationPattern`, startup validation, schema-driven admin page | Task 1 |
| Which locales are allowed | Tasks 2 (`NormalizeLocale`), 14 (`PortalLocales`), 16 (free-text field) |
| Locales canonicalised and validated at the write boundary | Task 2 (`NormalizeLocale` / `NormalizeLocaleOrEmpty`), 5 (upsert + `CreateAsync`), 7–9 (backfill + per-backend upserts), 1 (options validation); read paths stay permissive in Tasks 11, 19, 20 |
| Resolution — `LocaleResolution`, `IWikiLocaleResolver`, the 5-step chain | Task 4 |
| Draft translations must not leak | Task 10 (service), 11 (controller `IncludeDrafts`), 19 (prerender always false), 20 (in-game gate) |
| `IWikiLocalizationService` | Task 10 |
| `IWikiService` additions (all five, `GetRevisionsForLocaleAsync` named not overloaded, upsert mirrors `UpdateAsync`, delete cascade) | Task 5 |
| `UpsertTranslationAsync` optimistic concurrency (`expectedRevisionNumber`, create-only on null, conflicts never retried) | Task 5 (contract + in-memory CAS), 7–9 (per-backend CAS), 12 (409, no retry), 14 (client), 16 (editor holds the revision, offers a reload) |
| HTTP surface — all five rows of the spec's table, plus `?lang=` on the five listings | Tasks 11, 12, 13 |
| Portal reading — `lang` from localStorage, `?lang=` override, `MudAlert` iff fallback, per-session dismissal, edit link, language chips | Tasks 14, 15 |
| Portal authoring — locale selector, inherited metadata disabled with hint, `?lang=` deep link, history/diff | Tasks 16, 17 |
| Portal staff — coverage column and locale filter | Task 18 |
| SEO — `IWikiLocalizationService`, `hreflang` + `x-default`, canonical unchanged, `url-strategy.md` Locale subsection | Tasks 19, 22 |
| In-game — `LOCALE` attribute, `/SOURCE`, `wiki()` MaxArgs 3, `wikilist()`/`wikirecent()` | Task 20 |
| Seeding — `SourceLocale` on seeded pages, no seeded translations | Task 21 |
| Error handling — all ten rows of the spec's table | Malformed lang on a read: Tasks 11, 19, 20. Malformed locale on a write: Tasks 2, 5–9. Invalid `Wiki.DefaultLocale`: Task 1. Upsert on nonexistent page / source shadow: Tasks 5–9. Concurrent **insert** race (retried once): Tasks 7–9. Concurrent **update** (never retried): Tasks 5–9, 12, 16. Null expected revision with an existing translation: Tasks 5–9, 12. Protected source page: Task 12. Delete last translation: Tasks 5, 6. Delete source page cascade: Tasks 5, 7, 8, 9 |
| Testing — all nine named suites | `WikiLocaleResolverTests` (4), `InMemoryWikiServiceTests` (5), `WikiServiceIntegrationTests` equivalent as `WikiTranslationIntegrationTests` including the negative constraint cases (6), concurrency (6), `NormalizeLocale` (2), backfill migration (7–9 + 21), draft visibility (10, 11), bUnit (15, 16), `WikiStartupSeedingTests` (21) |
| Risks — cross-backend test before backends and the three backends already disagreeing; backfill is pre-production-only and carries no rollback path; no pre-emptive title cache; category out of scope | Phase 2 ordering + Task 6's negative cases; Global Constraints + Tasks 7–9 + Task 22's Remaining list; Tasks 10/13/18 comments; Task 22 Remaining list |

**Deliberate readings of the spec, all flagged in-place rather than silently applied:**

1. **`CreateAsync` gains an optional trailing `sourceLocale`** (Task 5). The spec calls the `IWikiService` additions "purely additive" and lists five methods, but also says seeding "gains `SourceLocale`". An optional trailing parameter is source-compatible — every existing call site keeps compiling untouched, which is the constraint that actually matters — and it avoids a sixth method that exists only to set one field.
2. **`WikiEdit.razor` has no protection control to disable** (Phase 5 preamble). The spec says "category/tags/protection fields render visible but disabled"; protection is page-level and lives only in `AdminWiki`'s batch actions. Category and Tags are disabled; Published stays editable because a translation owns it, which is what makes "draft French while English stays live" work.
3. **`NormalizeLocale` is split in two** (Task 2). The spec gives one signature returning `OneOf<string, Error<string>>` and separately requires the read path to treat a bad `?lang=` as absent rather than a 400. One function cannot be both without every read site unwrapping an error it is contractually required to ignore, so `NormalizeLocale` is the spec's write-boundary signature verbatim and `NormalizeLocaleOrEmpty` is the permissive read form. The spec's table of entry points maps onto the two exactly.
4. **The migration backfill stamps `WikiOptions.DefaultLocaleFallback`, not `Wiki.DefaultLocale`** (Task 7). It cannot read the configured value: `OptionsService` is an `IOptionsFactory<SharpMUSHOptions>` over `ISharpDatabase`, so options live *in* the database the migration is preparing, and Core.Arango instantiates migrations reflectively with no DI. It does not need to — the migration ships in the same release that introduces `wiki_default_locale`, and an admin cannot have changed a setting that did not exist, so the configured value at backfill time is necessarily the parameter default. Task 1 makes that a single named constant so the two cannot drift.
5. **The startup locale check is restated in `ValidateSharpOptions` rather than calling `WikiHelpers.NormalizeLocale`** (Task 1). `SharpMUSH.Contracts`, where that helper lives, references `SharpMUSH.Configuration`; the dependency cannot run the other way. Task 2 adds a test asserting the two rules agree, so the duplication is held honest rather than merely noted.
6. **A translation-conflict response is 409, not 400** (Task 12). The spec says `Error<string>`; at the HTTP boundary that has to become a status code, and a well-formed request that lost a race is not a malformed one. The client distinguishes the two so the editor can offer a reload rather than a generic failure.
7. **`WikiLocalizationService` keeps one diagnostic path for an unstamped `SourceLocale`** (Task 10). The spec removes read-time normalisation, and this plan removes it from the resolver entirely. But a read can never fail for locale reasons, so a row the backfill has not reached still has to render: the service logs a Warning naming the page and uses the configured default for that one read. That is degradation over a broken row, expected never to fire, and nothing is allowed to depend on it — as distinct from the old design, where empty was normal, expected, and produced by `CreateAsync` on purpose.
