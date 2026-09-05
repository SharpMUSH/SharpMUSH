using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>Hosts <see cref="SceneLive"/> alongside a MudPopoverProvider (required by its MudSelect).</summary>
file sealed class SceneLiveHarness : ComponentBase
{
	[Parameter] public string Id { get; set; } = string.Empty;

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenComponent<MudPopoverProvider>(0);
		builder.CloseComponent();
		builder.OpenComponent<SceneLive>(1);
		builder.AddAttribute(2, nameof(SceneLive.Id), Id);
		builder.CloseComponent();
	}
}

/// <summary>
/// Serves the scene REST API the way the server does (camelCase JSON, long Unix-millis
/// timestamps). Active and recent lists carry one scene; the scene's poses include one
/// edited pose carrying raw Markup and a distinct tag set for the chip filter.
/// </summary>
internal sealed class SceneSurfaceApiHandler : HttpMessageHandler
{
	private const string SceneList = """
	[
	  {"id":"S1","status":"active","isPublic":true,"isTempRoom":false,"scheduledFor":null,
	   "startedAt":1700000000000,"lastActivityAt":1700000500000,"poseCount":2,
	   "ownerDbref":"#1","ownerName":"Wizard","starterDbref":"#1","starterName":"Wizard",
	   "roomDbref":"#7","roomName":"The Tavern","meta":{"title":"Barroom Brawl"}}
	]
	""";

	private const string Scene = """
	{"id":"S1","status":"active","isPublic":true,"isTempRoom":false,"scheduledFor":null,
	 "startedAt":1700000000000,"lastActivityAt":1700000500000,"poseCount":2,
	 "ownerDbref":"#1","ownerName":"Wizard","starterDbref":"#1","starterName":"Wizard",
	 "roomDbref":"#7","roomName":"The Tavern","meta":{"title":"Barroom Brawl"}}
	""";

	// Two poses: one shown as "Mysterious Stranger" (ShowAsName) that was edited (editCount 2),
	// one plain by "Bartender". Distinct tags: combat, dialogue.
	private const string Poses = """
	[
	  {"id":"P1","sceneId":"S1","authorDbref":"#10","authorName":"Alice","showAsName":"Mysterious Stranger",
	   "originDbref":"#7","originName":"The Tavern","source":"pose","tags":["combat"],"meta":{},
	   "createdAt":1700000100000,"isDeleted":false,"content":"draws a blade","markup":"draws a blade",
	   "editCount":2,"lastEditedAt":1700000200000,"lastEditorDbref":"#10","lastEditorName":"Alice"},
	  {"id":"P2","sceneId":"S1","authorDbref":"#11","authorName":"Bob","showAsName":"Bartender",
	   "originDbref":"#7","originName":"The Tavern","source":"say","tags":["dialogue"],"meta":{},
	   "createdAt":1700000300000,"isDeleted":false,"content":"says calm down","markup":"says calm down",
	   "editCount":1,"lastEditedAt":null,"lastEditorDbref":null,"lastEditorName":null}
	]
	""";

	/// <summary>
	/// Whether a new scene shows up on reads after the first. Off is the refused case. Instance state:
	/// these tests run in parallel and a static would leak the answer between them.
	/// </summary>
	public bool ASceneAppears { get; set; }

	private int _activeListCalls;

	private const string SceneListWithNewScene = """
	[
	  {"id":"S1","status":"active","isPublic":true,"isTempRoom":false,"scheduledFor":null,
	   "startedAt":1700000000000,"lastActivityAt":1700000500000,"poseCount":2,
	   "ownerDbref":"#1","ownerName":"Wizard","starterDbref":"#1","starterName":"Wizard",
	   "roomDbref":"#7","roomName":"The Tavern","meta":{"title":"Barroom Brawl"}},
	  {"id":"S2","status":"active","isPublic":true,"isTempRoom":false,"scheduledFor":null,
	   "startedAt":1700000600000,"lastActivityAt":1700000600000,"poseCount":0,
	   "ownerDbref":"#1","ownerName":"Wizard","starterDbref":"#1","starterName":"Wizard",
	   "roomDbref":"#7","roomName":"The Tavern","meta":{"title":"A Quiet Corner"}}
	]
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		string? body = path switch
		{
			// Keyed on the ACTIVE list specifically: the page reads recent and active from this same
			// path on load, so counting every read would have the scene appear before the form was
			// even used. It shows up on the second active read — the page's first poll — which is
			// what "a scene was created" looks like from out here.
			"/api/scenes" when ASceneAppears
				&& request.RequestUri.Query.Contains("filter=active", StringComparison.Ordinal)
				&& ++_activeListCalls > 1 => SceneListWithNewScene,
			"/api/scenes" => SceneList,
			"/api/scenes/S1" => Scene,
			"/api/scenes/S1/poses" => Poses,
			_ => null,
		};

		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// Test double for the GameHub connection. Records commands sent via
/// <see cref="SendCommandAsync"/> (so we can assert the live editor sends a command, not a
/// service write) and lets a test raise <see cref="OnSceneEventReceived"/>. Also implements
/// <see cref="ISceneHubControl"/> recording the scene groups joined/left.
/// </summary>
internal sealed class FakeSceneHub : IConnectionStateService, ISceneHubControl
{
	public List<string> SentCommands { get; } = [];
	public List<string> Joined { get; } = [];
	public List<string> Left { get; } = [];

	/// <summary>Set to make <see cref="JoinSceneAsync"/> refuse, as the hub does for a caller with no character.</summary>
	public HubException? JoinRefusal { get; set; }

	/// <summary>
	/// Starts connected, as most tests want. False reproduces a returning player: the hub is fresh on
	/// every page load and nothing on a plain load connects it.
	/// </summary>
	public bool IsConnected { get; set; } = true;

	public int ConnectCalls { get; private set; }

	/// <summary>
	/// Counted apart from <see cref="ConnectCalls"/>: ConnectAsync returns early whenever a hub object
	/// exists, connected or not, so only a reconnect can revive a dropped one.
	/// </summary>
	public int ReconnectCalls { get; private set; }

	public HubConnectionState ConnectionState =>
		IsConnected ? HubConnectionState.Connected : HubConnectionState.Disconnected;

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action? OnPluginsChanged;
	public event Action<SceneEventMessage>? OnSceneEventReceived;

	public Task ConnectAsync()
	{
		ConnectCalls++;
		IsConnected = true;
		OnConnectionStateChanged?.Invoke();
		return Task.CompletedTask;
	}

	public Task DisconnectAsync() => Task.CompletedTask;
	public Task ReconnectAsync()
	{
		ReconnectCalls++;
		return ConnectAsync();
	}

	public Task SendCommandAsync(string command)
	{
		SentCommands.Add(command);
		return Task.CompletedTask;
	}

	public Task JoinSceneAsync(string sceneId)
	{
		if (JoinRefusal is { } refusal) return Task.FromException(refusal);

		Joined.Add(sceneId);
		return Task.CompletedTask;
	}

	public Task LeaveSceneAsync(string sceneId)
	{
		Left.Add(sceneId);
		return Task.CompletedTask;
	}

	public void RaiseScene(SceneEventMessage msg) => OnSceneEventReceived?.Invoke(msg);

	// Keep the compiler from flagging the otherwise-unused events.
	public void Touch()
	{
		OnOutputReceived?.Invoke(null!);
		OnRoomEventReceived?.Invoke(null!);
	}
}

public class SceneSurfaceTests : TrackingBunitContext
{
	private readonly FakeSceneHub _hub = new();
	private readonly SceneSurfaceApiHandler _api = new();

	/// <summary>
	/// The terminal MainLayout mounts on every page — already open as the character, and the channel
	/// the compose box sends through.
	/// </summary>
	private readonly ITerminalService _terminal = Substitute.For<ITerminalService>();

	public SceneSurfaceTests()
	{
		var apiClient = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SceneService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SceneService>.Instance))
			.AddSingleton<IConnectionStateService>(_hub)
			.AddSingleton<ISceneHubControl>(_hub)
			.AddSingleton(_terminal)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	/// <summary>
	/// The field binds as the player types. MudTextField binds on change (blur) by default, which left
	/// the text empty through an entire pose and the Send button never clickable.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_EnablesSend_AsSoonAsSomethingIsTyped()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-live-compose button").HasAttribute("disabled")).IsTrue();

		cut.Find(".scene-live-compose textarea").Input("a raven settles on the well");

		await Assert.That(cut.Find(".scene-live-compose button").HasAttribute("disabled")).IsFalse();
	}

	/// <summary>Enter is a newline. A pose is prose; only the button sends it.</summary>
	[TUnit.Core.Test]
	public async Task SceneLive_EnterDoesNotSend()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		var box = cut.Find(".scene-live-compose textarea");
		box.Input("half a thought");
		box.KeyDown(Key.Enter);

		await _terminal.DidNotReceive().SendAsync(Arg.Any<string>());
	}

	/// <summary>
	/// The send button issues a scene-targeted game command down the terminal websocket, defaulting to
	/// emit — the mode that renders the author's words verbatim with no name glued to the front, which
	/// is what a compose box on a scene's own page is for. Newlines go out as %r: the channel is
	/// line-delimited, and the engine expands it back before matching the verb's pattern.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_SendButton_SendsASceneEmitWithNewlinesAsPercentR()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		cut.Find(".scene-live-compose textarea").Input("line one\nline two");
		cut.Find(".scene-live-compose button").Click();

		await _terminal.Received().SendAsync("+scene/emit S1=line one%rline two");
		// Never the hub: its SendCommand publishes onto a subject nothing subscribes to.
		await Assert.That(_hub.SentCommands).IsEmpty();
	}

	/// <summary>
	/// A returning player loads /scenes/{id}/live with a valid session and a fresh hub singleton —
	/// nothing on that path logs in again, so nothing had connected the hub. The page rendered its
	/// "not connected to the game" banner over a permanently disabled compose box while the sidebar
	/// said the same character was online. The page needs the connection, so the page establishes it.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_ConnectsTheHub_WhenItIsNotConnectedYet()
	{
		_hub.IsConnected = false;

		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (_hub.ConnectCalls == 0)
				throw new InvalidOperationException("hub not connected yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(_hub.ConnectCalls).IsEqualTo(1);
		await Assert.That(_hub.IsConnected).IsTrue();
		// And with the connection up it joins the scene group, which is what delivers live poses.
		await Assert.That(_hub.Joined).Contains("S1");
	}

	/// <summary>An already-connected hub is left alone — reconnecting would drop the group joins.</summary>
	[TUnit.Core.Test]
	public async Task SceneLive_DoesNotReconnectAnAlreadyConnectedHub()
	{
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (_hub.Joined.Count == 0)
				throw new InvalidOperationException("scene group not joined yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(_hub.ConnectCalls).IsEqualTo(0);
	}

	[TUnit.Core.Test]
	public async Task ActiveSceneWidget_RendersSceneFromApi()
	{
		var cut = Render<ActiveSceneWidget>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Barroom Brawl"))
				throw new InvalidOperationException("scene not loaded yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Barroom Brawl"); // scene title from meta
		await Assert.That(cut.Markup).Contains("The Tavern");     // room name
		await Assert.That(cut.Markup).Contains("/scenes/S1/live"); // join link
	}

	[TUnit.Core.Test]
	public async Task SceneDetail_RendersPosesWithMarkupAndEditedBadge()
	{
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		// Pose body rendered client-side from Markup.
		await Assert.That(markup).Contains("draws a blade");
		await Assert.That(markup).Contains("says calm down");
		// Display persona uses ShowAsName.
		await Assert.That(markup).Contains("Mysterious Stranger");
		await Assert.That(markup).Contains("Bartender");
		// Edited pose (editCount > 1) shows the badge; the unedited one does not add a second.
		// The localizer stub echoes resource keys, so the badge renders as its key.
		await Assert.That(markup).Contains("RolEditedBadge");
	}

	[TUnit.Core.Test]
	public async Task SceneDetail_TagChipFilter_FiltersRenderedPoses()
	{
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// Both poses visible initially.
		await Assert.That(cut.Markup).Contains("draws a blade");
		await Assert.That(cut.Markup).Contains("says calm down");

		// Click the "combat" tag chip → only the combat pose remains.
		var combatChip = cut.FindAll(".mud-chip")
			.First(c => c.TextContent.Trim() == "combat");
		combatChip.Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("says calm down"))
				throw new InvalidOperationException("filter not applied yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("draws a blade");   // combat pose stays
		await Assert.That(cut.Markup).DoesNotContain("says calm down"); // dialogue pose filtered out
	}

	/// <summary>
	/// The editor joins the scene group and sends a game command rather than writing through the
	/// scene service — still the contract. What changed is which command and down which channel: it
	/// used to send a bare <c>:pose</c> on the game hub, whose SendCommand publishes onto a NATS
	/// subject nothing subscribes to, so the pose never reached the engine at all. It now sends the
	/// scene-targeted <c>+scene/emit</c> verb down the command terminal's websocket, which the engine
	/// already consumes.
	///
	/// <para>The old assertion "never @emit" is preserved in spirit: a raw <c>@emit</c> would be
	/// recorded only if the poser happened to be focused on this scene in this room.
	/// <c>+scene/emit</c> names the scene, so it records unconditionally.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_Editor_SendsASceneCommandOnTheTerminal_AndJoinsScene()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// JoinScene was invoked for the scene group on init.
		await Assert.That(_hub.Joined).Contains("S1");

		cut.Find(".scene-live-compose textarea").Input("waves hello");
		cut.Find(".scene-live-compose button").Click();

		await _terminal.Received().SendAsync("+scene/emit S1=waves hello");
		await Assert.That(_hub.SentCommands).IsEmpty();

		// No optimistic insert: the author's pose only appears after the round-trip event.
		await Assert.That(cut.Markup).DoesNotContain("waves hello");
	}

	/// <summary>
	/// A refused join is not a missing scene. The REST fetch already returned this scene, so the caller may
	/// read it and its archive renders; only the live subscription was refused (no character, or visibility
	/// revoked in the moment between the two calls). Reporting that as "scene not found" told a user with a
	/// perfectly readable scene that it does not exist.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_WhenTheHubRefusesTheJoin_KeepsTheSceneAndSaysLiveIsUnavailable()
	{
		_hub.JoinRefusal = new HubException("Joining a scene requires a character; guests cannot join scenes.");

		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("RolSceneLiveUnavailable"))
				throw new InvalidOperationException("join refusal not handled yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("RolSceneLiveUnavailable");
		await Assert.That(markup).DoesNotContain("RolSceneNotFound")
			.Because("the scene was fetched successfully; calling it missing is false");
		// The archive the caller is entitled to is still on screen.
		await Assert.That(markup).Contains("Barroom Brawl");
		await Assert.That(markup).Contains("draws a blade");
		// Nothing renders a pose but the round-trip event, and this connection is in no scene group.
		await Assert.That(cut.Find("button.mud-icon-button").HasAttribute("disabled")).IsTrue();
		await Assert.That(_hub.Joined).IsEmpty();
	}

	/// <summary>
	/// The other half of the same split, and the one with the security property: a scene that does not exist
	/// and a scene that exists but is not visible to this caller are BOTH a 404 from the REST route, so both
	/// reach this one card with this one message. A refusal a caller can tell apart is a way to enumerate
	/// private scene ids, which is why the server refuses to distinguish them and why the page must not
	/// invent a distinction the server withheld.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_WhenTheSceneIsNotFetchable_SaysNotFound_WithoutNamingWhy()
	{
		// The fake API answers 404 for any id it does not serve — exactly as the real route answers 404 for
		// a missing scene and for a private one the caller may not see, indistinguishably.
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S404"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("RolSceneNotFound"))
				throw new InvalidOperationException("not-found state not reached yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("RolSceneNotFound");
		await Assert.That(cut.Markup).DoesNotContain("RolSceneLiveUnavailable")
			.Because("naming the live-subscription reason here would tell a stranger the scene exists");
	}

	[TUnit.Core.Test]
	public async Task SceneLive_AppendsPoseOnSceneEvent()
	{
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// Round-trip the author's pose as a realtime event → it renders exactly once.
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage(
			SceneId: "S1",
			EventType: "pose",
			ActorName: "Mysterious Stranger",
			PoseId: "P3",
			Content: "waves hello",
			Markup: "waves hello",
			Tags: ["greeting"],
			Source: "pose",
			Location: "The Tavern",
			Timestamp: 1700000600000)));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("waves hello"))
				throw new InvalidOperationException("event not patched yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("waves hello");
	}

	/// <summary>
	/// The scene browser can start a scene.
	///
	/// <para>It could not. <c>/scenes</c> listed what already existed and offered Read and Join, and
	/// that was all — the only way to begin one was <c>+scene/create</c> typed into the terminal, which
	/// nothing in the portal mentions. A player who arrived through the web, made a character and went
	/// looking for roleplay reached the page named after it and found no way in.</para>
	///
	/// <para>It sends the same verb down the same websocket the compose box uses, because that
	/// connection is already open as the character.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartsAScene_ThroughTheTerminal()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));

		cut.Find(".scene-start button").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-start-title input").Input("The Lantern Room");
		cut.Find(".scene-start-submit").Click();

		await _terminal.Received().SendAsync("+scene/create The Lantern Room");
	}

	/// <summary>
	/// With no open connection there is nothing to send the verb down, so the page does not pretend
	/// otherwise — the same rule the compose box follows.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_DoesNotOfferToStartAScene_WithoutAConnection()
	{
		_terminal.IsConnected.Returns(false);
		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scenes-page"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".scene-start button")).IsEmpty();
	}

	/// <summary>
	/// The start button appears when the terminal connects, not only if it happened to be connected
	/// already when the page rendered.
	///
	/// <para>This is how the first version of the affordance failed in a real browser: the page read
	/// <c>IsConnected</c> once while rendering and never subscribed to the change, so a player who
	/// navigated to /scenes while the websocket was still coming up saw a page with no way to start a
	/// scene — and nothing ever brought it back. The stubbed connection in the test above hid it,
	/// because there the state was already true before the first render.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ShowsTheStartButton_WhenTheTerminalConnectsAfterRender()
	{
		_terminal.IsConnected.Returns(false);
		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scenes-page"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-start button")).IsEmpty();

		_terminal.IsConnected.Returns(true);
		_terminal.ConnectionStateChanged += Raise.Event<Action<bool>>(true);

		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));
	}

	/// <summary>
	/// A scene started from the browser is watchable by anyone, and says so without needing a verb:
	/// that is now what the engine does when nobody specifies. The box is here so the choice is
	/// visible at the moment it is made, not because the page has to ask for the default.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartedFromTheBrowser_AreVisibleToOthersByDefault()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));

		cut.Find(".scene-start button").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-start-title input").Input("The Lantern Room");
		cut.Find(".scene-start-submit").Click();

		await _terminal.Received().SendAsync("+scene/create The Lantern Room");
		await _terminal.DidNotReceive().SendAsync("+scene/private");
	}

	/// <summary>Unticking it is the case that needs a command, because it is the exception now.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartedWithWatchingOff_StayPrivate()
	{
		_terminal.IsConnected.Returns(true);
		_api.ASceneAppears = true;
		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));

		cut.Find(".scene-start button").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-start-title input").Input("A quiet corner");
		cut.Find(".scene-start-public input").Change(false);
		cut.Find(".scene-start-submit").Click();

		await _terminal.Received().SendAsync("+scene/create A quiet corner");
		// Waited for: the form polls the roster before saying anything else, because it will not send
		// this until it can see the scene exists.
		cut.WaitForAssertion(
			() => _terminal.Received().SendAsync("+scene/private"),
			TimeSpan.FromSeconds(10));
	}

	/// <summary>
	/// A hub that exists but has dropped is revived, not left alone.
	///
	/// <para>The page asked for ConnectAsync, which returns the moment it sees a hub object —
	/// connected or not. So a player whose connection dropped while they were reading arrived at a
	/// live scene that could never reconnect: the banner stayed up and the compose box stayed dead
	/// until they reloaded. ReconnectAsync tears the dead hub down first, and there are no scene
	/// groups to preserve precisely because nothing is connected.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_RevivesAHubThatExistsButHasDropped()
	{
		_hub.IsConnected = false;

		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		await Assert.That(_hub.ReconnectCalls).IsGreaterThan(0)
			.Because("ConnectAsync cannot revive a hub that already exists; only a reconnect can");
	}

	/// <summary>
	/// A creation the engine refused does not turn somebody else's scene private.
	///
	/// <para>+scene/private acts on the scene the player is focused on, and the form used to send it
	/// the instant after +scene/create without waiting to see whether anything had been created. A
	/// refusal — an unapproved player, a name the engine would not take — leaves focus on whatever
	/// scene the player already had, so the tick box would have made THAT one private instead. The
	/// verb now goes only after the roster shows the scene exists.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_WhenTheEngineCreatesNothing_TouchesNoOtherScene()
	{
		_terminal.IsConnected.Returns(true);
		_api.ASceneAppears = false;

		var cut = Render<Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));

		cut.Find(".scene-start button").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-start-title input").Input("Never Created");
		cut.Find(".scene-start-public input").Change(false);
		cut.Find(".scene-start-submit").Click();

		cut.WaitForAssertion(
			() => _terminal.Received().SendAsync("+scene/create Never Created"),
			TimeSpan.FromSeconds(5));

		// Long enough to outlast the roster poll, so this is "it never sent it" rather than "it had
		// not got there yet" — the distinction the whole test rests on.
		await Task.Delay(TimeSpan.FromSeconds(3));

		await _terminal.DidNotReceive().SendAsync("+scene/private");
	}
}
