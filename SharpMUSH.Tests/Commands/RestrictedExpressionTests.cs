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
	public async Task ExplicitInputsAndPureOperationsWork()
	{
		await Assert.That(await Eval("restrictedexpr(add,add(%0,%1),2,3)")).IsEqualTo("5");
		await Assert.That(await Eval("restrictedexpr(ucstr first,ucstr(first(%0)),hello world)")).IsEqualTo("HELLO");
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
	[Arguments(false)]
	[Arguments(true)]
	public async Task RestrictedDebugExecutorDoesNotForwardExpressionOrResults(bool denied)
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
		var state = ParserState.RootFor(target) with { Flags = ParserStateFlags.NoDebug };
		var parser = Factory.FunctionParser.FromState(state);
		var notify = Factory.Services.GetRequiredService<INotifyService>();
		notify.ClearReceivedCalls();
		var expression = denied ? "restrictedexpr(ucstr,get(%0),private-input)" : "restrictedexpr(ucstr,ucstr(%0),private-input)";
		var result = (await parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
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

}
