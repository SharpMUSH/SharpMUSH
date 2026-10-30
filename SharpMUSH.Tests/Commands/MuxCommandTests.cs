using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using MarkupString;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// TinyMUX's command switches and commands that PennMUSH has no spelling for: <c>/quiet</c>, <c>/now</c>,
/// <c>/instant</c>, <c>@eval</c>, <c>@mail/quick</c> and the rest.
/// </summary>
public class MuxCommandTests : ServerTestBase
{
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private async Task<(string Name, string DbRef)> CreateThing(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		return (name, await Eval($"create({name})"));
	}

	private static IDisposable MuxQuiet()
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { MuxQuiet = true }
		});

	[Test]
	public async Task SetQuietLeavesOutTheConfirmation()
	{
		var (name, thing) = await CreateThing("MuxSetQuiet");

		await Cmd($"@set {thing}=MUXLOUD:value");
		await Cmd($"@set/quiet {thing}=MUXQUIET:value");
		// A QUIET thing silences its owner's later @sets anyway, so this comes last.
		await Cmd($"@set/quiet {thing}=QUIET");

		await Assert.That(await Eval($"get({thing}/MUXQUIET) [hasflag({thing},QUIET)]")).IsEqualTo("value 1");
		await Assert.That(SetLine($"{name}/MUXQUIET - Set.")).IsFalse();
		await Assert.That(Notifications.For(God).Any(line => line.Contains($"{name} - QUIET set."))).IsFalse();
		await Assert.That(SetLine($"{name}/MUXLOUD - Set.")).IsTrue();
	}

	private bool SetLine(string line)
		=> TestHelpers.ReceivedNotifyLocalizedRendering(NotifyService, nameof(ErrorMessages.Notifications.AttributeSet), line);

	[Test]
	public async Task EvalRunsTheExpressionAndShowsNothing()
	{
		var (_, thing) = await CreateThing("MuxEval");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxEvalRan");
		var before = Notifications.CountFor(God);

		await Cmd($"@eval [attrib_set({thing}/RAN,{marker})]{marker}");

		await Assert.That(await Eval($"get({thing}/RAN)")).IsEqualTo(marker);
		await Assert.That(Notifications.For(God).Skip(before).Any(line => line.Contains(marker))).IsFalse();
	}

	[Test]
	public async Task DestroyInstantFreesTheObjectAtOnce()
	{
		var (_, thing) = await CreateThing("MuxInstant");

		await Cmd($"@destroy/instant {thing}");

		await Assert.That(await Eval($"isdbref({thing})")).IsEqualTo("0");
	}

	[Test]
	public async Task DolistNowRunsInline()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxNowRan");
		var before = Notifications.CountFor(God);

		// Queued, the @dolist's think would come after the one that follows it.
		await Cmd($"@force/inline me={{@dolist/now 1=think {marker}-##;think {marker}-after}}");

		await Notifications.WaitForAsync(God, $"{marker}-after", startIndex: before);
		var heard = Notifications.For(God).Skip(before).Where(line => line.Contains(marker)).ToArray();
		await Assert.That(heard).IsEquivalentTo([$"{marker}-1", $"{marker}-after"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>Without mux_quiet, drop/quiet and get/quiet leave out the messages and run the action.</summary>
	[Test]
	public async Task DropQuietRunsOnlyTheAction()
	{
		var (_, thing) = await CreateThing("MuxDrop");
		var message = TestIsolationHelpers.GenerateUniqueName("MuxDropMsg");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxDropAct");
		await Eval($"[attrib_set({thing}/DROP,{message})][attrib_set({thing}/SUCCESS,{message})][attrib_set({thing}/ADROP,lit(@pemit %#={action}))]");
		var before = Notifications.CountFor(God);

		await Cmd($"drop/quiet {thing}");

		await Notifications.WaitForAsync(God, action, startIndex: before);
		await Assert.That(Notifications.For(God).Skip(before).Any(line => line.Contains(message))).IsFalse();

		await Cmd($"get/quiet {thing}");
		await Assert.That(await Eval($"loc({thing})")).IsEqualTo(God.ToString());
		await Assert.That(Notifications.For(God).Skip(before).Any(line => line.Contains(message))).IsFalse();
	}

	/// <summary>TinyMUX's drop/quiet and get/quiet: the dropper still sees its message; the o-message and action are left out.</summary>
	[Test]
	public async Task DropAndGetQuietKeepYourMessageAndLeaveOutTheAction()
	{
		using var _ = MuxQuiet();
		var (_, thing) = await CreateThing("MuxDrop");
		var dropped = TestIsolationHelpers.GenerateUniqueName("MuxDropMsg");
		var taken = TestIsolationHelpers.GenerateUniqueName("MuxTakeMsg");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxDropAct");
		await Eval($"[attrib_set({thing}/DROP,{dropped})][attrib_set({thing}/SUCCESS,{taken})][attrib_set({thing}/ADROP,lit(@pemit %#={action}))][attrib_set({thing}/ASUCCESS,lit(@pemit %#={action}))]");
		var before = Notifications.CountFor(God);

		await Cmd($"drop/quiet {thing}");
		await Cmd($"get/quiet {thing}");
		await WebAppFactoryArg.QueueBarrierAsync();

		var heard = Notifications.For(God).Skip(before).ToArray();
		await Assert.That(await Eval($"loc({thing})")).IsEqualTo(God.ToString());
		await Assert.That(heard.Any(line => line.Contains(dropped))).IsTrue();
		await Assert.That(heard.Any(line => line.Contains(taken))).IsTrue();
		await Assert.That(heard.Any(line => line.Contains(action))).IsFalse();
	}

	/// <summary>TinyMUX honours /quiet only on what the mover controls; otherwise the command runs in full.</summary>
	[Test]
	public async Task DropQuietOnSomethingYouDoNotControlRunsInFull()
	{
		using var _ = MuxQuiet();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxDropOther");
		var (_, thing) = await CreateThing("MuxDropOther");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxDropOtherAct");
		await Eval($"attrib_set({thing}/ADROP,lit(@pemit %#={action}))");
		await Cmd($"@tel {thing}={player.DbRef}");
		var before = Notifications.CountFor(player.DbRef);

		await CmdAs(player.DbRef, player.Handle, $"drop/quiet {thing}");

		await Notifications.WaitForAsync(player.DbRef, action, startIndex: before);
	}

	/// <summary>A wizard's give/quiet leaves out only the default notices of the gift changing hands.</summary>
	[Test]
	public async Task GiveQuietLeavesOutTheGivenNotices()
	{
		using var _ = MuxQuiet();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxGive");
		var (name, thing) = await CreateThing("MuxGift");
		await Cmd($"@set {player.DbRef}=ENTER_OK");
		await Cmd($"@tel {player.DbRef}=here");
		var godBefore = Notifications.CountFor(God);
		var playerBefore = Notifications.CountFor(player.DbRef);

		await Cmd($"give/quiet {player.DbRef}={thing}");
		await WebAppFactoryArg.QueueBarrierAsync();

		await Assert.That(await Eval($"loc({thing})")).IsEqualTo(player.DbRef.ToString());
		await Assert.That(Notifications.For(God).Skip(godBefore).Any(line => line.Contains(name))).IsFalse();
		await Assert.That(Notifications.For(player.DbRef).Skip(playerBefore).Any(line => line.Contains(name))).IsFalse();
	}

	[Test]
	public async Task MailQuickTakesTheSubjectOnTheLeft()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxMailQuick");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail/quick #{player.DbRef.Number}/Quick a/b=Quick body"));

		await Assert.That(await EvalAs(player.DbRef, "mailsubject(1)")).IsEqualTo("Quick a/b");
		await Assert.That(await EvalAs(player.DbRef, "mail(1)")).IsEqualTo("Quick body");
	}

	[Test]
	public async Task MailQuickKeepsAMessageThatStartsWithASlash()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxMailSlash");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"@mail/quick #{player.DbRef.Number}/Topic=/path"));

		await Assert.That(await EvalAs(player.DbRef, "mailsubject(1)")).IsEqualTo("Topic");
		await Assert.That(await EvalAs(player.DbRef, "mail(1)")).IsEqualTo("/path");
	}

	[Test]
	[Arguments("wizard")]
	[Arguments("admin")]
	public async Task WallAudienceSwitchesNeedTheirOwnCommandsLock(string audience)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxWall");
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxWallMsg");
		// ANNOUNCE passes @wall's lock and neither @wizwall's nor @rwall's.
		await Cmd($"@power #{player.DbRef.Number}=Announce");
		var before = Notifications.CountFor(player.DbRef);

		await CmdAs(player.DbRef, player.Handle, $"@wall/{audience} {marker}");
		await WebAppFactoryArg.QueueBarrierAsync();

		var heard = Notifications.For(player.DbRef).Skip(before).ToArray();
		await Assert.That(heard).Contains(line => line.Contains("Permission denied"));
		await Assert.That(Notifications.For(God).Any(line => line.Contains(marker))).IsFalse();
	}

	/// <summary>Without mux_quiet, enter/quiet leaves out @move and @omove and still runs @amove.</summary>
	[Test]
	public async Task EnterQuietStillRunsTheMoveAction()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxEnter");
		var box = TestIsolationHelpers.GenerateUniqueName("MuxBox");
		var message = TestIsolationHelpers.GenerateUniqueName("MuxMoveMsg");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxMoveAct");
		await CmdAs(player.DbRef, player.Handle, $"@create {box}");
		await CmdAs(player.DbRef, player.Handle, $"drop {box}");
		await CmdAs(player.DbRef, player.Handle, $"&MOVE me={message}");
		await CmdAs(player.DbRef, player.Handle, $"&AMOVE me=@pemit me={action}");
		var before = Notifications.CountFor(player.DbRef);

		await CmdAs(player.DbRef, player.Handle, $"enter/quiet {box}");

		await Notifications.WaitForAsync(player.DbRef, action, startIndex: before);
		await Assert.That(await EvalAs(player.DbRef, "name(loc(me))")).IsEqualTo(box);
		await Assert.That(Notifications.For(player.DbRef).Skip(before).Any(line => line.Contains(message))).IsFalse();
	}

	/// <summary>
	/// TinyMUX's enter/quiet and leave/quiet hush that side of the move: the container's ENTER or LEAVE message
	/// is left out and its action still runs, and the mover's own MOVE triad runs as always.
	/// </summary>
	[Test]
	public async Task EnterAndLeaveQuietHushTheirSideOfTheMove()
	{
		using var _ = MuxQuiet();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxEnter");
		var box = TestIsolationHelpers.GenerateUniqueName("MuxBox");
		var entered = TestIsolationHelpers.GenerateUniqueName("MuxEnterMsg");
		var left = TestIsolationHelpers.GenerateUniqueName("MuxLeaveMsg");
		var moved = TestIsolationHelpers.GenerateUniqueName("MuxMoveMsg");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxBoxAct");
		await CmdAs(player.DbRef, player.Handle, $"@create {box}");
		await CmdAs(player.DbRef, player.Handle, $"drop {box}");
		await CmdAs(player.DbRef, player.Handle, $"&ENTER {box}={entered}");
		await CmdAs(player.DbRef, player.Handle, $"&AENTER {box}=@pemit %#={action}-in");
		await CmdAs(player.DbRef, player.Handle, $"&LEAVE {box}={left}");
		await CmdAs(player.DbRef, player.Handle, $"&ALEAVE {box}=@pemit %#={action}-out");
		await CmdAs(player.DbRef, player.Handle, $"&MOVE me={moved}");
		var before = Notifications.CountFor(player.DbRef);

		await CmdAs(player.DbRef, player.Handle, $"enter/quiet {box}");
		await Notifications.WaitForAsync(player.DbRef, $"{action}-in", startIndex: before);
		await Assert.That(await EvalAs(player.DbRef, "name(loc(me))")).IsEqualTo(box);

		await CmdAs(player.DbRef, player.Handle, "leave/quiet");
		await Notifications.WaitForAsync(player.DbRef, $"{action}-out", startIndex: before);

		var heard = Notifications.For(player.DbRef).Skip(before).ToArray();
		await Assert.That(heard.Any(line => line.Contains(entered) || line.Contains(left))).IsFalse();
		await Assert.That(heard.Count(line => line.Contains(moved))).IsEqualTo(2);
	}

	/// <summary>TinyMUX's goto/quiet, through an exit the mover controls, leaves out the exit's o-messages and actions.</summary>
	[Test]
	public async Task GotoQuietLeavesOutTheExitsActionsOnlyThroughYourOwnExit()
	{
		using var _ = MuxQuiet();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxGoto");
		var name = TestIsolationHelpers.GenerateUniqueName("MuxGoto");
		var passed = TestIsolationHelpers.GenerateUniqueName("MuxGotoMsg");
		var action = TestIsolationHelpers.GenerateUniqueName("MuxGotoAct");
		var rooms = (await Eval($"[setq(0,num(dig({name}A)))][setq(1,num(dig({name}B)))]%q0 %q1")).Split(' ');
		var mine = await Eval($"open({name}Mine,{rooms[1]},{rooms[0]})");
		var theirs = await Eval($"open({name}Theirs,{rooms[0]},{rooms[1]})");
		await Cmd($"@chown {mine}={player.DbRef}");
		foreach (var exit in new[] { mine, theirs })
		{
			await Eval($"[attrib_set({exit}/SUCCESS,{passed})][attrib_set({exit}/ASUCCESS,lit(@pemit %#={action}-{exit}))]");
		}

		await Cmd($"@tel {player.DbRef}={rooms[0]}");
		var before = Notifications.CountFor(player.DbRef);

		await CmdAs(player.DbRef, player.Handle, $"goto/quiet {name}Mine");
		await CmdAs(player.DbRef, player.Handle, $"goto/quiet {name}Theirs");
		await Notifications.WaitForAsync(player.DbRef, $"{action}-{theirs}", startIndex: before);

		var heard = Notifications.For(player.DbRef).Skip(before).ToArray();
		await Assert.That(await EvalAs(player.DbRef, "num(loc(me))")).IsEqualTo(rooms[0]);
		await Assert.That(heard.Count(line => line.Contains(passed))).IsEqualTo(2);
		await Assert.That(heard.Any(line => line.Contains($"{action}-{mine}"))).IsFalse();
	}

	/// <summary>TinyMUX's @teleport/quiet leaves out the teleport messages and the room's arrival message; MOVE still runs.</summary>
	[Test]
	public async Task TeleportQuietKeepsTheMoveTriad()
	{
		using var _ = MuxQuiet();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxTel");
		var name = TestIsolationHelpers.GenerateUniqueName("MuxTel");
		var tported = TestIsolationHelpers.GenerateUniqueName("MuxTportMsg");
		var moved = TestIsolationHelpers.GenerateUniqueName("MuxTelMove");
		var room = await Eval($"num(dig({name}))");
		await CmdAs(player.DbRef, player.Handle, $"&TPORT me={tported}");
		await CmdAs(player.DbRef, player.Handle, $"&MOVE me={moved}");
		var before = Notifications.CountFor(player.DbRef);

		await Cmd($"@teleport/quiet {player.DbRef}={room}");
		await Notifications.WaitForAsync(player.DbRef, moved, startIndex: before);

		await Assert.That(await EvalAs(player.DbRef, "num(loc(me))")).IsEqualTo(room);
		await Assert.That(Notifications.For(player.DbRef).Skip(before).Any(line => line.Contains(tported))).IsFalse();
	}

	/// <summary>Without mux_quiet, @teleport/quiet is @teleport/silent: the teleport messages and @move are left out.</summary>
	[Test]
	public async Task TeleportQuietIsSilentWithoutMuxQuiet()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MuxTelSilent");
		var name = TestIsolationHelpers.GenerateUniqueName("MuxTelSilent");
		var tported = TestIsolationHelpers.GenerateUniqueName("MuxTelSilentTport");
		var moved = TestIsolationHelpers.GenerateUniqueName("MuxTelSilentMove");
		var room = await Eval($"num(dig({name}))");
		await CmdAs(player.DbRef, player.Handle, $"&TPORT me={tported}");
		await CmdAs(player.DbRef, player.Handle, $"&MOVE me={moved}");
		var before = Notifications.CountFor(player.DbRef);

		await Cmd($"@teleport/quiet {player.DbRef}={room}");
		await WebAppFactoryArg.QueueBarrierAsync();

		var heard = Notifications.For(player.DbRef).Skip(before).ToArray();
		await Assert.That(await EvalAs(player.DbRef, "num(loc(me))")).IsEqualTo(room);
		await Assert.That(heard.Any(line => line.Contains(tported) || line.Contains(moved))).IsFalse();
	}

	[Test]
	[Arguments("@pemit/object me=")]
	[Arguments("@force/now me=think ")]
	[Arguments("@emit/here ")]
	[Arguments("@switch/now 1=1,think ")]
	public async Task TinyMuxSwitchesReachTheTarget(string command)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxSwitch");
		var before = Notifications.CountFor(God);

		await Cmd($"{command}{marker}");

		await Notifications.WaitForAsync(God, marker, startIndex: before);
	}
}
