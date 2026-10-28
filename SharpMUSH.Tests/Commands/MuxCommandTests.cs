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

	[Test]
	[Arguments("@pemit/object me=")]
	[Arguments("@force/now me=think ")]
	public async Task TinyMuxSwitchesReachTheTarget(string command)
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxSwitch");
		var before = Notifications.CountFor(God);

		await Cmd($"{command}{marker}");

		await Notifications.WaitForAsync(God, marker, startIndex: before);
	}
}
