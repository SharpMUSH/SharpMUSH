using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Admin &gt; Discord: the bot's setup checklist, linked channels, role mappings with what was changed by hand, and
/// linked members; each action goes to <c>api/discord/admin</c> and the page reads everything again.
/// </summary>
public class AdminDiscordPageTests : TrackingBunitContext
{
	private static readonly DateTimeOffset At = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

	private readonly DiscordApi _api = new();
	private readonly ManualTime _time = new();

	public AdminDiscordPageTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddLogging()
			.AddSingleton<TimeProvider>(_time)
			.AddSingleton<DiscordAdminService>()
			.AddSingleton<RoleRegistryClient>()
			.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: [GameFeatures.Discord]))
			.AddLocalization();
		AddAuthorization().SetAuthorized("staff");
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private sealed class DiscordApi : HttpMessageHandler
	{
		public bool Forbidden { get; set; }

		public string ChannelState { get; set; } = "stopped";

		public string Status { get; set; } = "connected";

		public List<string> Calls { get; } = [];

		public DiscordMapRequest? Mapped { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (request.Method != HttpMethod.Get)
			{
				lock (Calls) Calls.Add($"{request.Method} {path}");
				if (path == "/api/discord/admin/roles" && request.Method == HttpMethod.Post)
				{
					Mapped = await request.Content!.ReadFromJsonAsync<DiscordMapRequest>(cancellationToken);
				}

				if (path.EndsWith("/resume", StringComparison.Ordinal))
				{
					ChannelState = "ok";
				}

				return Json(new DiscordAdminAnswer("Done.", "ok"));
			}

			if (Forbidden && path.StartsWith("/api/discord/admin", StringComparison.Ordinal))
			{
				return new HttpResponseMessage(HttpStatusCode.Forbidden);
			}

			return path switch
			{
				"/api/discord/admin/connection" => Json(new DiscordConnectionView(Status, "Lantern", "https://discord.com/invite", 1,
					[
						new DiscordSetupStepView("token", "Bot token", true, null, null),
						new DiscordSetupStepView("intents", "Privileged intents", false, "Turn on Server Members Intent.", "https://discord.com/developers"),
					])),
				"/api/discord/admin/channels" => Json(new List<DiscordChannelView>
				{
					new("1427000000000000001", "rp-general", "Test Server", "discord/1427000000000000001", ChannelState, 0,
						ChannelState == "stopped" ? "The webhook was deleted twice." : null, ["@everyone"], At),
				}),
				"/api/discord/admin/channels/available" => Json(new List<DiscordTextChannelView>
				{
					new("1427000000000000001", "rp-general", "Test Server", [], true),
					new("1427000000000000002", "ooc", "Test Server", ["Manage Webhooks"], false),
				}),
				"/api/discord/admin/roles" => Json(new DiscordRolesView(
					[new DiscordRoleMappingView("1427000000000000102", "Approved", "approved", "report", null, 1, At)],
					[new DiscordDriftView("1427000000000000102", "Approved", "approved", "2002", "bob.k", true, At)])),
				"/api/discord/admin/members" => Json(new List<DiscordMemberView>
				{
					new("1001", "grave.dev", "acct-1", "Grave", At),
				}),
				"/api/roles" => new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent("""
						[{"slug":"everyone","name":"Everyone","category":"","color":"","priority":0,"isSystem":true,"permissions":{},"createdAt":0,"updatedAt":0},
						 {"slug":"builder","name":"Builder","category":"","color":"","priority":10,"isSystem":true,"permissions":{},"createdAt":0,"updatedAt":0},
						 {"slug":"wizard","name":"Wizard","category":"","color":"","priority":90,"isSystem":true,"permissions":{},"createdAt":0,"updatedAt":0}]
						""", System.Text.Encoding.UTF8, "application/json"),
				},
				_ => new HttpResponseMessage(HttpStatusCode.NotFound),
			};
		}

		private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
	}

	/// <summary>Time that moves only when the test says: each tick fires every timer once.</summary>
	private sealed class ManualTime : TimeProvider
	{
		private readonly List<(TimerCallback Callback, object? State)> _timers = [];

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			lock (_timers) _timers.Add((callback, state));
			return new Stopped();
		}

		public void Tick()
		{
			(TimerCallback Callback, object? State)[] timers;
			lock (_timers) timers = [.. _timers];
			foreach (var (callback, state) in timers)
			{
				callback(state);
			}
		}

		private sealed class Stopped : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;

			public void Dispose()
			{
			}

			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}

	private IRenderedComponent<MudHarness> RenderPage(string ready = ".adm-discord-connection")
	{
		var cut = Render<MudHarness>(p => p.AddChildContent<AdminDiscord>());
		cut.WaitForAssertion(() => cut.Find(ready), TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ShowsTheChecklist_Channels_Mappings_Drift_AndMembers()
	{
		var cut = RenderPage();

		var steps = cut.FindAll(".adm-discord-step");
		await Assert.That(steps.Select(step => step.GetAttribute("data-step"))).IsEquivalentTo(new[] { "token", "intents" });
		await Assert.That(steps[0].ClassList).Contains("adm-discord-step--done");
		await Assert.That(steps[1].TextContent).Contains("Turn on Server Members Intent.");
		await Assert.That(cut.Find(".adm-discord-invite").GetAttribute("href")).IsEqualTo("https://discord.com/invite");

		await Assert.That(cut.Find(".adm-discord-channels .adm-discord-row").GetAttribute("data-channel")).IsEqualTo("1427000000000000001");
		await Assert.That(cut.Find(".adm-discord-fault").TextContent).Contains("The webhook was deleted twice.");
		await Assert.That(cut.FindAll(".adm-discord-open").Count).IsEqualTo(1).Because("@everyone may still post there");
		await Assert.That(cut.Find(".adm-discord-roles .adm-discord-row").GetAttribute("data-role")).IsEqualTo("1427000000000000102");
		await Assert.That(cut.Find(".adm-discord-drift .adm-discord-row").TextContent).Contains("bob.k");
		await Assert.That(cut.Find(".adm-discord-members .adm-discord-row").TextContent).Contains("Grave");
	}

	/// <summary>A reconnect finishes after the request that asked for it; the page follows it without a reload.</summary>
	[Test]
	public async Task WhileConnecting_ThePageReadsTheConnectionAgainUntilItSettles()
	{
		_api.Status = "connecting";
		var cut = RenderPage();
		await Assert.That(cut.Find(".adm-discord-status").TextContent).Contains("connecting");

		_api.Status = "connected";
		_time.Tick();
		cut.WaitForAssertion(() =>
		{
			if (cut.Find(".adm-discord-status").TextContent.Trim() != "connected")
			{
				throw new InvalidOperationException("still connecting");
			}
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task ResumingAStoppedChannel_PostsAndReadsItAgain()
	{
		var cut = RenderPage();

		await cut.Find("button.adm-discord-resume").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("button.adm-discord-resume").Count != 0) throw new InvalidOperationException("still stopped");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(_api.Calls).Contains("POST /api/discord/admin/channels/1427000000000000001/resume");
	}

	[Test]
	public async Task MappingARole_SendsTheGameRoleAndTheDiscordRole()
	{
		var cut = RenderPage();

		await cut.Find(".adm-discord-map-discord input").InputAsync("@Builders");
		// No select is open in a test, so the game role goes in through the select's own field.
		await cut.InvokeAsync(() => cut.FindComponents<MudBlazor.MudSelect<string>>()
			.Single(select => select.Instance.Class?.Contains("adm-discord-map-role") == true)
			.Instance.ValueChanged.InvokeAsync("builder"));
		await cut.Find("button.adm-discord-map").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (_api.Mapped is null) throw new InvalidOperationException("not mapped yet");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(_api.Mapped).IsEqualTo(new DiscordMapRequest("builder", "@Builders"));
	}

	[Test]
	public async Task Wizard_AsksToShowStaffOnDiscord()
	{
		var cut = RenderPage();

		await Assert.That(cut.FindAll(".adm-discord-map-confirm").Count).IsEqualTo(0);
		await cut.InvokeAsync(() => cut.FindComponents<MudBlazor.MudSelect<string>>()
			.Single(select => select.Instance.Class?.Contains("adm-discord-map-role") == true)
			.Instance.ValueChanged.InvokeAsync("wizard"));

		cut.WaitForAssertion(() => cut.Find(".adm-discord-map-confirm"), TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task WithoutDiscordAdmin_SaysWhatItNeeds()
	{
		_api.Forbidden = true;

		var cut = RenderPage(".adm-discord-forbidden");

		await Assert.That(cut.FindAll(".adm-discord-connection").Count).IsEqualTo(0);
	}
}
