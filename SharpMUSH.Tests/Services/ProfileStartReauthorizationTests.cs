using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class ProfileStartReauthorizationTests
{
	private static readonly CapabilityActor Actor = new("operator", new DBRef(1, 100), new DBRef(1, 100));
	private static QueueInspectionScope Granted() => new(Actor,
		new HashSet<string> { PortalPermission.QueueInspectOwn, PortalPermission.DiagnosticsProfile });

	[Test]
	[Arguments("identity", false)]
	[Arguments("inspection", false)]
	[Arguments("profiling", false)]
	[Arguments("identity", true)]
	[Arguments("inspection", true)]
	[Arguments("profiling", true)]
	public async Task RevocationDuringSchedulerProbeCannotCreateOrReplaceCapture(string revoked, bool existing)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var previous = existing ? recorder.StartProfile(Actor, TimeSpan.FromSeconds(60)) : null;
		var queues = Substitute.For<IQueueControlService>();
		QueueInspectionScope? scope = Granted();
		queues.GetInspectionScopeAsync(Actor, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<QueueInspectionScope?>(scope));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<IReadOnlyList<QueueEntrySnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
		queues.ListAsync(Actor, 1, Arg.Any<CancellationToken>()).Returns(_ => { entered.TrySetResult(); return release.Task; });
		var service = new QueueDiagnosticsService(recorder, queues, NullLogger<QueueDiagnosticsService>.Instance);
		var pending = service.StartProfileAsync(Actor);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
		scope = revoked == "identity" ? null : new(Actor, new HashSet<string>
		{
			revoked == "inspection" ? PortalPermission.DiagnosticsProfile : PortalPermission.QueueInspectOwn
		});
		release.SetResult([]);
		var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
		await Assert.That(result.Value).IsEqualTo(DiagnosticsError.PermissionDenied);
		var captures = recorder.ProfileRegistrations();
		await Assert.That(captures.Count).IsEqualTo(existing ? 1 : 0);
		if (existing) await Assert.That(captures.Single().Id).IsEqualTo(previous!.Id);
	}

	[Test]
	[Arguments(false, true)]
	[Arguments(true, true)]
	[Arguments(true, false)]
	public async Task InitialDenialUnsupportedSchedulerAndEmptyAuthorizedQueueKeepTheirMeaning(bool authorized, bool unsupported)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var queues = Substitute.For<IQueueControlService>();
		queues.GetInspectionScopeAsync(Actor, Arg.Any<CancellationToken>()).Returns(authorized ? Granted() : null);
		var probes = 0;
		queues.ListAsync(Actor, 1, Arg.Any<CancellationToken>()).Returns(_ =>
		{
			probes++;
			return unsupported ? Task.FromException<IReadOnlyList<QueueEntrySnapshot>>(new NotSupportedException())
				: Task.FromResult<IReadOnlyList<QueueEntrySnapshot>>([]);
		});
		var service = new QueueDiagnosticsService(recorder, queues, NullLogger<QueueDiagnosticsService>.Instance);
		var result = await service.StartProfileAsync(Actor);
		if (!authorized || unsupported)
		{
			await Assert.That(result.Value).IsEqualTo(!authorized ? DiagnosticsError.PermissionDenied : DiagnosticsError.Unsupported);
			await Assert.That(recorder.ProfileRegistrations().Count).IsEqualTo(0);
		}
		else
		{
			await Assert.That(recorder.ProfileRegistrations().Single().Id).IsEqualTo(result.Expect<Guid>());
		}
		await Assert.That(probes).IsEqualTo(authorized ? 1 : 0);
	}
}
