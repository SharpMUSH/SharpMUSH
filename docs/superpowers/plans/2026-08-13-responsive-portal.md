# Responsive Portal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make all 108 `SharpMUSH.Client` views correct at phone, portrait-tablet, thin-desktop and fullscreen widths by replacing viewport media queries in page CSS with container queries, behind an enforceable boundary between shell CSS and page CSS.

**Architecture:** The shell (`css/shell.css`) owns viewport `@media` and decides how much width the content column gets. `MainLayout` wraps `@Body` in `.phosphor-page`, which declares `container: page / inline-size`. Every page and component stylesheet queries that container with `@container`, never the viewport — which is the only correct model, because both widget-aside widths come from runtime admin settings and are invisible to a media query. Cascade layers put vendor CSS below ours so the 117 `!important` declarations can be deleted.

**Tech Stack:** Blazor WASM (.NET 10), MudBlazor 9.4.0, Blazor scoped CSS (`*.razor.css` → `[b-xxxxx]` rewriting), TUnit + bUnit 2.8.6 for the guard test, Playwright (bundled chromium at `~/.cache/ms-playwright`) for the visual/overflow sweep.

**Spec:** `docs/superpowers/specs/2026-08-13-responsive-portal-design.md`

## Global Constraints

- **Branch:** `feature/responsive-portal`, worktree `/home/grave/RiderProjects/SharpMUSH-responsive`, based on `origin/main` @ `11834d13`.
- **C# style:** tabs, indent size 2. Enforced at build time by `VerifyEditorConfigFormatting`; a failure reports `FORMAT001`. Fix with `dotnet format whitespace --folder <project-dir> --exclude "**/bin/**" --exclude "**/obj/**"`, run twice (the formatter needs two passes to converge).
- **Razor/CSS style:** spaces, indent size 4. Not machine-enforced.
- **Line endings:** LF.
- **Ownership rule (the point of this plan):** `@media` never appears in a `*.razor.css`; among the global stylesheets it lives in `wwwroot/css/shell.css`, and in `globals.css` only for elements MudBlazor renders into its body-level portal, which have no container ancestor to query. `@container` appears only in `*.razor.css`.
- **Container tier literals:** exactly `48rem` (narrow), `64rem` (medium), `90rem` (roomy). No other value is permitted in a `@container` condition.
- **Tier ordering within a stylesheet:** roomy → medium → narrow, so the narrow block wins on source order where both `max-width` tiers match.
- **Units:** type and spacing in `rem`; borders, shadows, radii, fixed dimensions and viewport breakpoints in `px`.
- **Razor `@media` escaping:** irrelevant after this plan — pages must not contain `@media` at all. In a `.razor.css` file no escaping applies; `@container` is written plainly.
- **No `!important` in scoped CSS** once Task 3 lands. The cascade layer makes it unnecessary.
- **Comments:** explain *why*, not *what*. Do not narrate the code.
- **Build:** `dotnet build SharpMUSH.Client` and `dotnet run --project SharpMUSH.Tests.BUnit` must pass at the end of every task.

## File Structure

**Created:**

| Path | Responsibility |
|---|---|
| `SharpMUSH.Client/wwwroot/css/tokens.css` | design tokens, `@font-face`, per-`:lang` mono stack |
| `SharpMUSH.Client/wwwroot/css/shell.css` | app shell, sidebar, topbar, terminal drawer, bottom nav, touch ergonomics, `.phosphor-page`; **owns viewport `@media`** |
| `SharpMUSH.Client/wwwroot/css/utilities.css` | `.scroll-x`, `.toolbar-row` — width-agnostic helpers the page batches reuse. No `@media`: anything keyed to width belongs to the shell or to a page's container query |
| `SharpMUSH.Client/wwwroot/css/mush-syntax.css` | `.mush-*` softcode token colours |
| `SharpMUSH.Client/wwwroot/css/globals.css` | documented escape hatches — styles that must be global because scoped CSS cannot reach them, chiefly MudBlazor body-level portal content. The one global file besides `shell.css` permitted a `@media`, because portal content has no container to query |
| `SharpMUSH.Tests.BUnit/Layout/ResponsiveConventionsTests.cs` | the guard test and its burn-down exemption list |
| `tools/responsive-sweep/sweep.mjs` | Playwright screenshot + horizontal-overflow sweep |
| `tools/responsive-sweep/routes.json` | route map the sweep drives |
| `tools/responsive-sweep/README.md` | how to run the sweep |

**Modified:**

| Path | Change |
|---|---|
| `SharpMUSH.Client/wwwroot/css/custom.css` | reduced to a `@layer` declaration plus `@import`s |
| `SharpMUSH.Client/wwwroot/index.html` | vendor `<link>`s become preloads; vendor CSS imported into the `vendor` layer |
| `SharpMUSH.Client/Layout/MainLayout.razor:73-75` | `@Body` wrapped in `.phosphor-page` |
| `SharpMUSH.Tests.BUnit/SharpMUSH.Tests.BUnit.csproj` | copy `wwwroot/css/*.css`, not just `custom.css` |
| `SharpMUSH.Tests.BUnit/Resources/MonoFontStackTests.cs` | read the whole `css/` folder instead of one file |
| 66 `*.razor.css` + 4 new ones | `@media` → `@container`; `!important` removed |

---

### Task 1: Verify the scoped-CSS rewriter handles native nesting

The spec permits native CSS nesting only in global files until proven safe in scoped files. .NET's scoped-CSS rewriter parses selectors to inject the `[b-xxxxx]` attribute; if it mishandles a nested rule it will silently emit a selector that matches nothing. Settle it with a build, not an opinion.

**Files:**
- Modify (temporarily): `SharpMUSH.Client/Components/EmptyState.razor`, `SharpMUSH.Client/Components/EmptyState.razor.css` (create)
- Modify: `docs/superpowers/specs/2026-08-13-responsive-portal-design.md`

- [ ] **Step 1: Create a scoped stylesheet using nesting**

Create `SharpMUSH.Client/Components/EmptyState.razor.css`:

```css
.nesting-spike {
    color: red;

    & .child {
        color: blue;
    }

    @container (max-width: 48rem) {
        color: green;
    }
}
```

- [ ] **Step 2: Build and inspect the rewritten output**

```bash
cd /home/grave/RiderProjects/SharpMUSH-responsive
dotnet build SharpMUSH.Client -p:SkipFormatVerification=true
cat SharpMUSH.Client/obj/Debug/net10.0/scopedcss/Components/EmptyState.razor.rz.scp.css
```

Expected if nesting is safe: the outer selector becomes `.nesting-spike[b-xxxxx]`, the nested `& .child` still resolves under it, and the `@container` block survives.
Expected if nesting is unsafe: the nested rule loses the scope attribute, the `&` is mangled, or the build errors.

- [ ] **Step 3: Record the verdict in the spec**

Edit the "Spike before use: native CSS nesting" paragraph in
`docs/superpowers/specs/2026-08-13-responsive-portal-design.md` to state the outcome —
either "verified: the rewriter scopes nested rules correctly; nesting is permitted in
scoped stylesheets" or "verified unsafe: nesting stays confined to the global files",
citing what the rewritten output showed.

- [ ] **Step 4: Delete the spike file**

```bash
rm SharpMUSH.Client/Components/EmptyState.razor.css
```

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/specs/2026-08-13-responsive-portal-design.md
git commit -m "Record the CSS-nesting verdict for Blazor scoped stylesheets"
```

---

### Task 2: Split `custom.css` and adopt cascade layers

**Files:**
- Create: `SharpMUSH.Client/wwwroot/css/tokens.css`, `shell.css`, `utilities.css`, `mush-syntax.css`, `globals.css`
- Modify: `SharpMUSH.Client/wwwroot/css/custom.css` (becomes the manifest)
- Modify: `SharpMUSH.Client/wwwroot/index.html:9-11,16`
- Modify: `SharpMUSH.Tests.BUnit/SharpMUSH.Tests.BUnit.csproj`
- Modify: `SharpMUSH.Tests.BUnit/Resources/MonoFontStackTests.cs`

**Interfaces:**
- Produces: `wwwroot/css/shell.css` — the owner of viewport `@media` among the global stylesheets; `globals.css` is the only other file permitted one, and only for MudBlazor body-level portal content. Layer names, in order: `vendor, tokens, shell, utilities`.

- [ ] **Step 1: Update the test project's CSS inputs first, and watch it fail**

In `SharpMUSH.Tests.BUnit/SharpMUSH.Tests.BUnit.csproj`, replace the `custom.css` line in
the `MonoFontStackTests` `ItemGroup`:

```xml
    <None Include="..\SharpMUSH.Client\wwwroot\css\*.css" Link="client\css\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />
```

In `SharpMUSH.Tests.BUnit/Resources/MonoFontStackTests.cs`, replace the `Css()` helper.
The font stack and `@font-face` rules move to `tokens.css` in this task, so the test must
read the whole folder rather than one file:

```csharp
	// The stylesheet is split by responsibility (tokens / shell / utilities / syntax / globals),
	// so these assertions read the folder as one sheet rather than pinning a filename that the
	// next split would silently invalidate.
	private static string Css() =>
		string.Join("\n", Directory.EnumerateFiles(Path.Join(AppContext.BaseDirectory, "client", "css"), "*.css")
			.OrderBy(f => f, StringComparer.Ordinal)
			.Select(File.ReadAllText));
```

- [ ] **Step 2: Run the tests to confirm they fail**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/MonoFontStackTests/*"
```

Expected: FAIL — `client/css` does not exist yet.

- [ ] **Step 3: Split the file**

Move blocks out of `SharpMUSH.Client/wwwroot/css/custom.css` verbatim — no rule changes in
this task, only relocation:

| Destination | Source lines (at `11834d13`) |
|---|---|
| `tokens.css` | 1–189 — the `:root` token block, `@font-face` rules, the `:root:lang(zh\|ja\|ko)` mono override |
| `shell.css` | 190–365 (shell, topbar, main, widget asides), 366–618 (sidebar), 1391–1565 (the mobile toolkit and every `@media` block) |
| `mush-syntax.css` | the `.mush-*` colour rules ending at 1376 |
| `globals.css` | 1377–1389 — the `.char-picker` popover width and its existing comment explaining why it cannot be scoped |
| `utilities.css` | `.mobile-only` / `.desktop-only` / tap-target helpers currently inside the mobile toolkit block |

Everything else (the `ConfigNavDrawer` block at 619+ and any other single-component block)
goes to `shell.css` for now; Task 11 relocates component-specific blocks into their own
scoped stylesheets.

Head each new file with a one-line comment naming what it owns. `shell.css` keeps the
existing responsive-model comment block and gains this line:

```css
/* Viewport @media lives here and nowhere else. Pages and components query the
   .phosphor-page container instead — the content column's width depends on the collapsible
   sidebar and two admin-configured widget asides, none of which a media query can see.
   Enforced by ResponsiveConventionsTests. */
```

- [ ] **Step 4: Reduce `custom.css` to the manifest**

```css
/* Load order and cascade layers for the portal's global stylesheets.

   Vendor CSS is imported into a layer so our unlayered scoped stylesheets
   (SharpMUSH.Client.styles.css) beat it regardless of selector specificity. That is what
   the !important declarations across the portal used to do by hand. */
@layer vendor, tokens, shell, utilities;

@import url("../_content/MudBlazor/MudBlazor.min.css") layer(vendor);
@import url("../_content/PSC.Blazor.Components.MarkdownEditor/css/easymde.min.css") layer(vendor);
@import url("../_content/PSC.Blazor.Components.MarkdownEditor/css/markdowneditor.css") layer(vendor);

@import url("tokens.css") layer(tokens);
@import url("shell.css") layer(shell);
@import url("mush-syntax.css") layer(shell);
@import url("globals.css") layer(shell);
@import url("utilities.css") layer(utilities);
```

- [ ] **Step 5: Update `index.html`**

Replace lines 9–11 (the three vendor `<link rel="stylesheet">` tags — they are now
`@import`ed into the `vendor` layer) with preload hints so the import is not serialized
behind `custom.css`:

```html
    <link rel="preload" as="style" href="_content/MudBlazor/MudBlazor.min.css" />
    <link rel="preload" as="style" href="_content/PSC.Blazor.Components.MarkdownEditor/css/easymde.min.css" />
    <link rel="preload" as="style" href="_content/PSC.Blazor.Components.MarkdownEditor/css/markdowneditor.css" />
```

Leave line 16 (`css/custom.css`) and line 17 (`SharpMUSH.Client.styles.css`) exactly as
they are, and in that order — the scoped bundle must stay unlayered and last.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/MonoFontStackTests/*"
```

Expected: PASS, all four tests. `TheShellFetchesNothingFromAThirdParty` still passes
because every `@import` target is same-origin.

- [ ] **Step 7: Verify the portal still renders**

```bash
dotnet build SharpMUSH.Client -p:SkipFormatVerification=true
```

Expected: build succeeds. Visual confirmation happens in Task 4's baseline sweep.

- [ ] **Step 8: Commit**

```bash
git add SharpMUSH.Client/wwwroot/css SharpMUSH.Client/wwwroot/index.html \
        SharpMUSH.Tests.BUnit/SharpMUSH.Tests.BUnit.csproj \
        SharpMUSH.Tests.BUnit/Resources/MonoFontStackTests.cs
git commit -m "Split custom.css by responsibility, and put vendor CSS in a cascade layer"
```

---

### Task 3: Declare the content container and the ultrawide cap

**Files:**
- Modify: `SharpMUSH.Client/Layout/MainLayout.razor:73-75`
- Modify: `SharpMUSH.Client/wwwroot/css/shell.css`
- Modify: `SharpMUSH.Client/wwwroot/css/tokens.css`
- Test: `SharpMUSH.Tests.BUnit/Layout/ContentContainerTests.cs` (create)

**Interfaces:**
- Produces: `.phosphor-page` — the named query container `page`. Every later task writes `@container page (…)`. Full-bleed pages mark their own root element with the class `full-bleed`.

- [ ] **Step 1: Write the failing test**

Create `SharpMUSH.Tests.BUnit/Layout/ContentContainerTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The content column's width is not a function of the viewport: the sidebar collapses and both
/// widget asides are sized from runtime admin settings (MainLayout.razor). Pages therefore size
/// against a query container rather than the screen, and this fixes the one place that container
/// is declared.
/// </summary>
public class ContentContainerTests
{
	private static string Shell() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "css", "shell.css"));

	private static string MainLayout() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "razor", "Layout", "MainLayout.razor"));

	[Test]
	public async Task TheBodyIsWrappedInTheNamedQueryContainer()
	{
		await Assert.That(MainLayout()).Contains("phosphor-page")
			.Because("pages can only use @container if something declares the container around @Body");

		var rule = Regex.Match(Shell(), @"\.phosphor-page\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(rule.Success).IsTrue();
		await Assert.That(rule.Groups["body"].Value).Contains("container: page / inline-size")
			.Because("the container must be named 'page' and query the inline axis only");
	}

	[Test]
	public async Task FullHeightPagesKeepADefiniteHeightThroughTheWrapper()
	{
		// Inserting a wrapper between .phosphor-main and the page would otherwise break the pages
		// that set height:100% on their own root (/play, the wiki editor).
		await Assert.That(Shell()).Contains(":has(> .full-bleed)")
			.Because("full-bleed pages opt out of the cap and need the wrapper to pass height through");
	}

	[Test]
	public async Task WideViewportsCapTheReadingWidth()
	{
		var wide = Regex.Match(Shell(), @"@media\s*\(min-width:\s*1601px\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
		await Assert.That(wide.Success).IsTrue().Because("the ultrawide tier must exist");
		await Assert.That(wide.Groups["body"].Value).Contains("--content-max")
			.Because("the cap reads a token rather than hardcoding a width");
		await Assert.That(wide.Groups["body"].Value).Contains("margin-inline: auto")
			.Because("capped content is centred, not left-aligned against the sidebar");
	}
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/ContentContainerTests/*"
```

Expected: FAIL — `phosphor-page` appears nowhere.

- [ ] **Step 3: Wrap `@Body`**

In `SharpMUSH.Client/Layout/MainLayout.razor`, replace lines 73–75:

```razor
        <main class="phosphor-main @(_terminalOpen ? "phosphor-main--terminal" : "")">
            <div class="phosphor-page">
                @Body
            </div>
        </main>
```

- [ ] **Step 4: Add the token**

In `SharpMUSH.Client/wwwroot/css/tokens.css`, inside the `:root` block, after `--cpad-mobile`:

```css
	/* Reading-width cap for the wide tier. A fixed dimension, so px (see the unit rule). */
	--content-max: 1400px;
	/* Documentation only: container tiers are 48rem / 64rem / 90rem. A @container condition
	   cannot read a custom property any more than a @media condition can. */
	--tier-narrow: 48rem;
	--tier-medium: 64rem;
	--tier-roomy: 90rem;
```

- [ ] **Step 5: Declare the container and cap in `shell.css`**

Append to the shell section, immediately after the `.phosphor-main` rules:

```css
/* The seam between shell and page CSS. The shell decides how much width the content column
   gets; pages ask this container how much they got. Nothing else in the portal declares a
   container named 'page'. */
.phosphor-page {
	container: page / inline-size;
	min-height: 100%;
}

/* Two independent opt-outs, deliberately kept apart. `full-bleed` answers "this page wants
   the whole window width"; `full-height` answers "this page needs a definite height to size
   its own panes". Bundling them hid a defect: /mail needs the height for its three-pane
   scrolling but should keep the cap. A page needing both carries both. A page cannot set a
   class on its ancestor, which is what :has() is doing here. */
.phosphor-page:has(> .full-bleed) {
	max-width: none;
}

.phosphor-page:has(> .full-height) {
	height: 100%;
}

@media (min-width: 1601px) {
	.phosphor-page {
		max-width: var(--content-max);
		margin-inline: auto;
	}
}
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/ContentContainerTests/*"
```

Expected: PASS, all three tests.

- [ ] **Step 7: Mark the full-bleed pages**

Add `full-bleed` to the existing root-element class of each working surface, keeping every
current class:

- `SharpMUSH.Client/Pages/Play.razor`
- `SharpMUSH.Client/Pages/SoftcodeEditor.razor`
- `SharpMUSH.Client/Pages/SceneLive.razor`
- `SharpMUSH.Client/Pages/WikiPageEdit.razor`
- `SharpMUSH.Client/Pages/WikiPageDiff.razor`
- `SharpMUSH.Client/Pages/Admin/Layout/LayoutEditor.razor`

Each page's root is its outermost element inside the `@page` directive block; the class goes
on that element only, so `:has(> .full-bleed)` matches a direct child of the wrapper.

- [ ] **Step 8: Verify the build and commit**

```bash
dotnet build SharpMUSH.Client -p:SkipFormatVerification=true
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Client SharpMUSH.Tests.BUnit
git commit -m "Wrap the page body in a query container, and cap reading width on wide screens"
```

---

### Task 4: Build the Playwright sweep harness

Lands before the page batches so every batch can be checked as it is written rather than
audited afterwards.

**Files:**
- Create: `tools/responsive-sweep/sweep.mjs`, `routes.json`, `README.md`

**Interfaces:**
- Produces: `node tools/responsive-sweep/sweep.mjs [--profile <name>]` — exits non-zero if any route horizontally overflows at any width; writes PNGs to `tools/responsive-sweep/out/<profile>/<width>/<route>.png`.

- [ ] **Step 1: Write the route map**

Create `tools/responsive-sweep/routes.json`. Routes come from
`docs/design/url-strategy.md`; each entry is the path plus whether it needs an
authenticated admin session:

```json
{
  "public": ["/", "/login", "/register", "/wiki", "/characters", "/help", "/scenes"],
  "authenticated": ["/play", "/account", "/settings", "/settings/theme", "/mail", "/mail/compose", "/character/create", "/scenes/active", "/softcode"],
  "admin": [
    "/admin", "/admin/players", "/admin/characters", "/admin/accounts", "/admin/moderation",
    "/admin/profiles", "/admin/media", "/admin/server", "/admin/wiki", "/admin/wiki/assets",
    "/admin/config", "/admin/layouts", "/admin/roles", "/admin/applications", "/admin/packages",
    "/admin/restrictions", "/admin/sitelock", "/admin/banned-names", "/admin/suggestions",
    "/admin/import/config", "/admin/import/database"
  ]
}
```

Verify each path against `docs/design/url-strategy.md` and each page's `@page` directive
before committing; correct any that differ rather than assuming.

- [ ] **Step 2: Write the sweep script**

Create `tools/responsive-sweep/sweep.mjs`:

```js
// Drives the portal at four widths and fails on horizontal overflow. Overflow is the one
// responsive defect that is objective rather than a matter of taste, so it is the part that
// gates; the screenshots are for the parts that are not.
import { chromium } from 'playwright-core';
import { readFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';

const BASE = process.env.SWEEP_BASE ?? 'https://localhost:7102';
const OUT = new URL('./out/', import.meta.url).pathname;

// Portrait phone, portrait tablet, thin desktop window, fullscreen desktop.
const WIDTHS = [
	{ name: '390', width: 390, height: 844, mobile: true },
	{ name: '820', width: 820, height: 1180, mobile: true },
	{ name: '1280', width: 1280, height: 800, mobile: false },
	{ name: '2560', width: 2560, height: 1440, mobile: false },
];

const profile = process.argv.includes('--profile')
	? process.argv[process.argv.indexOf('--profile') + 1]
	: 'default';

const routes = JSON.parse(await readFile(new URL('./routes.json', import.meta.url), 'utf8'));
const all = [...routes.public, ...routes.authenticated, ...routes.admin];

const browser = await chromium.launch();
const failures = [];

for (const size of WIDTHS) {
	const context = await browser.newContext({
		viewport: { width: size.width, height: size.height },
		hasTouch: size.mobile,
		isMobile: size.mobile,
		ignoreHTTPSErrors: true,
	});
	const page = await context.newPage();

	for (const route of all) {
		await page.goto(BASE + route, { waitUntil: 'networkidle' });

		const overflow = await page.evaluate(() => {
			const el = document.scrollingElement;
			return { scroll: el.scrollWidth, inner: window.innerWidth };
		});

		// 1px of slack: sub-pixel layout rounding is not a defect.
		if (overflow.scroll > overflow.inner + 1) {
			failures.push(`${size.name}px ${route}: scrollWidth ${overflow.scroll} > innerWidth ${overflow.inner}`);
		}

		const file = join(OUT, profile, size.name, `${route === '/' ? 'index' : route.replaceAll('/', '_')}.png`);
		await mkdir(dirname(file), { recursive: true });
		await page.screenshot({ path: file, fullPage: true });
	}

	await context.close();
}

await browser.close();

if (failures.length > 0) {
	console.error(`Horizontal overflow on ${failures.length} route/width pairs:`);
	for (const f of failures) console.error(`  ${f}`);
	process.exit(1);
}
console.log(`No horizontal overflow across ${all.length} routes x ${WIDTHS.length} widths.`);
```

- [ ] **Step 3: Write the README**

Create `tools/responsive-sweep/README.md` documenting the dev stack the sweep needs:

```markdown
# Responsive sweep

Drives every portal route at 390 / 820 / 1280 / 2560 px, screenshots each, and fails on
horizontal overflow.

## Prerequisites

```bash
docker compose up -d                                # ArangoDB + NATS
dotnet run --project SharpMUSH.Server               # https://localhost:8081
dotnet run --project SharpMUSH.ConnectionServer     # :4201 telnet, :4202 http
dotnet run --project SharpMUSH.Client               # https://localhost:7102
```

A dev build of the Server does **not** serve the WASM client, so the sweep drives the
Client's own dev host on 7102.

## Run

```bash
node tools/responsive-sweep/sweep.mjs
node tools/responsive-sweep/sweep.mjs --profile sidebar-expanded
node tools/responsive-sweep/sweep.mjs --profile wide-aside
```

The two extra profiles exist because the content column's width does not follow the
viewport: expand the sidebar, or configure a wide right widget aside in
`/admin/layouts`, then re-run. These are the cases container queries exist to fix, so
they are the cases that prove the work.

Uses the chromium already under `~/.cache/ms-playwright` via `playwright-core`.
```

- [ ] **Step 4: Run the baseline sweep**

With the dev stack up:

```bash
node tools/responsive-sweep/sweep.mjs --profile baseline
```

Expected: FAIL, listing overflowing routes — this is the pre-existing damage, and the list
is the work item for Tasks 6–13. Record the failure list in the commit message.

- [ ] **Step 5: Commit**

```bash
git add tools/responsive-sweep
git commit -m "Add a responsive sweep that fails on horizontal overflow"
```

---

### Task 5: Land the guard test with a burn-down exemption list

The ownership rule is worthless if it is only written down. The test ships with every
currently non-conforming file listed by name; each later task deletes its own entries, so
the build stays green and the remaining work is always visible.

**Files:**
- Create: `SharpMUSH.Tests.BUnit/Layout/ResponsiveConventionsTests.cs`

**Interfaces:**
- Produces: `ResponsiveConventionsTests.NotYetMigrated` — a `HashSet<string>` of stylesheet paths relative to `client/razor/`, e.g. `"Pages/Play.razor.css"`. Later tasks remove entries from it and never add any.

- [ ] **Step 1: Write the test**

Create `SharpMUSH.Tests.BUnit/Layout/ResponsiveConventionsTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Fixes the boundary between shell CSS and page CSS. The shell describes the device and owns
/// every viewport @media; pages and components describe themselves and query the .phosphor-page
/// container. The split is not stylistic: the content column's width depends on a collapsible
/// sidebar and two admin-configured widget asides, so a media query inside a page is guessing.
/// </summary>
public class ResponsiveConventionsTests
{
	private static string RazorRoot => Path.Join(AppContext.BaseDirectory, "client", "razor");
	private static string CssRoot => Path.Join(AppContext.BaseDirectory, "client", "css");

	private static IEnumerable<string> ScopedStylesheets() =>
		Directory.EnumerateFiles(RazorRoot, "*.razor.css", SearchOption.AllDirectories);

	private static string Rel(string path) =>
		Path.GetRelativePath(RazorRoot, path).Replace('\\', '/');

	/// <summary>
	/// Every rule below matches on CSS syntax, and this codebase documents its stylesheets heavily —
	/// including prose that names the very at-rules being banned. Scanning raw text would fail a file
	/// for explaining the convention it follows.
	/// </summary>
	private static string StripComments(string css) =>
		Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

	/// <summary>
	/// Stylesheets still on the old viewport-query model. Each sweep batch deletes its own entries;
	/// the list must reach empty, which <see cref="TheMigrationIsFinished"/> asserts.
	/// </summary>
	private static readonly HashSet<string> NotYetMigrated = new(StringComparer.Ordinal)
	{
		"Components/Layout/ZoneRenderer.razor.css",
		"Components/ScenePoseLine.razor.css",
		"Components/Widgets/RecentWikiActivityWidget.razor.css",
		"Components/Widgets/WikiBodyWidget.razor.css",
		"Components/Widgets/WikiIndexWidget.razor.css",
		"Components/WikiDisplay.razor.css",
		"Components/WikiEdit.razor.css",
		"Layout/AccountPanel.razor.css",
		"Layout/ConfigLayout.razor.css",
		"Pages/Account.razor.css",
		"Pages/Admin/AdminConfig.razor.css",
		"Pages/Admin/Applications/AdminApplications.razor.css",
		"Pages/Admin/Config/DynamicConfig.razor.css",
		"Pages/Admin/Layout/AdminLayouts.razor.css",
		"Pages/Admin/Layout/LayoutEditor.razor.css",
		"Pages/Admin/Packages/AdminPackageAuthor.razor.css",
		"Pages/Admin/Packages/AdminPackageBrowse.razor.css",
		"Pages/Admin/Packages/AdminPackageRemotes.razor.css",
		"Pages/Admin/Packages/AdminPackageReview.razor.css",
		"Pages/Admin/Packages/AdminPackages.razor.css",
		"Pages/Admin/Restrictions.razor.css",
		"Pages/Admin/Roles/AdminRoles.razor.css",
		"Pages/Admin/Sitelock.razor.css",
		"Pages/CharacterProfile.razor.css",
		"Pages/Characters.razor.css",
		"Pages/DynamicApplication.razor.css",
		"Pages/Help.razor.css",
		"Pages/HelpAdminTopic.razor.css",
		"Pages/HelpTopic.razor.css",
		"Pages/Home.razor.css",
		"Pages/Login.razor.css",
		"Pages/Mail.razor.css",
		"Pages/MailCompose.razor.css",
		"Pages/MailDetail.razor.css",
		"Pages/Play.razor.css",
		"Pages/SceneDetail.razor.css",
		"Pages/SceneLive.razor.css",
		"Pages/Scenes.razor.css",
		"Pages/ScenesActive.razor.css",
		"Pages/Settings.razor.css",
		"Pages/SettingsTheme.razor.css",
		"Pages/Setup.razor.css",
		"Pages/SoftcodeEditor.razor.css",
		"Pages/WikiPageDiff.razor.css",
		"Pages/WikiPageHistory.razor.css",

		// No @media to convert, but !important to remove, so they are exempt until their batch
		// reaches them — MigratedStylesheetsCarryNoImportantDeclarations reads this same list.
		"Components/Widgets/CharacterDirectoryWidget.razor.css",
		"Pages/Admin/AdminMedia.razor.css",
		"Pages/Admin/AdminServer.razor.css",
		"Pages/Admin/ImportDatabase.razor.css",
	};

	/// <summary>
	/// Routable pages that legitimately ship no stylesheet: they redirect, or render only
	/// components that carry their own.
	/// </summary>
	private static readonly HashSet<string> PagesWithoutStylesheetByDesign = new(StringComparer.Ordinal)
	{
		"Pages/Admin/BannedNamesRedirect.razor",
		"Pages/Admin/RestrictionsRedirect.razor",
		"Pages/Admin/SitelockRedirect.razor",
		"Pages/NotFound.razor",
		"Pages/SettingsCharactersRedirect.razor",
		"Pages/WikiIndex.razor",

		// Not by design — these gain stylesheets during the sweep and their entries are
		// deleted with the task that writes them.
		"Pages/Admin/AdminAccounts.razor",   // Task 7
		"Pages/WikiPage.razor",              // Task 12
		"Pages/WikiPageEdit.razor",          // Task 12
		"Pages/CharacterCreate.razor",       // Task 13
		"Pages/Register.razor",              // Task 13
	};

	private static readonly string[] SanctionedTiers = ["48rem", "64rem", "90rem"];

	[Test]
	public async Task PagesQueryTheirContainerRatherThanTheViewport()
	{
		var offenders = ScopedStylesheets()
			.Where(f => !NotYetMigrated.Contains(Rel(f)))
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@media\b"))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page cannot see the sidebar or the admin-configured widget asides, so a "
				+ "viewport query inside one is wrong whenever either is present; use @container page");
	}

	[Test]
	public async Task TheShellDoesNotQueryContainers()
	{
		var shell = File.ReadAllText(Path.Join(CssRoot, "shell.css"));

		await Assert.That(Regex.IsMatch(StripComments(shell), @"@container\b")).IsFalse()
			.Because("the shell decides how much width content gets; it never asks");
	}

	[Test]
	public async Task ViewportQueriesLiveOnlyInTheShellOrTheEscapeHatches()
	{
		// globals.css is the second permitted home, and only because of what it holds: overrides for
		// elements MudBlazor renders into its body-level portal, outside .phosphor-page. Those have no
		// container ancestor to query, so the viewport is the only width available to them.
		var permitted = new[] { "shell.css", "globals.css" };

		var offenders = Directory.EnumerateFiles(CssRoot, "*.css")
			.Where(f => !permitted.Contains(Path.GetFileName(f)))
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@media\b"))
			.Select(Path.GetFileName)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("device-facing rules belong to the shell; the only exception is content that "
				+ "renders outside the container and so cannot query it");
	}

	[Test]
	public async Task ContainerTiersUseOnlyTheSanctionedLiterals()
	{
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			foreach (Match m in Regex.Matches(StripComments(File.ReadAllText(file)), @"@container[^{]*?\(\s*(?:min|max)-width:\s*(?<value>[^)]+)\)"))
			{
				var value = m.Groups["value"].Value.Trim();
				if (!SanctionedTiers.Contains(value))
					offenders.Add($"{Rel(file)}: {value}");
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because($"tiers drift into a private set of breakpoints unless they are fixed at {string.Join(" / ", SanctionedTiers)}");
	}

	[Test]
	public async Task NoScopedStylesheetPositionsAnythingFixed()
	{
		// container-type: inline-size makes .phosphor-page a containing block for fixed descendants,
		// so a fixed element inside page content would anchor to the content column rather than the
		// viewport. Fixed chrome belongs to the shell.
		var offenders = ScopedStylesheets()
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"position:\s*fixed"))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a query container is a containing block for position:fixed; put fixed chrome in shell.css");
	}

	[Test]
	public async Task MigratedStylesheetsCarryNoImportantDeclarations()
	{
		// Vendor CSS sits in the `vendor` cascade layer and the scoped bundle is unlayered, so the
		// scoped rule already wins on layer order regardless of specificity. Every !important here
		// was simulating that by hand.
		var offenders = ScopedStylesheets()
			.Where(f => !NotYetMigrated.Contains(Rel(f)))
			.Where(f => StripComments(File.ReadAllText(f)).Contains("!important", StringComparison.Ordinal))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("unlayered scoped CSS already beats the vendor layer; !important hides real conflicts");
	}

	[Test]
	public async Task EveryRoutablePageHasAStylesheet()
	{
		var offenders = Directory.EnumerateFiles(Path.Join(RazorRoot, "Pages"), "*.razor", SearchOption.AllDirectories)
			.Where(f => Regex.IsMatch(File.ReadAllText(f), @"^@page\b", RegexOptions.Multiline))
			.Where(f => !File.Exists(f + ".css"))
			.Select(Rel)
			.Where(r => !PagesWithoutStylesheetByDesign.Contains(r))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page with no stylesheet has nowhere to declare its container tiers, which is "
				+ "how a page ends up responsive by accident rather than by design");
	}

	[Test]
	public async Task TheJavaScriptBreakpointMirrorMatchesTheShell()
	{
		// layout.js decides whether the hamburger opens the drawer or collapses the rail, so it has
		// to agree with the media condition that decides which of those is on screen.
		var js = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "js", "layout.js"));
		var shell = File.ReadAllText(Path.Join(CssRoot, "shell.css"));

		var condition = Regex.Match(js, @"matchMedia\(\s*'(?<q>[^']+)'\s*\)");
		await Assert.That(condition.Success).IsTrue().Because("layout.js must state the condition it mirrors");

		var normalised = Regex.Replace(condition.Groups["q"].Value, @"\s+", " ").Trim();
		var shellNormalised = Regex.Replace(shell, @"\s+", " ");

		await Assert.That(shellNormalised).Contains($"@media {normalised}")
			.Because($"layout.js mirrors '{normalised}', which must appear verbatim in shell.css");
	}

	[Test]
	public async Task TheExemptionListHasNoStaleEntries()
	{
		var missing = NotYetMigrated
			.Where(r => !File.Exists(Path.Join(RazorRoot, r)))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(missing).IsEmpty()
			.Because("an entry naming a file that no longer exists silently exempts nothing and hides progress");
	}

	[Test, Skip("Enabled by the final sweep task, once NotYetMigrated is empty.")]
	public async Task TheMigrationIsFinished()
	{
		await Assert.That(NotYetMigrated).IsEmpty()
			.Because("the exemption list is a burn-down, not a permanent allowance");
	}
}
```

- [ ] **Step 2: Add `layout.js` to the test inputs**

`TheJavaScriptBreakpointMirrorMatchesTheShell` reads it, so it must be copied. In
`SharpMUSH.Tests.BUnit/SharpMUSH.Tests.BUnit.csproj`, in the same `ItemGroup` as the CSS
copy:

```xml
    <None Include="..\SharpMUSH.Client\wwwroot\js\layout.js" Link="client\js\layout.js" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3: Run the test**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/ResponsiveConventionsTests/*"
```

Expected: PASS for all enabled tests, `TheMigrationIsFinished` skipped.

If `EveryRoutablePageHasAStylesheet` names a page not already in
`PagesWithoutStylesheetByDesign`, the list above is incomplete — add it under the
"Not by design" comment with the task number that will write its stylesheet, rather than
above it. Also open `Pages/WikiIndex.razor` and confirm it really does render no markup of
its own; if it does, move it down to the "Not by design" group and handle it in Task 12.

- [ ] **Step 4: Format and commit**

```bash
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Tests.BUnit
git commit -m "Guard the shell/page CSS boundary with a burn-down exemption list"
```

---

## The sweep tasks (6–13)

Tasks 6 through 13 share one procedure. It is written out once here and referenced by each;
each task states only its file list and the defects specific to it.

**Per-batch procedure:**

- [ ] **Step 1: Remove this batch's files from the exemption list**

Delete the batch's entries from `NotYetMigrated` in
`SharpMUSH.Tests.BUnit/Layout/ResponsiveConventionsTests.cs`.

- [ ] **Step 2: Run the guard test and watch it fail**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/ResponsiveConventionsTests/*"
```

Expected: FAIL, naming exactly this batch's files under
`PagesQueryTheirContainerRatherThanTheViewport` and/or
`MigratedStylesheetsCarryNoImportantDeclarations`.

- [ ] **Step 3: Convert each stylesheet**

For each file:

1. Replace `@media (max-width: 760px)` with `@container page (max-width: 48rem)`. For a
   component that is itself resizable (a widget, a card, a panel in a grid), instead give
   the component's root `container-type: inline-size` and use an unnamed
   `@container (max-width: …)` so it responds to its own box.
2. Add a medium tier where the layout is multi-column:
   `@container page (max-width: 64rem)`. Order blocks roomy → medium → narrow.
3. Replace fixed `repeat(N, 1fr)` grids with `repeat(auto-fit, minmax(Npx, 1fr))` wherever
   the track count is arbitrary. A self-tiering grid needs no tier rule at all and is
   preferred over writing one.
4. Give every flex/grid child that holds wide content `min-width: 0`; prefer
   `minmax(0, 1fr)` over a bare `1fr`.
5. Delete every `!important`. If a rule stops applying, the vendor layer is not the reason —
   find the real conflict and fix it rather than restoring the flag.
6. Wrap genuinely wide, non-reflowable content (code blocks, `pre`, diff panes) in
   `.scroll-x` from `utilities.css` rather than letting it overflow the page.

- [ ] **Step 4: Run the guard test until green**

```bash
dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/ResponsiveConventionsTests/*"
```

Expected: PASS.

- [ ] **Step 5: Run the sweep against this batch's routes**

With the dev stack up:

```bash
node tools/responsive-sweep/sweep.mjs --profile <batch-name>
```

Expected: this batch's routes no longer appear in the overflow list. Review the PNGs at 390,
820, 1280 and 2560 for each route the batch touched.

- [ ] **Step 6: Commit**

```bash
git add SharpMUSH.Client SharpMUSH.Tests.BUnit
git commit -m "<batch-specific message>"
```

---

### Task 6: Batch A1 — admin pages with no responsive rules (part 1)

These eight stylesheets contain no breakpoint of any kind, so they are additions rather than
conversions: they need container tiers written from scratch.

**Files:**
- Modify: `Pages/Admin/Dashboard.razor.css` — `repeat(auto-fill, minmax(280px, 1fr))` stat grid is already fluid; add a narrow tier for the 44px avatar row and the flex header
- Modify: `Pages/Admin/Players.razor.css` — a "coming soon" stub: `.ph-header` plus a centred
  `.ph-empty` card, no toolbar. Tier its title and empty-card sizing
- Modify: `Pages/Admin/PlayerDetail.razor.css` — also a stub, same shape as Players
- Modify: `Pages/Admin/Moderation.razor.css`
- Modify: `Pages/Admin/AdminCharacters.razor.css`
- Modify: `Pages/Admin/AdminProfiles.razor.css`
- Modify: `Pages/Admin/AdminMedia.razor.css`
- Modify: `Pages/Admin/AdminServer.razor.css`

Only `AdminMedia.razor.css` and `AdminServer.razor.css` appear in `NotYetMigrated`, listed
there for their `!important` declarations rather than for any `@media`. Remove those two
entries in Step 1; Step 2 will then fail naming exactly those two under
`MigratedStylesheetsCarryNoImportantDeclarations`.

The other six have no breakpoint of any kind, so their defect is absence — which the guard
test cannot see. The sweep is what gates them: run Step 5 both before and after, and compare.

Commit message: `Give the admin player and content pages responsive tiers`

---

### Task 7: Batch A2 — admin pages with no responsive rules (part 2)

**Files:**
- Modify: `Pages/Admin/AdminWiki.razor.css` — `grid-template-columns: repeat(4, 1fr)` at line 27 becomes `repeat(auto-fit, minmax(180px, 1fr))`
- Modify: `Pages/Admin/AdminWikiAssets.razor.css`
- Modify: `Pages/Admin/BannedNames.razor.css`
- Modify: `Pages/Admin/Config/ConfigIndex.razor.css`
- Modify: `Pages/Admin/ImportConfig.razor.css`
- Modify: `Pages/Admin/ImportDatabase.razor.css`
- Modify: `Pages/Admin/SuggestionManagement.razor.css`
- Create: `Pages/Admin/AdminAccounts.razor.css`

`AdminAccounts.razor` uses `MudTable` and ships no stylesheet. Give it one that makes the
table usable in a narrow container — the row cells stack into labelled blocks below the
narrow tier:

```css
/* MudTable renders a real <table>, which cannot reflow. Below the narrow tier the rows become
   stacked blocks, each cell labelled from its column header via the data-label attribute the
   markup sets.

   ::deep leads the selector, and must. Blazor stamps the scope attribute only on elements the
   page's own markup declares; the class handed to <MudTable> lands on a <table> MudTable
   renders internally, which never carries it. Anchoring as `::deep .accounts-table thead`
   compiles to `.accounts-table[b-xxx] thead` and matches nothing at any width. A leading
   ::deep compiles to `[b-xxx] .accounts-table thead`, anchored on the page's own wrapper. */
@container page (max-width: 48rem) {
    ::deep .accounts-table thead {
        display: none;
    }

    ::deep .accounts-table tr {
        display: block;
        border-bottom: 1px solid var(--border);
        padding-block: 0.5rem;
    }

    ::deep .accounts-table td {
        display: grid;
        grid-template-columns: minmax(0, 8rem) minmax(0, 1fr);
        gap: 0.5rem;
        border: none;
    }

    ::deep .accounts-table td::before {
        content: attr(data-label);
        color: var(--text-dim);
        font-size: 0.8125rem;
    }
}
```

Add `Class="accounts-table"` to the `MudTable` in `Pages/Admin/AdminAccounts.razor` and
`data-label="@Loc["…"]"` to each `MudTd`, reusing the localizer key already used for that
column's header so no new resx key is needed. Then remove `Pages/Admin/AdminAccounts.razor`
from `PagesWithoutStylesheetByDesign` in the guard test.

The pattern is easy to get wrong in exactly one way, so verify it rather than eyeballing it:
load the page and assert the table element actually matches the compiled selector (in the
browser console, `document.querySelector('table').matches('[b-xxxxx] .accounts-table')`). A
table can *look* stacked while this rule is dead, because MudBlazor's own `mud-xs-table`
feature uses the same `data-label` attribute — but it is viewport-driven, so it misses the
sidebar-expanded and wide-aside cases this whole plan exists to handle.

Apply the same treatment to the `MudTable` in `Pages/Admin/Packages/AdminPackageRemotes.razor`
and `AdminPackageReview.razor` in Task 9, and `Pages/Account.razor` in Task 13.

Commit message: `Give the admin wiki, config and import pages responsive tiers`

---

### Task 8: Batch B1 — admin config, layout and dialogs

**Files:**
- Delete: `Pages/Admin/AdminConfig.razor`, `Pages/Admin/AdminConfig.razor.css`; remove the
  `<AdditionalFiles Include="Pages\Admin\AdminConfig.razor" />` line from
  `SharpMUSH.Client.csproj`. The component has no `@page` and no `@layout`, nothing
  references it (the only `AdminConfig` hits in the tree are the unrelated
  `AdminConfigService`), and it is not reachable through `DynamicComponent`, which only
  renders widget descriptors registered in `Program.cs`. `Config/DynamicConfig.razor` is the
  live replacement. Verify all of that again before deleting, then delete it rather than
  giving container tiers to a page nothing renders.
- Modify: `Pages/Admin/Config/DynamicConfig.razor.css` — keep the `position: sticky` rule; containment does not affect it
- Modify: `Pages/Admin/Layout/AdminLayouts.razor.css` — `grid-template-columns: 1fr !important` at line 39 loses the flag
- Modify: `Pages/Admin/Layout/LayoutEditor.razor.css` — the `820px` breakpoint at line 23 becomes the `64rem` medium tier; the 12-column zone grid at line 45 keeps 12 tracks above medium
- Create: `Pages/Admin/Layout/WidgetConfigDialog.razor.css`
- Create: `Pages/Admin/Roles/RoleEditDialog.razor.css`
- Create: `Pages/Admin/Applications/ApplicationEditDialog.razor.css`

MudBlazor dialogs render through a body-level portal, **outside** `.phosphor-page`, so they
have no `page` container to query. Each dialog declares its own:

```css
/* Rendered into MudBlazor's body-level portal, so there is no .phosphor-page ancestor to query.
   The dialog is its own container. */
.widget-config-dialog {
    container-type: inline-size;
}

@container (max-width: 48rem) {
    .widget-config-dialog .wcd-field-row {
        grid-template-columns: minmax(0, 1fr);
    }
}
```

Add the corresponding root class to each dialog's outermost `MudDialog` content element.

Commit message: `Give the admin config, layout editor and dialogs their own containers`

---

### Task 9: Batch B2 — packages, roles, applications, restrictions

**Files:**
- Modify: `Pages/Admin/Packages/AdminPackages.razor.css` — `grid-template-columns: 1fr 1fr !important` at line 80 loses the flag
- Modify: `Pages/Admin/Packages/AdminPackageAuthor.razor.css`
- Modify: `Pages/Admin/Packages/AdminPackageBrowse.razor.css` — line 70 loses its flag
- Modify: `Pages/Admin/Packages/AdminPackageRemotes.razor.css` — `1.4fr 1fr` at line 25 gets a medium tier before the narrow one; apply the `MudTable` stacking from Task 7
- Modify: `Pages/Admin/Packages/AdminPackageReview.razor.css` — apply the `MudTable` stacking from Task 7
- Modify: `Pages/Admin/Roles/AdminRoles.razor.css` — the `340px minmax(0, 1fr)` sidecar at line 92 stacks at the medium tier, not only the narrow one
- Modify: `Pages/Admin/Applications/AdminApplications.razor.css` — same sidecar treatment at line 84; `repeat(3, 1fr)` at line 180 becomes `repeat(auto-fit, minmax(140px, 1fr))`
- Modify: `Pages/Admin/Restrictions.razor.css`
- Modify: `Pages/Admin/Sitelock.razor.css`

The 340px sidecars are the clearest case for the medium tier: at a 1280px viewport with the
sidebar expanded, the container is ~1050px, and a 340px rail leaves 710px for a table that was
designed against 940px.

Commit message: `Stack the admin sidecar layouts before they run out of room`

---

### Task 10: Batch C1 — shared components and layouts

**Files:**
- Modify: `Components/Layout/ZoneRenderer.razor.css` — the 12-column zone grid; widgets inside it become their own containers
- Modify: `Components/ScenePoseLine.razor.css`
- Modify: `Components/WikiDisplay.razor.css` — `minmax(0, 1fr) 220px` TOC sidecar at line 8; keep the `position: sticky` TOC
- Modify: `Components/WikiEdit.razor.css` — `1fr 1fr` split at line 97; add `field-sizing: content` to the editor textarea
- Modify: `Layout/AccountPanel.razor.css`
- Modify: `Layout/ConfigLayout.razor.css` — `230px 1fr` at line 4. **Measured defect:** at 390px
  the nav stacks above the body and is 1273.75px tall, so `.config-section-body` starts at
  y≈1378 — roughly 1.5 screens of scrolling before any `/admin/config/*` page content is
  reachable. Collapse or otherwise shorten the nav at the narrow tier; the tiers Task 7 added
  to BannedNames, ImportConfig and ConfigIndex are correct but unreachable until this is fixed
- Modify: `Layout/OnboardingLayout.razor.css` — no breakpoints today
- Create: `Components/Help/HelpEntryPanel.razor.css` additions (file exists, no breakpoints)
- Modify: `Components/ObjectBrowser.razor.css` — no breakpoints today
- Modify: `Components/ServerStartupGate.razor.css` — no breakpoints today

`OnboardingLayout` and `ServerStartupGate` render outside `MainLayout`, so they have no
`page` container. Each declares its own on its root element, exactly as the dialogs do in
Task 8.

Commit message: `Make the shared panels and layouts size to their own box`

---

### Task 11: Batch C2 — widgets, and relocating component CSS out of the shell

**Files:**
- Modify: `Components/Widgets/CharacterDirectoryWidget.razor.css`
- Modify: `Components/Widgets/WelcomeTextWidget.razor.css`
- Modify: `Components/Widgets/RecentWikiActivityWidget.razor.css`
- Modify: `Components/Widgets/WikiBodyWidget.razor.css`
- Modify: `Components/Widgets/WikiIndexWidget.razor.css` — `minmax(0, 1fr)` category grid at line 212
- Create: `Components/Widgets/ActiveSceneWidget.razor.css`, `CharacterGalleryWidget.razor.css`, `OnlineCharactersWidget.razor.css`, `QuickLinksWidget.razor.css`, `QuickstartWidget.razor.css`, `StatsWidget.razor.css`
- Create: `Components/ConfigNavDrawer.razor.css`
- Modify: `SharpMUSH.Client/wwwroot/css/shell.css`

Widgets are the strongest case in the portal for self-containers: the same widget renders
full-width on `/`, in a 12-column zone cell, and in an admin-configured 280px aside. Every
widget root gets `container-type: inline-size` and queries itself unnamed — never `page`.

This task also does the relocation the ownership rule implies: any block in `shell.css` that
styles exactly one component moves into that component's scoped stylesheet. The
`ConfigNavDrawer` block (`custom.css:619+` before the split) is the clear case. Blocks that
style the shell itself — sidebar, topbar, terminal drawer, bottom nav, backdrop — stay, because
they are chrome rather than page content.

Leave `.char-picker` in `globals.css`: it sits on a `MudPaper` carrying MudBlazor's scope
identifier, so a scoped rule has no ancestor to anchor to. The existing comment already
explains this; keep it.

Commit message: `Size widgets to their own box, and move component CSS out of the shell`

---

### Task 12: Batch D1 — content and reading pages

**Files:**
- Modify: `Pages/Home.razor.css`
- Modify: `Pages/Characters.razor.css` — `minmax(0, 1fr)` at line 227; add `subgrid` so card internals align across a row
- Modify: `Pages/CharacterProfile.razor.css`
- Modify: `Pages/Help.razor.css`, `Pages/HelpTopic.razor.css`, `Pages/HelpAdminTopic.razor.css` — add `content-visibility: auto` with `contain-intrinsic-size` to the topic list, and `scroll-margin-block-start` to in-page anchor targets
- Modify: `Pages/WikiPageDiff.razor.css` — `1fr 1fr` split at line 164 stacks at the medium tier; the diff panes get `.scroll-x` rather than reflowing
- Modify: `Pages/WikiPageHistory.razor.css`
- Modify: `Pages/DynamicApplication.razor.css`
- Create: `Pages/WikiPage.razor.css`, `Pages/WikiPageEdit.razor.css`

Then remove `Pages/WikiPage.razor` and `Pages/WikiPageEdit.razor` from
`PagesWithoutStylesheetByDesign` in the guard test.

`WikiPageDiff` and `WikiPageEdit` already carry `full-bleed` from Task 3, so they are exempt
from the reading cap and keep the full window at 2560px — which is the point of a diff view.

Commit message: `Give the wiki, help and character pages container tiers`

---

### Task 13: Batch D2 and D3 — real-time surfaces, account and tools

**Files:**
- Modify: `Pages/Play.razor.css` — `minmax(0, 1fr) 270px` at line 23; the 270px rail stacks at the medium tier. Keep `height: 100%`; the page is `full-bleed`, so Task 3's `:has(> .full-bleed)` rule supplies the definite height
- Modify: `Pages/Scenes.razor.css`, `Pages/ScenesActive.razor.css`, `Pages/SceneDetail.razor.css`, `Pages/SceneLive.razor.css`
- Modify: `Pages/Mail.razor.css`, `Pages/MailDetail.razor.css`
- Modify: `Pages/MailCompose.razor.css` — add `field-sizing: content` to the body textarea
- Modify: `Pages/Account.razor.css` — 11 `!important` declarations, the most in the portal; apply the `MudTable` stacking from Task 7
- Modify: `Pages/Settings.razor.css`, `Pages/SettingsTheme.razor.css`, `Pages/Setup.razor.css`, `Pages/Login.razor.css`
- Modify: `Pages/SoftcodeEditor.razor.css` — 10 `!important` declarations; the page is `full-bleed`, so Monaco keeps the full window
- Create: `Pages/Register.razor.css`, `Pages/CharacterCreate.razor.css`

Then remove `Pages/Register.razor` and `Pages/CharacterCreate.razor` from
`PagesWithoutStylesheetByDesign` in the guard test.

Add `overscroll-behavior: contain` to the scrolling panes on `/play`, `/scenes/live` and
`/mail` so a flick at the end of the list does not scroll the page behind it.

Commit message: `Give the play, scene, mail and account surfaces container tiers`

---

### Task 14: Close the burn-down and prove the whole portal

**Files:**
- Modify: `SharpMUSH.Tests.BUnit/Layout/ResponsiveConventionsTests.cs`

- [ ] **Step 1: Confirm the exemption list is empty**

`NotYetMigrated` should contain no entries after Task 13. If any remain, they were missed —
finish them using the shared per-batch procedure before continuing.

- [ ] **Step 2: Enable the finishing test**

Remove the `Skip` argument from `TheMigrationIsFinished`:

```csharp
	[Test]
	public async Task TheMigrationIsFinished()
```

- [ ] **Step 3: Delete the exemption plumbing**

With the set empty and the migration asserted, the `NotYetMigrated` filters in
`PagesQueryTheirContainerRatherThanTheViewport` and
`MigratedStylesheetsCarryNoImportantDeclarations` no longer exclude anything. Delete the
`.Where(f => !NotYetMigrated.Contains(Rel(f)))` clause from both, delete the
`TheExemptionListHasNoStaleEntries` test and `TheMigrationIsFinished` along with the
`NotYetMigrated` field itself. What remains is the rule, stated once, applying to everything.

- [ ] **Step 4: Run the full test suites**

```bash
dotnet run --project SharpMUSH.Tests.BUnit
dotnet build -p:SkipFormatVerification=true
```

Expected: PASS.

- [ ] **Step 5: Run all three sweep profiles**

With the dev stack up:

```bash
node tools/responsive-sweep/sweep.mjs --profile final
node tools/responsive-sweep/sweep.mjs --profile final-sidebar-expanded    # expand the sidebar first
node tools/responsive-sweep/sweep.mjs --profile final-wide-aside          # configure a wide right aside in /admin/layouts first
```

Expected: all three exit zero. The last two are the cases the old viewport model could not
express, so they are what proves the architecture rather than just the styling.

- [ ] **Step 6: Update the CSS documentation**

In `docs/design/ui-patterns.md`, replace the section describing the 760px breakpoint model
with the three-layer ownership rule, the container tier vocabulary
(`48rem` / `64rem` / `90rem`), the `full-bleed` opt-out, and a pointer to
`ResponsiveConventionsTests` as the enforcement. State plainly that a page must never
contain `@media`, and why.

- [ ] **Step 7: Commit**

```bash
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
dotnet format whitespace --folder SharpMUSH.Tests.BUnit --exclude "**/bin/**" --exclude "**/obj/**"
git add SharpMUSH.Tests.BUnit docs/design/ui-patterns.md
git commit -m "Close the responsive burn-down, and document the shell/page CSS boundary"
```

---

## Self-review notes

- **Spec coverage.** Three-layer ownership → Tasks 2, 3, 5, 11. Container vocabulary → Task 3
  (declaration), 6–13 (use), 5 (enforcement). Ultrawide cap → Task 3. Cascade layers → Task 2,
  with `!important` removal enforced by Task 5 and executed in 6–13. File split → Task 2.
  Modern CSS: `subgrid` Task 12, `field-sizing` Tasks 10 and 13, `content-visibility` Task 12,
  `overscroll-behavior` Task 13, `scroll-margin-block-start` Task 12, logical properties
  throughout, `clamp()` as applied per batch. Nesting spike → Task 1. Guard test's eight rules →
  Task 5. Playwright sweep incl. the two extra profiles → Tasks 4 and 14. Batches A–D → Tasks
  6–13. Documentation → Task 14 Step 6.
- **Known deviation from the spec's task list.** The spec describes the guard test as landing
  before the sweep with no mention of an exemption mechanism. Enforcing all eight rules
  immediately would fail on 66 files at once and block every commit, so the test ships with an
  enumerated burn-down that Task 14 deletes. The end state is identical.
- **Naming consistency.** `NotYetMigrated`, `PagesWithoutStylesheetByDesign`, `SanctionedTiers`,
  `Rel()`, `ScopedStylesheets()`, `.phosphor-page`, `full-bleed`, layer order
  `vendor, tokens, shell, utilities` — each defined in Task 2, 3 or 5 and used unchanged after.
