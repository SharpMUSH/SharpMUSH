using System.Reflection;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueueEnumerationOrderingTests
{
	private static Scheduler Create()
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = 1000, PlayerQueueLimit = 1000 } });
		return new(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), Substitute.For<ISchedulerFactory>(),
			Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance, options);
	}

	private static async Task<List<QueueCommandReservation>> ReserveSparsePids(Scheduler queue, int count)
	{
		var reservations = new List<QueueCommandReservation>();
		// Model a long-running server with large PID gaps without running millions of commands.
		var counter = typeof(Scheduler).GetField("_nextPid", BindingFlags.Instance | BindingFlags.NonPublic)!;
		for (var index = 0; index < count; index++)
		{
			counter.SetValue(queue, index * 1_000_003L);
			var reservation = await queue.ReserveCommandList(MarkupText.Plain("think pending"), ParserState.RootFor(new DBRef(10)));
			await Assert.That(reservation.Admission.Accepted).IsTrue();
			reservations.Add(reservation);
		}
		return reservations;
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SparsePidsRetainAscendingOrderInFullAndBoundedInspection(bool bounded)
	{
		await using var queue = Create();
		var reservations = await ReserveSparsePids(queue, 300);
		try
		{
			var expected = reservations.Select(entry => entry.Admission.Pid!.Value).Take(bounded ? 101 : 300);
			IEnumerable<QueueEntrySnapshot> actual;
			if (bounded)
			{
				var actor = new CapabilityActor("operator", new DBRef(1, 100), new DBRef(1, 100));
				var capabilities = Substitute.For<IAdministrativeCapabilityService>();
				capabilities.GetGameActorAsync(actor.Executor!.Value, Arg.Any<CancellationToken>()).Returns(actor);
				capabilities.GetGrantedScopesAsync(actor, Arg.Any<CancellationToken>()).Returns(new HashSet<string> { PortalPermission.QueueInspect });
				var control = new QueueControlService(queue, capabilities, Substitute.For<IMediator>(), Substitute.For<IPermissionService>());
				actual = await control.ListAsync(actor, 101);
			}
			else actual = queue.EnumerateQueueEntries();
			await Assert.That(string.Join(',', actual.Select(entry => entry.Pid))).IsEqualTo(string.Join(',', expected));
		}
		finally { foreach (var reservation in reservations) reservation.Dispose(); }
	}

	[Test]
	public async Task TraversalRereadsRemovedEntriesAndDoesNotGrowWithNewAdmissions()
	{
		await using var queue = Create();
		var reservations = await ReserveSparsePids(queue, 300);
		try
		{
			using var entries = queue.EnumerateQueueEntries().GetEnumerator();
			await Assert.That(entries.MoveNext()).IsTrue();
			var first = entries.Current.Pid;
			reservations[1].Dispose();
			using var later = await queue.ReserveCommandList(MarkupText.Plain("think later"), ParserState.RootFor(new DBRef(10)));
			var remainder = new List<long>();
			while (entries.MoveNext()) remainder.Add(entries.Current.Pid);
			await Assert.That(first).IsEqualTo(reservations[0].Admission.Pid!.Value);
			await Assert.That(string.Join(',', remainder)).IsEqualTo(string.Join(',', reservations.Skip(2).Select(entry => entry.Admission.Pid!.Value)));
		}
		finally { foreach (var reservation in reservations) reservation.Dispose(); }
	}
}
