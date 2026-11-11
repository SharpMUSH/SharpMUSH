using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// TinyMUX's and RhostMUSH's switches SharpMUSH has under the same name: <c>@sweep/commands</c>,
/// <c>/listeners</c> and <c>/players</c>, <c>@stats/all</c> and <c>/me</c>, <c>@wall/pose</c> and <c>@trigger/notify</c>.
/// </summary>
public class DialectSwitchBehaviourTests : ServerTestBase
{
	private async Task<List<string>> HeardWhile(DBRef who, Func<Task> action)
	{
		var before = Notifications.CountFor(who);
		await action();
		await WebAppFactoryArg.QueueBarrierAsync();
		return [.. Notifications.For(who).Skip(before)];
	}

	[Test]
	public async Task SweepNamesOnlyTheKindsAskedFor()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "SweepKinds");
		var commander = TestIsolationHelpers.GenerateUniqueName("SweepCmd");
		var listener = TestIsolationHelpers.GenerateUniqueName("SweepEar");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@create {commander}");
			await CmdAs(actor.DbRef, actor.Handle, $"&CMD {commander}=$sweepkindtest:think hi");
			await CmdAs(actor.DbRef, actor.Handle, $"@create {listener}");
			await CmdAs(actor.DbRef, actor.Handle, $"@listen {listener}=*");

			var commands = await HeardWhile(actor.DbRef, () => CmdAs(actor.DbRef, actor.Handle, "@sweep/inventory/commands"));
			await Assert.That(commands.Any(line => line.Contains(commander, StringComparison.Ordinal))).IsTrue();
			await Assert.That(commands.Any(line => line.Contains(listener, StringComparison.Ordinal))).IsFalse();

			var listeners = await HeardWhile(actor.DbRef, () => CmdAs(actor.DbRef, actor.Handle, "@sweep/inventory/listeners"));
			await Assert.That(listeners.Any(line => line.Contains(listener, StringComparison.Ordinal))).IsTrue();
			await Assert.That(listeners.Any(line => line.Contains(commander, StringComparison.Ordinal))).IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task StatsMeCountsYourOwnObjectsWhateverTheArgument()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "StatsMeSwitch");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@create {TestIsolationHelpers.GenerateUniqueName("StatsMeThing")}");

			var heard = await HeardWhile(actor.DbRef, () => CmdAs(actor.DbRef, actor.Handle, "@stats/me Somebody"));

			await Assert.That(heard).Contains("2 objects = 0 rooms, 0 exits, 1 things, 1 players.");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task WallPosePutsTheNameFirst()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "WallPose");
		var token = TestIsolationHelpers.GenerateUniqueName("wallposed");
		try
		{
			await Cmd($"@set {actor.DbRef}=WIZARD");

			var shout = await CmdAs(actor.DbRef, actor.Handle, $"@wall/pose/emit waves {token}");

			await Assert.That(shout).IsEqualTo($"{actor.Name} waves {token}");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>@trigger/notify notifies the executor's semaphore once the attribute is queued.</summary>
	[Test]
	public async Task TriggerNotifyReleasesAWaitOnTheExecutor()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TriggerNotify");
		var thing = TestIsolationHelpers.GenerateUniqueName("TriggerNotifyThing");
		var token = TestIsolationHelpers.GenerateUniqueName("triggernotified");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@create {thing}");
			await CmdAs(actor.DbRef, actor.Handle, $"&DO {thing}=think done");

			var plain = await HeardWhile(actor.DbRef, async () =>
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@wait me=@pemit me={token}");
				await CmdAs(actor.DbRef, actor.Handle, $"@trigger {thing}/DO");
			});
			await Assert.That(plain.Any(line => line.Contains(token, StringComparison.Ordinal))).IsFalse();

			// The queued @notify releases the wait from the queue, so the woken entry needs a second pass.
			var notified = await HeardWhile(actor.DbRef, async () =>
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@trigger/notify/quiet {thing}/DO");
				await WebAppFactoryArg.QueueBarrierAsync();
			});
			await Assert.That(notified.Count(line => line.Contains(token, StringComparison.Ordinal))).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>@trigger/now/notify runs the attribute in place and still notifies the executor afterwards.</summary>
	[Test]
	public async Task TriggerNowNotifyStillReleasesTheWait()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TriggerNowNotify");
		var thing = TestIsolationHelpers.GenerateUniqueName("TriggerNowNotifyThing");
		var token = TestIsolationHelpers.GenerateUniqueName("triggernownotified");
		try
		{
			await CmdAs(actor.DbRef, actor.Handle, $"@create {thing}");
			await CmdAs(actor.DbRef, actor.Handle, $"&DO {thing}=think done");

			var notified = await HeardWhile(actor.DbRef, async () =>
			{
				await CmdAs(actor.DbRef, actor.Handle, $"@wait me=@pemit me={token}");
				await CmdAs(actor.DbRef, actor.Handle, $"@trigger/now/notify/quiet {thing}/DO");
				await WebAppFactoryArg.QueueBarrierAsync();
			});
			await Assert.That(notified.Count(line => line.Contains(token, StringComparison.Ordinal))).IsEqualTo(1);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
