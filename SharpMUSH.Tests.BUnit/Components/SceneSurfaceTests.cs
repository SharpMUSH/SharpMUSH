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
file sealed class SceneApiHandler : HttpMessageHandler
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

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
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

	public bool IsConnected => true;
	public HubConnectionState ConnectionState => HubConnectionState.Connected;

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action? OnPluginsChanged;
	public event Action<SceneEventMessage>? OnSceneEventReceived;

	public Task ConnectAsync() => Task.CompletedTask;
	public Task DisconnectAsync() => Task.CompletedTask;
	public Task ReconnectAsync() => Task.CompletedTask;

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
		OnConnectionStateChanged?.Invoke();
		OnOutputReceived?.Invoke(null!);
		OnRoomEventReceived?.Invoke(null!);
	}
}

public class SceneSurfaceTests : TrackingBunitContext
{
	private readonly FakeSceneHub _hub = new();

	public SceneSurfaceTests()
	{
		var apiClient = Track(new HttpClient(new SceneApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SceneService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SceneService>.Instance))
			.AddSingleton<IConnectionStateService>(_hub)
			.AddSingleton<ISceneHubControl>(_hub)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
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

	[TUnit.Core.Test]
	public async Task SceneLive_Editor_SendsCommandNotServiceWrite_AndJoinsScene()
	{
		var cut = Render<SceneLiveHarness>(p => p.Add(c => c.Id, "S1"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("draws a blade"))
				throw new InvalidOperationException("poses not loaded yet");
		}, TimeSpan.FromSeconds(5));

		// JoinScene was invoked for the scene group on init.
		await Assert.That(_hub.Joined).Contains("S1");

		// Type into the compose field and send.
		var textarea = cut.Find("textarea");
		textarea.Change("waves hello");
		cut.Find("button.mud-icon-button").Click();

		cut.WaitForAssertion(() =>
		{
			if (_hub.SentCommands.Count == 0)
				throw new InvalidOperationException("command not sent yet");
		}, TimeSpan.FromSeconds(5));

		// A normal pose command was sent on the hub — never @emit.
		await Assert.That(_hub.SentCommands).Contains(":waves hello");
		await Assert.That(_hub.SentCommands.Any(c => c.Contains("@emit"))).IsFalse();

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
}
