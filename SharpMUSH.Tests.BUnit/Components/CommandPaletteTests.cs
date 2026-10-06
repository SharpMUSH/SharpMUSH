using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// README §10 Q1's command palette: the destinations the viewer can use, filtered as they type, with
/// "Search the wiki" and "Find a character" for what was typed; arrows move, Enter goes, Escape closes.
/// </summary>
public class CommandPaletteTests : BunitContext
{
	private readonly BunitAuthorizationContext _auth;

	public CommandPaletteTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		JSInterop.Mode = JSRuntimeMode.Loose;
		_auth = AddAuthorization();
		_auth.SetAuthorized("player");
	}

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private IRenderedComponent<CommandPalette> RenderOpen(Action<bool>? onOpen = null) =>
		Render<CommandPalette>(p => p.Add(x => x.Open, true).Add(x => x.OpenChanged, o => onOpen?.Invoke(o)));

	private static List<string> Labels(IRenderedComponent<CommandPalette> cut) =>
		cut.FindAll(".kit-palette-label").Select(e => e.TextContent).ToList();

	[Test]
	public async Task Closed_RendersNothing()
		=> await Assert.That(Render<CommandPalette>().Markup.Trim()).IsEmpty();

	[Test]
	public async Task Open_IsAModalCombobox_ListingTheSections_AndOnlyTheStaffPagesTheViewerMayUse()
	{
		_auth.SetPolicies("roles.admin");
		var cut = RenderOpen();
		await Assert.That(cut.Find("[role='dialog']").GetAttribute("aria-modal")).IsEqualTo("true");
		await Assert.That(cut.Find("input[role='combobox']").GetAttribute("aria-controls")).IsEqualTo("kit-palette-list");
		var labels = Labels(cut);
		await Assert.That(labels).Contains("Wiki");
		await Assert.That(labels).Contains("Roles & permissions");
		await Assert.That(labels).DoesNotContain("Configuration").Because("config.admin was not granted");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TheSceneArchive_IsOffered_OnlyWhenTheGameHasTheSceneSystem(bool scenes)
	{
		Services.AddSingleton<ServerInfoService>(
			new StubServerInfoService(guestsEnabled: true, features: scenes ? [GameFeatures.Scenes] : []));

		await Assert.That(Labels(RenderOpen()).Contains("Scenes")).IsEqualTo(scenes);
	}

	[Test]
	public async Task Typing_Filters_AndOffersTheTwoSearches()
	{
		var cut = RenderOpen();
		cut.Find("input").Input("wi");
		var labels = Labels(cut);
		await Assert.That(labels[0]).IsEqualTo("Wiki");
		await Assert.That(labels).Contains("Search the wiki for “wi”");
		await Assert.That(labels).Contains("Find a character named “wi”");
		await Assert.That(labels).DoesNotContain("Scenes");
	}

	[Test]
	public async Task ArrowsMove_EnterGoes_AndClosing()
	{
		bool? open = null;
		var cut = RenderOpen(o => open = o);
		cut.Find("input").Input("tomas");
		cut.Find("[role='dialog']").KeyDown("ArrowDown");
		await Assert.That(cut.FindAll("[role='option']")[1].GetAttribute("aria-selected")).IsEqualTo("true");
		cut.Find("[role='dialog']").KeyDown("Enter");
		await Assert.That(Nav.Uri).EndsWith("/characters?q=tomas");
		await Assert.That(open).IsFalse();
	}

	[Test]
	public async Task AnArrowKey_NeverPreventsTheNextKeysDefault()
	{
		// preventDefault is decided when the dialog renders, so a flag set by ArrowDown would swallow
		// the next letter typed into the field.
		var cut = RenderOpen();
		cut.Find("[role='dialog']").KeyDown("ArrowDown");
		await Assert.That(cut.Find("[role='dialog']").OuterHtml.ToLowerInvariant()).DoesNotContain("preventdefault");
	}

	[Test]
	public async Task Open_TrapsFocus_AndCloseHandsItBack()
	{
		var cut = RenderOpen();
		await Assert.That(cut.FindComponents<MudBlazor.MudFocusTrap>().Count).IsEqualTo(1);
		JSInterop.VerifyInvoke("sharpmushLayout.rememberFocus");
		cut.Find("[role='dialog']").KeyDown("Escape");
		JSInterop.VerifyInvoke("sharpmushLayout.restoreFocus");
	}

	[Test]
	public async Task Escape_Closes()
	{
		bool? open = null;
		var cut = RenderOpen(o => open = o);
		cut.Find("[role='dialog']").KeyDown("Escape");
		await Assert.That(open).IsFalse();
	}
}
