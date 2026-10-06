using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <c>POST api/commands</c> admits its work uncharged, as socket input is, so nothing in the queue
/// bounds how many commands one account can stack up. The service does: past
/// <see cref="PortalCommandOptions.MaxPendingPerAccount"/> an account's request is refused without
/// being queued, and a slot comes back only when its entry leaves the queue.
/// </summary>
public class PortalCommandPendingLimitTests
{
	private const int Limit = 2;

	/// <summary>A queue whose entries never run until the test releases them, as a busy queue holds them.</summary>
	private sealed class HeldQueue
	{
		public ITaskScheduler Scheduler { get; } = Substitute.For<ITaskScheduler>();
		private readonly List<Action> _releases = [];
		private long _pid;

		public HeldQueue()
		{
			Scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
				.Returns(call =>
				{
					lock (_releases) _releases.Add(call.Arg<Action?>()!);
					return ValueTask.FromResult(new QueueAdmissionResult(Interlocked.Increment(ref _pid), QueueRejectionReason.None));
				});
		}

		public int Admitted
		{
			get { lock (_releases) return _releases.Count; }
		}

		/// <summary>The oldest held entry leaves the queue without running, as <c>@halt</c> takes it.</summary>
		public void ReleaseOldest()
		{
			Action release;
			lock (_releases)
			{
				release = _releases[0];
				_releases.RemoveAt(0);
			}
			release();
		}
	}

	private static PortalCommandService Service(HeldQueue queue) => new(
		Substitute.For<IMUSHCodeParser>(),
		queue.Scheduler,
		new CommandOutputCapture(),
		Substitute.For<IMediator>(),
		Substitute.For<IPermissionService>(),
		Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
		Options.Create(new PortalCommandOptions { MaxPendingPerAccount = Limit }),
		NullLogger<PortalCommandService>.Instance);

	private static SharpPlayer Player(int key) =>
		new TestObjectFactory().CreatePlayer(key, $"Busy{key}") is SharpPlayer player
			? player
			: throw new InvalidOperationException("The factory made no player.");

	[Test]
	public async Task AnAccountPastItsLimit_IsRefused_WithoutQueueingAnything()
	{
		var queue = new HeldQueue();
		var service = Service(queue);
		var player = Player(41);

		var waiting = Enumerable.Range(0, Limit)
			.Select(_ => service.RunAsync("account-a", player, new PortalCommandRequest("think")).AsTask())
			.ToList();
		var refused = await service.RunAsync("account-a", player, new PortalCommandRequest("think"));

		await Assert.That(refused is TooManyCommands { Limit: Limit }).IsTrue();
		await Assert.That(queue.Admitted).IsEqualTo(Limit);
		await Assert.That(waiting.Any(task => task.IsCompleted)).IsFalse();
	}

	/// <summary>One account's backlog is its own: another account's command still goes in.</summary>
	[Test]
	public async Task TheLimitIsPerAccount()
	{
		var queue = new HeldQueue();
		var service = Service(queue);
		var player = Player(42);

		for (var i = 0; i < Limit; i++) _ = service.RunAsync("account-b", player, new PortalCommandRequest("think")).AsTask();
		_ = service.RunAsync("account-c", player, new PortalCommandRequest("think")).AsTask();

		await Assert.That(queue.Admitted).IsEqualTo(Limit + 1);
	}

	/// <summary>
	/// A request that stops waiting does not free its slot: its entry is still in the queue. The slot
	/// comes back when the entry leaves it, and the next command is admitted.
	/// </summary>
	[Test]
	public async Task ASlotIsFreed_WhenTheEntryLeavesTheQueue_NotWhenTheCallerGivesUp()
	{
		var queue = new HeldQueue();
		var service = Service(queue);
		var player = Player(43);
		using var gaveUp = new CancellationTokenSource();

		var abandoned = service.RunAsync("account-d", player, new PortalCommandRequest("think"), gaveUp.Token).AsTask();
		var held = service.RunAsync("account-d", player, new PortalCommandRequest("think")).AsTask();
		await gaveUp.CancelAsync();
		await Assert.That(async () => await abandoned).Throws<OperationCanceledException>();

		var stillFull = await service.RunAsync("account-d", player, new PortalCommandRequest("think"));
		await Assert.That(stillFull is TooManyCommands).IsTrue();

		queue.ReleaseOldest();
		var next = service.RunAsync("account-d", player, new PortalCommandRequest("think")).AsTask();

		await Assert.That(queue.Admitted).IsEqualTo(Limit);
		await Assert.That(next.IsCompleted).IsFalse();
		await Assert.That(held.IsCompleted).IsFalse();
	}

	/// <summary>A command the queue itself refused holds no slot afterwards.</summary>
	[Test]
	public async Task ACommandTheQueueRefused_GivesItsSlotBack()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
			.Returns(ValueTask.FromResult(new QueueAdmissionResult(null, QueueRejectionReason.GlobalLimit)));
		var service = new PortalCommandService(Substitute.For<IMUSHCodeParser>(), scheduler, new CommandOutputCapture(),
			Substitute.For<IMediator>(), Substitute.For<IPermissionService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(),
			Options.Create(new PortalCommandOptions { MaxPendingPerAccount = Limit }), NullLogger<PortalCommandService>.Instance);
		var player = Player(44);

		for (var i = 0; i < Limit + 2; i++)
		{
			var outcome = await service.RunAsync("account-e", player, new PortalCommandRequest("think"));
			await Assert.That(outcome is Error<string>).IsTrue();
		}
	}
}
