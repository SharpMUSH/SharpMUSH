using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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

file sealed class SceneStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
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

	public bool IsConnected => true;
	public HubConnectionState ConnectionState => HubConnectionState.Connected;

	public event Action? OnConnectionStateChanged;
	public event Action<GameOutputMessage>? OnOutputReceived;
	public event Action<RoomEventMessage>? OnRoomEventReceived;
	public event Action<SceneEventMessage>? OnSceneEventReceived;
	public event Action? OnPluginsChanged;

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

public class SceneSurfaceTests : BunitContext
{
	private readonly FakeSceneHub _hub = new();

	public SceneSurfaceTests()
	{
		var apiClient = new HttpClient(new SceneApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SceneService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SceneService>.Instance))
			.AddSingleton<IConnectionStateService>(_hub)
			.AddSingleton<ISceneHubControl>(_hub)
			.AddSingleton<IStringLocalizer<SharedResource>, SceneStubLocalizer<SharedResource>>();

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
		await Assert.That(markup).Contains("edited");
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
		cut.InvokeAsync(() => _hub.RaiseScene(new SceneEventMessage(
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
