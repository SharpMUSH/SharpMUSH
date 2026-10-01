# D1 Phase 0 — Tokens and Kit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Merge the D1 token additions and build the §4 kit components with bUnit tests, with no page changes.

**Architecture:** Tokens land in `wwwroot/css/tokens.css` (layer `tokens`). Each kit piece is one Razor component under `SharpMUSH.Client/Components/Kit/` with its own scoped stylesheet, taking data through parameters and rendering real `<a>`/`<button>`/`<img>` elements. Three small helpers (`ImageUrlPolicy`, `NameHue`, `Initials`) back the image and no-image behaviour. A staff-only `/admin/kit` preview page exists so the kit can be screenshotted against board 20 before any page uses it.

**Tech Stack:** Blazor WASM, MudBlazor 9 (icons only in this phase), scoped CSS, bUnit + TUnit (`SharpMUSH.Tests.BUnit`), `IStringLocalizer<SharedResource>`.

**Spec:** `docs/design/d1/README.md` §2, §4, §9 and `docs/superpowers/specs/2026-09-29-image-attributes-and-oob-v2-design.md` §6 (image URL policy).

## Global Constraints

- The shell owns `@media`; a `*.razor.css` uses only `@container page (max-width: 48rem|64rem)` / `(min-width: 90rem)` or an unnamed component container. No `!important`, no `position: fixed` in scoped CSS. `ResponsiveConventionsTests` gates all of it.
- A class handed to a MudBlazor component is only reachable through `::deep` anchored on an element the file declares.
- MudBlazor 9 is the only component library; no new dependency.
- All copy goes through `IStringLocalizer<SharedResource>`; new keys go in `SharedResource.resx` (neutral only) and must not equal their own camelCase key.
- Don't hard-code `#00f5b7`; use `var(--accent)`, `var(--glow)`.
- `--text-faint` becomes `#7d8790`; `ThemeService.TextDisabled` and `BuiltInRoles` fallback change with it.
- Motion uses `--motion-pop` / `--motion-slide`.
- Icon-only controls carry `aria-label`; toggles expose `aria-pressed`/`aria-expanded`; current items carry `aria-current="page"`.
- C# is tabs (indent 2); `dotnet format whitespace --folder` twice before commit; stage only touched paths.

## Review Focus

1. An image URL of `javascript:` or `data:` or `http:` in any kit component renders the no-image fallback, never an `<img>` (Task 2 pins `ImageUrlPolicy`; Tasks 5 and 6 pin the components).
2. A name that is empty, one word, or multi-word yields sensible initials without throwing (Task 2).
3. A `Mention` with no colour underlines in `--text-faint`, never the accent (Task 7).
4. `GlassBanner` minimised still names the subject and exposes a restore button with an `aria-label` (Task 6).
5. `SidebarRow` with `Href` renders `<a>`; without it renders `<button type="button">`; `IsCurrent` sets `aria-current="page"` on either (Task 4).

---

### Task 1: Tokens

**Files:**
- Modify: `SharpMUSH.Client/wwwroot/css/tokens.css:60-105` (the `:root` block)
- Modify: `SharpMUSH.Client/Services/ThemeService.cs:128` (`TextDisabled`)
- Modify: `SharpMUSH.Contracts/Authorization/BuiltInRoles.cs:77`
- Test: `SharpMUSH.Tests.BUnit/Layout/DesignTokensTests.cs` (new)
- Test: `SharpMUSH.Tests/Theme/ThemeServiceTests.cs` (add one test)

**Interfaces:** Produces the CSS custom properties every later task reads: `--rail-bg --text-on-image --link-missing --warn --warn-tint --unread-alert --ooc-band-bg --ooc-band-border --ooc-icon-bg --ooc-icon-fg --rail-w --side-w --side-strip-w --aside-w --main-pad --radius-card --radius-row --radius-tile --radius-portrait --glass-bg --glass-pill-bg --glass-filter --glass-edge --on-image-shadow --scrim --scrim-open --blur-mask-soft --blur-mask-mid --blur-mask-deep --blur-mask-soft-open --blur-mask-mid-open --blur-mask-deep-open --mention-offset --mention-alpha`.

- [ ] **Step 1: Write the failing test**

```csharp
// SharpMUSH.Tests.BUnit/Layout/DesignTokensTests.cs
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Pins the D1 token set (docs/design/d1/README.md §2) in tokens.css.</summary>
public class DesignTokensTests
{
	private static string Tokens() => File.ReadAllText(Path.Join(ClientSource.CssRoot, "tokens.css"));

	private static readonly string[] D1Tokens =
	[
		"--rail-bg", "--text-on-image", "--link-missing", "--warn", "--warn-tint", "--unread-alert",
		"--ooc-band-bg", "--ooc-band-border", "--ooc-icon-bg", "--ooc-icon-fg",
		"--rail-w", "--side-w", "--side-strip-w", "--aside-w", "--main-pad",
		"--radius-card", "--radius-row", "--radius-tile", "--radius-portrait",
		"--glass-bg", "--glass-pill-bg", "--glass-filter", "--glass-edge", "--on-image-shadow",
		"--scrim", "--scrim-open",
		"--blur-mask-soft", "--blur-mask-mid", "--blur-mask-deep",
		"--blur-mask-soft-open", "--blur-mask-mid-open", "--blur-mask-deep-open",
		"--mention-offset", "--mention-alpha",
	];

	[Test]
	public async Task EveryD1TokenIsDeclaredOnRoot()
	{
		var css = Tokens();
		var missing = D1Tokens.Where(t => !Regex.IsMatch(css, $@"^\s*{Regex.Escape(t)}\s*:", RegexOptions.Multiline)).ToList();
		await Assert.That(missing).IsEmpty().Because("README §2 lists these as the additions every kit piece reads");
	}

	[Test]
	public async Task TextFaintMeetsAa()
	{
		await Assert.That(Tokens()).Contains("--text-faint: #7d8790")
			.Because("#5f6870 is 3.38:1 on --bg; the small labels it is used on need 4.5:1");
	}

	[Test]
	public async Task OldTextFaintValueIsGone()
	{
		await Assert.That(Tokens()).DoesNotContain("#5f6870");
	}
}
```

```csharp
// append to SharpMUSH.Tests/Theme/ThemeServiceTests.cs, inside the class
[Test]
public async Task TextDisabledMatchesTheFaintToken()
{
	var theme = ThemeService.Presets[0].ToMudTheme();
	await Assert.That(theme.PaletteDark.TextDisabled.ToString()).IsEqualTo("#7d8790ff")
		.Because("MudBlazor's disabled text must keep the same AA contrast as --text-faint");
}
```
(Check how `ThemeServiceTests.cs` already reaches a preset and `ToMudTheme`; use the same access. `MudColor.ToString()` yields `#rrggbbaa`; if the existing tests compare differently, follow them.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet run --project SharpMUSH.Tests.BUnit -- --treenode-filter "/*/*/DesignTokensTests/*"`
Expected: FAIL, all three (tokens missing, old value present).

- [ ] **Step 3: Merge the tokens**

In `tokens.css` `:root`, change `--text-faint: #5f6870;` to `--text-faint: #7d8790;` and, after `--mud-palette-navbar-gradient`, paste the body of `docs/design/d1/tokens-d1.css` (everything inside its `:root { }` except the `--text-faint` line), keeping its comments. Convert the file's 4-space indentation to the tab indentation `tokens.css` uses.

In `ThemeService.cs` line 128: `TextDisabled = "#7d8790",`.
In `BuiltInRoles.cs` line 77: `_ => "#7d8790"` (the Guest role colour is rendered as badge text on `--surface`; the same contrast argument applies).

- [ ] **Step 4: Run to verify pass**

Run the two filters above. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add SharpMUSH.Client/wwwroot/css/tokens.css SharpMUSH.Client/Services/ThemeService.cs SharpMUSH.Contracts/Authorization/BuiltInRoles.cs SharpMUSH.Tests.BUnit/Layout/DesignTokensTests.cs SharpMUSH.Tests/Theme/ThemeServiceTests.cs
git commit -m "feat(portal): merge the D1 design tokens and raise --text-faint to AA"
```

---

### Task 2: Kit helpers — ImageUrlPolicy, NameHue, Initials

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/ImageUrlPolicy.cs`
- Create: `SharpMUSH.Client/Components/Kit/NameHue.cs`
- Create: `SharpMUSH.Client/Components/Kit/Initials.cs`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/KitHelperTests.cs`

**Interfaces:**
- Produces `static bool ImageUrlPolicy.IsRenderable(string? url)` — true only for `/relative` (not `//`) or `https://`.
- Produces `static int NameHue.Of(string? name)` — stable FNV-1a hue 0–359 (the existing `GetHashCode() % 360` avatars change hue every reload because string hashing is randomised per process; this replaces that).
- Produces `static string Initials.From(string? name)` — up to two upper-case letters from the first two words, `"?"` when empty.

- [ ] **Step 1: Write the failing tests**

```csharp
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class KitHelperTests
{
	[Test]
	[Arguments("/api/wiki-assets/abc/x.jpg", true)]
	[Arguments("https://cdn.example/x.jpg", true)]
	[Arguments("http://cdn.example/x.jpg", false)]
	[Arguments("//cdn.example/x.jpg", false)]
	[Arguments("javascript:alert(1)", false)]
	[Arguments("data:image/png;base64,AAAA", false)]
	[Arguments("", false)]
	[Arguments(null, false)]
	[Arguments("  /x.jpg", true)]
	public async Task ImageUrlPolicy_AcceptsOnlySiteRelativeOrHttps(string? url, bool expected)
		=> await Assert.That(ImageUrlPolicy.IsRenderable(url)).IsEqualTo(expected);

	[Test]
	public async Task NameHue_IsStableAndInRange()
	{
		var a = NameHue.Of("Tomas Reyes");
		await Assert.That(a).IsEqualTo(NameHue.Of("Tomas Reyes"));
		await Assert.That(a).IsBetween(0, 359);
		await Assert.That(NameHue.Of(null)).IsBetween(0, 359);
	}

	[Test]
	[Arguments("Tomas Reyes", "TR")]
	[Arguments("Wren", "W")]
	[Arguments("  dace   kellan  ", "DK")]
	[Arguments("", "?")]
	[Arguments(null, "?")]
	public async Task Initials_TakeTheFirstTwoWords(string? name, string expected)
		=> await Assert.That(Initials.From(name)).IsEqualTo(expected);
}
```

- [ ] **Step 2: Run to verify failure** — `--treenode-filter "/*/*/KitHelperTests/*"`, expected: build error (types missing).

- [ ] **Step 3: Implement**

```csharp
// ImageUrlPolicy.cs
namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Which image URLs the portal will put in an <c>&lt;img src&gt;</c>. Softcode supplies these, so
/// the client accepts only what a site can serve safely: a site-relative path or https. Anything
/// else (http, protocol-relative, data:, javascript:) renders the no-image fallback.
/// </summary>
public static class ImageUrlPolicy
{
	public static bool IsRenderable(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return false;
		var s = url.Trim();
		if (s.StartsWith("//", StringComparison.Ordinal)) return false;
		if (s.StartsWith('/')) return true;
		return s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
	}
}
```

```csharp
// NameHue.cs
namespace SharpMUSH.Client.Components.Kit;

/// <summary>A hue (0–359) derived from a name with a stable hash, so a tinted fallback keeps its colour across reloads.</summary>
public static class NameHue
{
	public static int Of(string? name)
	{
		unchecked
		{
			var hash = 2166136261u;
			foreach (var c in name ?? string.Empty)
			{
				hash ^= c;
				hash *= 16777619u;
			}
			return (int)(hash % 360u);
		}
	}
}
```

```csharp
// Initials.cs
namespace SharpMUSH.Client.Components.Kit;

/// <summary>Up to two initials for a no-image tile: first letters of the first two words, upper-cased.</summary>
public static class Initials
{
	public static string From(string? name)
	{
		var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (words.Length == 0) return "?";
		var s = char.ToUpperInvariant(words[0][0]).ToString();
		if (words.Length > 1) s += char.ToUpperInvariant(words[1][0]);
		return s;
	}
}
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git add SharpMUSH.Client/Components/Kit SharpMUSH.Tests.BUnit/Components/Kit && git commit -m "feat(kit): image URL policy, stable name hue and initials helpers"`

---

### Task 3: SectionLabel, KitCard, PlainPageHeader

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/SectionLabel.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/KitCard.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/PlainPageHeader.razor` + `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/StaticKitTests.cs`

**Interfaces:**
- `SectionLabel`: `[Parameter] RenderFragment? ChildContent`, `[Parameter] string? Class`. Renders `<div class="kit-section-label">`.
- `KitCard`: `[Parameter] string? Title`, `[Parameter] string? Sub`, `[Parameter] RenderFragment? Controls`, `[Parameter] RenderFragment? ChildContent`, `[Parameter] bool Aside` (padding 12, 13px title) — otherwise the 52px header row (15px title). `[Parameter] string? Class`. Renders `<section class="kit-card">`, header `<header class="kit-card-head">` only when `Title` or `Controls` is set.
- `PlainPageHeader`: `[Parameter] string? Kicker`, `[Parameter] string Title` (EditorRequired), `[Parameter] string? Description`, `[Parameter] RenderFragment? Actions`. Renders `<header class="kit-page-head">` with `<h1>`.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class StaticKitTests : BunitContext
{
	[Test]
	public async Task SectionLabel_RendersItsText()
	{
		var cut = Render<SectionLabel>(p => p.AddChildContent("In scene"));
		await Assert.That(cut.Find(".kit-section-label").TextContent.Trim()).IsEqualTo("In scene");
	}

	[Test]
	public async Task KitCard_WithTitle_RendersHeaderRow()
	{
		var cut = Render<KitCard>(p => p.Add(x => x.Title, "Here").Add(x => x.Sub, "4 here").AddChildContent("<p>body</p>"));
		await Assert.That(cut.Find(".kit-card-head .kit-card-title").TextContent).IsEqualTo("Here");
		await Assert.That(cut.Find(".kit-card-head .kit-card-sub").TextContent).IsEqualTo("4 here");
		await Assert.That(cut.Find(".kit-card-body").InnerHtml).Contains("body");
	}

	[Test]
	public async Task KitCard_WithoutTitleOrControls_HasNoHeader()
	{
		var cut = Render<KitCard>(p => p.AddChildContent("x"));
		await Assert.That(cut.FindAll(".kit-card-head").Count).IsEqualTo(0);
	}

	[Test]
	public async Task KitCard_Aside_AddsTheAsideModifier()
	{
		var cut = Render<KitCard>(p => p.Add(x => x.Aside, true).Add(x => x.Title, "Exits · 3"));
		await Assert.That(cut.Find("section").ClassList).Contains("kit-card--aside");
	}

	[Test]
	public async Task PlainPageHeader_RendersKickerTitleDescriptionAndActions()
	{
		var cut = Render<PlainPageHeader>(p => p
			.Add(x => x.Kicker, "Admin · server settings")
			.Add(x => x.Title, "Chat")
			.Add(x => x.Description, "Chat and communication settings")
			.Add(x => x.Actions, b => b.AddMarkupContent(0, "<button>Reset</button>")));
		await Assert.That(cut.Find(".kit-page-kicker").TextContent).IsEqualTo("Admin · server settings");
		await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Chat");
		await Assert.That(cut.Find(".kit-page-desc").TextContent).IsEqualTo("Chat and communication settings");
		await Assert.That(cut.Find(".kit-page-actions button").TextContent).IsEqualTo("Reset");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`SectionLabel.razor`:
```razor
@* §4.1 sidebar section label: mono, 10px, uppercase, faint. The global .section-kicker keeps its .16em spacing; this is the .12em sidebar variant. *@
<div class="kit-section-label @Class">@ChildContent</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Class { get; set; }
}
```
`SectionLabel.razor.css`:
```css
.kit-section-label {
	font-family: var(--font-mono);
	font-size: 10px;
	letter-spacing: 0.12em;
	text-transform: uppercase;
	color: var(--text-faint);
	padding: 0 10px 6px;
}
```

`KitCard.razor`:
```razor
@* §4.4 card: --surface, 1px --border, radius --radius-card. Main cards get a 52px header row; aside cards are padded 12px with a 13px title. *@
<section class="kit-card @(Aside ? "kit-card--aside" : null) @Class">
    @if (Title is not null || Controls is not null)
    {
        <header class="kit-card-head">
            <div class="kit-card-titles">
                @if (Title is not null) { <div class="kit-card-title">@Title</div> }
                @if (Sub is not null) { <div class="kit-card-sub">@Sub</div> }
            </div>
            @if (Controls is not null) { <div class="kit-card-controls">@Controls</div> }
        </header>
    }
    <div class="kit-card-body">@ChildContent</div>
</section>

@code {
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Sub { get; set; }
    [Parameter] public RenderFragment? Controls { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public bool Aside { get; set; }
    [Parameter] public string? Class { get; set; }
}
```
`KitCard.razor.css`:
```css
.kit-card {
	background: var(--surface);
	border: 1px solid var(--border);
	border-radius: var(--radius-card);
	display: flex;
	flex-direction: column;
	min-width: 0;
}
.kit-card-head {
	min-height: 52px;
	display: flex;
	align-items: center;
	gap: 12px;
	padding: 0 16px;
	border-bottom: 1px solid var(--border-soft);
}
.kit-card-titles { flex: 1 1 auto; min-width: 0; }
.kit-card-title { font-size: 15px; font-weight: 700; color: var(--text); }
.kit-card-sub { font-size: 12px; color: var(--text-dim); margin-top: 1px; }
.kit-card-controls { display: flex; align-items: center; gap: 8px; }
.kit-card-body { padding: 0; min-width: 0; }

.kit-card--aside { padding: 12px; gap: 10px; }
.kit-card--aside .kit-card-head { min-height: 0; padding: 0; border-bottom: 0; }
.kit-card--aside .kit-card-title { font-size: 13px; }
```

`PlainPageHeader.razor`:
```razor
@* §4.9 header for pages without an image: mono kicker, 26px title, 13px dim description, actions on the baseline. *@
<header class="kit-page-head">
    <div class="kit-page-text">
        @if (Kicker is not null) { <div class="kit-page-kicker">@Kicker</div> }
        <h1 class="kit-page-title">@Title</h1>
        @if (Description is not null) { <p class="kit-page-desc">@Description</p> }
    </div>
    @if (Actions is not null) { <div class="kit-page-actions">@Actions</div> }
</header>

@code {
    [Parameter] public string? Kicker { get; set; }
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public string? Description { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
}
```
`PlainPageHeader.razor.css`:
```css
.kit-page-head { display: flex; align-items: flex-end; gap: 16px; }
.kit-page-text { flex: 1 1 auto; min-width: 0; }
.kit-page-kicker {
	font-family: var(--font-mono);
	font-size: 10px;
	letter-spacing: 0.12em;
	text-transform: uppercase;
	color: var(--text-faint);
}
.kit-page-title { margin: 4px 0 0; font-family: var(--font-display); font-size: 26px; font-weight: 700; color: var(--text); }
.kit-page-desc { margin: 4px 0 0; font-size: 13px; color: var(--text-dim); }
.kit-page-actions { display: flex; align-items: center; gap: 8px; flex-shrink: 0; }
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): section label, card and plain page header"`

---

### Task 4: SidebarRow

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/SidebarRow.razor` + `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/SidebarRowTests.cs`

**Interfaces:**
- `enum SidebarLead { Image, Avatar, Icon, Channel }` (in `SidebarRow.razor` `@code`, public).
- Parameters: `SidebarLead Lead` (default Icon), `string? ImageUrl`, `string? ImageUrl2` (second stacked avatar for a group page), `string? Name` (initials/alt source), `string? Icon` (MudBlazor icon svg string), `string Label` (EditorRequired), `int? Count`, `int Unread` (0 = none), `bool IsCurrent`, `bool Dim`, `string? Href`, `EventCallback OnClick`, `bool Collapsed` (renders only the lead; the label goes to `title`).
- Row heights by lead: Image 44, Avatar 40, Icon 40, Channel 36.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class SidebarRowTests : BunitContext
{
	[Test]
	public async Task WithHref_RendersAnAnchor_AndCurrentSetsAriaCurrent()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Salt Market").Add(x => x.Href, "/scenes/42").Add(x => x.IsCurrent, true));
		var a = cut.Find("a.kit-row");
		await Assert.That(a.GetAttribute("href")).IsEqualTo("/scenes/42");
		await Assert.That(a.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(a.ClassList).Contains("kit-row--current");
	}

	[Test]
	public async Task WithoutHref_RendersAButton_ThatRaisesOnClick()
	{
		var clicked = false;
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.OnClick, () => clicked = true));
		var b = cut.Find("button.kit-row");
		await Assert.That(b.GetAttribute("type")).IsEqualTo("button");
		b.Click();
		await Assert.That(clicked).IsTrue();
	}

	[Test]
	public async Task ImageLead_RendersImg_OnlyForARenderableUrl()
	{
		var ok = Render<SidebarRow>(p => p.Add(x => x.Label, "x").Add(x => x.Lead, SidebarLead.Image).Add(x => x.ImageUrl, "/a.jpg").Add(x => x.Name, "Lower Docks"));
		await Assert.That(ok.Find("img.kit-row-img").GetAttribute("src")).IsEqualTo("/a.jpg");
		var bad = Render<SidebarRow>(p => p.Add(x => x.Label, "x").Add(x => x.Lead, SidebarLead.Image).Add(x => x.ImageUrl, "javascript:1").Add(x => x.Name, "Lower Docks"));
		await Assert.That(bad.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(bad.Find(".kit-row-fallback")).IsNotNull();
	}

	[Test]
	public async Task AvatarLead_WithoutImage_ShowsInitialsOnATintedFill()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Dace Kellan").Add(x => x.Lead, SidebarLead.Avatar).Add(x => x.Name, "Dace Kellan"));
		var fb = cut.Find(".kit-row-avatar.kit-row-fallback");
		await Assert.That(fb.TextContent.Trim()).IsEqualTo("DK");
		await Assert.That(fb.GetAttribute("style")).Contains("hsl(");
	}

	[Test]
	public async Task GroupAvatar_StacksTwoImages()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Tomas, Dace").Add(x => x.Lead, SidebarLead.Avatar).Add(x => x.ImageUrl, "/t.jpg").Add(x => x.ImageUrl2, "/d.jpg"));
		await Assert.That(cut.FindAll("img.kit-row-avatar").Count).IsEqualTo(2);
		await Assert.That(cut.Find(".kit-row-lead").ClassList).Contains("kit-row-lead--group");
	}

	[Test]
	public async Task Unread_RendersAPill_AndBoldsTheRow()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Public").Add(x => x.Lead, SidebarLead.Channel).Add(x => x.Unread, 3));
		await Assert.That(cut.Find(".kit-row-unread").TextContent).IsEqualTo("3");
		await Assert.That(cut.Find(".kit-row").ClassList).Contains("kit-row--unread");
	}

	[Test]
	public async Task Count_RendersDimCount()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Theme").Add(x => x.Count, 12));
		await Assert.That(cut.Find(".kit-row-count").TextContent).IsEqualTo("12");
	}

	[Test]
	public async Task Collapsed_HidesTheLabelButKeepsItAsTitle()
	{
		var cut = Render<SidebarRow>(p => p.Add(x => x.Label, "Theme").Add(x => x.Collapsed, true));
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-row").GetAttribute("title")).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-row").GetAttribute("aria-label")).IsEqualTo("Theme");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`SidebarRow.razor`:
```razor
@* §4.2 one row shape; the lead changes by content. An <a> when it navigates, a <button> when it acts. *@
@if (Href is not null)
{
    <a class="@RowClass" href="@Href" aria-current="@(IsCurrent ? "page" : null)" title="@(Collapsed ? Label : null)" aria-label="@(Collapsed ? Label : null)">
        @Inner
    </a>
}
else
{
    <button type="button" class="@RowClass" aria-current="@(IsCurrent ? "page" : null)" title="@(Collapsed ? Label : null)" aria-label="@(Collapsed ? Label : null)" @onclick="OnClick">
        @Inner
    </button>
}

@code {
    public enum SidebarLead { Image, Avatar, Icon, Channel }

    [Parameter] public SidebarLead Lead { get; set; } = SidebarLead.Icon;
    [Parameter] public string? ImageUrl { get; set; }
    [Parameter] public string? ImageUrl2 { get; set; }
    [Parameter] public string? Name { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter] public int? Count { get; set; }
    [Parameter] public int Unread { get; set; }
    [Parameter] public bool IsCurrent { get; set; }
    [Parameter] public bool Dim { get; set; }
    [Parameter] public bool Collapsed { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private bool IsGroup => Lead == SidebarLead.Avatar && ImageUrl2 is not null;

    private string RowClass => string.Join(' ', new[]
    {
        "kit-row",
        $"kit-row--{Lead.ToString().ToLowerInvariant()}",
        IsCurrent ? "kit-row--current" : null,
        Unread > 0 ? "kit-row--unread" : null,
        Dim ? "kit-row--dim" : null,
        Collapsed ? "kit-row--collapsed" : null,
    }.Where(c => c is not null));

    private string FallbackStyle => $"background:hsl({NameHue.Of(Name ?? Label)} 30% 24%);color:hsl({NameHue.Of(Name ?? Label)} 45% 82%);";

    private RenderFragment Inner => __builder =>
    {
        <span class="kit-row-lead @(IsGroup ? "kit-row-lead--group" : null)" aria-hidden="true">
            @switch (Lead)
            {
                case SidebarLead.Image:
                    @if (ImageUrlPolicy.IsRenderable(ImageUrl)) { <img class="kit-row-img" src="@ImageUrl!.Trim()" alt="" /> }
                    else { <span class="kit-row-img kit-row-fallback">@Initials.From(Name ?? Label)</span> }
                    break;
                case SidebarLead.Avatar:
                    @if (ImageUrlPolicy.IsRenderable(ImageUrl)) { <img class="kit-row-avatar" src="@ImageUrl!.Trim()" alt="" /> }
                    else { <span class="kit-row-avatar kit-row-fallback" style="@FallbackStyle">@Initials.From(Name ?? Label)</span> }
                    @if (IsGroup && ImageUrlPolicy.IsRenderable(ImageUrl2)) { <img class="kit-row-avatar kit-row-avatar--second" src="@ImageUrl2!.Trim()" alt="" /> }
                    break;
                case SidebarLead.Icon:
                    <span class="kit-row-icon"><MudIcon Icon="@(Icon ?? Icons.Material.Outlined.Folder)" Size="Size.Small" /></span>
                    break;
                case SidebarLead.Channel:
                    <span class="kit-row-hash">#</span>
                    break;
            }
        </span>
        @if (!Collapsed)
        {
            <span class="kit-row-label">@Label</span>
            @if (Unread > 0) { <span class="kit-row-unread">@Unread</span> }
            else if (Count is not null) { <span class="kit-row-count">@Count</span> }
        }
        else if (Unread > 0)
        {
            <span class="kit-row-unread kit-row-unread--badge">@Unread</span>
        }
    };
}
```
Note: the `MudIcon` inside gets its size from the wrapper via `::deep`.

`SidebarRow.razor.css`:
```css
.kit-row {
	display: flex;
	align-items: center;
	gap: 8px;
	width: 100%;
	padding: 0 8px;
	border: 0;
	border-radius: var(--radius-row);
	background: transparent;
	color: var(--text);
	font: inherit;
	font-size: 14px;
	text-align: left;
	text-decoration: none;
	cursor: pointer;
	box-sizing: border-box;
	position: relative;
	transition: background var(--motion-pop);
}
.kit-row:hover { background: var(--surface); }
.kit-row:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.kit-row--image { height: 44px; }
.kit-row--avatar { height: 40px; }
.kit-row--icon { height: 40px; }
.kit-row--channel { height: 36px; padding: 0 10px; color: var(--text-dim); }
.kit-row--dim { color: var(--text-dim); }
.kit-row--unread { font-weight: 700; color: var(--text); }
.kit-row--current { background: var(--surface-2); color: var(--accent); font-weight: 600; }
.kit-row--collapsed { justify-content: center; padding: 0; }

.kit-row-lead { display: flex; align-items: center; flex-shrink: 0; }
.kit-row-lead--group { width: 36px; position: relative; height: 28px; }
.kit-row-img { width: 30px; height: 30px; border-radius: 8px; object-fit: cover; }
.kit-row-avatar { width: 28px; height: 28px; border-radius: 50%; object-fit: cover; }
.kit-row-lead--group .kit-row-avatar { width: 24px; height: 24px; position: absolute; left: 0; top: 2px; }
.kit-row-lead--group .kit-row-avatar--second { left: 12px; outline: 2px solid var(--surface-3); }
.kit-row-fallback {
	display: inline-flex;
	align-items: center;
	justify-content: center;
	background: var(--surface-2);
	color: var(--text-dim);
	font-size: 11px;
	font-weight: 700;
}
.kit-row-icon {
	width: 30px;
	height: 30px;
	border-radius: 8px;
	background: var(--surface-2);
	color: var(--text-dim);
	display: inline-flex;
	align-items: center;
	justify-content: center;
}
.kit-row--current .kit-row-icon { color: var(--accent); }
.kit-row-hash { width: 14px; font-family: var(--font-mono); font-size: 15px; color: var(--text-faint); }
.kit-row-label { flex: 1 1 auto; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.kit-row-count { font-size: 12px; color: var(--text-dim); }
.kit-row-unread {
	height: 18px;
	min-width: 18px;
	padding: 0 6px;
	border-radius: 9px;
	background: var(--accent);
	color: var(--accent-on);
	font-size: 11px;
	font-weight: 700;
	display: inline-flex;
	align-items: center;
	justify-content: center;
}
.kit-row-unread--badge { position: absolute; top: 2px; right: 2px; }
.kit-row--current .kit-row-lead--group .kit-row-avatar, .kit-row--current .kit-row-img { outline: 2px solid var(--accent); outline-offset: 1px; }
```

- [ ] **Step 4: Run to verify pass.** Also run `--treenode-filter "/*/*/ResponsiveConventionsTests/*"`; the `::deep` rule must be respected (only plain elements are styled here, so no `::deep` is needed).
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): sidebar row with image, avatar, icon and channel leads"`

---

### Task 5: PortraitTile, ImageTile, Pill

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/PortraitTile.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/ImageTile.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/Pill.razor` + `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/TileTests.cs`

**Interfaces:**
- `PortraitTile`: `string Name` (EditorRequired), `string? ImageUrl`, `string? Alt`, `string? Label` (default Name), `string? Status` (appended " · status" and dims the label when set), `bool Highlight` (accent ring: "you"), `string? Href`, `EventCallback OnClick`, `int Height` (default 72).
- `ImageTile`: `string Label` (EditorRequired), `string? ImageUrl`, `string? Alt`, `int? Count`, `int Height` (52; 76 for category covers), `string? Icon` (fallback icon, default `Icons.Material.Outlined.Image`), `string? Href`, `EventCallback OnClick`, `RenderFragment? Overlay` (pills/keycap placed over the image), `string? Sub` (second dim line).
- `Pill`: `RenderFragment ChildContent`, `string? Dot` (CSS colour of the 7px status dot; null = none), `string? Class`.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class TileTests : BunitContext
{
	[Test]
	public async Task PortraitTile_WithImage_RendersImgWithAlt()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.ImageUrl, "/t.jpg").Add(x => x.Alt, "Tomas at dusk"));
		var img = cut.Find("img.kit-portrait-img");
		await Assert.That(img.GetAttribute("alt")).IsEqualTo("Tomas at dusk");
		await Assert.That(cut.Find(".kit-portrait-label").TextContent.Trim()).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task PortraitTile_WithoutImage_ShowsInitialsAtTheSameHeight()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Dace Kellan"));
		var fb = cut.Find(".kit-portrait-fallback");
		await Assert.That(fb.TextContent.Trim()).IsEqualTo("DK");
		await Assert.That(fb.GetAttribute("style")).Contains("height:72px");
	}

	[Test]
	public async Task PortraitTile_RejectsUnsafeUrl()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "X").Add(x => x.ImageUrl, "http://evil/x.jpg"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
	}

	[Test]
	public async Task PortraitTile_Status_DimsAndAppends()
	{
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Wren").Add(x => x.Status, "away"));
		await Assert.That(cut.Find(".kit-portrait-label").TextContent.Trim()).IsEqualTo("Wren · away");
		await Assert.That(cut.Find(".kit-portrait-label").ClassList).Contains("kit-portrait-label--dim");
	}

	[Test]
	public async Task PortraitTile_ClickRunsCallback_AndButtonHasType()
	{
		var hit = false;
		var cut = Render<PortraitTile>(p => p.Add(x => x.Name, "Wren").Add(x => x.OnClick, () => hit = true));
		var b = cut.Find("button.kit-portrait");
		await Assert.That(b.GetAttribute("type")).IsEqualTo("button");
		b.Click();
		await Assert.That(hit).IsTrue();
	}

	[Test]
	public async Task ImageTile_WithoutImage_RendersIconFill()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Out").Add(x => x.Count, 3));
		await Assert.That(cut.Find(".kit-tile-fallback")).IsNotNull();
		await Assert.That(cut.Find(".kit-tile-count").TextContent).IsEqualTo("3");
	}

	[Test]
	public async Task ImageTile_Height_AppliesToImageAndFallback()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Theme").Add(x => x.ImageUrl, "/t.jpg").Add(x => x.Height, 76));
		await Assert.That(cut.Find("img.kit-tile-img").GetAttribute("style")).Contains("height:76px");
	}

	[Test]
	public async Task ImageTile_OverlayAndSub_Render()
	{
		var cut = Render<ImageTile>(p => p.Add(x => x.Label, "Customs House").Add(x => x.Sub, "Closed after dusk")
			.Add(x => x.Overlay, b => b.AddMarkupContent(0, "<span class=\"ov\">Locked</span>")));
		await Assert.That(cut.Find(".kit-tile-overlay .ov").TextContent).IsEqualTo("Locked");
		await Assert.That(cut.Find(".kit-tile-sub").TextContent).IsEqualTo("Closed after dusk");
	}

	[Test]
	public async Task Pill_WithDot_RendersDotInColour()
	{
		var cut = Render<Pill>(p => p.Add(x => x.Dot, "var(--accent)").AddChildContent("In a scene"));
		await Assert.That(cut.Find(".kit-pill-dot").GetAttribute("style")).Contains("var(--accent)");
		await Assert.That(cut.Find(".kit-pill").TextContent.Trim()).IsEqualTo("In a scene");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`PortraitTile.razor`:
```razor
@* §4.5 portrait tile: 2-column grids of people. No image → initials on a tinted fill at the same size. *@
@if (Href is not null)
{
    <a class="kit-portrait @(Highlight ? "kit-portrait--highlight" : null)" href="@Href">@Inner</a>
}
else
{
    <button type="button" class="kit-portrait @(Highlight ? "kit-portrait--highlight" : null)" @onclick="OnClick">@Inner</button>
}

@code {
    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;
    [Parameter] public string? ImageUrl { get; set; }
    [Parameter] public string? Alt { get; set; }
    [Parameter] public string? Label { get; set; }
    [Parameter] public string? Status { get; set; }
    [Parameter] public bool Highlight { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public int Height { get; set; } = 72;

    private string Text => Status is null ? (Label ?? Name) : $"{Label ?? Name} · {Status}";
    private string FallbackStyle => $"height:{Height}px;background:hsl({NameHue.Of(Name)} 30% 24%);color:hsl({NameHue.Of(Name)} 45% 82%);";

    private RenderFragment Inner => __builder =>
    {
        @if (ImageUrlPolicy.IsRenderable(ImageUrl))
        {
            <img class="kit-portrait-img" src="@ImageUrl!.Trim()" alt="@(Alt ?? Name)" style="@($"height:{Height}px;")" loading="lazy" />
        }
        else
        {
            <span class="kit-portrait-fallback" style="@FallbackStyle" aria-hidden="true">@Initials.From(Name)</span>
        }
        <span class="kit-portrait-label @(Status is not null ? "kit-portrait-label--dim" : null)">@Text</span>
    };
}
```
`PortraitTile.razor.css`:
```css
.kit-portrait {
	display: flex;
	flex-direction: column;
	gap: 4px;
	min-width: 0;
	padding: 0;
	border: 0;
	background: transparent;
	color: var(--text);
	font: inherit;
	font-size: 12px;
	text-align: left;
	text-decoration: none;
	cursor: pointer;
}
.kit-portrait:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: var(--radius-tile); }
.kit-portrait-img, .kit-portrait-fallback {
	width: 100%;
	border-radius: var(--radius-tile);
	object-fit: cover;
	display: flex;
	align-items: center;
	justify-content: center;
	font-size: 15px;
	font-weight: 700;
	box-sizing: border-box;
}
.kit-portrait--highlight .kit-portrait-img, .kit-portrait--highlight .kit-portrait-fallback { outline: 2px solid var(--accent); outline-offset: -2px; }
.kit-portrait-label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.kit-portrait-label--dim { color: var(--text-dim); }
.kit-portrait--highlight .kit-portrait-label { color: var(--accent); }
```

`ImageTile.razor`:
```razor
@* §4.6 image tile: rooms, exits, pages, scenes. Full-width image (52px; 76 for covers), label with an optional dim count. *@
@if (Href is not null)
{
    <a class="kit-tile" href="@Href">@Inner</a>
}
else
{
    <button type="button" class="kit-tile" @onclick="OnClick">@Inner</button>
}

@code {
    [Parameter, EditorRequired] public string Label { get; set; } = string.Empty;
    [Parameter] public string? ImageUrl { get; set; }
    [Parameter] public string? Alt { get; set; }
    [Parameter] public int? Count { get; set; }
    [Parameter] public int Height { get; set; } = 52;
    [Parameter] public string? Icon { get; set; }
    [Parameter] public string? Sub { get; set; }
    [Parameter] public RenderFragment? Overlay { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }

    private RenderFragment Inner => __builder =>
    {
        <span class="kit-tile-media" style="@($"height:{Height}px;")">
            @if (ImageUrlPolicy.IsRenderable(ImageUrl))
            {
                <img class="kit-tile-img" src="@ImageUrl!.Trim()" alt="@(Alt ?? string.Empty)" style="@($"height:{Height}px;")" loading="lazy" />
            }
            else
            {
                <span class="kit-tile-fallback" aria-hidden="true"><MudIcon Icon="@(Icon ?? Icons.Material.Outlined.Image)" Size="Size.Small" /></span>
            }
            @if (Overlay is not null) { <span class="kit-tile-overlay">@Overlay</span> }
        </span>
        <span class="kit-tile-row">
            <span class="kit-tile-label">@Label</span>
            @if (Count is not null) { <span class="kit-tile-count">@Count</span> }
        </span>
        @if (Sub is not null) { <span class="kit-tile-sub">@Sub</span> }
    };
}
```
`ImageTile.razor.css`:
```css
.kit-tile {
	display: flex;
	flex-direction: column;
	gap: 4px;
	width: 100%;
	min-width: 0;
	padding: 0;
	border: 0;
	background: transparent;
	color: var(--text);
	font: inherit;
	font-size: 13px;
	text-align: left;
	text-decoration: none;
	cursor: pointer;
}
.kit-tile:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: var(--radius-tile); }
.kit-tile-media { position: relative; display: block; width: 100%; border-radius: var(--radius-tile); overflow: hidden; }
.kit-tile-img { display: block; width: 100%; object-fit: cover; }
.kit-tile-fallback {
	position: absolute;
	inset: 0;
	display: flex;
	align-items: center;
	justify-content: center;
	background: var(--surface-2);
	color: var(--text-dim);
}
.kit-tile-overlay { position: absolute; top: 6px; left: 6px; right: 6px; display: flex; gap: 6px; pointer-events: none; }
.kit-tile-row { display: flex; align-items: baseline; gap: 6px; }
.kit-tile-label { flex: 1 1 auto; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.kit-tile-count { font-size: 12px; color: var(--text-dim); }
.kit-tile-sub { font-size: 12px; color: var(--text-dim); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
```

`Pill.razor`:
```razor
@* §4.7 pill on images: 26px, glass, optional 7px status dot. *@
<span class="kit-pill @Class">
    @if (Dot is not null) { <span class="kit-pill-dot" style="@($"background:{Dot};")" aria-hidden="true"></span> }
    @ChildContent
</span>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Dot { get; set; }
    [Parameter] public string? Class { get; set; }
}
```
`Pill.razor.css`:
```css
.kit-pill {
	height: 26px;
	padding: 0 10px;
	border-radius: 13px;
	background: var(--glass-pill-bg);
	backdrop-filter: var(--glass-filter);
	-webkit-backdrop-filter: var(--glass-filter);
	box-shadow: var(--glass-edge);
	color: var(--text);
	font-size: 12px;
	display: inline-flex;
	align-items: center;
	gap: 6px;
	white-space: nowrap;
}
.kit-pill-dot { width: 7px; height: 7px; border-radius: 50%; }
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): portrait tile, image tile and pill"`

---

### Task 6: CapsuleButton and GlassBanner

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/CapsuleButton.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/GlassBanner.razor` + `.razor.css`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx` — add `KitBannerMinimise` = "Minimise banner", `KitBannerRestore` = "Show banner"
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/GlassBannerTests.cs`

**Interfaces:**
- `CapsuleButton`: `RenderFragment? ChildContent`, `string? Icon`, `bool Primary`, `bool? Pressed` (renders `aria-pressed`), `string? AriaLabel` (required when `ChildContent` is null → icon-only 36×36), `string? Href`, `EventCallback OnClick`, `string? Class`, `bool Disabled`.
- `GlassBanner`: `string Title` (EditorRequired), `string? ImageUrl`, `string? Alt`, `(double X, double Y)? Focal`, `string? Kicker`, `RenderFragment? Secondary` (the lines under the title), `RenderFragment? Actions` (top-right), `RenderFragment? Back` (top-left), `int Height` (150), `int OpenHeight` (200), `bool Open` (uses the `*-open` masks and `--scrim-open`, height `OpenHeight`), `RenderFragment? OpenContent` (rendered over the image while `Open`), `bool Minimised`, `EventCallback<bool> MinimisedChanged`, `string? Fact` (the one fact on the minimised strip), `bool Minimisable` (default true), `string TitleSize` ("play" 17px | "hero" 26px | "character" 28px).
- If `ImageUrl` is not renderable, the banner still renders with `--surface-2` in place of the image; the caller decides whether to use `PlainPageHeader` instead.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class GlassBannerTests : BunitContext
{
	public GlassBannerTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task RendersImageBlurLayersScrimAndTitle()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Lower Docks").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Alt, "The quay").Add(x => x.Kicker, "Theme"));
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("alt")).IsEqualTo("The quay");
		await Assert.That(cut.FindAll(".kit-banner-blur").Count).IsEqualTo(3);
		await Assert.That(cut.Find(".kit-banner-scrim")).IsNotNull();
		await Assert.That(cut.Find(".kit-banner-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".kit-banner-kicker").TextContent).IsEqualTo("Theme");
		await Assert.That(cut.Find(".kit-banner").GetAttribute("style")).Contains("height:150px");
	}

	[Test]
	public async Task Focal_SetsObjectPosition()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Focal, (0.5, 0.6)));
		await Assert.That(cut.Find("img.kit-banner-img").GetAttribute("style")).Contains("object-position:50% 60%");
	}

	[Test]
	public async Task Open_UsesOpenHeightAndModifier()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Open, true).Add(x => x.OpenContent, b => b.AddMarkupContent(0, "<p class=\"d\">desc</p>")));
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--open");
		await Assert.That(cut.Find(".kit-banner").GetAttribute("style")).Contains("height:200px");
		await Assert.That(cut.Find(".kit-banner-open .d").TextContent).IsEqualTo("desc");
	}

	[Test]
	public async Task Minimised_ShowsStripWithTitleFactAndRestore()
	{
		var restored = false;
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "Harbour Ward").Add(x => x.ImageUrl, "/r.jpg").Add(x => x.Fact, "5 here").Add(x => x.Minimised, true).Add(x => x.MinimisedChanged, v => restored = !v));
		await Assert.That(cut.Find(".kit-banner-strip .kit-banner-strip-title").TextContent).IsEqualTo("Harbour Ward");
		await Assert.That(cut.Find(".kit-banner-strip .kit-banner-strip-fact").TextContent).IsEqualTo("5 here");
		var restore = cut.Find(".kit-banner-strip button");
		await Assert.That(restore.GetAttribute("aria-label")).IsEqualTo("Show banner");
		await Assert.That(restore.GetAttribute("aria-expanded")).IsEqualTo("false");
		restore.Click();
		await Assert.That(restored).IsTrue();
	}

	[Test]
	public async Task MinimiseButton_HasLabelAndExpandedState()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "/r.jpg"));
		var btn = cut.Find("button.kit-banner-minimise");
		await Assert.That(btn.GetAttribute("aria-label")).IsEqualTo("Minimise banner");
		await Assert.That(btn.GetAttribute("aria-expanded")).IsEqualTo("true");
	}

	[Test]
	public async Task UnsafeImage_RendersNoImgButKeepsTitle()
	{
		var cut = Render<GlassBanner>(p => p.Add(x => x.Title, "x").Add(x => x.ImageUrl, "javascript:1"));
		await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-banner").ClassList).Contains("kit-banner--noimage");
	}

	[Test]
	public async Task CapsuleButton_IconOnlyRequiresAriaLabel_PrimaryAndPressed()
	{
		var cut = Render<CapsuleButton>(p => p.Add(x => x.Icon, MudBlazor.Icons.Material.Outlined.Notes).Add(x => x.AriaLabel, "Description").Add(x => x.Pressed, true).Add(x => x.Primary, true));
		var b = cut.Find("button.kit-capsule");
		await Assert.That(b.GetAttribute("aria-label")).IsEqualTo("Description");
		await Assert.That(b.GetAttribute("aria-pressed")).IsEqualTo("true");
		await Assert.That(b.ClassList).Contains("kit-capsule--primary");
		await Assert.That(b.ClassList).Contains("kit-capsule--icon");
	}

	[Test]
	public async Task CapsuleButton_WithHref_IsAnAnchor()
	{
		var cut = Render<CapsuleButton>(p => p.Add(x => x.Href, "/wiki").AddChildContent("Back to Wiki"));
		await Assert.That(cut.Find("a.kit-capsule").GetAttribute("href")).IsEqualTo("/wiki");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`CapsuleButton.razor`:
```razor
@* §4.3 capsule: 36px, radius 18, glass. Primary = solid accent. Icon-only = 36×36 with an aria-label. *@
@if (Href is not null)
{
    <a class="@Css" href="@Href" aria-label="@AriaLabel" aria-pressed="@PressedAttr">@Inner</a>
}
else
{
    <button type="button" class="@Css" aria-label="@AriaLabel" aria-pressed="@PressedAttr" disabled="@Disabled" @onclick="OnClick">@Inner</button>
}

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Icon { get; set; }
    [Parameter] public bool Primary { get; set; }
    [Parameter] public bool? Pressed { get; set; }
    [Parameter] public string? AriaLabel { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public bool Disabled { get; set; }

    private bool IconOnly => ChildContent is null;
    private string? PressedAttr => Pressed is null ? null : (Pressed.Value ? "true" : "false");
    private string Css => string.Join(' ', new[] { "kit-capsule", Primary ? "kit-capsule--primary" : null, IconOnly ? "kit-capsule--icon" : null, Class }.Where(c => c is not null));

    private RenderFragment Inner => __builder =>
    {
        @if (Icon is not null) { <MudIcon Icon="@Icon" Size="Size.Small" /> }
        @ChildContent
    };
}
```
`CapsuleButton.razor.css`:
```css
.kit-capsule {
	height: 36px;
	padding: 0 14px 0 12px;
	border: 0;
	border-radius: 18px;
	background: var(--glass-bg);
	backdrop-filter: var(--glass-filter);
	-webkit-backdrop-filter: var(--glass-filter);
	box-shadow: var(--glass-edge);
	color: var(--text);
	font: inherit;
	font-size: 13px;
	font-weight: 600;
	display: inline-flex;
	align-items: center;
	gap: 6px;
	box-sizing: border-box;
	white-space: nowrap;
	text-decoration: none;
	cursor: pointer;
	transition: filter var(--motion-pop);
}
.kit-capsule:hover { filter: brightness(1.15); }
.kit-capsule:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.kit-capsule:disabled { opacity: 0.5; cursor: default; }
.kit-capsule--icon { width: 36px; padding: 0; justify-content: center; }
.kit-capsule--primary { background: var(--accent); color: var(--accent-on); font-weight: 700; box-shadow: none; }
```

`GlassBanner.razor`:
```razor
@inject IStringLocalizer<SharedResource> Loc
@* §4.3 glass banner: image, three masked backdrop-blur layers, scrim, text bottom-left, capsules top-right, Back top-left.
   Minimised: a 48px strip with a left-weighted blur and a restore button. *@
@if (Minimised)
{
    <div class="kit-banner-strip">
        @if (HasImage) { <img class="kit-banner-strip-img" src="@ImageUrl!.Trim()" alt="" /> }
        <div class="kit-banner-strip-blur" aria-hidden="true"></div>
        <div class="kit-banner-strip-scrim" aria-hidden="true"></div>
        <div class="kit-banner-strip-row">
            <span class="kit-banner-strip-title">@Title</span>
            @if (Fact is not null) { <span class="kit-banner-strip-fact">@Fact</span> }
            <span class="kit-banner-spacer"></span>
            <CapsuleButton Icon="@Icons.Material.Outlined.ExpandMore" AriaLabel="@Loc["KitBannerRestore"]" OnClick="@(() => MinimisedChanged.InvokeAsync(false))" Class="kit-banner-restore" />
        </div>
    </div>
    @* aria-expanded lives on the plain element the test looks for *@
}
else
{
    <div class="kit-banner @(Open ? "kit-banner--open" : null) @(HasImage ? null : "kit-banner--noimage")" style="@($"height:{(Open ? OpenHeight : Height)}px;")">
        @if (HasImage) { <img class="kit-banner-img" src="@ImageUrl!.Trim()" alt="@(Alt ?? string.Empty)" style="@FocalStyle" /> }
        <div class="kit-banner-blur kit-banner-blur--soft" aria-hidden="true"></div>
        <div class="kit-banner-blur kit-banner-blur--mid" aria-hidden="true"></div>
        <div class="kit-banner-blur kit-banner-blur--deep" aria-hidden="true"></div>
        <div class="kit-banner-scrim" aria-hidden="true"></div>
        @if (Open && OpenContent is not null) { <div class="kit-banner-open">@OpenContent</div> }
        <div class="kit-banner-text kit-banner-text--@TitleSize">
            @if (Kicker is not null) { <div class="kit-banner-kicker">@Kicker</div> }
            <div class="kit-banner-title">@Title</div>
            @if (Secondary is not null) { <div class="kit-banner-secondary">@Secondary</div> }
        </div>
        @if (Back is not null) { <div class="kit-banner-back">@Back</div> }
        <div class="kit-banner-actions">
            @Actions
            @if (Minimisable)
            {
                <button type="button" class="kit-capsule-native kit-banner-minimise" aria-label="@Loc["KitBannerMinimise"]" aria-expanded="true" @onclick="@(() => MinimisedChanged.InvokeAsync(true))">
                    <MudIcon Icon="@Icons.Material.Outlined.ExpandLess" Size="Size.Small" />
                </button>
            }
        </div>
    </div>
}

@code {
    [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
    [Parameter] public string? ImageUrl { get; set; }
    [Parameter] public string? Alt { get; set; }
    [Parameter] public (double X, double Y)? Focal { get; set; }
    [Parameter] public string? Kicker { get; set; }
    [Parameter] public RenderFragment? Secondary { get; set; }
    [Parameter] public RenderFragment? Actions { get; set; }
    [Parameter] public RenderFragment? Back { get; set; }
    [Parameter] public int Height { get; set; } = 150;
    [Parameter] public int OpenHeight { get; set; } = 200;
    [Parameter] public bool Open { get; set; }
    [Parameter] public RenderFragment? OpenContent { get; set; }
    [Parameter] public bool Minimised { get; set; }
    [Parameter] public EventCallback<bool> MinimisedChanged { get; set; }
    [Parameter] public string? Fact { get; set; }
    [Parameter] public bool Minimisable { get; set; } = true;
    [Parameter] public string TitleSize { get; set; } = "play";

    private bool HasImage => ImageUrlPolicy.IsRenderable(ImageUrl);
    private string FocalStyle => Focal is { } f
        ? $"object-position:{Math.Round(f.X * 100)}% {Math.Round(f.Y * 100)}%;"
        : string.Empty;
}
```
The minimised strip's restore button needs `aria-expanded="false"`; `CapsuleButton` has no such parameter, so render the restore as a plain `<button type="button" class="kit-capsule-native">` too (same classes as the minimise button) rather than through `CapsuleButton`. Replace the `<CapsuleButton … Class="kit-banner-restore" />` line with:
```razor
<button type="button" class="kit-capsule-native kit-banner-restore" aria-label="@Loc["KitBannerRestore"]" aria-expanded="false" @onclick="@(() => MinimisedChanged.InvokeAsync(false))">
    <MudIcon Icon="@Icons.Material.Outlined.ExpandMore" Size="Size.Small" />
</button>
```
and drop the stray comment line.

`GlassBanner.razor.css` (`.kit-capsule-native` duplicates the icon capsule look because a scoped stylesheet cannot reach into `CapsuleButton`; keep it in sync with `CapsuleButton.razor.css`):
```css
.kit-banner {
	position: relative;
	border-radius: var(--radius-card);
	border: 1px solid var(--border);
	overflow: hidden;
	background: var(--surface-2);
	transition: height var(--motion-slide);
}
.kit-banner-img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
.kit-banner-blur { position: absolute; inset: 0; pointer-events: none; }
.kit-banner-blur--soft { backdrop-filter: blur(2px); -webkit-backdrop-filter: blur(2px); mask-image: var(--blur-mask-soft); -webkit-mask-image: var(--blur-mask-soft); }
.kit-banner-blur--mid { backdrop-filter: blur(6px); -webkit-backdrop-filter: blur(6px); mask-image: var(--blur-mask-mid); -webkit-mask-image: var(--blur-mask-mid); }
.kit-banner-blur--deep { backdrop-filter: blur(12px); -webkit-backdrop-filter: blur(12px); mask-image: var(--blur-mask-deep); -webkit-mask-image: var(--blur-mask-deep); }
.kit-banner-scrim { position: absolute; inset: 0; background: var(--scrim); pointer-events: none; }
.kit-banner--open .kit-banner-blur--soft { mask-image: var(--blur-mask-soft-open); -webkit-mask-image: var(--blur-mask-soft-open); }
.kit-banner--open .kit-banner-blur--mid { mask-image: var(--blur-mask-mid-open); -webkit-mask-image: var(--blur-mask-mid-open); }
.kit-banner--open .kit-banner-blur--deep { mask-image: var(--blur-mask-deep-open); -webkit-mask-image: var(--blur-mask-deep-open); }
.kit-banner--open .kit-banner-scrim { background: var(--scrim-open); }
.kit-banner--noimage .kit-banner-blur, .kit-banner--noimage .kit-banner-scrim { display: none; }

.kit-banner-open { position: absolute; left: 16px; right: 16px; top: 56px; bottom: 64px; overflow: auto; color: var(--text); font-size: 14px; line-height: 1.5; text-shadow: var(--on-image-shadow); }
.kit-banner-text { position: absolute; left: 16px; right: 16px; bottom: 14px; text-shadow: var(--on-image-shadow); color: var(--text); min-width: 0; }
.kit-banner-kicker { font-family: var(--font-mono); font-size: 10px; letter-spacing: 0.12em; text-transform: uppercase; color: var(--text-on-image); }
.kit-banner-title { font-weight: 700; font-size: 17px; }
.kit-banner-text--hero .kit-banner-title { font-size: 26px; }
.kit-banner-text--character .kit-banner-title { font-size: 28px; }
.kit-banner-secondary { font-size: 12px; color: var(--text-on-image); margin-top: 2px; }
.kit-banner-back { position: absolute; top: 10px; left: 10px; display: flex; gap: 6px; }
.kit-banner-actions { position: absolute; top: 10px; right: 10px; display: flex; gap: 6px; }

.kit-capsule-native {
	height: 36px;
	width: 36px;
	padding: 0;
	border: 0;
	border-radius: 18px;
	background: var(--glass-bg);
	backdrop-filter: var(--glass-filter);
	-webkit-backdrop-filter: var(--glass-filter);
	box-shadow: var(--glass-edge);
	color: var(--text);
	display: inline-flex;
	align-items: center;
	justify-content: center;
	cursor: pointer;
	transition: filter var(--motion-pop);
}
.kit-capsule-native:hover { filter: brightness(1.15); }
.kit-capsule-native:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }

.kit-banner-strip {
	position: relative;
	height: 48px;
	border-radius: var(--radius-lg);
	border: 1px solid var(--border);
	overflow: hidden;
	background: var(--surface-2);
}
.kit-banner-strip-img { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; }
.kit-banner-strip-blur { position: absolute; inset: 0; backdrop-filter: blur(6px); -webkit-backdrop-filter: blur(6px); mask-image: linear-gradient(to right, black 0%, black 30%, transparent 60%); -webkit-mask-image: linear-gradient(to right, black 0%, black 30%, transparent 60%); pointer-events: none; }
.kit-banner-strip-scrim { position: absolute; inset: 0; background: linear-gradient(to right, rgba(10, 11, 13, 0.72) 0%, rgba(10, 11, 13, 0.45) 35%, rgba(10, 11, 13, 0) 65%); pointer-events: none; }
.kit-banner-strip-row { position: absolute; inset: 0; display: flex; align-items: center; gap: 10px; padding: 0 6px 0 14px; text-shadow: var(--on-image-shadow); color: var(--text); }
.kit-banner-strip-title { font-size: 14px; font-weight: 700; }
.kit-banner-strip-fact { font-size: 12px; color: var(--text-on-image); }
.kit-banner-spacer { flex: 1 1 auto; }
```

Add to `SharedResource.resx` (neutral):
```xml
<data name="KitBannerMinimise" xml:space="preserve"><value>Minimise banner</value></data>
<data name="KitBannerRestore" xml:space="preserve"><value>Show banner</value></data>
```
Check `PortalSurfaces.cs` (`SharpMUSH.Tests.BUnit/Resources/`) for whether a `Kit` prefix must be declared as player-facing; if the prefix map is closed, add `"Kit"` beside `"Nav"`, and mirror it in `tools/i18n/extract_untranslated.py`.

- [ ] **Step 4: Run to verify pass**, plus `SharedResourceLocalizationTests`, `DeclaredLocaleCoverageTests`, `PortalSurfacesTests`, `ResponsiveConventionsTests`.
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): capsule button and glass banner with minimise"`

---

### Task 7: Mention and OocBand

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/Mention.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/OocBand.razor` + `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/MentionTests.cs`

**Interfaces:**
- `Mention`: `string Name` (EditorRequired), `string? Color` (CSS colour; null → fallback), `bool Self` (uses `var(--accent)`), `string? Href` (default `/character/{Name}` URL-escaped), `EventCallback OnClick` (when set with no Href, renders a `<button>`; used by Play for the sheet drawer), `RenderFragment? ChildContent` (display text; default Name).
- `OocBand`: `string Name`, `string? Color`, `string? Time`, `RenderFragment ChildContent`. Renders the §4.10 band with the initials tile.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class MentionTests : BunitContext
{
	[Test]
	public async Task Mention_DefaultsToTheProfileLink_AndSetsNameColour()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Tomas Reyes").Add(x => x.Color, "#ffb454"));
		var a = cut.Find("a.mention");
		await Assert.That(a.GetAttribute("href")).IsEqualTo("/character/Tomas%20Reyes");
		await Assert.That(a.GetAttribute("style")).Contains("--name:#ffb454");
		await Assert.That(a.TextContent).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task Mention_WithoutColour_SetsNoNameVariable_SoTheUnderlineFallsBackToFaint()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Wren"));
		await Assert.That(cut.Find("a.mention").GetAttribute("style") ?? "").DoesNotContain("--name");
	}

	[Test]
	public async Task Mention_Self_UsesTheAccent()
	{
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Ilsa").Add(x => x.Self, true));
		await Assert.That(cut.Find("a.mention").GetAttribute("style")).Contains("--name:var(--accent)");
	}

	[Test]
	public async Task Mention_WithOnClickAndNoHref_IsAButton()
	{
		var hit = false;
		var cut = Render<Mention>(p => p.Add(x => x.Name, "Ilsa").Add(x => x.Href, null).Add(x => x.OnClick, () => hit = true));
		cut.Find("button.mention").Click();
		await Assert.That(hit).IsTrue();
	}

	[Test]
	public async Task OocBand_RendersInitialsTileNameTimeAndText()
	{
		var cut = Render<OocBand>(p => p.Add(x => x.Name, "Wren Halloway").Add(x => x.Color, "#5aa9ff").Add(x => x.Time, "17:08").AddChildContent("brb, making tea"));
		await Assert.That(cut.Find(".kit-ooc-initials").TextContent).IsEqualTo("WH");
		await Assert.That(cut.Find(".kit-ooc-tag").TextContent).IsEqualTo("OOC");
		await Assert.That(cut.Find(".kit-ooc-name").GetAttribute("style")).Contains("#5aa9ff");
		await Assert.That(cut.Find(".kit-ooc-time").TextContent).IsEqualTo("17:08");
		await Assert.That(cut.Find(".kit-ooc-text").TextContent.Trim()).IsEqualTo("brb, making tea");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`Mention.razor`:
```razor
@* §4.8 name mention: body colour and weight, a 1px underline in the name colour at --mention-alpha. Never the accent unless it is the viewer. *@
@if (Href is not null)
{
    <a class="mention" href="@Href" style="@Style">@Text</a>
}
else
{
    <button type="button" class="mention" style="@Style" @onclick="OnClick">@Text</button>
}

@code {
    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;
    [Parameter] public string? Color { get; set; }
    [Parameter] public bool Self { get; set; }
    [Parameter] public string? Href { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private bool _hrefSet;

    public override Task SetParametersAsync(ParameterView parameters)
    {
        _hrefSet = parameters.TryGetValue<string?>(nameof(Href), out _);
        return base.SetParametersAsync(parameters);
    }

    protected override void OnParametersSet()
    {
        if (!_hrefSet) Href = "/character/" + Uri.EscapeDataString(Name);
    }

    private string? Style => Self ? "--name:var(--accent)" : Color is null ? null : $"--name:{Color}";

    private RenderFragment Text => __builder =>
    {
        @if (ChildContent is not null) { @ChildContent } else { @Name }
    };
}
```
`Mention.razor.css`:
```css
.mention {
	color: inherit;
	font: inherit;
	font-weight: inherit;
	padding: 0;
	border: 0;
	background: transparent;
	cursor: pointer;
	text-decoration: underline 1px color-mix(in srgb, var(--name, var(--text-faint)) var(--mention-alpha), transparent);
	text-underline-offset: var(--mention-offset);
}
.mention:hover { text-decoration-color: var(--name, var(--text-faint)); }
.mention:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; border-radius: 3px; }
```

`OocBand.razor`:
```razor
@* §4.10 OOC band: tinted row pulled out 11px so text stays aligned with poses; a 44px initials tile stands in for the portrait. *@
<div class="kit-ooc">
    <div class="kit-ooc-tile" aria-hidden="true">
        <span class="kit-ooc-initials">@Initials.From(Name)</span>
        <span class="kit-ooc-tag">OOC</span>
    </div>
    <div class="kit-ooc-body">
        <div class="kit-ooc-head">
            <span class="kit-ooc-name" style="@(Color is null ? null : $"color:{Color};")">@Name</span>
            @if (Time is not null) { <span class="kit-ooc-time">@Time</span> }
        </div>
        <div class="kit-ooc-text">@ChildContent</div>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public string Name { get; set; } = string.Empty;
    [Parameter] public string? Color { get; set; }
    [Parameter] public string? Time { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
}
```
`OocBand.razor.css`:
```css
.kit-ooc {
	display: flex;
	gap: 12px;
	margin: 0 -11px;
	padding: 10px 11px;
	border-radius: 12px;
	background: var(--ooc-band-bg);
	border: 1px solid var(--ooc-band-border);
}
.kit-ooc-tile {
	width: 44px;
	height: 44px;
	flex-shrink: 0;
	border-radius: var(--radius-portrait);
	background: var(--ooc-icon-bg);
	color: var(--ooc-icon-fg);
	display: flex;
	flex-direction: column;
	align-items: center;
	justify-content: center;
	gap: 1px;
}
.kit-ooc-initials { font-size: 13px; font-weight: 700; }
.kit-ooc-tag { font-family: var(--font-mono); font-size: 9px; font-weight: 700; letter-spacing: 0.08em; }
.kit-ooc-body { min-width: 0; flex: 1 1 auto; }
.kit-ooc-head { display: flex; align-items: baseline; gap: 8px; }
.kit-ooc-name { font-weight: 700; color: var(--text); }
.kit-ooc-time { font-size: 12px; color: var(--text-dim); }
.kit-ooc-text { font-size: 14px; line-height: 1.5; color: var(--text); margin-top: 2px; }
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): name mention and OOC band"`

---

### Task 8: BottomBar and TypeChips

**Files:**
- Create: `SharpMUSH.Client/Components/Kit/BottomBar.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Components/Kit/TypeChips.razor` + `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Components/Kit/BottomBarTests.cs`

**Interfaces:**
- `BottomBar`: `RenderFragment ChildContent`, `string? Class`. `--surface-2`, 1px border, radius 16, padding `10px 12px`, `position: sticky; bottom: 0`.
- `TypeChips`: `IReadOnlyList<(string Key, string Label)> Items`, `string Selected`, `EventCallback<string> SelectedChanged`, `string AriaLabel`. Renders `<div role="radiogroup">` of `<button role="radio" aria-checked>` 30px chips, selected in accent.

- [ ] **Step 1: Failing tests**

```csharp
using Bunit;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class BottomBarTests : BunitContext
{
	[Test]
	public async Task BottomBar_WrapsContent()
	{
		var cut = Render<BottomBar>(p => p.AddChildContent("<input />"));
		await Assert.That(cut.Find(".kit-bottom-bar input")).IsNotNull();
	}

	[Test]
	public async Task TypeChips_IsARadioGroup_AndSelectionChanges()
	{
		string? picked = null;
		var items = new List<(string, string)> { ("pose", "Pose"), ("say", "Say"), ("ooc", "OOC"), ("cmd", "Command") };
		var cut = Render<TypeChips>(p => p.Add(x => x.Items, items).Add(x => x.Selected, "pose").Add(x => x.AriaLabel, "Message type").Add(x => x.SelectedChanged, v => picked = v));
		await Assert.That(cut.Find("[role=radiogroup]").GetAttribute("aria-label")).IsEqualTo("Message type");
		var radios = cut.FindAll("[role=radio]");
		await Assert.That(radios.Count).IsEqualTo(4);
		await Assert.That(radios[0].GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(radios[0].ClassList).Contains("kit-chip--on");
		radios[2].Click();
		await Assert.That(picked).IsEqualTo("ooc");
	}
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement**

`BottomBar.razor`:
```razor
@* §4.11 one shape for the Play composer and the Config unsaved bar. Sticky, never fixed (scoped CSS may not use fixed). *@
<div class="kit-bottom-bar @Class">@ChildContent</div>

@code {
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Class { get; set; }
}
```
`BottomBar.razor.css`:
```css
.kit-bottom-bar {
	position: sticky;
	bottom: 0;
	background: var(--surface-2);
	border: 1px solid var(--border);
	border-radius: var(--radius-card);
	padding: 10px 12px;
	display: flex;
	flex-direction: column;
	gap: 8px;
}
```

`TypeChips.razor`:
```razor
@* Message-type chips: 30px, radius 15, the selected one in accent. A radiogroup, because only one is ever chosen. *@
<div class="kit-chips" role="radiogroup" aria-label="@AriaLabel">
    @foreach (var (key, label) in Items)
    {
        var on = key == Selected;
        <button type="button" role="radio" aria-checked="@(on ? "true" : "false")" class="kit-chip @(on ? "kit-chip--on" : null)" @onclick="@(() => SelectedChanged.InvokeAsync(key))">@label</button>
    }
</div>

@code {
    [Parameter, EditorRequired] public IReadOnlyList<(string Key, string Label)> Items { get; set; } = [];
    [Parameter] public string Selected { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> SelectedChanged { get; set; }
    [Parameter] public string AriaLabel { get; set; } = string.Empty;
}
```
`TypeChips.razor.css`:
```css
.kit-chips { display: flex; gap: 6px; flex-wrap: wrap; }
.kit-chip {
	height: 30px;
	padding: 0 12px;
	border: 0;
	border-radius: 15px;
	background: var(--border);
	color: var(--text);
	font: inherit;
	font-size: 13px;
	cursor: pointer;
	transition: background var(--motion-pop);
}
.kit-chip:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.kit-chip--on { background: var(--accent); color: var(--accent-on); font-weight: 700; }
```

- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** — `git commit -m "feat(kit): bottom bar and type chips"`

---

### Task 9: Kit preview page (staff-only)

**Files:**
- Create: `SharpMUSH.Client/Pages/Admin/KitPreview.razor` + `.razor.css`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx` — `AdmKitPreviewTitle` = "Design kit", `AdmKitPreviewDesc` = "Every D1 kit piece with sample data, for checking against the boards."
- Test: `SharpMUSH.Tests.BUnit/Pages/KitPreviewTests.cs`

**Interfaces:** Route `/admin/kit`, `[Authorize(Policy = PortalPermission.LayoutAdmin)]`. Uses `docs/design/d1/samples/*.jpg` copied to `SharpMUSH.Client/wwwroot/assets/kit-samples/` (three files: `lower-docks.jpg`, `tomas.jpg`, `harbour-row.jpg`; ~150 KB total) so the preview has real images. The page is not linked from navigation.

- [ ] **Step 1: Failing test**

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Pages.Admin;

namespace SharpMUSH.Tests.BUnit.Pages;

public class KitPreviewTests : BunitContext
{
	public KitPreviewTests()
	{
		Services.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task RendersOneOfEveryKitPiece()
	{
		var cut = Render<KitPreview>();
		foreach (var cls in new[] { ".kit-section-label", ".kit-row", ".kit-banner", ".kit-card", ".kit-portrait", ".kit-tile", ".kit-pill", ".mention", ".kit-ooc", ".kit-bottom-bar", ".kit-chips", ".kit-page-head" })
			await Assert.That(cut.FindAll(cls).Count).IsGreaterThan(0).Because(cls);
	}
}
```
(If `KitPreview` needs `AuthorizeView` services in the test, add `Services.AddAuthorizationCore()` and a `TestAuthorizationContext` as other admin page tests do — check `SharpMUSH.Tests.BUnit/Pages/` for the pattern.)

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement** — `KitPreview.razor` renders, in order and with sample data from board 20: a `PlainPageHeader` (kicker "D1 · app patterns", title from `Loc["AdmKitPreviewTitle"]`, description from `Loc["AdmKitPreviewDesc"]`); a 232px column with `SectionLabel` + four `SidebarRow`s (image/avatar/icon/channel, one current, one unread 2); a `GlassBanner` (title "Harbour Ward", kicker "Theme", image `/assets/kit-samples/harbour-row.jpg`, `Back` capsule "Back", `Actions` History + primary Edit) and a second minimised one; a `KitCard Aside Title="Here · 4"` with a 2-column grid of four `PortraitTile`s (two with `/assets/kit-samples/tomas.jpg`, one "Dace Kellan" no image, one with `Status="away"`); a `KitCard Aside Title="Exits · 3"` with `ImageTile`s including an `Overlay` `Pill` "3 there"; a paragraph with `Mention`s (one with colour, one without, one `Self`); an `OocBand`; a `BottomBar` with `TypeChips` (Pose/Say/OOC/Command) and a plain `<input>` plus a primary `CapsuleButton` "Send". All strings other than the two resx ones are sample data and may stay literal, marked with a comment `@* sample data, not copy *@`.

`KitPreview.razor.css`:
```css
.kit-preview { display: grid; grid-template-columns: 232px minmax(0, 1fr) 196px; gap: 16px; padding: var(--cpad); }
.kit-preview-side { background: var(--surface-3); border-radius: var(--radius-card); padding: 12px 8px; display: flex; flex-direction: column; gap: 2px; }
.kit-preview-main { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
.kit-preview-aside { display: flex; flex-direction: column; gap: 14px; }
.kit-preview-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px; }
.kit-preview-prose { font-size: 15px; line-height: 1.6; padding: 16px; }
@container page (max-width: 64rem) { .kit-preview { grid-template-columns: 232px minmax(0, 1fr); } .kit-preview-aside { grid-column: 1 / -1; } }
@container page (max-width: 48rem) { .kit-preview { grid-template-columns: minmax(0, 1fr); } }
```

- [ ] **Step 4: Run to verify pass**, then the whole `SharpMUSH.Tests.BUnit` project.

- [ ] **Step 5: Visual check.** Run the portal (memory: `sharpmush-portal-visual-verification`), sign in as the admin, open `/admin/kit` at 1280×800, screenshot with Playwright's Chromium, and compare with `docs/design/d1/boards/20-patterns.png`. Show the screenshot with `claude-show`. Fix structural differences in the components; note data-only differences in the PR.

- [ ] **Step 6: Commit** — `git add SharpMUSH.Client/Pages/Admin/KitPreview.razor* SharpMUSH.Client/wwwroot/assets/kit-samples SharpMUSH.Client/Resources/SharedResource.resx SharpMUSH.Tests.BUnit/Pages/KitPreviewTests.cs && git commit -m "feat(portal): staff-only /admin/kit preview of the D1 kit"`

---

### Task 10: Docs and gate

**Files:**
- Modify: `docs/design/ui-patterns.md` §13 — replace the `--sharp-*`/Catppuccin/DB-stored description with two paragraphs pointing at `wwwroot/css/tokens.css`, the D1 README §2 table, and the fact that `IThemeService` is localStorage-backed and changes only the MudTheme palette.
- Modify: `CLAUDE.md` — the `IThemeService — DB-backed MudTheme + CSS variables` line becomes `IThemeService — localStorage-backed accent preset applied to MudTheme (CSS variables are static in tokens.css)`.
- Modify: `docs/design/d1/README.md` — none.

- [ ] **Step 1: Edit the two docs as above.**
- [ ] **Step 2: Format gate** — `dotnet format whitespace --folder SharpMUSH.Client --exclude "**/bin/**" --exclude "**/obj/**"` (twice), same for `SharpMUSH.Tests.BUnit`, `SharpMUSH.Tests`, `SharpMUSH.Contracts`.
- [ ] **Step 3: Full gate** — `dotnet run --project SharpMUSH.Tests.BUnit` and `dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ThemeServiceTests/*"`; both green.
- [ ] **Step 4: Commit** — `git commit -m "docs: describe the theme tokens as they are"` and push `git push -u origin worktree-d1-phase-0-tokens-kit`.
