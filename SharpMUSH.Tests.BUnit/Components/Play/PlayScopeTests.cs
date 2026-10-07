using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §7.4 / board 13: Play is a layout scope (zone RightSidebar) whose default aside is the Here and
/// Exits widgets. The widgets read the play connection's room and act through the page's context.
/// </summary>
public class PlayScopeTests : BunitContext
{
	private readonly OobChannelStore _store = new();
	private readonly IPlayTerminalService _play = Substitute.For<IPlayTerminalService>();

	public PlayScopeTests()
	{
		Services.AddLocalization();
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_play.OobChannels.Returns(_store);
		Services.AddSingleton(_play);
	}

	[Test]
	public async Task Play_IsAScope_WithOnlyTheRightSidebar()
	{
		var scope = LayoutScopes.Find("play");
		await Assert.That(scope).IsNotNull();
		await Assert.That(scope!.Zones).IsEquivalentTo(new[] { WidgetZone.RightSidebar });
	}

	[Test]
	public async Task TheDefaultAside_IsHereThenExits()
	{
		var layout = new LayoutService(Substitute.For<IHttpClientFactory>(), NullLogger<LayoutService>.Instance).GetDefaultLayout(LayoutScopes.Play);
		await Assert.That(layout.Zones[WidgetZone.RightSidebar].OrderBy(p => p.Order).Select(p => p.WidgetName).ToList())
			.IsEquivalentTo(new[] { "Here", "Exits" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task HereAndExits_AreRegistered_ForTheRightSidebarOnly()
	{
		await Assert.That(BuiltInWidgets.Named("Here").AllowedZones).IsEquivalentTo(new[] { WidgetZone.RightSidebar });
		await Assert.That(BuiltInWidgets.Named("Exits").AllowedZones).IsEquivalentTo(new[] { WidgetZone.RightSidebar });
	}

	private void PushRoom()
	{
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#312","type":"player","name":"Tomas Reyes","cmd":"look #312","profile":true}]}""");
		_store.Set(OobEntryParser.RoomExitsPackage, """{"v":2,"exits":[{"dbref":"#1210","name":"Harbour Row","aliases":["n"],"cmd":"goto #1210"}]}""");
	}

	private IRenderedComponent<CascadingValue<PlayPageContext>> InPlay<T>(PlayPageContext context) where T : IComponent =>
		Render<CascadingValue<PlayPageContext>>(p => p.Add(x => x.Value, context).AddChildContent<T>());

	[Test]
	public async Task TheHereWidget_ShowsTheRoom_AndOpensTheSheetThroughThePage()
	{
		RoomOccupant? opened = null;
		PushRoom();
		var cut = InPlay<HereWidget>(new PlayPageContext(o => { opened = o; return Task.CompletedTask; }, _ => Task.CompletedTask));
		await cut.Find(".kit-portrait").ClickAsync();
		await Assert.That(opened?.Name).IsEqualTo("Tomas Reyes");
	}

	[Test]
	public async Task TheExitsWidget_GoesThroughThePage_AndFollowsNewRooms()
	{
		string? sent = null;
		var cut = InPlay<ExitsWidget>(new PlayPageContext(_ => Task.CompletedTask, c => { sent = c; return Task.CompletedTask; }));
		await Assert.That(cut.FindAll(".exit").Count).IsEqualTo(0);
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".exit"), TimeSpan.FromSeconds(5));
		await cut.Find(".exit button.exit-go").ClickAsync();
		await Assert.That(sent).IsEqualTo("goto #1210");
	}

	[Test]
	public async Task OutsidePlay_TheExitsWidget_TakesNoKeys()
	{
		// Placed on Home or a profile, single-letter exit shortcuts would fire on a page that is not Play.
		PushRoom();
		Render<ExitsWidget>();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sharpmushLayout.registerExitKeys")).IsFalse();
		InPlay<ExitsWidget>(new PlayPageContext(_ => Task.CompletedTask, _ => Task.CompletedTask));
		JSInterop.VerifyInvoke("sharpmushLayout.registerExitKeys");
	}

	[Test]
	public async Task OutsidePlay_AnExitStillGoes_ThroughThePlayConnection()
	{
		PushRoom();
		var cut = Render<ExitsWidget>();
		await cut.Find(".exit button.exit-go").ClickAsync();
		await _play.Received(1).SendAsync("goto #1210");
	}
}
