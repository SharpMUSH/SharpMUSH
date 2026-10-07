using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using QueueScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Commands;

public class WizardCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
	private QueueScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<QueueScheduler>();

	/// <summary>
	/// Everything <paramref name="who"/> was notified of while <paramref name="action"/> ran, in
	/// order. The <see cref="INotifyService"/> substitute is shared across the whole test session,
	/// so a bare <c>DidNotReceive()</c> asserts that the recipient was never sent that message by
	/// ANY test - which makes the assertion pass or fail on what else happened to run. Windowing
	/// through the recipient-keyed recorder scopes it to this command.
	/// </summary>
	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	private Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private Task RunAs(TestIsolationHelpers.TestPlayer player, string command) =>
		WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)).AsTask();

	private Task AsGod(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	private async Task<string> Eval(string expression) =>
		(await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	/// <summary>A thing of <paramref name="owner"/>'s, not HALTed, and its unique name.</summary>
	private async Task<(DBRef Thing, string Name)> OwnedThingAsync(TestIsolationHelpers.TestPlayer owner, string prefix)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);
		await AsGod($"@chown/preserve {thing}={owner.DbRef}");
		await AsGod($"@set {thing}=!HALT");
		return (thing, await Eval($"name({thing})"));
	}

	/// <summary>
	/// PennMUSH <c>do_halt</c> (<c>src/cque.c:2176-2178</c>) tells the owner <c>Halted: QA(#n)</c>, and
	/// <c>do_halt1</c> says nothing more when the enactor owns the object, but leaves it HALTed
	/// (<c>:2277-2278</c>). SharpMUSH said <c>Halted QA.</c>.
	/// </summary>
	[Test]
	public async ValueTask HaltOwnObjectReportsHaltedNameAndDbref()
	{
		var owner = await PlayerAsync("HaltOwner");
		try
		{
			var (thing, name) = await OwnedThingAsync(owner, "HaltOwnThing");

			var heard = await MessagesWhile(owner.DbRef, () => RunAs(owner, $"@halt {thing}"));

			await Assert.That(heard.Where(message => message.Contains(name))).IsEquivalentTo([$"Halted: {name}(#{thing.Number})"]);
			await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("1");
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
		}
	}

	/// <summary>
	/// Halting someone else's object tells the enactor whose it was and the owner who did it
	/// (<c>src/cque.c:2267-2275</c>), after <c>do_halt</c>'s own report to the owner.
	/// </summary>
	[Test]
	public async ValueTask HaltOthersObjectTellsEnactorAndOwner()
	{
		var owner = await PlayerAsync("HaltVictimOwner");
		var wizard = await PlayerAsync("HaltWizard");
		try
		{
			await AsGod($"@set {wizard.DbRef}=WIZARD");
			var (thing, name) = await OwnedThingAsync(owner, "HaltOthersThing");
			var ownerBefore = WebAppFactoryArg.Notifications.CountFor(owner.DbRef);

			var wizardHeard = await MessagesWhile(wizard.DbRef, () => RunAs(wizard, $"@halt {thing}"));
			var ownerHeard = WebAppFactoryArg.Notifications.For(owner.DbRef).Skip(ownerBefore).ToList();

			await Assert.That(wizardHeard.Where(message => message.Contains(name)))
				.IsEquivalentTo([$"Halted: {owner.Name}'s {name}(#{thing.Number})"]);
			await Assert.That(ownerHeard.Where(message => message.Contains(name))).IsEquivalentTo(
				[$"Halted: {name}(#{thing.Number})", $"Halted: {name}(#{thing.Number}), by {wizard.Name}"]);
			await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("1");
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
			await ConnectionService.Disconnect(wizard.Handle);
		}
	}

	/// <summary>
	/// <c>@halt</c> alone is <c>do_halt</c> on the enactor (<c>src/cque.c:2239-2240</c>); <c>@halt me</c>
	/// adds <c>All of your objects have been halted.</c> (<c>:2259</c>). Neither sets HALT on a player.
	/// </summary>
	[Test]
	public async ValueTask HaltSelfReportsAsPennMUSHDoes()
	{
		var player = await PlayerAsync("HaltSelf");
		try
		{
			var bare = await MessagesWhile(player.DbRef, () => RunAs(player, "@halt"));
			var me = await MessagesWhile(player.DbRef, () => RunAs(player, "@halt me"));

			await Assert.That(bare).Contains($"Halted: {player.Name}(#{player.DbRef.Number})");
			await Assert.That(bare).DoesNotContain("All of your objects have been halted.");
			await Assert.That(me).Contains($"Halted: {player.Name}(#{player.DbRef.Number})");
			await Assert.That(me).Contains("All of your objects have been halted.");
			await Assert.That(await Eval($"hasflag({player.DbRef},HALT)")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>Halting another player (<c>src/cque.c:2260-2264</c>).</summary>
	[Test]
	public async ValueTask HaltOtherPlayerTellsBothPlayers()
	{
		var victim = await PlayerAsync("HaltedPlayer");
		var wizard = await PlayerAsync("HaltPlayerWizard");
		try
		{
			await AsGod($"@set {wizard.DbRef}=WIZARD");
			var victimBefore = WebAppFactoryArg.Notifications.CountFor(victim.DbRef);

			var wizardHeard = await MessagesWhile(wizard.DbRef, () => RunAs(wizard, $"@halt {victim.DbRef}"));
			var victimHeard = WebAppFactoryArg.Notifications.For(victim.DbRef).Skip(victimBefore).ToList();

			await Assert.That(wizardHeard.Where(message => message.Contains(victim.Name)))
				.IsEquivalentTo([$"All objects for {victim.Name} have been halted."]);
			await Assert.That(victimHeard.Where(message => message.Contains(victim.Name) || message.Contains(wizard.Name))).IsEquivalentTo([
				$"Halted: {victim.Name}(#{victim.DbRef.Number})",
				$"All of your objects have been halted by {wizard.Name}."
			]);
		}
		finally
		{
			await ConnectionService.Disconnect(victim.Handle);
			await ConnectionService.Disconnect(wizard.Handle);
		}
	}

	/// <summary><c>do_halt</c>'s report is skipped for a QUIET owner (<c>src/cque.c:2176</c>).</summary>
	[Test]
	public async ValueTask HaltIsSilentForQuietOwner()
	{
		var owner = await PlayerAsync("HaltQuietOwner");
		try
		{
			var (thing, name) = await OwnedThingAsync(owner, "HaltQuietThing");
			await AsGod($"@set {owner.DbRef}=QUIET");

			var heard = await MessagesWhile(owner.DbRef, () => RunAs(owner, $"@halt {thing}"));

			await Assert.That(heard).DoesNotContain($"Halted: {name}(#{thing.Number})");
			await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("1");
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
		}
	}

	/// <summary>
	/// Replacement actions need control (<c>src/cque.c:2249-2251</c>): the HALT power lets a player halt
	/// what they do not control, but not hand it new work.
	/// </summary>
	[Test]
	public async ValueTask HaltWithActionsNeedsControl()
	{
		var owner = await PlayerAsync("HaltActionsOwner");
		var halter = await PlayerAsync("HaltPowered");
		try
		{
			await AsGod($"@power {halter.DbRef}=Halt");
			var (thing, _) = await OwnedThingAsync(owner, "HaltActionsThing");

			var heard = await MessagesWhile(halter.DbRef, () => RunAs(halter, $"@halt {thing}=think nope"));

			await Assert.That(heard).Contains("You may not use @halt obj=command on this object.");
			await Assert.That(await Eval($"hasflag({thing},HALT)")).IsEqualTo("0");
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
			await ConnectionService.Disconnect(halter.Handle);
		}
	}

	/// <summary><c>do_haltpid</c> (<c>src/cque.c:2293-2303</c>): not a number, or nobody's pid.</summary>
	[Test]
	public async ValueTask HaltPidRejectsWhatIsNotAPid()
	{
		var player = await PlayerAsync("HaltPidBad");
		try
		{
			var word = await MessagesWhile(player.DbRef, () => RunAs(player, "@halt/pid abc"));
			var unknown = await MessagesWhile(player.DbRef, () => RunAs(player, "@halt/pid 999999999"));

			await Assert.That(word).Contains("That is not a valid pid!");
			await Assert.That(unknown).Contains("That is not a valid pid!");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary><c>@halt/pid</c> reports the halted entry as <c>do_haltpid</c> does (<c>src/cque.c:2335</c>).</summary>
	[Test]
	public async ValueTask HaltPidReportsQueueEntryHalted()
	{
		var owner = await PlayerAsync("HaltPidOwner");
		try
		{
			var (thing, _) = await OwnedThingAsync(owner, "HaltPidThing");
			var parked = await Scheduler.AdmitCommandList(MarkupText.Plain("think parked"),
				ParserState.Empty with { Executor = thing, Enactor = thing, Caller = thing }, TimeSpan.FromMinutes(10));
			await Assert.That(parked.Accepted).IsTrue();

			var heard = await MessagesWhile(owner.DbRef, () => RunAs(owner, $"@halt/pid {parked.Pid}"));

			await Assert.That(heard).Contains($"Queue entry with pid {parked.Pid} halted.");
		}
		finally
		{
			await ConnectionService.Disconnect(owner.Handle);
		}
	}

	/// <summary>
	/// <c>@halt/all</c> without HaltAny (<c>src/cque.c:2346-2349</c>), refused before anything is halted.
	/// </summary>
	[Test]
	public async ValueTask HaltAllNeedsThePowerToHaltTheWorld()
	{
		var player = await PlayerAsync("HaltAllMortal");
		try
		{
			var heard = await MessagesWhile(player.DbRef, () => RunAs(player, "@halt/all"));

			await Assert.That(heard).Contains("You do not have the power to bring the world to a halt.");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	/// <summary>
	/// <c>@allhalt</c> is PennMUSH's <c>do_allhalt</c> (<c>src/cque.c:2343-2358</c>): every object's queue
	/// is halted, and every player is told who did it and then, unless QUIET, <c>do_halt</c>'s
	/// <c>Halted: &lt;name&gt;(#&lt;dbref&gt;)</c>. The enactor is told nothing more than any other
	/// player — no count.
	/// </summary>
	/// <remarks>
	/// The halts are recorded here, not carried out. The scheduler is session-wide, so a real
	/// <c>@allhalt</c> cancels whatever every other suite has queued at that moment. In full runs that
	/// failed <c>InputSessionCommandTests</c> one test per halted player: a reply to <c>@input</c> that
	/// never ran, an input timeout callback that never set its attribute. <c>[NotInParallel]</c> cannot
	/// keep the two apart, because it only serialises against other <c>[NotInParallel]</c> tests. The
	/// command still runs through the ordinary parser and dispatch, against a <see cref="SharpMUSH.Implementation.Commands.Commands"/>
	/// whose Mediator keeps <see cref="HaltObjectQueueRequest"/> to itself.
	/// <para>
	/// The notices go to every player in the world, so that instance also has a notifier of its own:
	/// another suite's players never hear this test's world halt, and what this test's own players
	/// heard is read from that notifier's private recorder.
	/// </para>
	/// </remarks>
	[Test]
	public async ValueTask AllhaltCommand()
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AllhaltWizard");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		var quiet = await PlayerAsync("AllhaltQuiet");
		await AsGod($"@set {quiet.DbRef}=QUIET");
		var bystander = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AllhaltBystander");
		var parked = await Scheduler.AdmitCommandList(MarkupText.Plain("think parked"),
			ParserState.Empty with { Executor = bystander, Enactor = bystander, Caller = bystander }, TimeSpan.FromMinutes(10));
		await Assert.That(parked.Accepted).IsTrue();

		try
		{
			var halts = new ConcurrentQueue<DBRef>();
			var heard = new TestHelpers.NotificationRecorder();
			var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(
				WebAppFactoryArg.Services, HaltRecordingMediator.Wrap(Mediator, halts),
				TestHelpers.CreateNotifyServiceSubstitute(heard));
			var parser = WebAppFactoryArg.CommandParserWith(
				((ILibraryProvider<CommandDefinition>)commands).Get(), wizard.DbRef, wizard.Handle);

			await parser.CommandParse(wizard.Handle, ConnectionService, MarkupText.Plain("@allhalt"));

			await Assert.That(halts.Select(halted => halted.Number)).Contains(bystander.Number)
				.Because("every object in the world is asked to halt, the one this test made among them");
			await Assert.That(halts.Select(halted => halted.Number)).Contains(wizard.DbRef.Number);
			await Assert.That(heard.For(wizard.DbRef)).IsEquivalentTo([
				$"Your objects have been globally halted by {wizard.Name}",
				$"Halted: {wizard.Name}(#{wizard.DbRef.Number})"
			]);
			await Assert.That(heard.For(quiet.DbRef)).IsEquivalentTo([$"Your objects have been globally halted by {wizard.Name}"])
				.Because("do_halt's report is skipped for a QUIET player; the global notice is not");
			await Assert.That(Scheduler.HasPendingWork($"dbref:{bystander}", $"delay:{bystander}")).IsTrue()
				.Because("the scheduler is session-wide: whatever another suite has queued must survive this test");
		}
		finally
		{
			await Scheduler.HaltByPid(parked.Pid!.Value);
			await ConnectionService.Disconnect(wizard.Handle);
			await ConnectionService.Disconnect(quiet.Handle);
		}
	}

	/// <summary>
	/// A Mediator that records each <see cref="HaltObjectQueueRequest"/> instead of sending it, and passes
	/// every other request to the real one.
	/// </summary>
	public class HaltRecordingMediator : DispatchProxy
	{
		private IMediator _inner = null!;
		private ConcurrentQueue<DBRef> _halts = null!;

		public static IMediator Wrap(IMediator inner, ConcurrentQueue<DBRef> halts)
		{
			var proxy = Create<IMediator, HaltRecordingMediator>();
			var recorder = (HaltRecordingMediator)(object)proxy;
			recorder._inner = inner;
			recorder._halts = halts;
			return proxy;
		}

		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (args is [HaltObjectQueueRequest halt, ..])
			{
				_halts.Enqueue(halt.DbRef);
				return Unit.ValueTask;
			}

			try
			{
				return targetMethod!.Invoke(_inner, args);
			}
			catch (TargetInvocationException ex) when (ex.InnerException is not null)
			{
				ExceptionDispatchInfo.Throw(ex.InnerException);
				throw;
			}
		}
	}

	[Test]
	public async ValueTask DrainCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@drain #1")).AsTask());

		await Assert.That(messages.Any(m => m.StartsWith("#-1"))).IsFalse()
			.Because("@drain must not report an error return code");
	}

	[Test]
	public async ValueTask ForceCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the think output is globally unique in the session.
		var token = TestIsolationHelpers.GenerateUniqueName("Forced");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@force #1=think {token}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextEquals(msg, token)),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Verifies that @force evaluates functions inside &amp;attr obj=value commands.
	/// <c>@force me=&amp;testattr me=[add(1,1)]</c> should set the attribute to "2" (evaluated),
	/// not the literal string "[add(1,1)]".
	/// </summary>
	[Test]
	public async ValueTask ForceCommand_EvaluatesAmpersandAttrValue()
	{
		var attrName = $"FORCEEVAL_{Guid.NewGuid():N}"[..20];

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@force me=&{attrName} me=[add(1,1)]"));

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"think [get(me/{attrName})]"));

		var attrValue = result.Message?.ToPlainText()?.Trim() ?? "";
		await Assert.That(attrValue).IsEqualTo("2")
			.Because("@force should evaluate [add(1,1)] to 2 before the & command stores it");
	}

	/// <summary>
	/// Regression test for the command-argument subtree-reuse optimization (ArgumentSplit /
	/// SharpMUSHParserVisitor.EvaluateArgumentSubtree): @FORCE is EqSplit + RSBrace WITHOUT
	/// NoParse/RSNoParse, so its RHS is both (a) brace-preserved by the NoParse boundary-finding
	/// pass (CommandBehavior.RSBrace -> ParserStateFlags.PreserveBraces) and (b) eagerly
	/// function-evaluated afterwards — exercising the brace-preservation and subtree-reuse paths
	/// together in the same argument.
	/// </summary>
	[Test]
	public async ValueTask ForceCommand_PreservesBraces_WhileEvaluatingFunctionInside()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain("@force me={@pemit me=[add(1,2)]}"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("3"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	public async ValueTask NotifyCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@notify #1"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.Notified), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask WaitCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Note: This test doesn't verify the wait actually happened, just that the command executed
		var messages = await MessagesWhile(executor, () =>
			Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@wait 1=think Waited")).AsTask());

		await Assert.That(messages.Any(m => m.StartsWith("#-1"))).IsFalse()
			.Because("@wait must not report an error return code");
	}

	/// <summary>
	/// Verifies that @wait evaluates functions inside &amp;attr obj=value commands.
	/// <c>@wait 1=&amp;testattr obj=[add(1,1)]</c> should, after the delay fires, set the
	/// attribute to "2" (evaluated), not the literal string "[add(1,1)]".
	/// 
	/// This works because the DirectInput flag (ParserStateFlags) is cleared for queue/callback
	/// contexts, so the &amp; command evaluates the RHS via ParsedMessage().
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_EvaluatesAmpersandAttrValue()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalWiz");
		var uniqueId = Guid.NewGuid().ToString("N");
		var attrName = $"WIZWAIT_{uniqueId[..8].ToUpper()}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{attrName} {testObj}=[add(1,1)]}}"));

		// Poll until the @wait callback sets the attribute (or 10s timeout).
		// Polling replaces a fixed Task.Delay so the test isn't fragile against
		var obj = (await Mediator.Send(new GetObjectNodeQuery(testObj))).Expect<AnySharpObject>();
		await TestHelpers.WaitForAttribute(AttributeService, obj, attrName, 10000);

		var attr = (await AttributeService.GetAttributeAsync(obj, obj, attrName,
			IAttributeService.AttributeMode.Read, false))
			.Expect<SharpAttribute[]>("@wait callback should have set the attribute");

		await Assert.That(attr.Last().Value.ToPlainText()).IsEqualTo("2")
			.Because("@wait should evaluate [add(1,1)] to 2 when the callback fires");
	}

	/// <summary>
	/// Verifies that @wait preserves pattern-match %0-%9 from the enclosing $command scope.
	/// When a $command pattern sets %0 to a matched value, @wait callbacks should still see
	/// that %0, not @wait's own args. This matches PennMUSH wenv preservation behavior.
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_PreservesPatternMatchArgs()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitArgObj");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var resultAttr = $"RESULT_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&CMD_TEST_{uniqueId} {testObj}=$testcmd_{uniqueId} *:@wait 1={{&{resultAttr} {testObj}=%0}}"));

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"testcmd_{uniqueId} hello_world"));

		// Poll until the @wait callback sets the attribute (or 10s timeout).
		// Polling replaces a fixed Task.Delay so the test isn't fragile against
		var obj = (await Mediator.Send(new GetObjectNodeQuery(testObj))).Expect<AnySharpObject>();
		await TestHelpers.WaitForAttribute(AttributeService, obj, resultAttr, 10000);

		var attr = (await AttributeService.GetAttributeAsync(obj, obj, resultAttr,
			IAttributeService.AttributeMode.Read, false))
			.Expect<SharpAttribute[]>("@wait callback should have set the attribute");

		await Assert.That(attr.Last().Value.ToPlainText()).IsEqualTo("hello_world")
			.Because("@wait callback should see %0 from the enclosing $command pattern, not @wait's delay arg");
	}

	[Test]
	public async ValueTask UptimeCommand()
	{
		// Pattern C: unique receiver (non-wizard) isolates the notification — content is dynamic timestamps.
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "UptimeTest");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@uptime"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<SharpMessage>(msg => TestHelpers.MessagePlainTextContains(msg, "SharpMUSH Uptime:")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DbckCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dbck"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Not Supported for SharpMUSH."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask DumpCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@dump"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Dump command does nothing for SharpMUSH. Consider using @backup."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask QuotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@quota #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("The quota system is disabled on this server."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask AllquotaCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@allquota"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("Usage: @allquota <amount>"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask BootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@boot #1"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), TestHelpers.MatchingMessage("That player is not connected."), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the wall output is session-unique.
		var token = TestIsolationHelpers.GenerateUniqueName("Wall");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wall {token}"));

		await NotifyService
			.Received(1)
			.Notify(executor.Number, Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Announcement: {token}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask WizwallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Pattern A: embed unique token so the wizwall output is session-unique.
		var token = TestIsolationHelpers.GenerateUniqueName("Wizwall");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wizwall {token}"));

		await NotifyService
			.Received(1)
			.Notify(executor.Number, Arg.Is<SharpMessage>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, $"Broadcast: {token}")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	/// <summary>
	/// Everything connection <paramref name="handle"/> was sent while <paramref name="action"/> ran.
	/// The wall commands address descriptors rather than objects, so their output is recorded by
	/// handle; see <see cref="MessagesWhile"/> for why the window matters.
	/// </summary>
	private async Task<List<string>> HandleMessagesWhile(long handle, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountForHandle(handle);
		await action();
		return [.. recorder.ForHandle(handle).Skip(before)];
	}

	/// <summary>
	/// A connected mortal, a connected ROYALTY and a connected WIZARD, for the wall audience tests.
	/// </summary>
	private async Task<(TestIsolationHelpers.TestPlayer Mortal, TestIsolationHelpers.TestPlayer Royal,
		TestIsolationHelpers.TestPlayer Wizard)> CreateWallAudienceAsync(string prefix)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Mortal");
		var royal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Royal");
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, $"{prefix}Wizard");

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {royal.DbRef}=ROYALTY"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));

		return (mortal, royal, wizard);
	}

	/// <summary>
	/// PennMUSH src/speech.c do_wall(): @wizwall broadcasts with flag mask "WIZARD", so a mortal
	/// must never see it.
	/// </summary>
	[Test]
	public async ValueTask WizwallReachesOnlyWizards()
	{
		var (mortal, royal, wizard) = await CreateWallAudienceAsync("WizwallAud");
		var token = TestIsolationHelpers.GenerateUniqueName("WizwallAud");

		var mortalMessages = new List<string>();
		var royalMessages = new List<string>();
		var wizardMessages = await HandleMessagesWhile(wizard.Handle, async () =>
			royalMessages = await HandleMessagesWhile(royal.Handle, async () =>
				mortalMessages = await HandleMessagesWhile(mortal.Handle, async () =>
					await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wizwall {token}")))));

		await Assert.That(wizardMessages).Contains($"Broadcast: {token}");
		await Assert.That(royalMessages.Any(x => x.Contains(token, StringComparison.Ordinal))).IsFalse();
		await Assert.That(mortalMessages.Any(x => x.Contains(token, StringComparison.Ordinal))).IsFalse();
	}

	/// <summary>
	/// PennMUSH src/speech.c do_wall(): @rwall broadcasts with flag mask "WIZARD ROYALTY", which
	/// flaglist_check_long() treats as any-of, so wizards and royalty see it and mortals do not.
	/// </summary>
	[Test]
	public async ValueTask RwallReachesOnlyRoyaltyAndWizards()
	{
		var (mortal, royal, wizard) = await CreateWallAudienceAsync("RwallAud");
		var token = TestIsolationHelpers.GenerateUniqueName("RwallAud");

		var mortalMessages = new List<string>();
		var royalMessages = new List<string>();
		var wizardMessages = await HandleMessagesWhile(wizard.Handle, async () =>
			royalMessages = await HandleMessagesWhile(royal.Handle, async () =>
				mortalMessages = await HandleMessagesWhile(mortal.Handle, async () =>
					await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@rwall {token}")))));

		await Assert.That(wizardMessages).Contains($"Admin: {token}");
		await Assert.That(royalMessages).Contains($"Admin: {token}");
		await Assert.That(mortalMessages.Any(x => x.Contains(token, StringComparison.Ordinal))).IsFalse();
	}

	/// <summary>@wall has no flag mask, so every connected player gets it, mortals included.</summary>
	[Test]
	public async ValueTask WallReachesEveryone()
	{
		var (mortal, royal, wizard) = await CreateWallAudienceAsync("WallAud");
		var token = TestIsolationHelpers.GenerateUniqueName("WallAud");

		var mortalMessages = new List<string>();
		var royalMessages = new List<string>();
		var wizardMessages = await HandleMessagesWhile(wizard.Handle, async () =>
			royalMessages = await HandleMessagesWhile(royal.Handle, async () =>
				mortalMessages = await HandleMessagesWhile(mortal.Handle, async () =>
					await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wall {token}")))));

		await Assert.That(mortalMessages).Contains($"Announcement: {token}");
		await Assert.That(royalMessages).Contains($"Announcement: {token}");
		await Assert.That(wizardMessages).Contains($"Announcement: {token}");
	}

	/// <summary>
	/// PennMUSH declares @wall/@rwall/@wizwall with the NOEVAL switch (src/command.c), which
	/// suppresses evaluation of the message; without it the message is evaluated like any other
	/// command argument.
	/// </summary>
	[Test]
	public async ValueTask WallNoEvalSwitchLeavesMessageUnevaluated()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("WallNoEval");

		var evaluated = await HandleMessagesWhile(1,
			() => Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wall {token}[add(1,2)]")).AsTask());
		var literal = await HandleMessagesWhile(1,
			() => Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wall/noeval {token}[add(1,2)]")).AsTask());

		await Assert.That(evaluated).Contains($"Announcement: {token}3");
		await Assert.That(literal).Contains($"Announcement: {token}[add(1,2)]");
	}

	[Test]
	[DependsOn(nameof(ReadCacheCommand))]
	public async ValueTask PollCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll WizardPollDisplay999"));
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageSet), executor, executor)).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll"));
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollCurrentMessageFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_TogglesHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideToggle");
		// @hide is permission-gated (CanHide: wizard/royalty or the Hide power) - grant WIZARD.
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();



		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_YesSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideYes");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/yes"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OnSwitch_SetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOn");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_NoSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideNo");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/no"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OffSwitch_UnsetsHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideOff");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
	}

	// PennMUSH's hide_player has no "already hidden/visible" branch for the no-target case: it
	// unconditionally re-applies the requested state to every connection and re-sends the same
	// notify (bsd.c:7234-7250). Repeating /on (or /off) just repeats the same message.
	[Test]
	public async ValueTask Hide_OnSwitch_Repeated_StillNotifiesHidden()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideAlready");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/on"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoLongerAppearOnWho), testPlayer.DbRef)).IsTrue();
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsTrue();
	}

	[Test]
	public async ValueTask Hide_OffSwitch_Repeated_StillNotifiesVisible()
	{
		// Use isolated player to avoid modifying shared God (#1).
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HideVisible");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));


		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MarkupText.Plain("@hide/off"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NowAppearOnWho), testPlayer.DbRef)).IsTrue();
		await Assert.That(ConnectionService.Get(testPlayer.Handle)?.IsHidden).IsFalse();
	}

	[Test]
	// @purge frees every GOING_TWICE object in the shared world, including other tests' fixtures.
	[NotInParallel]
	public async ValueTask PurgeCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@purge"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PurgeComplete), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ReadCacheCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@readcache"));

		// ReadCacheReindexing is always sent before the try/catch, so it is deterministic.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ReadCacheReindexing), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@shutdown"));

		// No switch → else branch sends ShutdownInitiated, then ShutdownNoteWebApp is always sent.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownInitiated), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask ShutdownRebootCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@shutdown/reboot"));

		// /reboot switch → ShutdownRebootInitiated is sent in the REBOOT branch.
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.ShutdownRebootInitiated), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(AllhaltCommand))]
	public async ValueTask ChownallCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		// Chown a throwaway player's objects instead of God's (#1). @chownall on a player
		// without /preserve strips WIZARD/ROYALTY and sets HALT on every swept object; God
		// owns the shared system handlers (#8 HTTP Handler, #9 Event Handler), so
		// "@chownall #1" would de-wizard and HALT them and — because the test DB is shared
		// PerTestSession — poison those handlers for every later test in the run.
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ChownallOwner");
		var thingName = TestIsolationHelpers.GenerateUniqueName("ChownallThing");
		await Parser.CommandParse(owner.Handle, ConnectionService, MarkupText.Plain($"@create {thingName}"));

		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(executor);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chownall #{owner.DbRef.Number}"));

		// Read from the recorder, keyed on this test's own player: the session-shared substitute's
		// call list is not this test's to rely on (#1251). do_chownall tells the executor alone,
		// "Ownership changed for %d objects." (src/wiz.c:1002) — the player itself is not swept.
		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(executor).Skip(before).Any(delivery =>
			delivery.Message == "Ownership changed for 1 objects.")).IsTrue();
		await Assert.That(WebAppFactoryArg.Notifications.For(owner.DbRef)
			.Any(message => message.StartsWith("Ownership changed for ", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	[DependsOn(nameof(PollCommand))]
	public async ValueTask SuggestListCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@suggest/list"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.NoSuggestionCategoriesDefined), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestListCommand))]
	public async ValueTask SuggestAddCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@suggest/add testcat547=testword923"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.SuggestAddedWordToCategoryFormat), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(SuggestAddCommand))]
	public async ValueTask PollSetCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll TestPollMessage897"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageSet), executor, executor)).IsTrue();
	}

	[Test]
	[DependsOn(nameof(PollSetCommand))]
	public async ValueTask PollClearCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@poll/clear"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PollMessageCleared), executor, executor)).IsTrue();
	}


}
