using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class QueueDiagnosticsAuthorizationTests
{
	private sealed class Harness
	{
		public readonly CapabilityActor Actor = new("account", new DBRef(1, 100), new DBRef(1, 100));
		public readonly QueueDiagnosticsRecorder Recorder = new();
		public readonly IQueueControlService Queues = Substitute.For<IQueueControlService>();
		public readonly QueueDiagnosticsService Service;
		public QueueInspectionScope? Scope;
		public bool Controls = true;
		public Harness()
		{
			Scope = new(Actor, new HashSet<string> { PortalPermission.QueueInspectOwn, PortalPermission.DiagnosticsProfile });
			Queues.GetInspectionScopeAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<QueueInspectionScope?>(Scope));
			Queues.ListAsync(Arg.Any<CapabilityActor>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QueueEntrySnapshot>>([]));
			Queues.CanInspectAsync(Arg.Any<QueueInspectionScope>(), Arg.Any<DBRef?>(), Arg.Any<DBRef?>(), Arg.Any<CancellationToken>())
				.Returns(call => Task.FromResult(call.ArgAt<DBRef?>(1) == Actor.ActiveCharacter
					? Controls && call.Arg<QueueInspectionScope>().Scopes.Contains(PortalPermission.QueueInspectOwn)
					: call.Arg<QueueInspectionScope>().Scopes.Contains(PortalPermission.QueueInspect)));
			Service = new(Recorder, Queues, NullLogger<QueueDiagnosticsService>.Instance);
		}
		public QueueObservation Record(long pid, DBRef owner, string attribute)
		{
			var item = Recorder.Admitted(pid, new DBRef((int)pid + 10, 100), owner, "enqueue", attribute);
			item.Complete(QueueOutcome.Completed);
			return item;
		}
	}

	[Test]
	public async Task HistoryFiltersBeforePaginationAndNeverReturnsHiddenMetadata()
	{
		var h = new Harness();
		h.Record(1, h.Actor.ActiveCharacter!.Value, "VISIBLE_OLD");
		h.Record(2, h.Actor.ActiveCharacter.Value, "VISIBLE_NEW");
		h.Record(3, new DBRef(9, 100), "PRIVATE_ATTRIBUTE");
		var result = await h.Service.InspectAsync(h.Actor, limit: 1);
		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(result.AsT0.Recent.Single().SourceAttribute).IsEqualTo("VISIBLE_NEW");
		await Assert.That(result.AsT0.NextHistoryCursor).IsNotNull();
		await Assert.That(JsonSerializer.Serialize(result.AsT0).Contains("PRIVATE_ATTRIBUTE", StringComparison.Ordinal)).IsFalse();
		var next = await h.Service.InspectAsync(h.Actor, 1, result.AsT0.NextHistoryCursor);
		await Assert.That(next.AsT0.Recent.Single().SourceAttribute).IsEqualTo("VISIBLE_OLD");
	}

	[Test]
	public async Task GlobalInspectionDoesNotBypassAnExplicitOwnDenial()
	{
		var h = new Harness();
		h.Scope = new(h.Actor, new HashSet<string> { PortalPermission.QueueInspect });
		h.Record(1, h.Actor.ActiveCharacter!.Value, "OWN_DENIED");
		h.Record(2, new DBRef(9, 100), "GLOBAL_ALLOWED");
		var result = await h.Service.InspectAsync(h.Actor);
		await Assert.That(result.AsT0.Recent.Single().SourceAttribute).IsEqualTo("GLOBAL_ALLOWED");
		await Assert.That(result.AsT0.CanProfile).IsFalse();
	}

	[Test]
	public async Task RevocationDiscardsQueuedSamplesAndPriorResults()
	{
		var h = new Harness();
		var started = await h.Service.StartProfileAsync(h.Actor);
		await Assert.That(started.IsT0).IsTrue();
		var observation = h.Recorder.Admitted(1, new DBRef(2, 100), h.Actor.ActiveCharacter, "enqueue");
		using (observation.Enter()) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
		h.Scope = null;
		await h.Service.CollectProfilesAsync();
		await Assert.That(h.Recorder.Profile(started.AsT0)).IsNull();
		var denied = await h.Service.InspectAsync(h.Actor);
		await Assert.That(denied.AsT1).IsEqualTo(DiagnosticsError.PermissionDenied);
	}

	[Test]
	public async Task ProfileReadsRecheckSourceAuthorityAndBatchChecksAreBounded()
	{
		var h = new Harness(); await h.Service.StartProfileAsync(h.Actor);
		var source = new DBRef(2, 100);
		var observation = h.Recorder.Admitted(1, source, h.Actor.ActiveCharacter, "enqueue");
		using (observation.Enter())
			for (var i = 0; i < 100; i++) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
		await h.Service.CollectProfilesAsync();
		await h.Queues.Received(1).CanInspectAsync(Arg.Any<QueueInspectionScope>(), h.Actor.ActiveCharacter, source, Arg.Any<CancellationToken>());
		var visible = await h.Service.InspectAsync(h.Actor);
		await Assert.That(visible.AsT0.Profile!.Rows.Single().Count).IsEqualTo(100L);
		h.Controls = false;
		var denied = await h.Service.InspectAsync(h.Actor);
		await Assert.That(denied.AsT0.Profile!.Rows.Count).IsEqualTo(0);
	}

	[Test]
	public async Task ProfilingRequiresInspectionAndCannotCrossTheActiveCharacterBinding()
	{
		var h = new Harness();
		h.Scope = new(h.Actor, new HashSet<string> { PortalPermission.DiagnosticsProfile });
		await Assert.That((await h.Service.StartProfileAsync(h.Actor)).AsT1).IsEqualTo(DiagnosticsError.PermissionDenied);
		h.Scope = new(h.Actor, new HashSet<string> { PortalPermission.QueueInspectOwn, PortalPermission.DiagnosticsProfile });
		await h.Service.StartProfileAsync(h.Actor);
		var other = h.Actor with { ActiveCharacter = new DBRef(3, 100), Executor = new DBRef(3, 100) };
		h.Scope = h.Scope with { Actor = other };
		await Assert.That((await h.Service.InspectAsync(other)).AsT0.Profile).IsNull();
		await Assert.That((await h.Service.StopProfileAsync(other)).AsT1).IsEqualTo(DiagnosticsError.NotFound);
	}
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task StopFlushesPendingSamplesWithFreshSourceAuthorization(bool controls)
	{
		var h = new Harness();
		await h.Service.StartProfileAsync(h.Actor);
		var observation = h.Recorder.Admitted(1, new DBRef(2, 100), h.Actor.ActiveCharacter, "enqueue");
		using (observation.Enter()) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 4, true));
		h.Controls = controls;
		await Assert.That((await h.Service.StopProfileAsync(h.Actor)).IsT0).IsTrue();
		var report = (await h.Service.InspectAsync(h.Actor)).AsT0.Profile!;
		await Assert.That(report.Recording).IsFalse();
		await Assert.That(report.Rows.Sum(row => row.Count)).IsEqualTo(controls ? 1L : 0L);
		await Assert.That(h.Recorder.DrainProfileSamples().Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StoppedProfileIsDiscardedWhenAuthorityIsRevoked(bool invalidActor)
	{
		var h = new Harness();
		var originalScope = h.Scope;
		await h.Service.StartProfileAsync(h.Actor);
		var observation = h.Recorder.Admitted(1, new DBRef(2, 100), h.Actor.ActiveCharacter, "enqueue");
		using (observation.Enter()) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 4, true));
		await h.Service.StopProfileAsync(h.Actor);
		await Assert.That((await h.Service.InspectAsync(h.Actor)).AsT0.Profile!.Rows.Sum(row => row.Count)).IsEqualTo(1L);
		h.Scope = invalidActor ? null : new(h.Actor, new HashSet<string> { PortalPermission.QueueInspectOwn });
		await h.Service.CollectProfilesAsync();
		h.Scope = originalScope;
		await Assert.That((await h.Service.InspectAsync(h.Actor)).AsT0.Profile).IsNull();
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	public async Task CancelledCollectionPreservesSamplesForEveryProfile(int cancelAt)
	{
		var h = new Harness();
		var first = h.Recorder.StartProfile(h.Actor, TimeSpan.FromMinutes(1))!;
		var other = h.Actor with { AccountId = "other" };
		var second = h.Recorder.StartProfile(other, TimeSpan.FromMinutes(1))!;
		var observation = h.Recorder.Admitted(1, new DBRef(2, 100), h.Actor.ActiveCharacter, "enqueue");
		using (observation.Enter()) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 4, true));
		using var cancellation = new CancellationTokenSource();
		var interrupt = true;
		var checks = 0;
		h.Queues.CanInspectAsync(Arg.Any<QueueInspectionScope>(), Arg.Any<DBRef?>(), Arg.Any<DBRef?>(), Arg.Any<CancellationToken>())
			.Returns(_ =>
			{
				if (interrupt && ++checks == cancelAt) { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); }
				return Task.FromResult(true);
			});
		try { await h.Service.CollectProfilesAsync(cancellation.Token); throw new Exception("Expected cancellation"); }
		catch (OperationCanceledException) { }
		interrupt = false;
		using (observation.Enter()) h.Recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 4, true));
		await h.Service.StopProfileAsync(h.Actor);
		await Assert.That(h.Recorder.Profile(first.Id)!.Aggregates.Sum(row => row.Count)).IsEqualTo(2L);
		await Assert.That(h.Recorder.Profile(second.Id)!.Aggregates.Sum(row => row.Count)).IsEqualTo(2L);
		await h.Service.CollectProfilesAsync();
		await Assert.That(h.Recorder.Profile(first.Id)!.Aggregates.Sum(row => row.Count)).IsEqualTo(2L);
	}

	[Test]
	public async Task ActiveInspectionRequestsOnlyOneExtraVisibleRowForTruncation()
	{
		var h = new Harness();
		var entries = Enumerable.Range(1, 101).Select(pid => new QueueEntrySnapshot(pid, new DBRef(2, 100),
			h.Actor.ActiveCharacter, "enqueue", QueueEntryState.Ready, null, "")).ToArray();
		h.Queues.ListAsync(h.Actor, 101, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<QueueEntrySnapshot>>(entries));
		var report = (await h.Service.InspectAsync(h.Actor)).AsT0;
		await Assert.That(report.Active.Count).IsEqualTo(100);
		await Assert.That(report.ActiveTruncated).IsTrue();
		await h.Queues.DidNotReceive().ListAsync(h.Actor, Arg.Any<CancellationToken>());
	}

}
