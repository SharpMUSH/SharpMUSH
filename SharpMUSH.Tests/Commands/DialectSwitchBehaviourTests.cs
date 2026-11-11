using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// TinyMUX's and RhostMUSH's switches SharpMUSH has under the same name: <c>@sweep/commands</c>,
/// <c>/listeners</c> and <c>/players</c>, <c>@stats/all</c> and <c>/me</c>, <c>@wall/pose</c>, <c>@trigger/notify</c>,
/// <c>@clone/parent</c> and <c>/inventory</c>, <c>@wipe/regexp</c> and <c>@decompile/regexp</c>, <c>@verb/no_name</c>
/// and <c>@cpattr/clear</c>. <c>page/blind</c> is in <see cref="PageCommandTests"/>.
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

	[Test]
	public async Task CloneParentInheritsInsteadOfCopying()
	{
		var original = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("CloneParentOriginal")}"));
		await Cmd($"&GREETING {original}=hello");

		var clone = DBRef.Parse(await Cmd($"@clone/parent {original}"));

		await Assert.That(await Eval($"[parent({clone})] [hasattr({clone},GREETING)] [get({clone}/GREETING)]"))
			.IsEqualTo($"#{original.Number} 0 hello");
	}

	[Test]
	public async Task CloneInventoryPutsTheCloneInYourHands()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CloneInventory");
		var thing = TestIsolationHelpers.GenerateUniqueName("CloneInventoryThing");
		try
		{
			var original = DBRef.Parse(await CmdAs(actor.DbRef, actor.Handle, $"@create {thing}"));
			await CmdAs(actor.DbRef, actor.Handle, $"@drop {thing}");

			var beside = await CmdAs(actor.DbRef, actor.Handle, $"@clone #{original.Number}");
			var carried = await CmdAs(actor.DbRef, actor.Handle, $"@clone/inventory #{original.Number}");

			await Assert.That(await Eval($"[loc({beside})] [loc({carried})]"))
				.IsEqualTo($"{await Eval($"loc({actor.DbRef})")} #{actor.DbRef.Number}");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task WipeAndDecompileTakeARegularExpression()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("WipeRegexp")}"));
		await Cmd($"&AB1 {thing}=one");
		await Cmd($"&AB2 {thing}=two");
		await Cmd($"&XAB {thing}=three");

		var decompiled = await HeardWhile(WebAppFactoryArg.ExecutorDBRef, () => Cmd($"@decompile/db/regexp {thing}/^AB"));
		await Assert.That(decompiled.Count(line => line.StartsWith("&AB", StringComparison.Ordinal)
			&& line.Contains($"#{thing.Number}=", StringComparison.Ordinal))).IsEqualTo(2);
		await Assert.That(decompiled.Any(line => line.StartsWith("&XAB", StringComparison.Ordinal)
			&& line.Contains($"#{thing.Number}=", StringComparison.Ordinal))).IsFalse();

		await Cmd($"&AB1`CHILD {thing}=four");
		await Cmd($"&_KEEP {thing}=engine");

		var invalid = await HeardWhile(WebAppFactoryArg.ExecutorDBRef, () => Cmd($"@wipe/regexp {thing}/*AB"));
		await Assert.That(invalid).Contains("Invalid Regular Expression.");

		var wiped = await HeardWhile(WebAppFactoryArg.ExecutorDBRef, () => Cmd($"@wipe/regexp {thing}/^AB|^_KEEP"));
		// @wipe's own tally, as a wildcard wipe gives it.
		await Assert.That(wiped.Any(line => line.EndsWith(" attributes wiped.", StringComparison.Ordinal))).IsTrue();
		await Assert.That(await Eval($"lattr({thing}/**)")).IsEqualTo("_KEEP XAB");
	}

	[Test]
	public async Task VerbNoNameLeavesTheNameOffOnlyTheDefault()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(WebAppFactoryArg.ExecutorDBRef)))
			.Expect<AnySharpObject>().Expect<SharpPlayer>();
		var room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("VerbNoName"), god));
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoNameActor", room);
		var observer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "VerbNoNameEar", room);
		var token = TestIsolationHelpers.GenerateUniqueName("verbed");
		try
		{
			var plain = await HeardWhile(observer.DbRef,
				() => CmdAs(actor.DbRef, actor.Handle, $"@verb me=me,,,,tests {token}.,"));
			var bare = await HeardWhile(observer.DbRef,
				() => CmdAs(actor.DbRef, actor.Handle, $"@verb/no_name me=me,,,,{token} happens.,"));
			await CmdAs(actor.DbRef, actor.Handle, $"&OTEST me=owhat {token}");
			var fromAttribute = await HeardWhile(observer.DbRef,
				() => CmdAs(actor.DbRef, actor.Handle, $"@verb/no_name me=me,,,OTEST,unused,"));

			await Assert.That(plain).Contains($"{actor.Name} tests {token}.");
			await Assert.That(bare).Contains($"{token} happens.");
			await Assert.That(fromAttribute).Contains($"{actor.Name} owhat {token}");
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
			await ConnectionService.Disconnect(observer.Handle);
		}
	}

	[Test]
	public async Task CpattrClearRemovesTheSourceOnceCopied()
	{
		var source = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("CpattrClearFrom")}"));
		var target = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("CpattrClearTo")}"));
		await Cmd($"&MOVED {source}=carried");
		await Cmd($"&KEPT {source}=stays");

		await Cmd($"@cpattr {source}/KEPT={target}");
		await Cmd($"@cpattr/clear {source}/MOVED={target}");

		await Assert.That(await Eval($"[lattr({source})] [get({target}/MOVED)] [get({target}/KEPT)]"))
			.IsEqualTo("KEPT carried stays");
	}
}
