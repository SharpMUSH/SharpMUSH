using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Answers <c>GET api/palette</c> with <see cref="Response"/> and records the pins written.</summary>
internal sealed class StubPaletteClient() : PaletteClient(Substitute.For<IHttpClientFactory>())
{
	public PaletteResponse? Response { get; set; }

	public List<IReadOnlyList<string>> PinWrites { get; } = [];

	public override Task<ApiResult<PaletteResponse>> GetAsync(string? inputSession = null)
		=> Task.FromResult<ApiResult<PaletteResponse>>(Response is { } response
			? response
			: new ApiFailure(ApiFailureKind.Unexpected, "no palette"));

	public override Task<ApiResult<Success>> SetPinsAsync(IReadOnlyList<string> pins)
	{
		PinWrites.Add(pins);
		return Task.FromResult<ApiResult<Success>>(new Success());
	}
}

/// <summary>Answers <c>POST api/commands</c> with one line naming the command, recording each.</summary>
internal sealed class PaletteCommandHandler : HttpMessageHandler
{
	private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

	public ConcurrentQueue<string> Commands { get; } = new();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var command = JsonSerializer.Deserialize<PortalCommandRequest>(await request.Content!.ReadAsStringAsync(ct), Web)!;
		Commands.Enqueue(command.Command);
		return new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(JsonSerializer.Serialize(new PortalCommandResponse([$"ran {command.Command}"], null, false), Web),
				Encoding.UTF8, "application/json")
		};
	}
}

/// <summary>
/// README §10 Q1's command palette: the destinations the viewer can use, filtered as they type, with
/// "Search the wiki" and "Find a character" for what was typed; arrows move, Enter goes, Escape closes.
/// </summary>
public class CommandPaletteTests : BunitContext
{
	private readonly BunitAuthorizationContext _auth;
	private readonly StubPaletteClient _palette = new();
	private readonly PaletteCommandHandler _commands = new();
	private readonly IPlayTerminalService _terminal = Substitute.For<IPlayTerminalService>();
	private readonly PaletteContext _page = new();

	public CommandPaletteTests()
	{
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_ => new HttpClient(_commands) { BaseAddress = new Uri("https://localhost:8081/") });
		Services.AddLocalization();
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton<PaletteClient>(_palette);
		Services.AddSingleton(_page);
		Services.AddSingleton(_terminal);
		Services.AddSingleton(new GameCommandService(factory));
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
		await cut.FindAll("[role='option']").Last().ClickAsync();
		labels = Labels(cut);
		await Assert.That(labels).Contains("Roles & permissions").Because("past the group's first rows, under its more row");
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
		await cut.Find("input").InputAsync("wi");
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
		await cut.Find("input").InputAsync("tomas");
		await cut.Find("[role='dialog']").KeyDownAsync("ArrowDown");
		await Assert.That(cut.FindAll("[role='option']")[1].GetAttribute("aria-selected")).IsEqualTo("true");
		await cut.Find("[role='dialog']").KeyDownAsync("Enter");
		await Assert.That(Nav.Uri).EndsWith("/characters?q=tomas");
		await Assert.That(open).IsFalse();
	}

	[Test]
	public async Task AnArrowKey_NeverPreventsTheNextKeysDefault()
	{
		// preventDefault is decided when the dialog renders, so a flag set by ArrowDown would swallow
		// the next letter typed into the field.
		var cut = RenderOpen();
		await cut.Find("[role='dialog']").KeyDownAsync("ArrowDown");
		await Assert.That(cut.Find("[role='dialog']").OuterHtml.ToLowerInvariant()).DoesNotContain("preventdefault");
	}

	[Test]
	public async Task Open_TrapsFocus_AndCloseHandsItBack()
	{
		var cut = RenderOpen();
		await Assert.That(cut.FindComponents<MudBlazor.MudFocusTrap>().Count).IsEqualTo(1);
		JSInterop.VerifyInvoke("sharpmushLayout.rememberFocus");
		await cut.Find("[role='dialog']").KeyDownAsync("Escape");
		JSInterop.VerifyInvoke("sharpmushLayout.restoreFocus");
	}

	[Test]
	public async Task Escape_Closes()
	{
		bool? open = null;
		var cut = RenderOpen(o => open = o);
		await cut.Find("[role='dialog']").KeyDownAsync("Escape");
		await Assert.That(open).IsFalse();
	}

	private static PaletteEntryView Entry(string name, string label, string line, PaletteAction action = PaletteAction.Run,
		bool always = true, string system = "scene")
		=> new($"#10:1/{name}", label, line, action, PaletteScope.Room, system, "Scene Logger", Always: always);

	private static PaletteResponse Response(IReadOnlyList<PaletteEntryView> entries, IReadOnlyList<string>? pins = null,
		IReadOnlyList<PaletteContextView>? stack = null)
		=> new(entries, null, ["PAGE"], pins ?? [], stack ?? [], 0, []);

	private static List<string> Groups(IRenderedComponent<CommandPalette> cut) =>
		cut.FindAll(".kit-palette-group").Select(e => e.TextContent).ToList();

	private static async Task ChooseAsync(IRenderedComponent<CommandPalette> cut, string label)
		=> await cut.FindAll("[role='option']").Single(e => e.QuerySelector(".kit-palette-label")?.TextContent == label).ClickAsync();

	[Test]
	public async Task TheServersEntries_JoinTheGroups_WhenTheyArrive()
	{
		_palette.Response = Response(
			[Entry("WHO", "Who is on", "+who", system: "who-where"), Entry("POSE", "Pose", "+scene/pose <text>", always: false)],
			stack: [new PaletteContextView("scene", new Dictionary<string, string>(), 1, "Scene 12 in the Tavern")]);

		var cut = RenderOpen();

		cut.WaitForState(() => Groups(cut).Contains("Always"));
		await Assert.That(Groups(cut)).IsEquivalentTo(["Here", "Always", "Go to"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.Find(".kit-palette-info").TextContent).IsEqualTo("Scene 12 in the Tavern");
	}

	[Test]
	public async Task Reopening_DropsTheLastOpeningsRows_BeforeTheServerAnswersAgain()
	{
		_palette.Response = Response([Entry("WHO", "Who is on", "+who", system: "who-where")]);
		var cut = RenderOpen();
		cut.WaitForState(() => Labels(cut).Contains("Who is on"));

		// The account switched character between openings, and this time the server does not answer.
		_palette.Response = null;
		cut.Render(p => p.Add(x => x.Open, false));
		cut.Render(p => p.Add(x => x.Open, true));

		await Assert.That(Labels(cut)).DoesNotContain("Who is on");
		await Assert.That(Groups(cut)).IsEquivalentTo(["Go to"]);
	}

	[Test]
	public async Task WithoutTheServersAnswer_ThePagesStillShow()
	{
		var cut = RenderOpen();

		await Assert.That(Groups(cut)).IsEquivalentTo(["Go to"]);
		await Assert.That(Labels(cut)).Contains("Wiki");
	}

	[Test]
	public async Task ARunEntry_WithoutTheTerminal_RunsThroughTheServer_AndShowsWhatItAnswered()
	{
		_palette.Response = Response([Entry("WHO", "Who is on", "+who")]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Who is on");

		cut.WaitForState(() => cut.FindAll(".kit-palette-output").Any(e => e.TextContent.Contains("ran +who")));
		await Assert.That(_commands.Commands).IsEquivalentTo(["+who"]);
	}

	[Test]
	public async Task ARunEntry_WithTheTerminalConnected_IsSentThroughIt()
	{
		_terminal.IsConnected.Returns(true);
		_palette.Response = Response([Entry("WHO", "Who is on", "+who")]);
		bool? open = null;
		var cut = RenderOpen(o => open = o);
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Who is on");

		await _terminal.Received(1).SendAsync("+who");
		await Assert.That(_commands.Commands).IsEmpty();
		await Assert.That(open).IsFalse();
	}

	[Test]
	public async Task AnEntryWithSlots_AsksForThem_ThenRunsTheFilledLine()
	{
		_palette.Response = Response([Entry("PAGE", "Page someone", "page <who>=<message>")]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Page someone");
		var slots = cut.FindAll(".kit-palette-slot");
		await Assert.That(slots.Select(e => e.GetAttribute("aria-label"))).IsEquivalentTo(["who", "message"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await slots[0].InputAsync("Tomas");
		await cut.FindAll(".kit-palette-slot")[1].InputAsync("hello there");
		await cut.Find("[role='dialog']").KeyDownAsync("Enter");

		cut.WaitForState(() => !_commands.Commands.IsEmpty);
		await Assert.That(_commands.Commands).IsEquivalentTo(["page Tomas=hello there"]);
	}

	[Test]
	public async Task EscapeInTheSlots_GoesBackToTheList()
	{
		_palette.Response = Response([Entry("PAGE", "Page someone", "page <who>=<message>")]);
		bool? open = null;
		var cut = RenderOpen(o => open = o);
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Page someone");
		await cut.Find("[role='dialog']").KeyDownAsync("Escape");

		await Assert.That(cut.FindAll(".kit-palette-slot")).IsEmpty();
		await Assert.That(Labels(cut)).Contains("Page someone");
		await Assert.That(open).IsNull();
	}

	[Test]
	public async Task AnInsertEntry_WithNoPlayPageOpen_KeepsTheLine_AndGoesToPlay()
	{
		_palette.Response = Response([Entry("DESC", "Describe yourself", "@describe me=", PaletteAction.Insert)]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Describe yourself");

		await Assert.That(Nav.Uri).EndsWith("/play");
		await Assert.That(_page.TakePendingInsert()).IsEqualTo("@describe me=");
	}

	[Test]
	public async Task AnInsertEntry_GoesIntoTheOpenPlayComposer()
	{
		string? inserted = null;
		using var listening = _page.ListenForInsert(line => { inserted = line; return Task.CompletedTask; });
		_palette.Response = Response([Entry("DESC", "Describe yourself", "@describe me=", PaletteAction.Insert)]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await ChooseAsync(cut, "Describe yourself");

		await Assert.That(inserted).IsEqualTo("@describe me=");
		await Assert.That(_commands.Commands).IsEmpty();
	}

	[Test]
	public async Task ThePinButton_PinsTheRow_AndWritesThePins()
	{
		_palette.Response = Response([Entry("WHO", "Who is on", "+who")]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await cut.FindAll("[role='option']").Single(e => e.TextContent.Contains("Who is on"))
			.QuerySelector(".kit-palette-pin")!.ClickAsync();

		await Assert.That(_palette.PinWrites.Single()).IsEquivalentTo(["#10:1/WHO"]);
		await Assert.That(Groups(cut)[0]).IsEqualTo("Pinned");
		await Assert.That(cut.Find(".kit-palette-pin--on").GetAttribute("aria-label")).IsEqualTo("Unpin");
	}

	[Test]
	public async Task AltP_UnpinsTheActiveRow()
	{
		_palette.Response = Response([Entry("WHO", "Who is on", "+who")], pins: ["#10:1/WHO"]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-pin--on");

		await cut.Find("[role='dialog']").KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "p", AltKey = true });

		await Assert.That(_palette.PinWrites.Single()).IsEmpty();
	}

	[Test]
	public async Task ThePagesOwnRows_ShowUnderHere_AndRunWhatThePageSays()
	{
		var ran = false;
		_page.Set("editor", "#8", [PaletteRow.Page("editor-save", "icon", "Save", () => { ran = true; return Task.CompletedTask; })]);
		var cut = RenderOpen();

		await Assert.That(Groups(cut)[0]).IsEqualTo("Here");
		await ChooseAsync(cut, "Save");

		await Assert.That(ran).IsTrue();
	}

	[Test]
	public async Task Typing_OffersBuiltInCommands_AndTheirHelp()
	{
		_palette.Response = Response([]);
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-group");

		await cut.Find("input").InputAsync("pag");

		await Assert.That(Labels(cut)).Contains("page");
		await Assert.That(Labels(cut)).Contains("Help on page");
	}

	[Test]
	public async Task EntriesThatAreNotValid_AreListedBelowTheEmptyPalette()
	{
		_palette.Response = Response([]) with { Invalid = ["#10/BROKEN: not a JSON object"], Dropped = 3 };
		var cut = RenderOpen();
		cut.WaitForElement(".kit-palette-problems");

		await Assert.That(cut.Find(".kit-palette-problem").TextContent).IsEqualTo("#10/BROKEN: not a JSON object");
		await Assert.That(cut.Find(".kit-palette-problems").TextContent).Contains("3 palette entries were left out");

		await cut.Find("input").InputAsync("who");
		await Assert.That(cut.FindAll(".kit-palette-problems")).IsEmpty();
	}
}
