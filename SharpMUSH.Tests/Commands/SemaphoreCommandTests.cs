using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class SemaphoreCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ITaskScheduler Scheduler => WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RejectedCommandCounterWriteRetainsTheWaitingEntry(bool drain)
	{
		var player = (await Mediator.Send(new GetObjectNodeQuery(new SharpMUSH.Library.Models.DBRef(1)))).AsPlayer;
		var target = await Mediator.Send(new SharpMUSH.Library.Commands.Database.CreateRoomCommand("command-accounting-" + Guid.NewGuid().ToString("N"), player));
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(target, ["SEMAPHORE"]);
		var state = ParserState.RootFor(player.Object.DBRef);
		var admitted = await Scheduler.WriteCommandList(MarkupText.Plain("think accounting-finished"), state,
			semaphore, 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await Assert.That(admitted.Accepted).IsTrue();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.ArgAt<GetObjectNodeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.CreateStream(call.ArgAt<GetAttributeQuery>(0), call.ArgAt<CancellationToken>(1)));
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(false);
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.ClearAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(false);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(WebAppFactoryArg.Services, mediator);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(WebAppFactoryArg.Services);
		parser.CurrentState.Returns(state with { Arguments = new() { ["0"] = new(target + "/SEMAPHORE") } });
		var metadata = (SharpMUSH.Library.Attributes.SharpCommandAttribute)Attribute.GetCustomAttribute(
			typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod(drain ? "Drain" : "Notify")!, typeof(SharpMUSH.Library.Attributes.SharpCommandAttribute))!;
		try
		{
			using var budget = ExecutionBudget.FromMilliseconds(30000);
			using var scope = budget.Enter();
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			{
				if (drain) await commands.Drain(parser, metadata); else await commands.Notify(parser, metadata);
			});
			var writeTokens = mediator.ReceivedCalls().Where(call => call.GetArguments().FirstOrDefault() is
				SharpMUSH.Library.Commands.Database.SetAttributeCommand or SharpMUSH.Library.Commands.Database.ClearAttributeCommand)
				.SelectMany(call => call.GetArguments().OfType<CancellationToken>()).ToArray();
			await Assert.That(writeTokens.Length).IsEqualTo(1);
			await Assert.That(writeTokens[0]).IsEqualTo(budget.Token);
			await Assert.That((await Mediator.CreateStream(new GetAttributeQuery(target, ["SEMAPHORE"])).LastAsync()).Value.ToPlainText()).IsEqualTo("1");
			using (await Scheduler.EnterSemaphoreMutationAsync())
				await Assert.That(await Scheduler.DrainCounted(semaphore)).IsEqualTo(1);
		}
		finally { await Scheduler.HaltByPid(admitted.Pid!.Value); }
	}

	[Test]
	public async ValueTask NotifyCommand_ShouldWakeWaitingTask()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemNotify");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testMessage = $"TaskExecuted_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{uniqueAttr}=think {testMessage}"));

		await Task.Delay(200);

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify {semObj}/{uniqueAttr}"));

		await Task.Delay(2000);

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage(testMessage), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DolistInline_ShouldExecuteImmediately()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist/inline a b c=@pemit #1=Inline{uniqueId}"));

		// @dolist/inline a b c fires 3 iterations, each @pemit emits the same unique string.
		// Received(3) is the exact count: one for element "a", one for "b", one for "c".
		await NotifyService.Received(3).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage($"Inline{uniqueId}"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test, Skip("Needs a better way of testing. This is too timing sensitive.")]
	public async ValueTask DolistDefault_ShouldQueueCommands()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dolist a b c=@pemit #1=Queued{uniqueId}"));

		// @dolist (without /inline) queues commands; they must NOT execute synchronously.
		await NotifyService
			.DidNotReceive()
			.Notify(
				TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"Queued{uniqueId}")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NotifySetQ_CommandShouldAcceptParameters()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Guards against CB.RSArgs interfering with comma parsing of qreg parameters.
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQParam");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		var result = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify/setq {semObj}/{uniqueAttr}=0,TestValue"));

		// The command should not generate a parsing error about pairs.
		// It might say "no queue entry" but must NOT say the pairs-error message.
		await NotifyService.DidNotReceive().Notify(
			Arg.Any<AnySharpObject>(),
			Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextEquals(msg, "Q-register assignments must be in pairs: qreg,value[,qreg,value...]")),
			TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask NotifySetQ_ShouldSetQRegisterForWaitingTask()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemSetQWait");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";
		var testValue = $"TestValue_{uniqueId.Substring(0, 8)}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait {semObj}/{uniqueAttr}=think QRegValue:%q0"));

		await Task.Delay(200);

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@notify/setq {semObj}/{uniqueAttr}=0,{testValue}"));

		await Task.Delay(2000);

		await NotifyService.Received(1).Notify(
			TestHelpers.MatchingObject(executor),
			TestHelpers.MatchingMessage($"QRegValue:{testValue}"), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask DrainCommand_Basic()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemDrain");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"SEM_{uniqueId}";

		// drain (with nothing queued) - should not throw exception
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@drain {semObj}/{uniqueAttr}"));

		// No assertion - just verify no exceptions
	}

	[Test]
	public async ValueTask DrainCommandSubtractsActualWaitersAndClearsCredits()
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "CountedDrain");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		async ValueTask Command(string command) => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Command($"@wait {target}/{attribute}=think first");
		await Command($"@wait {target}/{attribute}=think second");
		await Assert.That(await Count()).IsEqualTo("2");
		await Command($"@drain {target}/{attribute}=1");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
		await Command($"@notify {target}/{attribute}=2");
		await Assert.That(await Count()).IsEqualTo("-2");
		await Command($"@drain {target}/{attribute}=1");
		await Assert.That(await Count()).IsEqualTo("-2");
		await Command($"@drain/all {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
		await Command($"@wait {target}/{attribute}=think reused");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async ValueTask ReleaseAllPreservesPublishedTimeoutAccountingBeforeNewWait(bool notifyAll)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "DrainTimeout");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(
			target, [attribute]);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		async ValueTask Command(string command) => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Scheduler.EnqueueWork(async () => { blocked.SetResult(); await release.Task; return null; }, "drain-block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var timeout = await Scheduler.WriteCommandList(MarkupText.Plain("think timeout"), WebAppFactoryArg.FunctionParser.CurrentState,
				semaphore, 0, manageSemaphoreCount: true);
			await Command($"@wait {target}/{attribute}=think pending");
			await Scheduler.ReleaseScheduledWork(timeout.Pid!.Value, semaphoreTimeout: true);
			await Command($"@{(notifyAll ? "notify" : "drain")}/all {target}/{attribute}");
			await Assert.That(await Count()).IsIn("", "0");
			await Command($"@wait {target}/{attribute}=think later");
			await Assert.That(await Count()).IsEqualTo("1");
			await Scheduler.EnqueueWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "drain-complete", "test");
		}
		finally { release.SetResult(); }
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(await Count()).IsEqualTo("1");
		await Command($"@drain {target}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
	}

	[Test]
	public async ValueTask NotifyCreditSurvivesAlreadyPublishedManagedTimeout()
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "TimeoutCredit");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		var semaphore = new SharpMUSH.Library.Models.DbRefAttribute(target, [attribute]);
		var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Scheduler.EnqueueWork(async () => { blocked.SetResult(); await release.Task; return null; }, "credit-block", "test");
		await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			var timeout = await Scheduler.WriteCommandList(MarkupText.Plain("think timeout"),
				WebAppFactoryArg.FunctionParser.CurrentState, semaphore, 0, manageSemaphoreCount: true);
			await Scheduler.ReleaseScheduledWork(timeout.Pid!.Value, semaphoreTimeout: true);
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@notify {target}/{attribute}"));
			await Scheduler.EnqueueWork(() => { completed.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "credit-complete", "test");
		}
		finally { release.SetResult(); }
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var result = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({target}/{attribute})")))!.Message!.ToPlainText();
		await Assert.That(result).IsEqualTo("-1");
	}

	[Test]
	public async ValueTask OrdinaryOwnerCanCreateAndConsumeCustomSemaphoreCredits()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SemaphoreOwner");
		var outsider = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SemaphoreOutsider");
		var attribute = $"SEM_{Guid.NewGuid():N}";
		async ValueTask Command(long handle, string command) => await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command));
		async ValueTask<string> Count() => (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/{attribute})")))!.Message!.ToPlainText();
		await Command(player.Handle, $"@notify me/{attribute}=2");
		await Assert.That(await Count()).IsEqualTo("-2");
		var created = await Mediator.CreateStream(new GetAttributeQuery(player.DbRef, [attribute])).LastAsync();
		await Assert.That((await created.Owner.WithCancellation(CancellationToken.None))!.Object.Key).IsEqualTo(1);
		await Assert.That(created.Flags.Select(x => x.Name).ToArray()).Contains("locked");
		await Command(player.Handle, $"@wait me/{attribute}=think first credit");
		await Assert.That(await Count()).IsEqualTo("-1");
		await Command(player.Handle, $"@wait me/{attribute}=think second credit");
		await Command(player.Handle, $"@wait me/{attribute}=think pending");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(outsider.Handle, $"@notify {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(outsider.Handle, $"@drain {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("1");
		await Command(player.Handle, $"@notify me/{attribute}");
		await Assert.That(await Count()).IsEqualTo("0");
		await Command(1, $"@set {player.DbRef}=LINK_OK");
		await Command(outsider.Handle, $"@notify {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("-1");
		await Command(outsider.Handle, $"@drain {player.DbRef}/{attribute}");
		await Assert.That(await Count()).IsEqualTo("");
	}

	[Test]
	public async ValueTask WaitCommand_WithTime_CanExecute()
	{
		var uniqueId = Guid.NewGuid().ToString("N");

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1=@pemit #1=Wait{uniqueId}"));

		// No assertion - just verify no exceptions and command parses.
		// We don't wait for execution as this tests command parsing, not scheduler execution.
	}

	/// <summary>
	/// Verifies that @wait callbacks with multiple semicolon-separated commands in braces
	/// execute ALL commands, not just the first one.
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_MultipleCommandsInBraces()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitMulti");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var attrA = $"WAITMULTI_A_{uniqueId}";
		var attrB = $"WAITMULTI_B_{uniqueId}";

		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{attrA} {testObj}=valueA; &{attrB} {testObj}=valueB}}"));

		await Task.Delay(3000);

		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));

		var attrResultA = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrA,
			IAttributeService.AttributeMode.Read, false);
		var attrResultB = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, attrB,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attrResultA.IsAttribute).IsTrue()
			.Because($"First command in @wait callback should set {attrA}");
		await Assert.That(attrResultA.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("valueA");

		await Assert.That(attrResultB.IsAttribute).IsTrue()
			.Because($"Second command in @wait callback should set {attrB}");
		await Assert.That(attrResultB.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("valueB");
	}

	/// <summary>
	/// PennMUSH compatibility regression test.
	///
	/// On PennMUSH, <c>@wait 1={&amp;attr obj=[add(1,1)]}</c> evaluates the function call
	/// <c>[add(1,1)]</c> when the callback fires, resulting in the attribute being set to the
	/// string <c>"2"</c> (the computed result), NOT the literal text <c>"[add(1,1)]"</c>.
	///
	/// ## PennMUSH source proof (command.c lines 1424-1432, PennMUSH 1.8.8)
	///
	/// The ATTRIB_SET internal command (which handles both <c>&amp;</c> and <c>@</c>-style
	/// attribute setting) is registered as:
	/// <code>
	///   {"ATTRIB_SET", NULL, command_atrset,
	///    CMD_T_ANY | CMD_T_EQSPLIT | CMD_T_NOGAGGED | CMD_T_INTERNAL, 0, 0}
	/// </code>
	/// Crucially, it has <em>neither</em> <c>CMD_T_NOPARSE</c> nor <c>CMD_T_RS_NOPARSE</c>,
	/// so the normal path evaluates both sides of the <c>=</c>.
	///
	/// However, there is a special-case for direct player input (command.c ~line 1425):
	/// <code>
	///   if ((cmd->func == command_atrset) &amp;&amp;
	///       (queue_entry->queue_type &amp; QUEUE_NOLIST)) {
	///     // Special case: eqsplit, noeval of rhs only
	///     command_argparse(..., rs, ..., noeval=1, ...);  // RHS NOT evaluated
	///     SW_SET(sw, SWITCH_NOEVAL);
	///   } else {
	///     // Normal path: both sides evaluated (noeval=false)
	///     command_argparse(..., rs, ..., noeval=0, ...);  // RHS IS evaluated
	///   }
	/// </code>
	/// When typed at the player prompt, <c>QUEUE_NOLIST</c> is set → RHS stored as-is (code).
	/// When run from a command queue (<c>@wait</c> callback), <c>QUEUE_NOLIST</c> is NOT set →
	/// RHS is evaluated and the result is stored.
	///
	/// ## Empirical proof (live PennMUSH 1.8.8 session)
	/// <code>
	///   &amp;DIRECT_TEST testobject=[add(1,1)]
	///   think DIRECT_RESULT:[get(testobject/DIRECT_TEST)]  →  [add(1,1)]  (literal, not evaluated)
	///
	///   @wait 0={&amp;WAIT_TEST testobject=[add(1,1)]}
	///   think WAIT_RESULT:[get(testobject/WAIT_TEST)]      →  2           (evaluated!)
	///
	///   @wait 0={&amp;WAIT_MATH testobject=[add(3,4)]}
	///   think MATH_RESULT:[get(testobject/WAIT_MATH)]      →  7           (3+4=7, evaluated!)
	/// </code>
	///
	/// ## Root cause in SharpMUSH
	/// SharpMUSH declares <c>&amp;</c> (SetAttribute) with <see cref="CommandBehavior.NoParse"/>
	/// unconditionally. In <c>ArgumentSplit</c> (SharpMUSHParserVisitor), NoParse commands place
	/// their RHS into a <see cref="CallState"/> whose <c>Message</c> is the raw unevaluated
	/// string; the deferred <c>ParsedMessage</c> lambda is never consumed by
	/// <c>SetAttribute</c>, which reads <c>args["2"].Message!</c> directly.
	///
	/// The correct fix must evaluate the RHS when <c>&amp;</c> runs from a command queue context
	/// (equivalent to PennMUSH's non-QUEUE_NOLIST path) without evaluating it during direct
	/// player input or when storing <c>$pattern:code</c> attribute values.
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_EvaluatesFunctionsInAmpersandCallback()
	{
		var testObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitEvalAttr");
		var uniqueId = Guid.NewGuid().ToString("N");
		var uniqueAttr = $"EVALTEST_{uniqueId[..8].ToUpper()}";

		// Unix timestamp "1" is in the far past, so Quartz fires the job immediately.
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@wait 1={{&{uniqueAttr} {testObj}=[add(1,1)]}}"));

		await Task.Delay(2000);

		// PennMUSH evaluates [add(1,1)] → "2" before storing the attribute.
		// SharpMUSH currently stores the literal "[add(1,1)]" instead (the bug).
		var obj = await Mediator.Send(new GetObjectNodeQuery(testObj));
		var attr = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, uniqueAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue();
		await Assert.That(attr.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("2");
	}

	/// <summary>
	/// Tests the BBS-style pattern: user-defined command creates an object, then @wait
	/// callback uses num() + setr() to get its dbref and store it in an attribute.
	/// Simulates the +bbnewgroup flow: $cmd *:@create %0; @wait 1={&amp;groups store=[num(%0)]}
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_PatternMatchPreservesPercentZero()
	{
		var storeObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "WaitStore");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var uniqueAttr = $"WAITSTOR_{uniqueId}";
		var cmdAttr = $"CMD_WAITST_{uniqueId}";

		var cmdPattern = $"$+waitstore_{uniqueId.ToLower()} *:@create %0; @wait 1={{&{uniqueAttr} {storeObj}=[num(%0)]}}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{cmdAttr} {storeObj}={cmdPattern}"));

		var targetName = $"WaitTgt_{uniqueId}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"+waitstore_{uniqueId.ToLower()} {targetName}"));

		await Task.Delay(3000);

		var storeObjNode = await Mediator.Send(new GetObjectNodeQuery(storeObj));
		var attr = await AttributeService.GetAttributeAsync(storeObjNode.Known, storeObjNode.Known, uniqueAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{uniqueAttr} should have been set by the @wait callback");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(attrValue).StartsWith("#")
			.Because($"num(%0) in the @wait callback should resolve to a dbref like #N, but got: {attrValue}");
		await Assert.That(attrValue).DoesNotContain("-1")
			.Because($"num(%0) should find the created object, not return #-1. Got: {attrValue}");
	}

	/// <summary>
	/// Simulates the BBS +bbnewgroup flow more closely:
	/// $pattern *:@switch hasflag(%#,wizard)=1, {@create %0; @wait 1={@switch [setr(0,num(%0))]=#-1,...,{&groups store=%q0}}}
	/// </summary>
	[Test]
	public async ValueTask WaitCommand_BBSNewGroupFlow()
	{
		var storeObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "BBSFlow");
		var uniqueId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		var grpAttr = $"GROUPS_{uniqueId}";
		var cmdAttr = $"CMD_BBSFL_{uniqueId}";

		var cmdPattern = $"$+bbsflow_{uniqueId.ToLower()} *:@switch hasflag(%#,wizard)=1,{{@create %0; @wait 1={{@switch [setr(0,num(%0))]=#-1,{{@pemit %#=Bad name}},{{&{grpAttr} {storeObj}=%q0}}}}}}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&{cmdAttr} {storeObj}={cmdPattern}"));

		var targetName = $"BBSTgt_{uniqueId}";
		await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"+bbsflow_{uniqueId.ToLower()} {targetName}"));

		await Task.Delay(3000);

		var storeObjNode = await Mediator.Send(new GetObjectNodeQuery(storeObj));
		var attr = await AttributeService.GetAttributeAsync(storeObjNode.Known, storeObjNode.Known, grpAttr,
			IAttributeService.AttributeMode.Read, false);

		await Assert.That(attr.IsAttribute).IsTrue()
			.Because($"&{grpAttr} should have been set by the @wait callback's @switch non-#-1 branch");

		var attrValue = attr.AsAttribute.Last().Value.ToPlainText();
		await Assert.That(attrValue).StartsWith("#")
			.Because($"The groups attribute should contain a dbref, but got: {attrValue}");
		await Assert.That(attrValue).DoesNotContain("-1")
			.Because($"num(%0) should find the created object, not return #-1. Got: {attrValue}");
	}
	[Test]
	public async Task FirstWaiterAndTimeoutKeepSemaphoreCountConsistent()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemBudgetCount");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wait {semObj}/{name}=think timeout"));
		var obj = await Mediator.Send(new GetObjectNodeQuery(semObj));
		var initial = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(initial.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("1");
		var tasks = await Scheduler.GetSemaphoreTasks(new SharpMUSH.Library.Models.DbRefAttribute(semObj, [name])).ToArrayAsync();
		await Scheduler.RescheduleSemaphoreTask(tasks.Single().Pid, TimeSpan.Zero);
		var count = "1";
		for (var attempt = 0; attempt < 50 && count != "0"; attempt++)
		{
			await Task.Delay(100);
			var current = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
			count = current.AsAttribute.Last().Value.ToPlainText();
		}
		await Assert.That(count).IsEqualTo("0");
	}

	[Test]
	public async Task TimeoutAfterSemaphoreResetCannotCreateNotifyCredit()
	{
		var semObj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemResetCount");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@wait {semObj}/{name}=think timeout"));
		var tasks = await Scheduler.GetSemaphoreTasks(new SharpMUSH.Library.Models.DbRefAttribute(semObj, [name])).ToArrayAsync();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {semObj}=0"));
		await Scheduler.ReleaseScheduledWork(tasks.Single().Pid, semaphoreTimeout: true);
		var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Scheduler.EnqueueWork(() => { drained.SetResult(); return ValueTask.FromResult<CallState?>(null); }, "reset-drained", "test");
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var obj = await Mediator.Send(new GetObjectNodeQuery(semObj));
		var current = await AttributeService.GetAttributeAsync(obj.Known, obj.Known, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(current.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("0");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltingExecutorOrSemaphoreTargetReleasesPendingReservation(bool haltTarget)
	{
		var executor = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemHaltExecutor");
		var semaphore = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "SemHaltTarget");
		var name = "COUNT_" + Guid.NewGuid().ToString("N");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {semaphore}=1"));
		var attribute = new SharpMUSH.Library.Models.DbRefAttribute(semaphore, [name]);
		var before = Scheduler.GetQueueUsage().Total;
		var admitted = await Scheduler.WriteCommandList(MarkupText.Plain("think ignored"), ParserState.RootFor(executor), attribute, 1, TimeSpan.FromHours(1));
		await Assert.That(admitted.Accepted).IsTrue();
		var haltedObject = haltTarget ? semaphore : executor;
		var incarnation = (await Mediator.Send(new GetObjectNodeQuery(haltedObject))).AsThing.Object.DBRef;
		await Scheduler.Halt(new SharpMUSH.Library.Models.DBRef(haltedObject.Number, incarnation.CreationMilliseconds + 1));
		await Assert.That(Scheduler.GetQueueUsage().Total).IsEqualTo(before + 1);
		await Scheduler.Halt(new SharpMUSH.Library.Models.DBRef(haltedObject.Number));
		await Assert.That(Scheduler.GetQueueUsage().Total).IsEqualTo(before);
		await Assert.That((await Scheduler.GetSemaphoreTasks(attribute).ToArrayAsync()).Length).IsEqualTo(0);
		var obj = (await Mediator.Send(new GetObjectNodeQuery(semaphore))).Known;
		var current = await AttributeService.GetAttributeAsync(obj, obj, name, IAttributeService.AttributeMode.Read, false);
		await Assert.That(current.AsAttribute.Last().Value.ToPlainText()).IsEqualTo("0");
	}

}
