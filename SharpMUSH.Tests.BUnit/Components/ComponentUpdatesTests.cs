using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>The server page's updates panel: what the agent reported, and the update it asks for.</summary>
public class ComponentUpdatesTests : TrackingBunitContext
{
	private sealed class Api : HttpMessageHandler
	{
		public string? Status { get; set; }
		public List<string> Posted { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Method == HttpMethod.Post)
			{
				Posted.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
				var job = new UpdateJobStatus(Guid.NewGuid(), [ServerComponents.Renderer], null, DateTimeOffset.UtcNow, "admin", null,
					UpdateJobStates.Queued, null, null, null);
				return Json(HttpStatusCode.Accepted, job);
			}
			return Status is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(Status, Encoding.UTF8, "application/json")
			};
		}

		private static HttpResponseMessage Json<T>(HttpStatusCode code, T body) => new(code)
		{
			Content = new StringContent(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), Encoding.UTF8, "application/json")
		};
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

	private readonly Api _api = new();
	private readonly ManualTime _time = new();
	private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

	public ComponentUpdatesTests()
	{
		var client = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddEchoLocalizer()
			.AddSingleton<AdminServerService>()
			.AddSingleton<TimeProvider>(_time)
			.AddSingleton(_dialogs);
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private static UpdateComponentStatus Component(string name, string? latest) =>
		new(name, name, $"sharpmush/sharpmush-{name}", "latest", "1.0.0", "0123456789abcdef", "sha256:old", latest, "running", null);

	private static string Status(bool connected = true, UpdateJobStatus? job = null) => JsonSerializer.Serialize(
		new AdminUpdatesStatus(true, connected, DateTimeOffset.UtcNow, "sharpmush", "latest",
			[Component(ServerComponents.Renderer, "sha256:new"), Component(ServerComponents.Server, "sha256:old"),
				Component(ServerComponents.SocketServer, null)],
			["1.1.0", "1.0.0"], DateTimeOffset.UtcNow, null, job, 12, 9),
		JsonSerializerOptions.Web);

	private IRenderedComponent<MudHarness> RenderPanel() =>
		Render<MudHarness>(p => p.AddChildContent<ComponentUpdates>());

	private void ConfirmAnswers(bool? answer) =>
		_dialogs.ShowMessageBoxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DialogOptions>())
			.Returns(Task.FromResult(answer));

	[Test]
	public async Task ShowsEachComponentAndWhetherItsTagMoved()
	{
		_api.Status = Status();
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find("[data-component='renderer']"), TimeSpan.FromSeconds(5));

		string State(string component) => cut.Find($"[data-component='{component}'] .adm-updates-state").TextContent.Trim();
		await Assert.That(State(ServerComponents.Renderer)).IsEqualTo("AdmUpdNewer");
		await Assert.That(State(ServerComponents.Server)).IsEqualTo("AdmUpdCurrent");
		await Assert.That(State(ServerComponents.SocketServer)).IsEqualTo("AdmUpdNotChecked");
		await Assert.That(cut.Find("[data-component='server'] .adm-updates-version").TextContent).IsEqualTo("1.0.0 · 0123456");
		await Assert.That(cut.Find(".kit-card-sub").TextContent).IsEqualTo("AdmUpdAgentTag(sharpmush, latest)");
	}

	[Test]
	public async Task WithoutAnAgentItSaysSoAndOffersNothing()
	{
		_api.Status = Status(connected: false);
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find(".adm-updates-noagent"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".adm-updates-noagent").TextContent.Trim()).IsEqualTo("AdmUpdAgentMissing");
		await Assert.That(cut.FindAll(".adm-updates-row").Count).IsEqualTo(0);
	}

	[Test]
	public async Task UpdatingAllWarnsAboutTheConnectionsItDropsAndPostsInOrder()
	{
		_api.Status = Status();
		ConfirmAnswers(true);
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find(".adm-updates-all"), TimeSpan.FromSeconds(5));

		await cut.Find(".adm-updates-all").ClickAsync(new());

		cut.WaitForState(() => _api.Posted.Count == 1, TimeSpan.FromSeconds(5));
		await _dialogs.Received(1).ShowMessageBoxAsync("AdmUpdConfirmTitle",
			Arg.Is<string>(text => text.Contains("AdmUpdConfirmSocket(12, 9, AdmUpdDelayMinutes(5))") && text.Contains("AdmUpdConfirmServer")),
			"AdmUpdUpdate", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DialogOptions>());
		var request = JsonSerializer.Deserialize<AdminUpdateRequest>(_api.Posted[0], JsonSerializerOptions.Web)!;
		await Assert.That(request.Components).IsEquivalentTo(ServerComponents.InUpdateOrder);
		await Assert.That(request.Tag).IsNull();
		await Assert.That(request.SocketDelaySeconds).IsEqualTo(300);
		cut.WaitForAssertion(() => cut.Find(".adm-updates-job[data-state='queued']"), TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task CancellingTheConfirmationSendsNothing()
	{
		_api.Status = Status();
		ConfirmAnswers(null);
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find("[data-component='renderer'] button"), TimeSpan.FromSeconds(5));

		await cut.Find("[data-component='renderer'] button").ClickAsync(new());

		await Assert.That(_api.Posted).IsEmpty();
	}

	[Test]
	public async Task AnUpdateInProgressDisablesEveryButton()
	{
		var running = new UpdateJobStatus(Guid.NewGuid(), ServerComponents.InUpdateOrder, null, DateTimeOffset.UtcNow, "admin", null,
			UpdateJobStates.Running, ServerComponents.Server, null, null);
		_api.Status = Status(job: running);
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find(".adm-updates-job"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".adm-updates-job").TextContent.Trim()).IsEqualTo("AdmUpdJobRunning(AdmUpdServer)");
		await Assert.That(cut.FindAll(".adm-updates-row button").All(button => button.HasAttribute("disabled"))).IsTrue();
		await Assert.That(cut.Find(".adm-updates-all").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task ThePanelReadsAgainAndKeepsItsViewWhileTheServerIsAway()
	{
		_api.Status = Status();
		var cut = RenderPanel();
		cut.WaitForAssertion(() => cut.Find("[data-component='renderer']"), TimeSpan.FromSeconds(5));

		// The server is being replaced: the read fails, and the panel keeps what it showed.
		_api.Status = null;
		_time.Tick();
		cut.WaitForAssertion(() => cut.Find(".adm-updates-unread"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".adm-updates-row").Count).IsEqualTo(3);

		var done = new UpdateJobStatus(Guid.NewGuid(), ServerComponents.InUpdateOrder, null, DateTimeOffset.UtcNow, "admin", null,
			UpdateJobStates.Succeeded, null, null, DateTimeOffset.UtcNow);
		_api.Status = Status(job: done);
		_time.Tick();
		cut.WaitForAssertion(() => cut.Find(".adm-updates-job[data-state='succeeded']"), TimeSpan.FromSeconds(5));
	}
}
