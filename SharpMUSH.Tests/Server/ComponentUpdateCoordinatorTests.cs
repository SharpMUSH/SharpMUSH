using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Server;

public class ComponentUpdateCoordinatorTests
{
	private const string Token = "synthetic-update-agent-token-of-forty-chars";

	private sealed class Clock : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 11, 13, 21, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private static UpdateComponentStatus Component(string name, string tag = "1.0.0", string? latest = "sha256:new") =>
		new(name, name == ServerComponents.Server ? "sharpmush-server" : name, $"sharpmush/sharpmush-{name}", tag, "1.0.0",
			"abc", "sha256:old", latest, "running", null);

	private static UpdateAgentReport Report(string? tag = "1.0.0", UpdateAgentJobReport? job = null) =>
		new(ComponentUpdateCoordinator.Protocol, "sharpmush", tag,
			ServerComponents.InUpdateOrder.Select(name => Component(name)).ToList(), ["1.1.0", "1.0.0"], null, job, null);

	private static (ComponentUpdateCoordinator Coordinator, Clock Clock, IGameBroadcastService Broadcast) Create(string? token = Token)
	{
		var clock = new Clock();
		var broadcast = Substitute.For<IGameBroadcastService>();
		return (new ComponentUpdateCoordinator(token, clock, broadcast), clock, broadcast);
	}

	private static AdminUpdateRequest Request(params string[] components) => new(components, null, 0);

	[Test]
	public async Task AShortOrMissingTokenTurnsTheAgentOff()
	{
		var (coordinator, _, _) = Create("too-short");
		await Assert.That(coordinator.Configured).IsFalse();
		await Assert.That(coordinator.Authenticates("too-short")).IsFalse();
		var (configured, _, _) = Create();
		await Assert.That(configured.Authenticates(Token)).IsTrue();
		await Assert.That(configured.Authenticates(Token + "x")).IsFalse();
		await Assert.That(configured.Authenticates(null)).IsFalse();
	}

	[Test]
	public async Task NothingIsQueuedBeforeTheAgentReports()
	{
		var (coordinator, _, _) = Create();
		var result = await coordinator.QueueAsync(Request(ServerComponents.Renderer), "admin");
		await Assert.That(result.Expect<Error<string>>().Value).Contains("not been heard from");
	}

	[Test]
	public async Task AQueuedUpdateIsHandedOverInUpdateOrderUntilTheAgentRunsIt()
	{
		var (coordinator, _, broadcast) = Create();
		coordinator.Poll(Report());
		var job = (await coordinator.QueueAsync(Request(ServerComponents.Server, ServerComponents.Renderer), "admin"))
			.Expect<UpdateJobStatus>();
		await Assert.That(job.Components).IsEquivalentTo(new[] { ServerComponents.Renderer, ServerComponents.Server });
		await Assert.That(job.SocketNotBefore).IsNull();
		await broadcast.DidNotReceiveWithAnyArgs().BroadcastAsync(default!);

		var handed = coordinator.Poll(Report()).Expect<UpdateAgentJob>();
		await Assert.That(handed.Id).IsEqualTo(job.Id);
		// Handed again until the agent says it took it: a lost answer must not lose the update.
		await Assert.That(coordinator.Poll(Report()).Expect<UpdateAgentJob>().Id).IsEqualTo(job.Id);

		var running = new UpdateAgentJobReport(job.Id, job.Components, null, UpdateJobStates.Running, ServerComponents.Renderer, null, null);
		coordinator.Poll(Report(job: running)).Expect<NotFound>();
		var status = coordinator.Status(0, 0);
		await Assert.That(status.Job!.State).IsEqualTo(UpdateJobStates.Running);
		await Assert.That(status.Job.RequestedBy).IsEqualTo("admin");

		var second = await coordinator.QueueAsync(Request(ServerComponents.Renderer), "admin");
		await Assert.That(second.Expect<Error<string>>().Value).Contains("already in progress");
	}

	[Test]
	public async Task TheSocketServerUpdateWarnsPlayersAndWaits()
	{
		var (coordinator, clock, broadcast) = Create();
		coordinator.Poll(Report());
		var job = (await coordinator.QueueAsync(new AdminUpdateRequest([ServerComponents.SocketServer], null, 300), "admin"))
			.Expect<UpdateJobStatus>();
		await Assert.That(job.SocketNotBefore).IsEqualTo(clock.Now.AddMinutes(5));
		var sent = broadcast.ReceivedCalls().Single().GetArguments().Single();
		await Assert.That(sent is SharpMessage message && message is string text && text == ComponentUpdateCoordinator.SocketWarning(300)).IsTrue();
		await Assert.That(ComponentUpdateCoordinator.SocketWarning(300)).Contains("5 minutes");
	}

	[Test]
	public async Task AnUpdateTheAgentNeverTakesFails()
	{
		var (coordinator, clock, _) = Create();
		coordinator.Poll(Report());
		await coordinator.QueueAsync(Request(ServerComponents.Renderer), "admin");
		clock.Now += ComponentUpdateCoordinator.ClaimWindow + TimeSpan.FromSeconds(1);
		var status = coordinator.Status(0, 0);
		await Assert.That(status.Job!.State).IsEqualTo(UpdateJobStates.Failed);
		await Assert.That(status.AgentConnected).IsFalse();
	}

	[Test]
	public async Task ARequestIsCheckedAgainstWhatTheAgentReported()
	{
		var (coordinator, _, _) = Create();
		coordinator.Poll(Report());
		var all = ServerComponents.InUpdateOrder.ToArray();
		string Refusal(AdminUpdateRequest request) =>
			coordinator.QueueAsync(request, "admin").AsTask().Result.Expect<Error<string>>().Value;

		await Assert.That(Refusal(Request())).Contains("at least one");
		await Assert.That(Refusal(Request("nats"))).IsEqualTo("Unknown component.");
		await Assert.That(Refusal(Request(ServerComponents.Server, ServerComponents.Server))).IsEqualTo("Unknown component.");
		await Assert.That(Refusal(new AdminUpdateRequest(all, "../1.0.0", 0))).Contains("not a release tag");
		await Assert.That(Refusal(new AdminUpdateRequest(all, "9.9.9", 0))).Contains("no release tagged 9.9.9");
		await Assert.That(Refusal(new AdminUpdateRequest([ServerComponents.Server], "1.1.0", 0))).Contains("every component");
		await Assert.That(Refusal(new AdminUpdateRequest(all, null, 3600))).Contains("between 0 and 1800");

		var moved = await coordinator.QueueAsync(new AdminUpdateRequest(all, "1.1.0", 0), "admin");
		await Assert.That(moved.Expect<UpdateJobStatus>().Tag).IsEqualTo("1.1.0");
	}

	[Test]
	public async Task AFixedComposeTagCannotBeMoved()
	{
		var (coordinator, _, _) = Create();
		coordinator.Poll(Report(tag: null));
		var result = await coordinator.QueueAsync(new AdminUpdateRequest(ServerComponents.InUpdateOrder, "1.1.0", 0), "admin");
		await Assert.That(result.Expect<Error<string>>().Value).Contains("fixes its image tags");
	}

	[Test]
	public async Task ANewServerTakesOverTheUpdateTheAgentIsRunning()
	{
		// The server is one of the things an update replaces: the new process learns of it from the agent.
		var (coordinator, _, _) = Create();
		var id = Guid.NewGuid();
		coordinator.Poll(Report(job: new(id, ServerComponents.InUpdateOrder, "1.1.0", UpdateJobStates.Running, ServerComponents.SocketServer, null, null)));
		var job = coordinator.Status(3, 2).Job!;
		await Assert.That(job.Id).IsEqualTo(id);
		await Assert.That(job.Step).IsEqualTo(ServerComponents.SocketServer);
		await Assert.That(job.Tag).IsEqualTo("1.1.0");
	}

	[Test]
	public async Task AgentEndpointRequiresTheTokenAndAProtocolItSpeaks()
	{
		var (coordinator, _, _) = Create();
		var controller = new UpdateAgentController(coordinator)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
		await Assert.That(controller.Poll(Report())).IsTypeOf<UnauthorizedResult>();
		await Assert.That(coordinator.Status(0, 0).AgentSeenAt).IsNull();

		controller.Request.Headers[UpdateAgentController.TokenHeader] = Token;
		await Assert.That(controller.Poll(Report() with { Protocol = 2 })).IsTypeOf<ObjectResult>();
		await Assert.That(controller.Poll(Report())).IsTypeOf<NoContentResult>();
		await Assert.That(controller.Response.Headers.CacheControl.ToString()).IsEqualTo("no-store");
		await Assert.That(coordinator.Status(0, 0).AgentConnected).IsTrue();
	}

	/// <summary>A poll exactly as deploy/update-agent/agent.py sends it, taken from a run against Docker.</summary>
	private const string AgentPoll = """
		{"protocol": 1, "project": "sharpmush", "tag": "dev",
		 "components": [{"component": "renderer", "service": "renderer", "image": "sharpmush/sharpmush-renderer", "tag": "dev",
		   "version": "dev", "revision": "069eca29deeb0722fc139efb9932e8c2a7e7af46",
		   "runningDigest": "sha256:fe9a", "latestDigest": "sha256:fe9a", "state": "running", "startedAt": "2026-11-13T21:53:51.281541Z"}],
		 "availableTags": [], "registryReadAt": null,
		 "job": {"id": "6089c018-c9f3-4873-8b34-b95c075d99ec", "components": ["renderer"], "tag": null, "state": "succeeded",
		   "step": null, "message": null, "finishedAt": "2026-11-13T21:53:53.386612Z"},
		 "error": null}
		""";

	[Test]
	public async Task TheAgentsJsonReadsAndTheJobItReadsBackIsCamelCase()
	{
		var report = System.Text.Json.JsonSerializer.Deserialize<UpdateAgentReport>(AgentPoll, System.Text.Json.JsonSerializerOptions.Web)!;
		await Assert.That(report.Components![0].UpdateAvailable).IsFalse();
		await Assert.That(report.Job!.State).IsEqualTo(UpdateJobStates.Succeeded);
		await Assert.That(report.Components[0].StartedAt).IsEqualTo(new DateTimeOffset(2026, 11, 13, 21, 53, 51, TimeSpan.Zero).AddTicks(2815410));

		var job = new UpdateAgentJob(Guid.Empty, [ServerComponents.Renderer], "1.0.0", null);
		var json = System.Text.Json.JsonSerializer.Serialize(job, System.Text.Json.JsonSerializerOptions.Web);
		await Assert.That(json).IsEqualTo("""{"id":"00000000-0000-0000-0000-000000000000","components":["renderer"],"tag":"1.0.0","socketNotBefore":null}""");
	}

	[Test]
	public async Task AdministrationRequiresServerAdminPolicy()
	{
		var policy = typeof(ComponentUpdatesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
		await Assert.That(policy.Policy).IsEqualTo(PortalPermission.ServerAdmin);
	}

	[Test]
	[Arguments("1.0.0", null, "1.1.0", true)]
	[Arguments("1.0.0", null, "1.0.1-rc.1", true)]
	[Arguments("1.1.0", null, "1.0.0", false)]
	[Arguments("1.0.0", null, "1.0.0-rc.1", false)]
	[Arguments("1.0.0-rc.1", null, "1.0.0", true)]
	[Arguments("1.0.0", null, "latest", true)]
	[Arguments("1.0.0", null, "dev", true)]
	[Arguments("dev", "dev", "1.1.0", false)]
	[Arguments("dev", "dev", "latest", false)]
	[Arguments("latest", "1.1.0", "1.0.0", false)]
	[Arguments("latest", "1.1.0", "1.2.0", true)]
	[Arguments("latest", null, "1.2.0", false)]
	[Arguments("1.9.0", null, "1.10.0", true)]
	public async Task AMoveNeverGoesBack(string current, string? running, string target, bool allowed) =>
		await Assert.That(ComponentUpdateCoordinator.MovesForward(current, running, target)).IsEqualTo(allowed);

	[Test]
	public async Task TheVersionListOffersOnlyNewerReleases()
	{
		var (coordinator, _, _) = Create();
		coordinator.Poll(Report(tag: "1.0.0") with { AvailableTags = ["1.1.0", "1.0.0", "0.9.0"] });
		await Assert.That(coordinator.Status(0, 0).AvailableTags).IsEquivalentTo(new[] { "1.1.0", "1.0.0" });
		var back = await coordinator.QueueAsync(new AdminUpdateRequest(ServerComponents.InUpdateOrder, "0.9.0", 0), "admin");
		await Assert.That(back.Expect<Error<string>>().Value).Contains("cannot go back");
	}

	[Test]
	public async Task NothingIsQueuedWhileTheAgentRunsAnUpdateThisServerLost()
	{
		var (coordinator, _, _) = Create();
		coordinator.Poll(Report(job: new(Guid.NewGuid(), [ServerComponents.Renderer], null, UpdateJobStates.Running, ServerComponents.Renderer, null, null)));
		// The panel shows the agent's update; a second one waits until it ends.
		var result = await coordinator.QueueAsync(Request(ServerComponents.Server), "admin");
		await Assert.That(result.Expect<Error<string>>().Value).Contains("already in progress");
	}

	[Test]
	public async Task PlayersHearWhenAWarnedUpdateStopsBeforeTheSocketServer()
	{
		var (coordinator, _, broadcast) = Create();
		coordinator.Poll(Report());
		var job = (await coordinator.QueueAsync(new AdminUpdateRequest(ServerComponents.InUpdateOrder, null, 60), "admin")).Expect<UpdateJobStatus>();
		coordinator.Poll(Report(job: new(job.Id, job.Components, null, UpdateJobStates.Running, ServerComponents.Server, null, null)));
		coordinator.Poll(Report(job: new(job.Id, job.Components, null, UpdateJobStates.Failed, null, "pull access denied", null)));

		var sent = broadcast.ReceivedCalls().Select(call => call.GetArguments().Single()).ToList();
		await Assert.That(sent.Count).IsEqualTo(2);
		await Assert.That(sent[1] is SharpMessage message && message is string text && text == ComponentUpdateCoordinator.SocketCalledOff).IsTrue();
	}

	[Test]
	public async Task AnAgentPollThroughTheFrontDoorIsNotFound()
	{
		var (coordinator, _, _) = Create();
		var controller = new UpdateAgentController(coordinator)
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
		controller.Request.Headers[UpdateAgentController.TokenHeader] = Token;
		controller.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
		await Assert.That(controller.Poll(Report())).IsTypeOf<NotFoundResult>();
		await Assert.That(coordinator.Status(0, 0).AgentSeenAt).IsNull();
	}

	[Test]
	public async Task AnUpdateWhoseWarningFailsIsNeverHandedOver()
	{
		var (coordinator, _, broadcast) = Create();
		broadcast.BroadcastAsync(default!).ReturnsForAnyArgs(_ => throw new InvalidOperationException("bus down"));
		coordinator.Poll(Report());
		await Assert.That(async () => await coordinator.QueueAsync(new AdminUpdateRequest([ServerComponents.SocketServer], null, 60), "admin"))
			.Throws<InvalidOperationException>();
		coordinator.Poll(Report()).Expect<SharpMUSH.Library.DiscriminatedUnions.NotFound>();
		await Assert.That(coordinator.Status(0, 0).Job).IsNull();
	}

	[Test]
	public async Task AnUpdateThatExpiresWhileItsWarningIsSentIsNotReportedQueued()
	{
		var (coordinator, clock, broadcast) = Create();
		var sent = new TaskCompletionSource();
		broadcast.BroadcastAsync(default!).ReturnsForAnyArgs(_ => new ValueTask(sent.Task));
		coordinator.Poll(Report());
		var queueing = coordinator.QueueAsync(new AdminUpdateRequest([ServerComponents.SocketServer], null, 60), "admin").AsTask();
		clock.Now += ComponentUpdateCoordinator.ClaimWindow + TimeSpan.FromSeconds(1);
		coordinator.Poll(Report()).Expect<SharpMUSH.Library.DiscriminatedUnions.NotFound>();
		sent.SetResult();
		var result = await queueing;
		await Assert.That(result.Expect<Error<string>>().Value).Contains("did not take");
		await Assert.That(coordinator.Status(0, 0).Job!.State).IsEqualTo(UpdateJobStates.Failed);
	}

	[Test]
	public async Task AnUpdateWhoseWarningOutlastsTheClaimWindowFailsWithoutAPoll()
	{
		var (coordinator, clock, broadcast) = Create();
		var sent = new TaskCompletionSource();
		broadcast.BroadcastAsync(default!).ReturnsForAnyArgs(_ => new ValueTask(sent.Task), _ => ValueTask.CompletedTask);
		coordinator.Poll(Report());
		var queueing = coordinator.QueueAsync(new AdminUpdateRequest([ServerComponents.SocketServer], null, 60), "admin").AsTask();
		clock.Now += ComponentUpdateCoordinator.ClaimWindow + TimeSpan.FromSeconds(1);
		sent.SetResult();
		var result = await queueing;
		await Assert.That(result.Expect<Error<string>>().Value).Contains("did not take");
		var messages = broadcast.ReceivedCalls().Select(call => call.GetArguments().Single()).ToList();
		await Assert.That(messages.Count).IsEqualTo(2);
		await Assert.That(messages[1] is SharpMessage message && message is string text && text == ComponentUpdateCoordinator.SocketCalledOff).IsTrue();
	}
}
