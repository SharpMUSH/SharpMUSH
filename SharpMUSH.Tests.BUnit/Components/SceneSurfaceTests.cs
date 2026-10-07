using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
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
	/// Whether <c>+scene/create</c> makes a scene. Off is the refused case: the engine says why and the
	/// character's focus stays where it was. Instance state: these tests run in parallel and a static
	/// would leak the answer between them.
	/// </summary>
	public bool ASceneAppears { get; set; }

	/// <summary>The scene the acting character is focused on, as <c>scenefocus(me)</c> answers.</summary>
	public string Focus { get; set; } = "#-1 NOT FOCUSED";

	/// <summary>What the engine tells a character whose create it refused.</summary>
	public const string Refusal = "You must be approved to do that.";

	/// <summary>Whether the focused scene is watchable, as <c>scene(scenefocus(me),public)</c> answers.</summary>
	public bool FocusIsPublic { get; set; } = true;

	/// <summary>Whether <c>+scene/private</c> refuses, saying <see cref="PrivacyRefusal"/> and changing nothing.</summary>
	public bool PrivacyRefused { get; set; }

	public const string PrivacyRefusal = "Only the scene's owner may change who can watch it.";

	/// <summary>
	/// The objid the account session is bound to. A request that names a different character is refused
	/// with 409 and not run, as <c>CommandsController</c> refuses it.
	/// </summary>
	public string BoundCharacter { get; set; } = "#1:1";

	/// <summary>The character the session switches to straight after <c>+scene/create</c> runs, when set.</summary>
	public string? SwitchAfterCreateTo { get; set; }

	public const string Switched = "This session now acts as someone else; the command was not run.";

	/// <summary>Whether <c>+scene/schedule</c> makes a scene; off, it says <see cref="Refusal"/> and makes none.</summary>
	public bool ScheduleAppears { get; set; } = true;

	/// <summary>The ids of the acting character's scenes, as <c>scenelist(mine)</c> answers.</summary>
	public string MemberScenes { get; set; } = "1 3";

	/// <summary>Another tab schedules a scene for the same character in the same moment, with this id.</summary>
	public string? ConcurrentScheduleId { get; set; }

	/// <summary>The id <c>+scene/schedule</c> gives the scene it makes.</summary>
	public const string ScheduledId = "9";

	/// <summary>How the page asks for the scene its schedule made: the new ids on the list, filtered.</summary>
	public const string NewlyScheduledPrefix = "squish(iter(setdiff(scenelist(mine),";

	/// <summary>The scheduled scene's fields as the +scene verbs set them, read back by <c>scene(9,…)</c>.</summary>
	private readonly Dictionary<string, string> _scheduled = new(StringComparer.Ordinal);

	/// <summary>Every command request the page sent, in order, whether or not it ran.</summary>
	private readonly List<PortalCommandRequest> _requests = [];

	/// <summary>Every command the page ran through <c>POST api/commands</c>, in order.</summary>
	private readonly List<string> _commands = [];

	/// <summary>
	/// Paths the server fails with a 503 and an <c>{ "error": … }</c> body, as it does when the scene
	/// store is unreachable. Settable mid-test, so a read can succeed and a later re-read fail.
	/// </summary>
	public HashSet<string> Failing { get; } = [];

	/// <summary>Paths whose request never gets an answer: the transport throws, as a dropped connection does.</summary>
	public HashSet<string> Unreachable { get; } = [];

	public const string StoreDown = "The scene store did not answer.";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		if (Unreachable.Contains(path))
		{
			throw new HttpRequestException("Connection refused.");
		}
		if (Failing.Contains(path))
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
			{
				Content = new StringContent($$"""{"error":"{{StoreDown}}"}""", Encoding.UTF8, "application/json")
			});
		}

		if (path == "/api/commands" && request.Method == HttpMethod.Post)
		{
			return RunCommandAsync(request, ct);
		}

		string? body = path switch
		{
			"/api/scenes" => SceneList,
			"/api/scenes/S1" => Scene,
			"/api/scenes/S1/poses" => Poses,
			_ => null,
		};

		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}

	/// <summary>
	/// Answers <c>POST api/commands</c> the way the engine would for the scene verbs: <c>+scene/create</c>
	/// focuses the new scene and says so, or says why not and leaves focus alone; the result expression
	/// <c>scenefocus(me)</c> reads the focus after the command.
	/// </summary>
	private async Task<HttpResponseMessage> RunCommandAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var command = (await request.Content!.ReadFromJsonAsync<PortalCommandRequest>(ct))!;
		lock (_commands) _requests.Add(command);
		if (command.Character is { } pinned && pinned != BoundCharacter)
		{
			return new HttpResponseMessage(HttpStatusCode.Conflict)
			{
				Content = new StringContent($$"""{"status":409,"detail":"{{Switched}}"}""", Encoding.UTF8, "application/problem+json")
			};
		}

		IReadOnlyList<string> output = [];
		lock (_commands) _commands.Add(command.Command);
		if (command.Command.StartsWith("+scene/create ", StringComparison.Ordinal))
		{
			if (ASceneAppears)
			{
				Focus = "S2";
				FocusIsPublic = true;
				output = ["Scene S2 created and focused (status: active)."];
			}
			else
			{
				output = [Refusal];
			}
			if (SwitchAfterCreateTo is { } next) BoundCharacter = next;
		}
		else if (command.Command.StartsWith("+scene/schedule ", StringComparison.Ordinal))
		{
			if (ScheduleAppears)
			{
				var title = command.Command["+scene/schedule ".Length..command.Command.IndexOf('=')];
				_scheduled["title"] = title;
				_scheduled["public"] = "1";
				MemberScenes = $"{ScheduledId} {MemberScenes}";
				if (ConcurrentScheduleId is { } other) MemberScenes = $"{other} {MemberScenes}";
				output = [$"Scheduled scene {ScheduledId} \"{title}\"."];
			}
			else
			{
				output = [Refusal];
			}
		}
		else if (command.Command.StartsWith($"+scene/title {ScheduledId}=", StringComparison.Ordinal))
		{
			_scheduled["title"] = command.Command[$"+scene/title {ScheduledId}=".Length..];
		}
		else if (command.Command.StartsWith($"+scene/pitch {ScheduledId}=", StringComparison.Ordinal))
		{
			_scheduled["summary"] = command.Command[$"+scene/pitch {ScheduledId}=".Length..];
		}
		else if (command.Command == $"+scene/cancel {ScheduledId}")
		{
			_scheduled["status"] = "cancelled";
		}
		else if (command.Command == $"+scene/private {ScheduledId}")
		{
			if (PrivacyRefused) output = [PrivacyRefusal];
			else _scheduled["public"] = "0";
		}
		else if (command.Command == "+scene/private")
		{
			if (PrivacyRefused) output = [PrivacyRefusal];
			else FocusIsPublic = false;
		}

		var result = command.Result switch
		{
			"scenefocus(me)" => Focus,
			"scene(scenefocus(me),public)" => FocusIsPublic ? "1" : "0",
			"scenelist(mine)" => MemberScenes,
			{ } diff when diff.StartsWith(NewlyScheduledPrefix, StringComparison.Ordinal) => string.Join(' ',
				MemberScenes.Split(' ').Except(diff[NewlyScheduledPrefix.Length..diff.IndexOf(')', NewlyScheduledPrefix.Length)].Split(' '))),
			{ } field when field.StartsWith($"scene({ScheduledId},", StringComparison.Ordinal) =>
				_scheduled.GetValueOrDefault(field[$"scene({ScheduledId},".Length..^1], ""),
			_ => null,
		};
		var answer = new PortalCommandResponse(output, result, Truncated: false);
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(answer) };
	}

	/// <summary>Every command request the page sent so far, in order, including any refused.</summary>
	public List<PortalCommandRequest> RequestsSent()
	{
		lock (_commands) return [.. _requests];
	}

	/// <summary>The commands run so far, in order, as a snapshot a test can assert on.</summary>
	public List<string> CommandsRun()
	{
		lock (_commands) return [.. _commands];
	}
}

/// <summary>
/// Test double for the GameHub connection. Lets a test raise <see cref="OnSceneEventReceived"/>. Also implements
/// <see cref="ISceneHubControl"/> recording the scene groups joined/left.
/// </summary>
internal sealed class FakeSceneHub : IConnectionStateService, ISceneHubControl
{
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
	public event Action? OnPluginsChanged { add { } remove { } }
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

	/// <summary>Whether scene events can arrive. False reproduces a scene connection that failed or dropped.</summary>
	public bool IsSceneLive { get; set; } = true;

	public event Action? OnSceneLiveChanged;

	public int EnsureSceneLiveCalls { get; private set; }

	public Task EnsureSceneLiveAsync()
	{
		EnsureSceneLiveCalls++;
		return Task.CompletedTask;
	}

	/// <summary>Sets <see cref="IsSceneLive"/> and says so, as the connection does on a drop or a reconnect.</summary>
	public void SetSceneLive(bool live)
	{
		IsSceneLive = live;
		OnSceneLiveChanged?.Invoke();
	}

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
			.AddSingleton(sp => new SceneService(sp.GetRequiredService<IHttpClientFactory>(), TestAccountAuth.Of(sp)))
			// The live view's story reads the directory for portraits; this API answers it 404, which
			// leaves initials.
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(),
				Microsoft.Extensions.Logging.Abstractions.NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IConnectionStateService>(_hub)
			.AddSingleton<ISceneHubControl>(_hub)
			.AddSingleton(_terminal)
			.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []))
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

		await Assert.That(cut.Find(".scene-live-compose button.scene-live-send").HasAttribute("disabled")).IsTrue();

		await cut.Find(".scene-live-compose textarea").InputAsync("a raven settles on the well");

		await Assert.That(cut.Find(".scene-live-compose button.scene-live-send").HasAttribute("disabled")).IsFalse();
	}

	/// <summary>Enter is a newline: a pose is prose. Ctrl/⌘+Enter, which inserts nothing, sends it.</summary>
	[TUnit.Core.Test]
	public async Task SceneLive_EnterIsANewline_CtrlEnterSends()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		var box = cut.Find(".scene-live-compose textarea");
		await box.InputAsync("half a thought");
		await box.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
		await box.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

		await _terminal.DidNotReceive().SendAsync(Arg.Any<string>());

		await cut.Find(".scene-live-compose textarea").KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

		await _terminal.Received().SendAsync("+scene/emit S1=half a thought");
	}

	/// <summary>
	/// The composer is IC or OOC. OOC goes out as the scene package's <c>+scene/ooc</c>, which the scene records
	/// tagged <c>ooc</c>, and the composer takes the OOC band's look while it is chosen.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_OocMode_SendsSceneOoc_AndLooksOoc()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() => cut.Find(".scene-live-compose textarea"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".scene-live-compose .kit-chip").Select(c => c.TextContent)).IsEquivalentTo(["RolModeIc", "NavPlayTypeOoc"])
			.Because("poses are free-written: the only choice is in or out of character");
		await cut.FindAll(".scene-live-compose .kit-chip").Single(c => c.TextContent == "NavPlayTypeOoc").ClickAsync();
		await Assert.That(cut.Find(".scene-live-compose").ClassList).Contains("scene-live-compose--ooc");

		await cut.Find(".scene-live-compose textarea").InputAsync("brb, tea");
		await cut.Find(".scene-live-compose button.scene-live-send").ClickAsync();

		await _terminal.Received().SendAsync("+scene/ooc S1=brb\\, tea");
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

		await cut.Find(".scene-live-compose textarea").InputAsync("line one\nline two");
		await cut.Find(".scene-live-compose button.scene-live-send").ClickAsync();

		await _terminal.Received().SendAsync("+scene/emit S1=line one%rline two");
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
		var combatChip = cut.FindAll(".kit-chips button")
			.First(c => c.TextContent.Trim() == "combat");
		await combatChip.ClickAsync();

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

		await cut.Find(".scene-live-compose textarea").InputAsync("waves hello");
		await cut.Find(".scene-live-compose button.scene-live-send").ClickAsync();

		await _terminal.Received().SendAsync("+scene/emit S1=waves hello");

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
		await Assert.That(cut.Find("button.scene-live-send").HasAttribute("disabled")).IsTrue();
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
			Timestamp: 1700000600000,
			ActorObjId: "#12:1700000000000")));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("waves hello"))
				throw new InvalidOperationException("event not patched yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("waves hello");
		cut.WaitForAssertion(() => cut.Find(".scene-live-count[data-count='3']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-live-count").GetAttribute("data-count")).IsEqualTo("3")
			.Because("the header's count was 2 when the scene was read, and a live pose adds one");
	}

	/// <summary>Signs the tab in with <paramref name="name"/> (#<paramref name="number"/>) as its acting character.</summary>
	/// <remarks>The account session is bound to the same character, as signing in binds it.</remarks>
	private async Task ActAsAsync(int number = 1, string name = "Wizard")
	{
		_api.BoundCharacter = $"#{number}:1";
		Services.AddSingleton(await SharpMUSH.Tests.BUnit.Components.Characters.CharactersApiFake.SignedInAsync(this,
			new AccountAuthService.CharacterSummary(number, 1, name, "PLAYER", IsActing: true)));
	}

	private async Task SubmitStartForm(IRenderedComponent<SharpMUSH.Client.Pages.Scenes> cut, string title, bool watchable = true)
	{
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));
		await cut.Find(".scene-start button").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".scene-start-title input"), TimeSpan.FromSeconds(5));
		await cut.Find(".scene-start-title input").InputAsync(title);
		if (!watchable) await cut.Find(".scene-start-public input").ChangeAsync(false);
		await cut.Find(".scene-start-submit").ClickAsync();
	}

	/// <summary>Opens the schedule form and fills it in, picking the date and time through the pickers' bindings.</summary>
	private void FillScheduleForm(IRenderedComponent<SharpMUSH.Client.Pages.Scenes> cut, string title, DateTime when,
		string pitch = "", bool watchable = true)
	{
		cut.WaitForAssertion(() => cut.Find(".scene-schedule-open"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-schedule-open").Click();
		cut.WaitForAssertion(() => cut.Find(".scene-schedule-title input"), TimeSpan.FromSeconds(5));
		cut.Find(".scene-schedule-title input").Input(title);
		var date = cut.FindComponent<MudDatePicker>();
		cut.InvokeAsync(() => date.Instance.DateChanged.InvokeAsync(when.Date)).GetAwaiter().GetResult();
		var time = cut.FindComponent<MudTimePicker>();
		cut.InvokeAsync(() => time.Instance.TimeChanged.InvokeAsync(when.TimeOfDay)).GetAwaiter().GetResult();
		if (pitch.Length > 0) cut.Find(".scene-schedule-pitch textarea").Change(pitch);
		if (!watchable) cut.Find(".scene-schedule-public input").Change(false);
	}

	/// <summary>
	/// The schedule button runs <c>+scene/schedule</c> as the acting character, with the time as epoch seconds
	/// read in the browser's zone, finds the new scene as the one id that joined the character's list, and
	/// sets the pitch on that id. The page then shows the schedule.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ScheduleASceneForLater_GoesOutAsTheVerb_AndLandsOnTheSchedule()
	{
		await ActAsAsync();
		var reports = 0;
		Services.GetRequiredService<SceneService>().Changed += () => reports++;
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();
		var when = DateTime.Today.AddDays(3).AddHours(20);
		var at = new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Local)).ToUnixTimeSeconds();

		FillScheduleForm(cut, "Lanterns at Midnight", when, pitch: "Bring your own intrigue.");
		cut.Find(".scene-schedule-submit").Click();

		WaitForCommand($"+scene/pitch {SceneSurfaceApiHandler.ScheduledId}=Bring your own intrigue.");
		var sent = _api.RequestsSent();
		var schedule = sent.Single(r => r.Command.StartsWith("+scene/schedule ", StringComparison.Ordinal));
		await Assert.That(schedule.Command).IsEqualTo($"+scene/schedule Lanterns at Midnight={at}");
		await Assert.That(schedule.Result).StartsWith($"{SceneSurfaceApiHandler.NewlyScheduledPrefix}1 3),");
		await Assert.That(schedule.Character).IsEqualTo("#1:1");
		cut.WaitForAssertion(() =>
		{
			if (!Services.GetRequiredService<NavigationManager>().Uri.EndsWith("/scenes?scheduled=1", StringComparison.Ordinal))
				throw new InvalidOperationException("not on the schedule yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-schedule")).IsEmpty();
		await Assert.That(reports).IsEqualTo(1);
		await Assert.That(_api.CommandsRun()).DoesNotContain($"+scene/private {SceneSurfaceApiHandler.ScheduledId}");
	}

	/// <summary>
	/// <c>+scene/schedule</c> splits at the first =, so a title holding one goes out without it and is then
	/// set whole by <c>+scene/title</c>, which names the scene first. Privacy goes the same way, by id.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_AScheduledTitleWithAnEquals_IsSetWhole_AndPrivacyByTheNewId()
	{
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		FillScheduleForm(cut, "Truce = Trouble", DateTime.Today.AddDays(2).AddHours(19), watchable: false);
		cut.Find(".scene-schedule-submit").Click();

		WaitForCommand($"+scene/private {SceneSurfaceApiHandler.ScheduledId}");
		var commands = _api.CommandsRun();
		var schedule = commands.Single(c => c.StartsWith("+scene/schedule ", StringComparison.Ordinal));
		await Assert.That(schedule.Count(c => c == '=')).IsEqualTo(1);
		await Assert.That(commands).Contains($"+scene/title {SceneSurfaceApiHandler.ScheduledId}=Truce = Trouble");
		await Assert.That(cut.FindAll(".scene-scheduled-note")).IsEmpty();
	}

	/// <summary>A schedule the engine refused leaves the form open with the engine's words, and runs nothing after it.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_ARefusedSchedule_SaysWhy_AndSendsNothingMore()
	{
		_api.ScheduleAppears = false;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		FillScheduleForm(cut, "Not Approved Yet", DateTime.Today.AddDays(1).AddHours(20), pitch: "Never sent.");
		cut.Find(".scene-schedule-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-schedule-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-schedule-error").TextContent).Contains(SceneSurfaceApiHandler.Refusal);
		await Assert.That(_api.CommandsRun().Any(c => c.StartsWith("+scene/pitch", StringComparison.Ordinal))).IsFalse();
	}

	/// <summary>A time that has already gone is caught in the form; nothing goes to the game.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_ATimeThatHasPassed_IsRefusedInTheForm()
	{
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		FillScheduleForm(cut, "Too Late", DateTime.Now.AddMinutes(-5));
		cut.Find(".scene-schedule-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-schedule-error"), TimeSpan.FromSeconds(5));
		await Assert.That(_api.CommandsRun()).IsEmpty();
	}

	/// <summary>
	/// A scene asked to be private is never left watchable: when +scene/private is refused the new scene is
	/// cancelled, and the form stays open with the engine's reason, so sending it again makes no duplicate.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ScheduledButPrivacyRefused_CancelsTheScene_AndKeepsTheFormOpen()
	{
		_api.PrivacyRefused = true;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		FillScheduleForm(cut, "Meant To Be Quiet", DateTime.Today.AddDays(2).AddHours(20), pitch: "Never sent.", watchable: false);
		cut.Find(".scene-schedule-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-schedule-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-schedule-error").TextContent).Contains(SceneSurfaceApiHandler.PrivacyRefusal);
		await Assert.That(_api.CommandsRun()).Contains($"+scene/cancel {SceneSurfaceApiHandler.ScheduledId}");
		await Assert.That(_api.CommandsRun().Any(c => c.StartsWith("+scene/pitch", StringComparison.Ordinal))).IsFalse()
			.Because("privacy goes first, and nothing more is sent to a scene that was cancelled");
	}

	/// <summary>
	/// Two scenes scheduled for the character in the same moment can't be told apart, but one is this form's:
	/// the form closes rather than inviting a duplicate, and the page says the details were not set.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_AConcurrentSchedule_ClosesTheForm_AndSetsNothingOnAGuess()
	{
		_api.ConcurrentScheduleId = "10";
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		FillScheduleForm(cut, "Twin Plans", DateTime.Today.AddDays(2).AddHours(20), pitch: "Which one?", watchable: false);
		cut.Find(".scene-schedule-submit").Click();

		cut.WaitForAssertion(() => cut.Find(".scene-scheduled-note"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-schedule")).IsEmpty();
		await Assert.That(_api.CommandsRun().Count(c => c.StartsWith("+scene/schedule", StringComparison.Ordinal))).IsEqualTo(1);
		await Assert.That(_api.CommandsRun().Any(c => c.StartsWith("+scene/pitch", StringComparison.Ordinal)
			|| c.StartsWith("+scene/private", StringComparison.Ordinal))).IsFalse();
	}

	private void WaitForCommand(string command) =>
		WaitFor(() => _api.CommandsRun().Contains(command), $"{command} was not run");

	private static void WaitFor(Func<bool> condition, string failure)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline) throw new TimeoutException(failure);
			Thread.Sleep(20);
		}
	}

	/// <summary>
	/// The scene browser starts a scene by running <c>+scene/create</c> as the tab's acting character
	/// through <c>POST api/commands</c> (#1485).
	///
	/// <para>It used to send the verb down the terminal websocket, which plays whichever character it
	/// connected as — not necessarily the one the tab acts as after a switch — and which answers the
	/// screen rather than the page. So the page asked the terminal <c>num(me)</c> first, and then guessed
	/// at the new scene by diffing the roster. The command route runs as the session's character and
	/// answers this call alone, so neither guard is needed.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartsAScene_ThroughTheCommandRoute()
	{
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "The Lantern Room");

		WaitForCommand("+scene/create The Lantern Room");
		await _terminal.DidNotReceive().SendAsync(Arg.Any<string>());
		await _terminal.DidNotReceive().SendCommandAsync(Arg.Any<string>(), Arg.Any<int>());
	}

	/// <summary>Nobody acting means nobody to start a scene as, so the page does not offer to.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_DoesNotOfferToStartAScene_WithoutAnActingCharacter()
	{
		_terminal.IsConnected.Returns(true);
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();
		cut.WaitForAssertion(() => cut.Find(".scenes-page"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".scene-start button")).IsEmpty();
	}

	/// <summary>
	/// The terminal is no longer what a scene is started through, so a player whose terminal is not
	/// connected — or not yet — can still start one.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_OffersToStartAScene_WithNoTerminal()
	{
		_terminal.IsConnected.Returns(false);
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

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
		_api.ASceneAppears = true;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "The Lantern Room");

		WaitForCommand("+scene/create The Lantern Room");
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));
		await Assert.That(_api.CommandsRun()).DoesNotContain("+scene/private");
	}

	/// <summary>Unticking it is the case that needs a command, because it is the exception now.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartedWithWatchingOff_StayPrivate()
	{
		_api.ASceneAppears = true;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "A quiet corner", watchable: false);

		WaitForCommand("+scene/private");
		var commands = _api.CommandsRun();
		await Assert.That(commands.IndexOf("+scene/private")).IsGreaterThan(commands.IndexOf("+scene/create A quiet corner"));
	}

	/// <summary>
	/// A scene the form saw created is reported, so the section sidebar — mounted with the layout, and
	/// not remounted by anything the form does — reloads its counts and live rows.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ACreatedScene_IsReportedToTheRestOfTheSection()
	{
		_api.ASceneAppears = true;
		await ActAsAsync();
		var reports = 0;
		Services.GetRequiredService<SceneService>().Changed += () => reports++;
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "A quiet corner");

		cut.WaitForAssertion(() =>
		{
			if (reports == 0) throw new InvalidOperationException("not reported yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(reports).IsEqualTo(1);
	}

	/// <summary>
	/// The scene goes out as the character the tab acts as even when the terminal plays somebody else:
	/// the page no longer asks the terminal anything, and no longer refuses on a mismatch.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_StartsAsTheActingCharacter_WhateverTheTerminalPlays()
	{
		_terminal.IsConnected.Returns(true);
		_terminal.ConnectedPlayerName.Returns("Wizard");
		_api.ASceneAppears = true;
		await ActAsAsync(314, "Wren Halloway");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "As Wren");

		WaitForCommand("+scene/create As Wren");
		await Assert.That(cut.FindAll(".scene-start-error")).IsEmpty();
		await _terminal.DidNotReceive().SendCommandAsync(Arg.Any<string>(), Arg.Any<int>());
	}

	/// <summary>
	/// A create the engine refused changed nothing, so nothing is reported, and the form says why in the
	/// engine's own words — the command's output is its answer to this page.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ARefusedCreate_ReportsNothing_AndSaysWhy()
	{
		_api.ASceneAppears = false;
		await ActAsAsync();
		var reports = 0;
		Services.GetRequiredService<SceneService>().Changed += () => reports++;
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Refused Quietly");

		cut.WaitForAssertion(() => cut.Find(".scene-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-start-error").TextContent).Contains(SceneSurfaceApiHandler.Refusal);
		await Assert.That(reports).IsEqualTo(0);
	}

	/// <summary>
	/// A creation the engine refused does not turn the character's existing scene private.
	///
	/// <para>+scene/private acts on the scene the character is focused on. A refusal leaves focus on the
	/// scene the character already had, so the tick box would make THAT one private if the verb went out
	/// regardless. It goes out only when the focus after the create is a scene it was not on before.</para>
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_WhenTheEngineCreatesNothing_TouchesNoOtherScene()
	{
		_api.ASceneAppears = false;
		_api.Focus = "S1";
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Never Created", watchable: false);

		cut.WaitForAssertion(() => cut.Find(".scene-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(_api.CommandsRun()).Contains("+scene/create Never Created");
		await Assert.That(_api.CommandsRun()).DoesNotContain("+scene/private");
	}

	/// <summary>
	/// After +scene/private the page asks the scene whether it is private now, in the same request: a
	/// verb that refused answers in its output, not with an error status, so a form that closed on any
	/// answer would leave a scene the player asked to keep private watchable by anyone.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_APrivacyTheEngineRefused_KeepsTheFormOpen_AndSaysWhy()
	{
		_api.ASceneAppears = true;
		_api.PrivacyRefused = true;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Meant To Be Quiet", watchable: false);

		cut.WaitForAssertion(() => cut.Find(".scene-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-start-error").TextContent).Contains(SceneSurfaceApiHandler.PrivacyRefusal);
		var asked = _api.RequestsSent().Single(request => request.Command == "+scene/private");
		await Assert.That(asked.Result).IsEqualTo("scene(scenefocus(me),public)");
	}

	/// <summary>A private scene that really is private closes the form with nothing to report.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_APrivacyThatTookHold_ClosesTheForm()
	{
		_api.ASceneAppears = true;
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Truly Quiet", watchable: false);

		WaitForCommand("+scene/private");
		cut.WaitForAssertion(() => cut.Find(".scene-start button"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-start-error")).IsEmpty();
		await Assert.That(_api.FocusIsPublic).IsFalse();
	}

	/// <summary>
	/// Every request of the start flow names the character the form was started as, so the server can
	/// refuse one the session would now run as somebody else.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_EveryStartRequest_NamesTheCharacterItStartedAs()
	{
		_api.ASceneAppears = true;
		await ActAsAsync(314, "Wren Halloway");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Pinned", watchable: false);

		WaitForCommand("+scene/private");
		var sent = _api.RequestsSent();
		await Assert.That(sent.Select(request => request.Command))
			.IsEquivalentTo(["think", "+scene/create Pinned", "+scene/private"]);
		await Assert.That(sent.All(request => request.Character == "#314:1")).IsTrue();
	}

	/// <summary>
	/// A character switch between the create and +scene/private does not make the other character's
	/// focused scene private: the private request still names the character the scene was made by, the
	/// server refuses it, and the form says so instead of closing.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Scenes_ASwitchMidStart_DoesNotRunPrivateAsTheOtherCharacter()
	{
		_api.ASceneAppears = true;
		_api.SwitchAfterCreateTo = "#2:1";
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Switched Away", watchable: false);

		cut.WaitForAssertion(() => cut.Find(".scene-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-start-error").TextContent).Contains(SceneSurfaceApiHandler.Switched);
		await Assert.That(_api.CommandsRun()).DoesNotContain("+scene/private");
		await Assert.That(_api.RequestsSent().Single(request => request.Command == "+scene/private").Character).IsEqualTo("#1:1");
	}

	/// <summary>A route that fails says so, rather than leaving the form looking as if nothing happened.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_WhenTheCommandRouteFails_SaysWhy()
	{
		_api.Failing.Add("/api/commands");
		await ActAsAsync();
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		await SubmitStartForm(cut, "Unreachable");

		cut.WaitForAssertion(() => cut.Find(".scene-start-error"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-start-error").TextContent).Contains(SceneSurfaceApiHandler.StoreDown);
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
	/// A 404 is the not-found card; any other failure is not. <see cref="SceneService"/> answered
	/// <see langword="null"/> for both, so a scene store that was down told every player that the scene
	/// they were in had stopped existing.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_WhenTheServerFails_SaysWhy_NotThatTheSceneIsGone()
	{
		_api.Failing.Add("/api/scenes/S1");
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() => cut.Find(".scene-load-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-load-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
		await Assert.That(cut.Markup).DoesNotContain("RolSceneNotFound");
	}

	[TUnit.Core.Test]
	public async Task SceneDetail_WhenTheServerFails_SaysWhy_NotThatTheSceneIsGone()
	{
		_api.Failing.Add("/api/scenes/S1");
		var cut = Render<SceneDetail>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() => cut.Find(".scene-load-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-load-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
		await Assert.That(cut.Markup).DoesNotContain("RolSceneNotFound");
	}

	/// <summary>
	/// A move re-reads the whole chain. When that read failed, the page cleared the log and put nothing
	/// back, so one dropped request emptied the scene a player was reading.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_AFailedReloadAfterAMove_KeepsThePosesOnScreen()
	{
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		_api.Failing.Add("/api/scenes/S1/poses");
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage(
			SceneId: "S1",
			EventType: "move",
			ActorName: "Wizard",
			PoseId: "P2",
			Content: string.Empty,
			Markup: string.Empty,
			Tags: [],
			Source: "pose",
			Location: "The Tavern",
			Timestamp: 1700000700000,
			ActorObjId: null)));

		cut.WaitForAssertion(() => cut.Find(".scene-poses-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("draws a blade");
		await Assert.That(cut.Markup).Contains("says calm down");
		await Assert.That(cut.Find(".scene-poses-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
	}

	/// <summary>
	/// A reload whose request throws in the transport (a dropped connection, not a 5xx) is a failed read
	/// too: the reload runs unobserved from the hub event, so nothing may escape it, and the log stays.
	/// </summary>
	[TUnit.Core.Test]
	public async Task SceneLive_AReloadThatThrowsAfterAMove_KeepsThePosesOnScreen()
	{
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		_api.Unreachable.Add("/api/scenes/S1/poses");
		await cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage(
			SceneId: "S1",
			EventType: "move",
			ActorName: "Wizard",
			PoseId: "P2",
			Content: string.Empty,
			Markup: string.Empty,
			Tags: [],
			Source: "pose",
			Location: "The Tavern",
			Timestamp: 1700000700000,
			ActorObjId: null)));

		cut.WaitForAssertion(() => cut.Find(".scene-poses-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("draws a blade");
		await Assert.That(cut.Markup).Contains("says calm down");
	}

	/// <summary>An archive that did not answer is not an empty archive.</summary>
	[TUnit.Core.Test]
	public async Task Scenes_AFailedListIsNotAnEmptyArchive()
	{
		_api.Failing.Add("/api/scenes");
		var cut = Render<SharpMUSH.Client.Pages.Scenes>();

		cut.WaitForAssertion(() => cut.Find(".scene-list-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-list-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
		await Assert.That(cut.Markup).DoesNotContain("ResNoScenesFound");
	}

	[TUnit.Core.Test]
	public async Task ScenesActive_AFailedListIsNotAnEmptyList()
	{
		_api.Failing.Add("/api/scenes");
		var cut = Render<ScenesActive>();

		cut.WaitForAssertion(() => cut.Find(".scene-list-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".scene-list-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
		await Assert.That(cut.Markup).DoesNotContain("WidNoActiveScenes");
	}

	[TUnit.Core.Test]
	public async Task ActiveSceneWidget_AFailedListSaysWhy()
	{
		_api.Failing.Add("/api/scenes");
		var cut = Render<ActiveSceneWidget>();

		cut.WaitForAssertion(() => cut.Find(".active-scene-widget-error"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".active-scene-widget-error").TextContent.Trim()).IsEqualTo(SceneSurfaceApiHandler.StoreDown);
		await Assert.That(cut.Markup).DoesNotContain("WidNoActiveScenes");
	}
}
