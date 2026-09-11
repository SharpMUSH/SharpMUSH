using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using NSubstitute;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

[NotInParallel]
public class RestrictedExpressionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private async Task<string> Eval(string expression) =>
		(await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
	private Task Cmd(string command) => Factory.CommandParser.CommandParse(1,
		Factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command)).AsTask();

	[Test]
	[Arguments(false, "flat")]
	[Arguments(true, "flat")]
	[Arguments(false, "nested")]
	[Arguments(true, "nested")]
	[Arguments(false, "fragments")]
	[Arguments(true, "fragments")]
	public async Task RestrictedSiblingArgumentsStopAtTheCombinedCeiling(bool restricted, string shape)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var space = library["space"].LibraryInformation;
		var expansions = 0;
		library["space"] = (space with { Function = async parser => { expansions++; return await space.Function(parser); } }, true);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value));
		var size = FunctionLimits.MaxOutputCodeUnits / 2 + 1;
		var expression = shape switch
		{
			"nested" => $"cat(space({size}),cat(space({size}),space({size})))",
			"fragments" => $"cat([space({size})][cat(space({size}),space({size}))])",
			_ => $"cat(space({size}),space({size}),space({size}))"
		};
		var result = await parser.FunctionParse(MarkupText.Plain(restricted ? $"restrictedexpr(space cat,{expression})" : expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
		await Assert.That(expansions).IsEqualTo(restricted ? 2 : 3);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task RestrictedFnChainsBoundRetainedSource(bool restricted, bool indirectEntry)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var fn = library["fn"].LibraryInformation;
		var calls = 0;
		library["fn"] = (fn with { Function = async parser => { calls++; return await fn.Function(parser); } }, true);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value));
		var payload = new string('x', 512 * 1024);
		var target = indirectEntry ? $"restrictedexpr,strlen,strlen({payload})" : $"strlen,{payload}";
		var expression = $"fn({string.Concat(Enumerable.Repeat("fn,", 11))}{target})";
		var result = await parser.FunctionParse(MarkupText.Plain(restricted && !indirectEntry ? $"restrictedexpr(fn strlen,{expression})" : expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(restricted ? ErrorMessages.Returns.OutputTooLarge : payload.Length.ToString());
		if (restricted) await Assert.That(calls).IsLessThan(12);
		else await Assert.That(calls).IsEqualTo(12);
	}

	[Test]
	[Arguments(1024)]
	[Arguments(512 * 1024)]
	public async Task RestrictedWrapperChainsBoundRetainedSource(int size)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var wrapper = library["restrictedexpr"].LibraryInformation;
		var calls = 0;
		library["restrictedexpr"] = (wrapper with { Function = async parser => { calls++; return await wrapper.Function(parser); } }, true);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value));
		var expression = $"strlen({new string('x', size)})";
		for (var i = 0; i < 12; i++) expression = $"restrictedexpr(restrictedexpr strlen,{expression})";
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(size == 1024 ? size.ToString() : ErrorMessages.Returns.OutputTooLarge);
		if (size == 1024) await Assert.That(calls).IsEqualTo(12);
		else await Assert.That(calls).IsLessThan(12);
	}

	[Test]
	public async Task RestrictedRetentionReleasesCompletedNestedArguments()
	{
		var size = FunctionLimits.MaxOutputCodeUnits * 3 / 4;
		var result = await Eval($"restrictedexpr(space strlen cat,cat(strlen(space({size})),strlen(space({size}))))");
		await Assert.That(result).IsEqualTo($"{size} {size}");
	}

	[Test]
	public async Task RestrictedWrapperScanStopsDuringWideChildTraversal()
	{
		var visitor = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Visitors.SharpMUSHParserVisitor>(
			Factory.Services, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, Factory.FunctionParser, MarkupText.Empty);
		var root = Substitute.For<Antlr4.Runtime.Tree.IParseTree>();
		var leaf = Substitute.For<Antlr4.Runtime.Tree.IParseTree>();
		root.ChildCount.Returns(128);
		leaf.ChildCount.Returns(0);
		using var cancel = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancel.Token);
		using var scope = budget.Enter();
		var reads = 0;
		root.GetChild(Arg.Any<int>()).Returns(_ => { reads++; cancel.Cancel(); return leaf; });
		var scan = visitor.GetType().GetMethod("ContainsRestrictedEvaluation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
		Exception? observed = null;
		try { scan.Invoke(visitor, [root]); }
		catch (System.Reflection.TargetInvocationException exception) { observed = exception.InnerException; }
		await Assert.That(observed).IsTypeOf<OperationCanceledException>();
		await Assert.That(reads).IsEqualTo(1);
	}

	[Test]
	[Arguments("fn", true)]
	[Arguments("arityalias", true)]
	[Arguments("fn", false)]
	[Arguments("arityalias", false)]
	public async Task RestrictedIndirectionRejectsExcessArgumentsBeforeDispatch(string name, bool restricted)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var invoked = false;
		library[name] = (library["fn"].LibraryInformation with
		{
			Function = _ => { invoked = true; return ValueTask.FromResult(new CallState(MarkupText.Plain("DISPATCHED"))); }
		}, true);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value));
		var arguments = string.Join(',', Enumerable.Repeat("", 33));
		var expression = $"{name}(cat,{arguments})";
		var result = await parser.FunctionParse(MarkupText.Plain(restricted ? $"restrictedexpr(fn cat,{expression})" : expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(restricted ? EvaluationRestrictions.Error : "DISPATCHED");
		await Assert.That(invoked).IsEqualTo(!restricted);
	}

	[Test]
	public async Task RestrictedIndirectionAllowsMaximumAuditedTargetArity()
	{
		var arguments = string.Join(',', Enumerable.Repeat("x", 32));
		await Assert.That(await Eval($"restrictedexpr(fn cat,fn(cat,{arguments}))")).IsEqualTo(string.Join(' ', Enumerable.Repeat("x", 32)));
	}

	[Test]
	[Arguments("first", "first(%0)")]
	[Arguments("rest", "rest(%0)")]
	[Arguments("extract", "extract(%0,1,1)")]
	[Arguments("words", "words(%0)")]
	public async Task AllocationHeavyListOperationsCannotEnterTheRestrictedProfile(string name, string expression)
	{
		await Assert.That(await Eval($"restrictedexpr({name},{expression},a b c)")).IsEqualTo(EvaluationRestrictions.Error);
	}

	[Test]
	[Arguments("#apply2/restrictedexpr,")]
	[Arguments("#apply2/restricted_alias,")]
	[Arguments("#apply3/fn,restrictedexpr,")]
	[Arguments("#apply3/fn,restricted_alias,")]
	public async Task AlreadyEvaluatedApplyCannotEnterRestrictedEvaluation(string apply)
	{
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser,
			Factory.Services.GetRequiredService<IConnectionService>(), "RestrictedApply");
		await Cmd($"&SECRET {target}=private-apply-value");
		var library = new FunctionLibraryService();
		foreach (var pair in Factory.FunctionParser.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var parser = (MUSHCodeParser)Factory.FunctionParser with { FunctionLibrary = library };
		var result = (await parser.FunctionParse(MarkupText.Plain($"ulambda({apply},get({target}/SECRET))")))!.Message!.ToPlainText();
		await Assert.That(result).IsEqualTo(EvaluationRestrictions.Error);
	}

	[Test]
	public async Task ExplicitInputsAndPureOperationsWork()
	{
		await Assert.That(await Eval("restrictedexpr(add,add(%0,%1),2,3)")).IsEqualTo("5");
		await Assert.That(await Eval("restrictedexpr(ucstr trim,ucstr(trim(%0)),hello world)")).IsEqualTo("HELLO WORLD");
		await Assert.That(await Eval("restrictedexpr(,%0%b%1,a,b)")).IsEqualTo("a b");
	}

	[Test]
	public async Task NestedAllowlistsAndIndirectCallsCannotBroaden()
	{
		await Assert.That(await Eval("restrictedexpr(add restrictedexpr,restrictedexpr(add sub,sub(3,1)))")).Contains("RESTRICTED EXPRESSION");
		await Assert.That(await Eval("restrictedexpr(fn add,fn(add,2,3))")).IsEqualTo("5");
		await Assert.That(await Eval("restrictedexpr(fn,fn(add,2,3))")).Contains("RESTRICTED EXPRESSION");
		await Assert.That(await Eval("restrictedexpr(get,get(#1/DESC))")).Contains("RESTRICTED EXPRESSION");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task RestrictedDebugExecutorDoesNotForwardExpressionOrResults(bool denied, bool substitutionTrace)
	{
		var connection = Factory.Services.GetRequiredService<IConnectionService>();
		DBRef target;
		using (var setupBudget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
		using (setupBudget.Enter())
		{
			target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connection, "RestrictedDebug");
			await Cmd($"&DEBUGFORWARDLIST {target}=#1");
			await Cmd($"@set {target}=DEBUG");
			await Assert.That(await Eval($"hasflag({target},DEBUG)")).IsEqualTo("1");
		}
		var state = ParserState.RootFor(target);
		var parser = Factory.FunctionParser.FromState(state);
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		var expression = denied ? "restrictedexpr(ucstr,get(%0),private-input)" : "restrictedexpr(ucstr,ucstr(%0),private-input)";
		var result = (await parser.FunctionParse(MarkupText.Plain(expression), substitutionTrace))!.Message!.ToPlainText();
		if (denied) await Assert.That(result).Contains("RESTRICTED EXPRESSION");
		else await Assert.That(result).IsEqualTo("PRIVATE-INPUT");
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictedSubstitutionTraceCannotEmitAfterParsing(bool ambient)
	{
		var restrictions = new EvaluationRestrictions([]);
		var state = ParserState.RootFor(Factory.FunctionParser.CurrentState.Executor!.Value) with
		{
			Flags = ParserStateFlags.Debug,
			Restrictions = ambient ? null : restrictions,
			EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new("private-input") }
		};
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		using var scope = ambient ? restrictions.Enter() : null;
		var result = await Factory.FunctionParser.FromState(state).FunctionParse(MarkupText.Plain("%0"), true);
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("private-input");
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	public async Task ParentRegistersAndIdentityAreNotInputs()
	{
		var parent = ParserState.RootFor(Factory.FunctionParser.CurrentState.Executor!.Value) with
		{
			TotalInvocations = new(),
			CallDepth = new(),
			FunctionRecursionDepths = new(),
			LimitExceeded = new(),
			EnvironmentRegisters = new Dictionary<string, CallState> { ["0"] = new("parent input") }
		};
		parent.Registers.First()["SECRET"] = MarkupText.Plain("classified");
		var parser = Factory.FunctionParser.FromState(parent);
		async Task<string> InParent(string expression) => (await parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
		await Assert.That(await InParent("r(secret)")).IsEqualTo("classified");
		foreach (var substitution in new[] { "%q<secret>", "%#", "%n", "%L", "%i0", "%$0", "%@" })
			await Assert.That(await InParent($"restrictedexpr(,{substitution})")).Contains("RESTRICTED EXPRESSION");
		await Assert.That(await InParent("restrictedexpr(,%0)")).IsEqualTo("");
		await Assert.That(await InParent("restrictedexpr(,%0,%q<secret>)")).IsEqualTo("%q<secret>");
		await Assert.That(await InParent("r(secret)")).IsEqualTo("classified");
	}

	[Test]
	public async Task AttributeAndSideEffectWrappersAreRejectedEvenForGod()
	{
		var name = "restricted" + Guid.NewGuid().ToString("N");
		await Cmd($"&{name} me=secret");
		await Cmd($"@function {name}=me,{name}");
		await Cmd($"@function/local {name}=me,{name}");
		foreach (var expression in new[] { $"u(me/{name})", $"{name}()", $"localfun({name})", "pemit(me,leak)", "trigger(me/CODE)", "set(me/DESC,denied)", "r(secret)" })
			await Assert.That(await Eval($"restrictedexpr(,{expression})")).Contains("RESTRICTED EXPRESSION");
		await Assert.That(await Eval("add(2,3)")).IsEqualTo("5");
	}
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task IndirectFunctionCannotReadAnAttributeFallback(bool halted)
	{
		var attribute = "restrictedfn" + Guid.NewGuid().ToString("N");
		var target = "me";
		using (var setupBudget = new ExecutionBudget(TimeSpan.FromSeconds(30)))
		using (setupBudget.Enter())
		{
			if (halted)
			{
				var created = await Factory.CommandParser.CommandParse(1,
					Factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain($"@create {attribute}"));
				target = created.Message!.ToPlainText();
				await Assert.That(SharpMUSH.Library.Models.DBRef.TryParse(target, out _)).IsTrue();
			}
			await Cmd($"&{attribute} {target}=classified attribute");
			if (halted) await Cmd($"@set {target}=HALT");
		}
		await Assert.That(await Eval($"get({target}/{attribute})")).IsEqualTo("classified attribute");
		if (halted) await Assert.That(await Eval($"hasflag({target},HALT)")).IsEqualTo("1");
		await Assert.That(await Eval($"restrictedexpr(fn,fn({target}/{attribute}))")).IsEqualTo(EvaluationRestrictions.Error);
		await Assert.That(await Eval("restrictedexpr(fn add,fn(add,2,3))")).IsEqualTo("5");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AttributeEvaluationCannotBypassObjectDataRestriction(bool stringTarget)
	{
		var original = Factory.FunctionParser;
		var parser = original.Push(original.CurrentState with { Restrictions = new EvaluationRestrictions(["add"]) });
		var executor = await parser.CurrentState.KnownExecutorObject(Factory.Services.GetRequiredService<Mediator.IMediator>());
		var attributes = Factory.Services.GetRequiredService<IAttributeService>();
		var denied = false;
		try
		{
			if (stringTarget)
				await attributes.EvaluateAttributeFunctionAsync(parser, executor, MarkupText.Plain("me/DESC"), [], ignorePermissions: true);
			else
				await attributes.EvaluateAttributeFunctionAsync(parser, executor, executor, "DESC", [], ignorePermissions: true);
		}
		catch (RestrictedExpressionException) { denied = true; }
		await Assert.That(denied).IsTrue();
	}

	[Test]
	public async Task AliasesUseCanonicalOperationsAndPluginNamesCannotImpersonateThem()
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("plus_alias", library["add"]);
		var parser = original with { FunctionLibrary = library };
		var allowed = await parser.FunctionParse(MarkupText.Plain("restrictedexpr(plus_alias,add(2,3))"));
		await Assert.That(allowed!.Message!.ToPlainText()).IsEqualTo("5");
		var called = false;
		library["add"] = (new FunctionDefinition(new SharpFunctionAttribute { Name = "add", Flags = FunctionFlags.Regular },
			_ => { called = true; return ValueTask.FromResult(new CallState("leak")); }), true);
		var denied = await parser.FunctionParse(MarkupText.Plain("restrictedexpr(plus_alias,add())"));
		await Assert.That(denied!.Message!.ToPlainText()).Contains("RESTRICTED EXPRESSION");
		await Assert.That(called).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DirectDispatchCannotBypassRestrictionsWithPrecheckedPermissions(bool prechecked)
	{
		var original = Factory.FunctionParser;
		var parser = original.Push(original.CurrentState with { Restrictions = new EvaluationRestrictions(["add"]) });
		var called = false;
		var definition = new FunctionDefinition(new SharpFunctionAttribute { Name = "add", Flags = FunctionFlags.Regular },
			_ => { called = true; return ValueTask.FromResult(new CallState("leak")); });
		var denied = false;
		try
		{
			await FunctionDispatcher.InvokeAsync(parser, definition,
				await parser.CurrentState.KnownExecutorObject(Factory.Services.GetRequiredService<Mediator.IMediator>()),
				true, Factory.Services.GetRequiredService<INotifyService>(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
				permissionsChecked: prechecked);
		}
		catch (RestrictedExpressionException) { denied = true; }
		await Assert.That(denied).IsTrue();
		await Assert.That(called).IsFalse();
	}

	private sealed record Options(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	[Test]
	public async Task RestrictedRootSharesTheInvocationBudget()
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var config = original.Configuration.CurrentValue;
		var parser = original with { Configuration = new Options(config with { Limit = config.Limit with { FunctionInvocationLimit = 2 } }) };
		var result = await parser.FunctionParse(MarkupText.Plain("restrictedexpr(add,add(1,add(2,3)))"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.Invoke);
	}

	[Test]
	[Arguments("space")]
	[Arguments("repeat")]
	public async Task ExpansionRejectsOversizeBeforeReturningAllocatedText(string function)
	{
		var original = Factory.FunctionParser;
		var args = function == "space"
			? new Dictionary<string, CallState> { ["0"] = new("5242881") }
			: new Dictionary<string, CallState> { ["0"] = new("ab"), ["1"] = new("2621441") };
		var result = await original.FunctionLibrary[function].LibraryInformation.Function(original.Push(original.CurrentState with { Arguments = args }));
		await Assert.That(result.Message!.Length).IsLessThan(100);
		await Assert.That(result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
	}

	[Test]
	public async Task ExplicitRootStateKeepsItsDeadlineAndRestrictions()
	{
		var original = Factory.FunctionParser;
		using var expired = new ExecutionBudget(TimeSpan.Zero);
		var root = ParserState.RootFor(original.CurrentState.Executor!.Value) with { TotalInvocations = null, ExecutionBudget = expired };
		var result = await original.FromState(root).FunctionParse(MarkupText.Plain("add(1,2)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(ExecutionBudget.Error);
		root = ParserState.RootFor(original.CurrentState.Executor!.Value) with { TotalInvocations = null, Restrictions = new EvaluationRestrictions(["add"]) };
		result = await original.FromState(root).FunctionParse(MarkupText.Plain("get(#1/DESC)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(EvaluationRestrictions.Error);
	}

	[Test]
	public async Task RestrictedCommandListsCannotScheduleDeferredWork()
	{
		var queue = Factory.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.ITaskScheduler>();
		var before = queue.GetQueueUsage().Total;
		using var scope = new EvaluationRestrictions([]).Enter();
		var result = await Factory.FunctionParser.CommandListParse(MarkupText.Plain("@wait 3600=think denied"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(EvaluationRestrictions.Error);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(before);
	}

	[Test]
	public async Task ExplicitInputExpansionIsBoundedBeforeJoining()
	{
		var input = new string('x', 512 * 1024);
		var copies = Enumerable.Repeat("%0", 11).ToArray();
		foreach (var body in new[] { string.Concat(copies), $"cat({string.Join(',', copies)})", $"strcat({string.Join(',', copies)})" })
			await Assert.That(await Eval($"restrictedexpr(cat strcat,{body},{input})")).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
		await Assert.That(await Eval("space(2147483647)")).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
		await Assert.That(await Eval("repeat(x,2147483647)")).IsEqualTo(ErrorMessages.Returns.OutputTooLarge);
		await Assert.That(await Eval("repeat(,2147483647)")).IsEqualTo("");
	}

	private sealed class ScanComparer : IEqualityComparer<string>
	{
		public Action? OnLookup { get; set; }
		public bool Equals(string? x, string? y) => StringComparer.OrdinalIgnoreCase.Equals(x, y);
		public int GetHashCode(string value)
		{
			var callback = OnLookup;
			OnLookup = null;
			callback?.Invoke();
			return StringComparer.OrdinalIgnoreCase.GetHashCode(value);
		}
	}
	private sealed class ScanLibrary(ScanComparer comparer) : LibraryService<string, FunctionDefinition>(comparer);
	private sealed class ScanTimer : TimeProvider
	{
		private Action? _fire;
		public void Fire() => _fire!();
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_fire = () => callback(state);
			return new Timer();
		}
		private sealed class Timer : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() { }
			public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		}
	}

	[Test]
	[Arguments("function", true)]
	[Arguments("command", true)]
	[Arguments("visitor", true)]
	[Arguments("function", false)]
	[Arguments("command", false)]
	[Arguments("visitor", false)]
	public async Task RestrictedEntryScanKeepsDeadlineAndCallerCancellationDistinct(string mode, bool expiry)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var comparer = new ScanComparer();
		var library = new ScanLibrary(comparer);
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var parser = original with
		{
			FunctionLibrary = library,
			Configuration = new Options(original.Configuration.CurrentValue with
			{ Debug = original.Configuration.CurrentValue.Debug with { DebugSharpParser = true } })
		};
		var timer = new ScanTimer();
		using var caller = new CancellationTokenSource();
		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1), caller.Token, timer);
		using var scope = budget.Enter();
		var scanned = false;
		comparer.OnLookup = () => { scanned = true; if (expiry) timer.Fire(); else caller.Cancel(); };
		async Task<CallState?> Invoke() => mode switch
		{
			"function" => await parser.FunctionParse(MarkupText.Plain("fn(add,1,2)")),
			"command" => await parser.CommandListParse(MarkupText.Plain("@pemit me=[fn(add,1,2)]")),
			_ => await parser.CommandListParseVisitor(MarkupText.Plain("@pemit me=[fn(add,1,2)]"))()
		};
		if (expiry) await Assert.That((await Invoke())?.Message?.ToPlainText()).IsEqualTo(ExecutionBudget.Error);
		else await Assert.ThrowsAsync<OperationCanceledException>(Invoke);
		await Assert.That(scanned).IsTrue();
	}

	[Test]
	[Arguments("restrictedexpr(add,private-input)")]
	[Arguments("fn(restrictedexpr,add,private-input)")]
	[Arguments("fn(fn,restricted_alias,add,private-input)")]
	public async Task RestrictedEntryFailureAfterScopeUnwindsDoesNotExposeDiagnostics(string expression)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library["restrictedexpr"] = (library["restrictedexpr"].LibraryInformation with
		{
			Function = _ =>
			{
				using var restricted = new EvaluationRestrictions(["add"]).Enter();
				throw new InvalidOperationException("private-input");
			}
		}, true);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		var notify = Substitute.For<INotifyService>();
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var services = Substitute.For<IServiceProvider>();
		services.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IMediator) ? mediator
			: call.Arg<Type>() == typeof(INotifyService) ? notify : original.ServiceProvider.GetService(call.Arg<Type>()));
		var parser = new MUSHCodeParser(logger, library, original.CommandLibrary, original.Configuration, services)
			.FromState(ParserState.RootFor(new DBRef(1, 1)));
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(EvaluationRestrictions.Error);
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
		await Assert.That(notify.ReceivedCalls().Any()).IsFalse();
		await Assert.That(mediator.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments("fn(add,1,2)")]
	[Arguments("fn(fn,add,1,2)")]
	[Arguments("fn_alias(add,1,2)")]
	public async Task OrdinaryFnRetainsConfiguredParserTracing(string expression)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("fn_alias", library["fn"]);
		var parser = original with
		{
			FunctionLibrary = library,
			Configuration = new Options(original.Configuration.CurrentValue with
			{
				Debug = original.Configuration.CurrentValue.Debug with { DebugSharpParser = true }
			})
		};
#pragma warning disable TUnit0055
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			await parser.FunctionParse(MarkupText.Plain(expression));
		}
		finally { Console.SetOut(previous); }
#pragma warning restore TUnit0055
		await Assert.That(output.ToString()).Contains("LT(1)=" + expression[..(expression.IndexOf('(') + 1)]);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnexpectedRestrictedFailuresDoNotLogOrNotify(bool ambient)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library["add"] = (library["add"].LibraryInformation with
		{
			Function = _ => throw new InvalidOperationException("private-failure")
		}, true);
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var restrictions = new EvaluationRestrictions(["add"]);
		var state = ParserState.RootFor(original.CurrentState.Executor!.Value) with
		{
			Restrictions = ambient ? null : restrictions
		};
		var parser = (original with { FunctionLibrary = library, Logger = logger }).FromState(state);
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		using var scope = ambient ? restrictions.Enter() : null;
		var result = await parser.FunctionParse(MarkupText.Plain("add(1,2)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(EvaluationRestrictions.Error);
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments("restricted_alias(ucstr,ucstr(%0),private-input)")]
	[Arguments("cat(restricted_alias(ucstr,ucstr(%0),private-input))")]
	[Arguments("fn(restricted_alias,ucstr,ucstr(%0),private-input)")]
	public async Task RestrictedWrapperAliasesSuppressSubstitutionFallbackDebug(string expression)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value)
			with
		{ Flags = ParserStateFlags.Debug });
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		var result = await parser.FunctionParse(MarkupText.Plain(expression), true);
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("PRIVATE-INPUT");
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments("-2147483648", "1", "a")]
	[Arguments("1", "-2147483648", "a b")]
	[Arguments("-2147483648", "-2147483648", "a b")]
	public async Task ExtractHandlesMinimumSignedPositionsWithoutAnException(string start, string length, string expected)
	{
		await Assert.That(await Eval($"extract(a b,{start},{length})")).IsEqualTo(expected);
		await Assert.That(await Eval($"restrictedexpr(extract,extract(a b,{start},{length}))")).IsEqualTo(EvaluationRestrictions.Error);
	}

	[Test]
	[Arguments(false, FunctionFlags.LogArgs)]
	[Arguments(false, FunctionFlags.LogName)]
	[Arguments(false, FunctionFlags.Deprecated)]
	[Arguments(true, FunctionFlags.LogArgs)]
	[Arguments(true, FunctionFlags.LogName)]
	[Arguments(true, FunctionFlags.Deprecated)]
	public async Task RestrictedCallsSuppressFunctionMetadataOutput(bool wrapper, FunctionFlags outputFlag)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var name = wrapper ? "restrictedexpr" : "add";
		var definition = library[name].LibraryInformation;
		library[name] = (definition with
		{
			Attribute = new SharpFunctionAttribute
			{
				Name = name, MinArgs = definition.Attribute.MinArgs, MaxArgs = definition.Attribute.MaxArgs,
				Flags = definition.Attribute.Flags | outputFlag
			}
		}, true);
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var parser = (original with { FunctionLibrary = library, Logger = logger }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value));
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		var result = await parser.FunctionParse(MarkupText.Plain("restrictedexpr(add,add(%0,2),3)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("5");
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictedCancellationPropagatesWithoutDiagnosticOutput(bool ambient)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library["add"] = (library["add"].LibraryInformation with { Function = _ => throw new OperationCanceledException() }, true);
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var restrictions = new EvaluationRestrictions(["add"]);
		var parser = (original with { FunctionLibrary = library, Logger = logger }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value)
			with
		{ Restrictions = ambient ? null : restrictions });
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		using var scope = ambient ? restrictions.Enter() : null;
		await Assert.That(async () => await parser.FunctionParse(MarkupText.Plain("add(1,2)"))).Throws<OperationCanceledException>();
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictedSyntaxDiagnosticsRemainSilent(bool ambient)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var restrictions = new EvaluationRestrictions(["add"]);
		var configured = original with
		{
			Logger = logger,
			Configuration = new Options(original.Configuration.CurrentValue with
			{
				Debug = original.Configuration.CurrentValue.Debug with { DebugSharpParser = true, ParserPredictionMode = ParserPredictionMode.TwoStage }
			})
		};
		var parser = configured.FromState(ParserState.RootFor(original.CurrentState.Executor!.Value)
			with
		{ Restrictions = ambient ? null : restrictions });
		using var scope = ambient ? restrictions.Enter() : null;
		var result = await parser.FunctionParse(MarkupText.Plain("add(1,"));
		await Assert.That(result!.HadErrors).IsTrue();
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
	}

	[Test]
	[Arguments("restrictedexpr(ucstr,ucstr(%0),private-input")]
	[Arguments("restricted_alias(ucstr,ucstr(%0),private-input")]
	[Arguments("fn(restricted_alias,ucstr,ucstr(%0),private-input")]
	public async Task MalformedRestrictedWrappersDoNotForwardRawInput(string expression)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var parser = (original with { FunctionLibrary = library }).FromState(ParserState.RootFor(original.CurrentState.Executor!.Value)
			with
		{ Flags = ParserStateFlags.Debug });
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		var result = await parser.FunctionParse(MarkupText.Plain(expression), true);
		await Assert.That(result!.HadErrors).IsTrue();
		await Assert.That(notify.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Notify")).IsFalse();
	}

	[Test]
	[Arguments("restrictedexpr(ucstr,ucstr(%0),private-input)")]
	[Arguments("restricted_alias(ucstr,ucstr(%0),private-input)")]
	[Arguments("fn(restricted_alias,ucstr,ucstr(%0),private-input)")]
	[Arguments("restricted_alias(ucstr,ucstr(%0),private-input")]
	[Arguments("fn(fn,restricted_alias,ucstr,ucstr(%0),private-input)")]
	[Arguments("fn(fn,restricted_alias,ucstr,ucstr(%0),private-input")]
	[Arguments("fn(not_registered,private-input")]
	public async Task InitialRestrictedWrapperParsingDoesNotTraceInputs(string expression)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var parser = original with
		{
			FunctionLibrary = library,
			Logger = logger,
			Configuration = new Options(original.Configuration.CurrentValue with
			{
				Debug = original.Configuration.CurrentValue.Debug with { DebugSharpParser = true, ParserPredictionMode = ParserPredictionMode.TwoStage }
			})
		};
		// This nonparallel test captures ANTLR Trace output; restore TUnit's writer in finally.
#pragma warning disable TUnit0055
		var previous = Console.Out;
		using var output = new StringWriter();
		try
		{
			Console.SetOut(output);
			await parser.FunctionParse(MarkupText.Plain(expression));
		}
		finally { Console.SetOut(previous); }
#pragma warning restore TUnit0055
		await Assert.That(output.ToString()).IsEmpty();
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
	}

	[Test]
	[Arguments("restrictedexpr(add,add(1,2))", false, false)]
	[Arguments("restricted_alias(add,add(1,2))", false, false)]
	[Arguments("fn(restricted_alias,add,add(1,2))", false, false)]
	[Arguments("add(1,2)", true, false)]
	[Arguments("add(1,2)", false, true)]
	[Arguments("restrictedexpr(add fn restrictedexpr,fn(add,1,restrictedexpr(add,add(1,1))))", false, false)]
	public async Task RestrictedDispatchDoesNotResolveAnExecutor(string expression, bool ambient, bool stateCarried)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		var services = Substitute.For<IServiceProvider>();
		services.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IMediator)
			? mediator : original.ServiceProvider.GetService(call.Arg<Type>()));
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var parser = new MUSHCodeParser(original.Logger, library, original.CommandLibrary, original.Configuration, services)
			.FromState(ParserState.RootFor(new DBRef(987654321, 1)) with
			{ Restrictions = stateCarried ? new EvaluationRestrictions(["add"]) : null });
		using var scope = ambient ? new EvaluationRestrictions(["add"]).Enter() : null;
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo("3");
		await Assert.That(mediator.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments(FunctionFlags.Disabled, false)]
	[Arguments(FunctionFlags.GodOnly, false)]
	[Arguments(FunctionFlags.WizardOnly, false)]
	[Arguments(FunctionFlags.Regular, true)]
	public async Task RestrictedObjectlessDispatchRetainsPermissionGates(FunctionFlags flags, bool overlay)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		var registry = Substitute.For<IUserDefinedFunctionService>();
		if (overlay) registry.GetBuiltinRestriction("add").Returns("wizard");
		var services = Substitute.For<IServiceProvider>();
		services.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IMediator) ? mediator
			: call.Arg<Type>() == typeof(IUserDefinedFunctionService) ? registry : original.ServiceProvider.GetService(call.Arg<Type>()));
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var add = library["add"].LibraryInformation;
		library["add"] = (add with { Attribute = new SharpFunctionAttribute { Name = "add", MinArgs = 2, MaxArgs = 2, Flags = flags } }, true);
		var parser = new MUSHCodeParser(original.Logger, library, original.CommandLibrary, original.Configuration, services)
			.FromState(ParserState.RootFor(new DBRef(987654321, 1)) with { Restrictions = new EvaluationRestrictions(["add"]) });
		var result = await parser.FunctionParse(MarkupText.Plain("add(1,2)"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(flags == FunctionFlags.Disabled
			? ErrorMessages.Returns.FunctionDisabled : ErrorMessages.Returns.PermissionDenied);
		await Assert.That(mediator.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	[Arguments("restrictedexpr(add,add(1,2))", "3")]
	[Arguments("restricted_alias(add,add(1,2))", "3")]
	[Arguments("fn(restricted_alias,add,add(1,2))", "3")]
	[Arguments("restrictedexpr(add,get(#1/DESC))", EvaluationRestrictions.Error)]
	public async Task RestrictedWrappersAcceptANullExecutorWithoutDiagnostics(string expression, string expected)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var logger = Substitute.For<ILogger<MUSHCodeParser>>();
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		library.Add("restricted_alias", library["restrictedexpr"]);
		var parser = (original with { Logger = logger, FunctionLibrary = library }).FromState(ParserState.RootFor(default)
			with
		{ Executor = null, Enactor = null, Caller = null });
		var result = await parser.FunctionParse(MarkupText.Plain(expression));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
		await Assert.That(logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Log")).IsFalse();
	}

}
