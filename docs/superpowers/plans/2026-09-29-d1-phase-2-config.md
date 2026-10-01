# D1 Phase 2 — Server Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restyle `/admin/config` and `/admin/config/{category}` to the D1 boards 27–29 using the Phase 0 kit: a settings-tree sidebar, card grid home, kit rows and controls per section, the unsaved bottom bar, and an "In this section" aside; fix the Content count bug.

**Architecture:** A new `ConfigSidebar` component replaces `ConfigNavDrawer` and is hosted by `ConfigLayout` (Phase 1 will later move it into the shell's page-sidebar slot; the component is written so that move is a one-line change). `ConfigIndex` and `DynamicConfig` keep their data flow (`ConfigSchemaService`, `AdminConfigService`, `ConfigValues`) and swap markup and CSS for kit pieces. The four custom config pages (Sitelock, BannedNames, Restrictions, ImportConfig) take `PlainPageHeader` and drop their duplicated header CSS.

**Tech Stack:** Blazor WASM, MudBlazor 9 (icons), the Phase 0 kit (`SharpMUSH.Client/Components/Kit`), bUnit + TUnit.

**Spec:** `docs/design/d1/README.md` §3, §4, §6.4, §9; boards `27-config-home.png`, `28-config-section-unsaved.png`, `29-config-section-saved.png`; board sources `boards-src/App-Config.dc.html`, `App-Config-Chat.dc.html`.

## Global Constraints

- The shell owns `@media`; page CSS uses `@container page (max-width: 48rem|64rem)` in roomy → medium → narrow order; no `!important`; no `position: fixed` in scoped CSS; `::deep` only anchored on an element the file declares. `ResponsiveConventionsTests` gates all of it.
- All copy goes through `IStringLocalizer<SharedResource>`; new keys use the `Adm` prefix (staff surface, neutral resx only) unless player-facing; existing hard-coded strings touched move into resx.
- Keep every existing authorization policy (`config.admin` on all config routes; the aside's admin links carry their own policies) and every route, including the `/admin/config/sitelockrules` alias.
- Kit tokens only: `--warn`, `--warn-tint` for the changed badge and unsaved bar; no hard-coded accent.
- README §6.4 bug: `ConfigIndex.GroupCategories["Content"]` gains `"Wiki"`.
- Q3 (collapse memory) is unanswered: the sidebar exposes a `Collapsed` parameter and the collapse button lands with the shell slot in Phase 1. Ruling recorded in the ledger.
- The board's "Related" aside card has no data source (no cross-references in the schema) and is dropped; "In this section" is built from the schema's groups.

## Review Focus

1. A category whose schema has no groups (flat) still renders its rows in one card and an empty "In this section" card is not shown (Task 4).
2. A property whose `Pattern` is `^.$` renders the narrow centred text input; every other text property renders full width (Task 4).
3. Keyboard users can reach every config card on the home page (they are links, not click-handlers on divs) and every sidebar row (Task 3, Task 1).
4. With the schema unavailable, the home page still lists the six groups and says why counts are missing; the sidebar still lists every section (Task 1, Task 3).
5. The current section's group is expanded in the sidebar on first render and after navigating to another section (Task 1).

---

### Task 1: ConfigSidebar replaces ConfigNavDrawer

**Files:**
- Modify: `SharpMUSH.Client/_Imports.razor` (add `@using SharpMUSH.Client.Components.Kit`)
- Create: `SharpMUSH.Client/Components/Admin/ConfigSidebar.razor` + `.razor.css`
- Create: `SharpMUSH.Client/Models/Configuration/ConfigSections.cs` (the static group/section table shared by the sidebar and the home cards)
- Delete: `SharpMUSH.Client/Components/ConfigNavDrawer.razor`, `.razor.css`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx` (keys below)
- Test: `SharpMUSH.Tests.BUnit/Components/ConfigSidebarTests.cs` (new); move `ConfigExport_SaysWhyItFailed` from `SharpMUSH.Tests.BUnit/Pages/AdminImportRefusalTests.cs` to render `ConfigSidebar`.

**Interfaces:**
- Produces `ConfigSections` (static): `record ConfigGroup(string Key, string TitleKey, string DescKey, string Icon, bool Important, IReadOnlyList<ConfigSection> Sections)`, `record ConfigSection(string Route, string SchemaCategory, string Icon, string TitleKey)`, `static IReadOnlyList<ConfigGroup> Groups`, `static ConfigGroup? GroupForRoute(string path)`, `static ConfigSection? SectionForRoute(string path)`. Groups (from `ConfigIndex.Categories` + `ConfigNavDrawer` today): Server (Net→`/admin/config/net`, Database), Performance (Limit, Command), Security (SitelockRules→`/admin/config/sitelock`, BannedNames, Restriction→`/admin/config/restrictions`), Content (Message, Cosmetic, Chat, **Wiki**), Logs (Log, File, TextFile, Dump), Advanced (Attribute, Flag, Cost, Compatibility, Alias, Debug, Function, Warning). Title keys are the existing resx keys used by `ConfigNavDrawer` and `ConfigIndex` today.
- Produces `ConfigSidebar` component: `[Parameter] bool Collapsed`, `[Parameter] IReadOnlyDictionary<string,int>? SectionCounts` (schema category → count; null = counts unknown). Renders: header (`.config-side-head`: title `Loc["Configuration"]` 18px/700 and sub-line `Loc["ResConfigSectionKicker"]`), `SectionLabel` "Settings", one `<details class="config-side-group">` per group with a `SidebarRow`-styled summary (icon tile, label, chevron; `open` when the group contains the current route) and its sections as indented `SidebarRow`s (`Lead=Icon`, `Icon=section icon`, `Count=SectionCounts[category]`, `IsCurrent` from `NavigationManager.Uri`), then a spacer, `SectionLabel` "Maintenance", Import (`SidebarRow Href="/admin/config/import"`) and Export (`SidebarRow OnClick=ExportAsync`, moved verbatim from `ConfigNavDrawer`). `Collapsed` renders only the group icon tiles (each a `SidebarRow Collapsed=true` linking to the group's first section) and the two maintenance icons.
- Resx (neutral only): `AdmConfigSettingsGroup` = "Settings" — no: reuse `Loc["Settings"]`; `AdmConfigMaintenance` — reuse `Loc["Maintenance"]`. New: `AdmConfigGroupToggle` = "Show or hide {0}" (aria-label on the summary).

- [ ] **Step 1: Failing tests**

```csharp
// SharpMUSH.Tests.BUnit/Components/ConfigSidebarTests.cs
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Models.Configuration;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

public class ConfigSidebarTests : TrackingBunitContext
{
	private BunitNavigationManager _nav = null!;

	public ConfigSidebarTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(new HttpClientHandler()) { BaseAddress = new Uri("https://localhost:8081/") }));
		Services.AddMudServices().AddSingleton(factory)
			.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
			.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
			.AddSingleton<AdminConfigService>()
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_nav = Services.GetRequiredService<BunitNavigationManager>();
	}

	[Test]
	public async Task ListsEveryGroupAndSection()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>();
		await Assert.That(cut.FindAll("details.config-side-group").Count).IsEqualTo(6);
		await Assert.That(cut.FindAll("details.config-side-group a.kit-row").Count).IsEqualTo(ConfigSections.Groups.Sum(g => g.Sections.Count));
		await Assert.That(cut.Find("a.kit-row[href='/admin/config/wiki']")).IsNotNull();
	}

	[Test]
	public async Task TheCurrentSectionsGroupIsOpen_AndTheRowIsCurrent()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>();
		var content = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/chat']") is not null);
		await Assert.That(content.HasAttribute("open")).IsTrue();
		await Assert.That(cut.Find("a[href='/admin/config/chat']").GetAttribute("aria-current")).IsEqualTo("page");
		var server = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/net']") is not null);
		await Assert.That(server.HasAttribute("open")).IsFalse();
	}

	[Test]
	public async Task NavigatingToAnotherSection_OpensItsGroup()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>();
		_nav.NavigateTo("/admin/config/net");
		cut.WaitForAssertion(() =>
		{
			var server = cut.FindAll("details.config-side-group").Single(d => d.QuerySelector("a[href='/admin/config/net']") is not null);
			Assert.That(server.HasAttribute("open")).IsTrue().GetAwaiter().GetResult();
		});
	}

	[Test]
	public async Task SectionCounts_RenderAsDimCounts()
	{
		_nav.NavigateTo("/admin/config/chat");
		var cut = Render<ConfigSidebar>(p => p.Add(x => x.SectionCounts, new Dictionary<string, int> { ["Chat"] = 7 }));
		var chat = cut.Find("a[href='/admin/config/chat']");
		await Assert.That(chat.QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("7");
	}

	[Test]
	public async Task Collapsed_ShowsOnlyGroupAndMaintenanceIcons()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>(p => p.Add(x => x.Collapsed, true));
		await Assert.That(cut.FindAll("details").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-row--collapsed").Count).IsEqualTo(6 + 2);
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
	}

	[Test]
	public async Task MaintenanceIsPinnedAtTheBottom_WithImportAndExport()
	{
		_nav.NavigateTo("/admin/config");
		var cut = Render<ConfigSidebar>();
		await Assert.That(cut.Find(".config-side-maint a[href='/admin/config/import']")).IsNotNull();
		await Assert.That(cut.Find(".config-side-maint button.kit-row").TextContent).Contains("Export");
	}
}
```
And in `AdminImportRefusalTests.ConfigExport_SaysWhyItFailed`, replace `Render<ConfigNavDrawer>()` with `Render<ConfigSidebar>()` and `cut.FindAll("button").First(b => b.TextContent.Contains("Export"))` stays.

- [ ] **Step 2: Run** `--treenode-filter "/*/*/ConfigSidebarTests/*"` → build error (types missing).

- [ ] **Step 3: Implement**

`_Imports.razor`: append `@using SharpMUSH.Client.Components.Kit`.

`SharpMUSH.Client/Models/Configuration/ConfigSections.cs`:
```csharp
using MudBlazor;

namespace SharpMUSH.Client.Models.Configuration;

/// <summary>One section of the configuration: a route, the schema category it edits, and its icon and title key.</summary>
public sealed record ConfigSection(string Route, string SchemaCategory, string Icon, string TitleKey);

/// <summary>A group of sections as the sidebar and the home cards present them.</summary>
public sealed record ConfigGroup(string Key, string TitleKey, string DescKey, string Icon, bool Important, IReadOnlyList<ConfigSection> Sections)
{
	public string FirstRoute => Sections[0].Route;
}

/// <summary>
/// The configuration's group and section table, shared by the sidebar tree and the home cards so the
/// two can never disagree (the Content card used to count 45 settings while the nav listed Wiki).
/// </summary>
public static class ConfigSections
{
	public static readonly IReadOnlyList<ConfigGroup> Groups =
	[
		new("Server", "Server", "ServerCategoryDescription", Icons.Material.Outlined.Dns, false,
		[
			new("/admin/config/net", "Net", Icons.Material.Outlined.NetworkCheck, "Network"),
			new("/admin/config/database", "Database", Icons.Material.Outlined.Storage, "Database"),
		]),
		new("Performance", "Performance", "PerformanceCategoryDescription", Icons.Material.Outlined.Speed, false,
		[
			new("/admin/config/limit", "Limit", Icons.Material.Outlined.Timer, "Limits"),
			new("/admin/config/command", "Command", Icons.Material.Outlined.Terminal, "Commands"),
		]),
		new("Security", "Security", "SecurityCategoryDescription", Icons.Material.Outlined.Shield, true,
		[
			new("/admin/config/sitelock", "SitelockRules", Icons.Material.Outlined.Lock, "Sitelock"),
			new("/admin/config/bannednames", "BannedNames", Icons.Material.Outlined.Block, "BannedNames"),
			new("/admin/config/restrictions", "Restriction", Icons.Material.Outlined.GppBad, "Restrictions"),
		]),
		new("Content", "Content", "ContentCategoryDescription", Icons.Material.Outlined.Article, false,
		[
			new("/admin/config/message", "Message", Icons.Material.Outlined.Message, "Messages"),
			new("/admin/config/cosmetic", "Cosmetic", Icons.Material.Outlined.Palette, "Cosmetic"),
			new("/admin/config/chat", "Chat", Icons.Material.Outlined.Chat, "Chat"),
			new("/admin/config/wiki", "Wiki", Icons.Material.Outlined.MenuBook, "Wiki"),
		]),
		new("Logs", "LogsAndFiles", "LogsCategoryDescription", Icons.Material.Outlined.FolderOpen, false,
		[
			new("/admin/config/log", "Log", Icons.Material.Outlined.Description, "Logging"),
			new("/admin/config/file", "File", Icons.Material.Outlined.Folder, "Files"),
			new("/admin/config/textfile", "TextFile", Icons.Material.Outlined.TextSnippet, "TextFiles"),
			new("/admin/config/dump", "Dump", Icons.Material.Outlined.Save, "DatabaseDumps"),
		]),
		new("Advanced", "Advanced", "AdvancedCategoryDescription", Icons.Material.Outlined.Tune, false,
		[
			new("/admin/config/attribute", "Attribute", Icons.Material.Outlined.Label, "Attributes"),
			new("/admin/config/flag", "Flag", Icons.Material.Outlined.Flag, "Flags"),
			new("/admin/config/cost", "Cost", Icons.Material.Outlined.MonetizationOn, "Costs"),
			new("/admin/config/compatibility", "Compatibility", Icons.Material.Outlined.Verified, "Compatibility"),
			new("/admin/config/alias", "Alias", Icons.Material.Outlined.Link, "Aliases"),
			new("/admin/config/debug", "Debug", Icons.Material.Outlined.BugReport, "Debug"),
			new("/admin/config/function", "Function", Icons.Material.Outlined.Functions, "Functions"),
			new("/admin/config/warning", "Warning", Icons.Material.Outlined.Warning, "Warnings"),
		]),
	];

	/// <summary>The section whose route the path is (or starts with, for the sitelockrules alias); null on the home page.</summary>
	public static ConfigSection? SectionForPath(string path)
	{
		var p = path.TrimEnd('/');
		if (p.Equals("/admin/config/sitelockrules", StringComparison.OrdinalIgnoreCase)) p = "/admin/config/sitelock";
		return Groups.SelectMany(g => g.Sections).FirstOrDefault(s => s.Route.Equals(p, StringComparison.OrdinalIgnoreCase));
	}

	public static ConfigGroup? GroupForPath(string path)
	{
		var section = SectionForPath(path);
		return section is null ? null : Groups.First(g => g.Sections.Contains(section));
	}
}
```

`ConfigSidebar.razor` (Components/Admin):
```razor
@using SharpMUSH.Client.Models.Configuration
@inject IStringLocalizer<SharedResource> Loc
@inject NavigationManager Nav
@inject AdminConfigService AdminConfigService
@inject ISnackbar Snackbar
@inject IJSRuntime JS
@implements IDisposable

@* D1 §6.4 page sidebar for /admin/config: a tree of the six groups, the open group's sections as
   indented rows with setting counts, and Maintenance pinned to the bottom. Hosted by ConfigLayout
   until the shell's page-sidebar slot exists (Phase 1); Collapsed renders the group icons only. *@
<div class="config-side @(Collapsed ? "config-side--collapsed" : null)">
    @if (!Collapsed)
    {
        <div class="config-side-head">
            <div class="config-side-title">@Loc["Configuration"]</div>
            <div class="config-side-sub">@Loc["ResConfigSectionKicker"]</div>
        </div>
        <SectionLabel>@Loc["Settings"]</SectionLabel>
    }
    <nav class="config-side-tree" aria-label="@Loc["Settings"]">
        @foreach (var group in ConfigSections.Groups)
        {
            var g = group;
            @if (Collapsed)
            {
                <SidebarRow Lead="SidebarRow.SidebarLead.Icon" Icon="@g.Icon" Label="@Loc[g.TitleKey]" Href="@g.FirstRoute" IsCurrent="@(g == _currentGroup)" Collapsed="true" />
            }
            else
            {
                <details class="config-side-group" open="@(_open.Contains(g.Key))" @ontoggle="@(() => Toggle(g.Key))">
                    <summary class="config-side-summary kit-row kit-row--icon" aria-label="@string.Format(Loc["AdmConfigGroupToggle"], Loc[g.TitleKey])">
                        <span class="kit-row-lead" aria-hidden="true"><span class="kit-row-icon"><MudIcon Icon="@g.Icon" Size="Size.Small" /></span></span>
                        <span class="kit-row-label">@Loc[g.TitleKey]</span>
                        <MudIcon Class="config-side-chevron" Icon="@Icons.Material.Outlined.ChevronRight" Size="Size.Small" />
                    </summary>
                    <div class="config-side-sections">
                        @foreach (var section in g.Sections)
                        {
                            var s = section;
                            <SidebarRow Lead="SidebarRow.SidebarLead.Icon" Icon="@s.Icon" Label="@Loc[s.TitleKey]" Href="@s.Route" IsCurrent="@(s == _currentSection)" Count="@CountFor(s)" />
                        }
                    </div>
                </details>
            }
        }
    </nav>
    <div class="config-side-spacer"></div>
    <div class="config-side-maint">
        @if (!Collapsed)
        {
            <SectionLabel>@Loc["Maintenance"]</SectionLabel>
        }
        <SidebarRow Lead="SidebarRow.SidebarLead.Icon" Icon="@Icons.Material.Outlined.Upload" Label="@Loc["Import"]" Href="/admin/config/import" IsCurrent="@_onImport" Collapsed="@Collapsed" />
        <SidebarRow Lead="SidebarRow.SidebarLead.Icon" Icon="@Icons.Material.Outlined.Download" Label="@Loc["Export"]" OnClick="ExportAsync" Collapsed="@Collapsed" />
    </div>
</div>

@code {
    [Parameter] public bool Collapsed { get; set; }

    /// <summary>Settings per schema category; null while the schema is unknown, so no counts show.</summary>
    [Parameter] public IReadOnlyDictionary<string, int>? SectionCounts { get; set; }

    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private ConfigSection? _currentSection;
    private ConfigGroup? _currentGroup;
    private bool _onImport;

    protected override void OnInitialized()
    {
        Nav.LocationChanged += OnLocationChanged;
        Locate();
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        Locate();
        StateHasChanged();
    }

    private void Locate()
    {
        var path = Nav.ToAbsoluteUri(Nav.Uri).AbsolutePath;
        _currentSection = ConfigSections.SectionForPath(path);
        _currentGroup = ConfigSections.GroupForPath(path);
        _onImport = path.Equals("/admin/config/import", StringComparison.OrdinalIgnoreCase);
        if (_currentGroup is not null) _open.Add(_currentGroup.Key);
    }

    private void Toggle(string key)
    {
        if (!_open.Remove(key)) _open.Add(key);
    }

    private int? CountFor(ConfigSection s) =>
        SectionCounts is not null && SectionCounts.TryGetValue(s.SchemaCategory, out var n) ? n : null;

    private async Task ExportAsync()
    {
        var export = await AdminConfigService.ExportConfigAsync();
        var (json, error) = export switch
        {
            string body => (body, null),
            ApiFailure failure => ((string?)null, failure.Message)
        };
        if (json is null)
        {
            Snackbar.Add($"{Loc["ExportFailed"]}: {error}", Severity.Error);
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("SharpMUSH.Files.saveText", "sharpmush-config.json", "application/json", json);
            Snackbar.Add(Loc["ConfigurationExported"].Value, Severity.Success);
        }
        catch (JSException)
        {
            Snackbar.Add(Loc["ExportFailed"].Value, Severity.Error);
        }
    }

    public void Dispose() => Nav.LocationChanged -= OnLocationChanged;
}
```
Note on `<details open>`: Blazor renders `open="True"`/omits for bool; with `@ontoggle` the DOM state and `_open` stay aligned because Toggle flips on the event. The `summary` reuses the `kit-row*` classes for its look, so `ConfigSidebar.razor.css` must style them here (they are SidebarRow's scoped classes and do not reach a plain element in this file):
```css
.config-side { display: flex; flex-direction: column; gap: 2px; min-height: 100%; padding: 12px 8px; }
.config-side-head { padding: 4px 10px 12px; }
.config-side-title { font-size: 18px; font-weight: 700; color: var(--text); }
.config-side-sub { font-size: 12px; color: var(--text-dim); margin-top: 2px; }
.config-side-tree { display: flex; flex-direction: column; gap: 2px; }
.config-side-group { display: block; }
.config-side-summary {
	display: flex; align-items: center; gap: 8px; height: 40px; padding: 0 8px;
	border-radius: var(--radius-row); color: var(--text); font-size: 14px; cursor: pointer; list-style: none;
	transition: background var(--motion-pop);
}
.config-side-summary::-webkit-details-marker { display: none; }
.config-side-summary:hover { background: var(--surface); }
.config-side-summary:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
.config-side-summary .kit-row-lead { display: flex; align-items: center; flex-shrink: 0; }
.config-side-summary .kit-row-icon {
	width: 30px; height: 30px; border-radius: 8px; background: var(--surface-2); color: var(--text-dim);
	display: inline-flex; align-items: center; justify-content: center;
}
.config-side-summary .kit-row-label { flex: 1 1 auto; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.config-side-summary ::deep .config-side-chevron { color: var(--text-dim); transition: transform var(--motion-pop); }
.config-side-group[open] > .config-side-summary ::deep .config-side-chevron { transform: rotate(90deg); }
.config-side-sections { display: flex; flex-direction: column; gap: 2px; padding: 2px 0 6px 20px; }
.config-side-spacer { flex: 1 1 auto; min-height: 16px; }
.config-side-maint { display: flex; flex-direction: column; gap: 2px; }
.config-side--collapsed { padding: 12px 4px; align-items: stretch; }
```
Resx: `AdmConfigGroupToggle` = "Show or hide {0}".

Delete `ConfigNavDrawer.razor` and `.razor.css`; update `AdminImportRefusalTests.ConfigExport_SaysWhyItFailed` to render `ConfigSidebar` (it needs `BunitNavigationManager` at `/admin/config`, which bUnit registers by default).

- [ ] **Step 4: Run** the sidebar tests, `AdminImportRefusalTests`, `ResponsiveConventionsTests` → PASS.
- [ ] **Step 5: Commit** — `feat(config): ConfigSidebar tree replaces ConfigNavDrawer`

---

### Task 2: ConfigLayout hosts the sidebar

**Files:**
- Modify: `SharpMUSH.Client/Layout/ConfigLayout.razor`, `.razor.css`
- Test: `SharpMUSH.Tests.BUnit/Layout/ConfigLayoutTests.cs` (new)

**Interfaces:** `ConfigLayout` renders `.config-shell` (grid `var(--side-w) minmax(0,1fr)`, gap 16px), `aside.config-section-nav` (`--surface-3`, radius 16, sticky top within main) holding `<ConfigSidebar SectionCounts="@_counts" />` where `_counts` comes from `ConfigSchemaService.GetSchemaAsync()` (per-category counts; null on failure), and `.config-section-body` for `@Body`. Medium tier: single column, the nav becomes a 17.5rem scroll box as today. Narrow: same.

- [ ] **Step 1: Failing test**
```csharp
public class ConfigLayoutTests : TrackingBunitContext
{
	[Test]
	public async Task HostsTheSidebar_AndBody_AndPassesCounts()
	{
		// services as in ConfigSidebarTests plus a fake api/config/schema answering one Chat property
		AddServices(schemaJson: """{"categories":[],"properties":{"Chat.MaxChannels":{"name":"MaxChannels","category":"Chat","path":"Chat.MaxChannels"}}}""");
		var cut = Render<ConfigLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>")));
		cut.WaitForAssertion(() => cut.Find("a[href='/admin/config/chat'] .kit-row-count"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("a[href='/admin/config/chat'] .kit-row-count").TextContent).IsEqualTo("1");
		await Assert.That(cut.Find(".config-section-body #body")).IsNotNull();
	}
}
```
(Write `AddServices` like `AdminImportRefusalTests.AddServices`, answering `api/config/schema` with the given JSON. Check `ConfigSchemaService.GetSchemaAsync` for the exact route it calls and match it.)

- [ ] **Step 2: Run** → FAIL (no count; layout still renders `ConfigNavDrawer`, which no longer exists → build error first).

- [ ] **Step 3: Implement**
```razor
@inherits LayoutComponentBase
@layout MainLayout
@using SharpMUSH.Client.Components.Admin
@using SharpMUSH.Library.API
@inject ConfigSchemaService SchemaService

<div class="config-shell">
    <aside class="config-section-nav">
        <ConfigSidebar SectionCounts="@_counts" />
    </aside>
    <div class="config-section-body">
        @Body
    </div>
</div>

@code {
    private IReadOnlyDictionary<string, int>? _counts;

    protected override async Task OnInitializedAsync()
    {
        if (await SchemaService.GetSchemaAsync() is ConfigurationSchema schema)
        {
            _counts = schema.Properties.Values
                .GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
```
```css
.config-shell { display: grid; grid-template-columns: var(--side-w) minmax(0, 1fr); gap: 16px; align-items: start; padding: 0; }
.config-section-nav { min-width: 0; background: var(--surface-3); border-radius: var(--radius-card); position: sticky; top: 0; max-height: 100dvh; overflow-y: auto; overscroll-behavior: contain; }
.config-section-body { min-width: 0; padding: var(--main-pad); }
@container page (max-width: 64rem) {
	.config-shell { grid-template-columns: minmax(0, 1fr); gap: 12px; }
	.config-section-nav { position: static; max-height: 17.5rem; border: 1px solid var(--border-soft); }
}
```
- [ ] **Step 4: Run** → PASS, plus `ResponsiveConventionsTests`.
- [ ] **Step 5: Commit** — `feat(config): layout hosts ConfigSidebar with schema counts`

---

### Task 3: Configuration home (board 27)

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/Config/ConfigIndex.razor`, `.razor.css`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx`
- Test: `SharpMUSH.Tests.BUnit/Pages/ConfigIndexTests.cs` (new); keep `AdminImportRefusalTests.ConfigHome_SaysWhyItHasNoCounts` green.

**Interfaces:** Page markup: `.config-home` grid `minmax(0,1fr) var(--aside-w)`, gap 14; header `PlainPageHeader Kicker=Loc["ResConfigHomeKicker"] Title=Loc["ServerConfiguration"] Description=Loc["ManageAllSettings"]`; `.config-cat-grid` 2 columns of `<a class="config-cat-card" href=group.FirstRoute>` each with `.config-cat-icon` (accent icon tile 40px), `.config-cat-title` (15px/700) + optional `Pill`-like `.config-cat-flag` "Important" in `--warn`/`--warn-tint`, `.config-cat-count` (`Loc.Plural("AdmConfigSettingsCount","count",n)`; hidden when counts unknown), `.config-cat-desc`, `.config-cat-chips` (section names as chips, 4 shown then `+N more`). Aside: `KitCard Aside Title=Loc["AdmConfigMoreTools"]` with `SidebarRow`s gated by `AuthorizeView` policies: Applications (`/admin/applications`, `applications.admin`), Layouts (`/admin/layout`, `layout.admin`), Packages (`/admin/packages`, `packages.admin`), Roles (`/admin/roles`, `roles.admin`), Wiki & media (`/admin/wiki`, `wiki.admin`); `KitCard Aside Title=Loc["AdmConfigHowChangesApply"]` with `Loc["AdmConfigHowChangesApplyBody"]`. Counts come from `ConfigSections` group → sum of category counts (fixes Content = 46).

Resx (neutral): `AdmConfigSettingsCount` = `{count, plural, one {# setting} other {# settings}}`, `AdmConfigMoreTools` = "More admin tools", `AdmConfigHowChangesApply` = "How changes apply", `AdmConfigHowChangesApplyBody` = "Edits stay local until you save a section. Export downloads the whole configuration as sharpmush-config.json; Import restores one.", `AdmConfigMoreSections` = "+{0} more", `AdmConfigToolApplications` = "Applications", `AdmConfigToolLayouts` = "Layouts", `AdmConfigToolPackages` = "Packages", `AdmConfigToolRoles` = "Roles", `AdmConfigToolWiki` = "Wiki & media".

- [ ] **Step 1: Failing tests**
```csharp
public class ConfigIndexTests : TrackingBunitContext
{
	// AddServices as AdminImportRefusalTests, with api/config/schema answering properties across categories:
	// Message×20, Cosmetic×17, Chat×7, Wiki×2, Net×1 (build the JSON with a loop)
	[Test] public async Task CardsAreLinks_ToTheGroupsFirstSection() { … Assert cut.Find("a.config-cat-card[href='/admin/config/message']") … }
	[Test] public async Task ContentCountsIncludeWiki() { … WaitForAssertion; the Content card's .config-cat-count text == "46 settings" … }
	[Test] public async Task SecurityCarriesTheImportantTag() { … .config-cat-card[href='/admin/config/sitelock'] .config-cat-flag text "Important" … }
	[Test] public async Task AdvancedShowsFourChipsThenMore() { … chips count 4 and ".config-cat-more" text "+4 more" … }
	[Test] public async Task AsideListsOnlyAuthorisedTools() { … this.AddTestAuthorization().SetPolicies("layout.admin"); render; only Layouts row present … }
}
```
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the markup and CSS described above (cards: `--surface`, 1px `--border`, radius `--radius-card`, padding 16; `.config-cat-icon` 40px `rgba(var(--glow), .10)` background, accent icon; chips 26px radius 13 `--surface-2` 12px; grid `repeat(2, minmax(0,1fr))`, one column below 48rem; the aside stacks under main below 64rem). Delete the `onclick` div. Use `ConfigSections.Groups` for everything; delete `Categories`/`GroupCategories` from the page.
- [ ] **Step 4: Run** → PASS, plus `AdminImportRefusalTests`, `ResponsiveConventionsTests`, `SharedResourceLocalizationTests`.
- [ ] **Step 5: Commit** — `feat(config): D1 home with linked group cards and admin aside; Content counts Wiki`

---

### Task 4: Section page (boards 28–29)

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor`, `.razor.css`
- Modify: `SharpMUSH.Client/Resources/SharedResource.resx`
- Test: `SharpMUSH.Tests.BUnit/Pages/DynamicConfigPageTests.cs` (new)

**Interfaces:** Page: `.config-section` grid `minmax(0,1fr) var(--aside-w)`; `PlainPageHeader Kicker=Loc["ResConfigSectionKicker"] Title=CategoryTitle Description=_categoryMetadata.Description` with `Actions` = `CapsuleButton Icon=RestartAlt OnClick=ResetToDefaults` "Reset to defaults" (text, glass). Per group: `SectionLabel` with `id="group-{slug}"` then `KitCard` (no header) whose body is the rows. Row `.cfg-row`: left `.cfg-row-text` = `.cfg-row-name` (14px/600) + `<code class="cfg-row-key">` faint mono + `.cfg-row-changed` warn badge (`Loc["ResChangedBadge"]`) when changed, `.cfg-row-help` 13px dim, `.cfg-row-error`; right `.cfg-row-control`: switch = `<button role="switch" aria-checked class="cfg-switch">` 44×24; numeric = `.cfg-range` faint mono `min–max` + `<input class="cfg-num">` 112px mono right-aligned; text = `.cfg-text` full width, or `.cfg-text--char` (56px centred) when `prop.Pattern == "^.$"`; select = pills (existing markup, restyled); stringlist/dictionary = existing markup, restyled with kit chips. Unsaved bar: `BottomBar Class="cfg-unsaved"` containing warn icon, `<strong>Unsaved changes</strong> in this section`, a `.cfg-count` warn-tint chip (`Loc.Plural("ResChangeCount","count",_changedCount)`), `CapsuleButton` Reset changes, `CapsuleButton Primary` Save configuration (spinner text while saving). Aside: `KitCard Aside Title=Loc["AdmConfigInThisSection"]` with one `<a class="cfg-toc" href="#group-{slug}">` per non-empty group showing name and count, current (hash match) in accent with a 2px left rule; the card is omitted when the category has no groups.

Resx: `AdmConfigInThisSection` = "In this section"; `AdmConfigResetToDefaultsShort` — reuse `Loc["ResetToDefaults"]`.

- [ ] **Step 1: Failing tests** (fake `api/config/schema` with a Chat category having groups General/Limits, properties MaxChannels (numeric, min 1 max 10000, group Limits, order 2), ChatTokenAlias (text, pattern `^.$`, group General, order 1), UseMuxComm (switch, group General, order 2), NoisyCemit (switch, group Behavior); and `api/config` returning `{"configuration":{"chat":{"maxChannels":200,"chatTokenAlias":"+","useMuxComm":false,"noisyCemit":false}},"metadata":{}}` — check `ConfigAccessor.GetValue` for the exact property path it reads and shape the JSON to it):
```csharp
[Test] RendersGroupsAsSectionLabelsWithCards — 3 .kit-section-label, 3 .kit-card
[Test] SingleCharPattern_RendersTheNarrowInput — input.cfg-text--char for ChatTokenAlias; input.cfg-num for MaxChannels with ".cfg-range" text "1–10000"
[Test] Switch_IsARoleSwitch_AndTogglesToChanged — button[role=switch] aria-checked false → click → aria-checked true and .cfg-row-changed present and .cfg-unsaved present with "1 change"
[Test] TocListsGroupsWithCounts — .cfg-toc count 3, first href "#group-general", count text "2"
[Test] FlatCategory_HasNoToc — schema with no groups → 1 .kit-card, 0 .cfg-toc
[Test] UnsavedBar_ResetChangesClearsIt
```
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** the markup and CSS. Keep every `@code` method; only `RenderFieldRow`, the header, the groups loop, the save bar and the aside change. CSS values from board 28: row padding `12px 16px`, rows separated by 1px `--border-soft`; switch 44×24 radius 12, on = `--accent` track + `--accent-on` knob, off = `--border` track + `#c3cad0` knob (the knob carries the non-text contrast: name a token `--switch-knob-off: #c3cad0` in `tokens.css` and pin it in `DesignTokensTests`); `.cfg-num` 112px height 36 `--surface-3` 1px `--border` radius 10 mono right; `.cfg-range` faint mono 12px margin-right 10; changed badge `--warn` on `--warn-tint`, 10px mono uppercase radius 99; unsaved bar layout row: icon, text (flex 1), chip, two capsules; below 48rem the row wraps and the control stacks under the text. Slugs: `group-` + lower-case name with non-alphanumerics → `-`.
- [ ] **Step 4: Run** → PASS, plus `ResponsiveConventionsTests`, `DesignTokensTests`.
- [ ] **Step 5: Commit** — `feat(config): D1 section page with kit rows, controls, unsaved bar and TOC`

---

### Task 5: Custom config pages take the plain header

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/Sitelock.razor` + `.razor.css`, `BannedNames.razor` + `.razor.css`, `Restrictions.razor` + `.razor.css`, `ImportConfig.razor` + `.razor.css`
- Test: extend `SharpMUSH.Tests.BUnit/Pages/AdminImportRefusalTests.cs` (`ConfigImport_…` keeps passing) and add `CustomConfigPagesUseThePlainHeader` in `ConfigIndexTests` (render each page with the failing services from `AdminImportRefusalTests` and assert `.kit-page-head h1` exists and `.kit-page-kicker` text is the resx kicker, not the literal).

- [ ] **Step 1: Failing test** as described (`Render<Sitelock>()`, `Render<BannedNames>()`, `Render<Restrictions>()`, `Render<ImportConfig>()`; each needs its own service — read the page's `@inject` list and register substitutes returning `ApiFailure`).
- [ ] **Step 2: Run** → FAIL (no `.kit-page-head`).
- [ ] **Step 3: Implement:** replace each `<div class="config-section-header">…</div>` with `<PlainPageHeader Kicker="@Loc["ResConfigSectionKicker"]" Title="@…" Description="@…" />` (Sitelock's description keeps its `<code>` list: pass it through a `Description`-less header and render the `<p class="cfg-desc">` under it, or extend `PlainPageHeader` with a `DescriptionContent` RenderFragment — do the latter: add `[Parameter] RenderFragment? DescriptionContent` rendered in the same `<p class="kit-page-desc">`, with a StaticKitTests case). Delete the `.config-section-header`, `.config-kicker`, `.config-title`, `.config-desc` rules from the four stylesheets; keep `.config-field-card` etc. Remove the literal "Admin · server settings".
- [ ] **Step 4: Run** → PASS, plus `ResponsiveConventionsTests` (each page still declares a tier).
- [ ] **Step 5: Commit** — `refactor(config): custom config pages use PlainPageHeader; kicker from resx`

---

### Task 6: Visual check, gates, PR

- [ ] **Step 1:** Restart the client dev server (`dotnet run --project SharpMUSH.Client --launch-profile http`; the server from Phase 0 is still up on 8081). Screenshot `/admin/config` and `/admin/config/chat` at 1280×800 with `/tmp/d1-run/full.mjs`, toggle a switch for the unsaved state, and compare with boards 27–29. Show them with `claude-show`. Fix structural differences.
- [ ] **Step 2:** `dotnet format whitespace --folder` ×2 on Client and Tests.BUnit; `dotnet run --project SharpMUSH.Tests.BUnit` green; `node --test tools/client-tests/*.test.mjs` green; `python3 tools/i18n/validate_resx.py` passes.
- [ ] **Step 3:** Push `worktree-d1-phase-2-config`; open the PR against `main` (note it stacks on #1439) with the board differences listed.
