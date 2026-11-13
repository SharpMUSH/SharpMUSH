using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A function frame runs over its caller's <see cref="EvaluationContext"/>; replacing a shared value with
/// <c>with</c> gives only that frame, and what forks from it, a context of its own.
/// </summary>
public class ParserStateContextTests
{
	[Test]
	public async Task AStateWithoutACommandHasTheNoOpInvoker()
	{
		var root = ParserState.RootFor(new DBRef(7));
		await Assert.That(root.CommandInvoker is not null).IsTrue();
		await Assert.That(root.ForFunction("add", ArgumentList.Empty).CommandInvoker is not null).IsTrue();
		await Assert.That(root.SnapshotForQueuedAction().CommandInvoker is not null).IsTrue();
	}

	[Test]
	public async Task AQueuedSnapshotDetachesTheEntireLiveHttpResponseContext()
	{
		var response = new HttpResponseContext { ContentType = "application/parent" };
		var state = ParserState.RootFor(new DBRef(7)) with { HttpResponse = response };
		await Assert.That(state.SnapshotForQueuedAction().HttpResponse).IsNull();
		await Assert.That(ReferenceEquals(state.HttpResponse, response)).IsTrue();
	}

	[Test]
	public async Task QueuedArgumentsRetainValuesWithoutRetainingDeferredParsers()
	{
		var argument = new CallState(MarkupText.Plain("captured"), 5, null,
			() => throw new InvalidOperationException("Retained the submitting parser"));
		var state = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(ArgumentList.Of(argument));
		var queued = state.SnapshotForQueuedAction();
		await Assert.That((await queued.ArgumentList["0"].GetParsedResultAsync()).Message.ToPlainText()).IsEqualTo("captured");
		await Assert.That((await queued.EnvironmentRegisters["0"].GetParsedResultAsync()).Message.ToPlainText()).IsEqualTo("captured");
	}

	[Test]
	public async Task QueuedArgumentsAreDetachedFromCallerMutation()
	{
		var arguments = new Dictionary<string, CallState> { ["0"] = new("before") };
		var state = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(ArgumentList.From(arguments)) with { CallerArguments = ArgumentList.From(arguments) };
		var queued = state.SnapshotForQueuedAction();
		arguments["0"] = new("after");
		await Assert.That(queued.ArgumentList["0"].Message.ToPlainText()).IsEqualTo("before");
		await Assert.That(queued.CallerArguments!["0"].Message.ToPlainText()).IsEqualTo("before");
	}

	[Test]
	public async Task AttributeInvocationBinderReplacesParentPresenceAndExcludesGeneratedKeys()
	{
		var parentArguments = new Dictionary<string, CallState> { ["parent"] = new("old") };
		var parent = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(ArgumentList.From(parentArguments));
		var arguments = new AttributeArgumentEnvironment { ["0"] = new(""), ["itemscount"] = new("1") };
		arguments.GeneratedKeys.Add("itemscount");
		var invocation = parent.ForAttributeArguments(ArgumentList.From(arguments));
		await Assert.That(invocation.SuppliedArgumentKeys!.Contains("0")).IsTrue();
		await Assert.That(invocation.SuppliedArgumentKeys.Contains("itemscount")).IsFalse();
		await Assert.That(invocation.SuppliedArgumentKeys.Contains("parent")).IsFalse();
		await Assert.That(invocation.EnvironmentRegisters.ContainsKey("itemscount")).IsTrue();
		await Assert.That(parent.ForAttributeArguments(ArgumentList.Empty).SuppliedArgumentKeys!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task QueuedPresenceIsImmutableAndDoesNotForceValues()
	{
		var environment = new Dictionary<string, CallState> { ["who"] = new CallState(MarkupText.Empty, 0, null, () => throw new InvalidOperationException("Presence forced an argument")) };
		var state = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(ArgumentList.From(environment));
		var queued = state.SnapshotForQueuedAction();
		environment.Clear();
		await Assert.That(queued.SuppliedArgumentKeys!.Contains("WHO")).IsTrue();
		await Assert.That(queued.ForFunction("hasarg", ArgumentList.Empty).SuppliedArgumentKeys!.Contains("who")).IsTrue();
		await Assert.That(queued.EnvironmentRegisters.ContainsKey("who")).IsTrue();
	}

	[Test]
	public async ValueTask AFunctionFrameSharesItsCallersContext()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var frame = root.ForFunction("add", ArgumentList.Empty);

		await Assert.That(ReferenceEquals(frame.Context, root.Context)).IsTrue();
		await Assert.That(frame.ParserFunctionDepth).IsEqualTo(1);
		await Assert.That(frame.Function).IsEqualTo("add");
	}

	[Test]
	public async ValueTask AFunctionFrameKeepsTheTypedLineAndSession()
	{
		var typed = ParserState.ForTypedLine(new DBRef(7), handle: 3, session: "session-a", outputLimit: 100, line: "think [add(1,2)]");
		var frame = typed.ForFunction("add", ArgumentList.Empty);

		await Assert.That(frame.TypedLine).IsEqualTo("think [add(1,2)]");
		await Assert.That(frame.ConnectionSessionId).IsEqualTo("session-a");
	}

	[Test]
	public async ValueTask ReplacingTheEnvironmentKeepsTheSharedValues()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var environment = new Dictionary<string, CallState>();
		var localized = root.ForAttributeArguments(ArgumentList.Empty, environment);

		await Assert.That(ReferenceEquals(localized.EnvironmentRegisters, environment)).IsTrue();
		await Assert.That(ReferenceEquals(localized.Context.EnvironmentRegisters, environment)).IsTrue();
		await Assert.That(ReferenceEquals(root.EnvironmentRegisters, environment)).IsFalse();
		// Everything it did not replace is still the evaluation's.
		await Assert.That(ReferenceEquals(localized.TotalInvocations, root.TotalInvocations)).IsTrue();
		await Assert.That(ReferenceEquals(localized.Registers, root.Registers)).IsTrue();
	}

	[Test]
	public async ValueTask ReplacingASharedValueCopiesTheContextForThatFrameOnly()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var registers = new System.Collections.Concurrent.ConcurrentStack<Dictionary<string, MString>>();
		var localized = root with { Registers = registers };

		await Assert.That(ReferenceEquals(localized.Context, root.Context)).IsFalse();
		await Assert.That(ReferenceEquals(localized.Registers, registers)).IsTrue();
		await Assert.That(ReferenceEquals(root.Registers, registers)).IsFalse();
		await Assert.That(ReferenceEquals(localized.TotalInvocations, root.TotalInvocations)).IsTrue();
	}

	[Test]
	public async ValueTask AnAttributeCallBindsItsEnvironmentOverTheSharedContext()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var arguments = new Dictionary<string, CallState> { ["0"] = new("item") };
		var call = root.ForAttributeCall(ArgumentList.From(arguments), arguments, new DBAttribute(new DBRef(9), "FN"), "FN", new DBRef(9));

		await Assert.That(call.Executor).IsEqualTo(new DBRef(9));
		await Assert.That(call.Caller).IsEqualTo(new DBRef(7));
		await Assert.That(call.Enactor).IsEqualTo(new DBRef(7));
		await Assert.That(ReferenceEquals(call.EnvironmentRegisters, arguments)).IsTrue();
		await Assert.That(call.SuppliedArgumentKeys!.Contains("0")).IsTrue();
		await Assert.That(ReferenceEquals(root.EnvironmentRegisters, arguments)).IsFalse();
		await Assert.That(root.SuppliedArgumentKeys).IsNull();
		await Assert.That(ReferenceEquals(call.TotalInvocations, root.TotalInvocations)).IsTrue();
		await Assert.That(ReferenceEquals(call.Registers, root.Registers)).IsTrue();

		// A function the body calls reads the same environment and actors.
		var inner = call.ForFunction("add", ArgumentList.Empty);
		await Assert.That(ReferenceEquals(inner.EnvironmentRegisters, arguments)).IsTrue();
		await Assert.That(inner.SuppliedArgumentKeys!.Contains("0")).IsTrue();
		await Assert.That(inner.Executor).IsEqualTo(new DBRef(9));
		await Assert.That(inner.Caller).IsEqualTo(new DBRef(7));

		// Its context, read whole, carries the environment; setting a context replaces it.
		await Assert.That(ReferenceEquals(call.Context.EnvironmentRegisters, arguments)).IsTrue();
		var reset = call with { Context = root.Context };
		await Assert.That(ReferenceEquals(reset.EnvironmentRegisters, root.EnvironmentRegisters)).IsTrue();
		await Assert.That(reset.SuppliedArgumentKeys).IsNull();
	}

	[Test]
	public async ValueTask ChangingAnActorLeavesTheCallersActorsAlone()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var changed = root with { Executor = new DBRef(9), HookedCommand = MarkupText.Plain("look") };

		await Assert.That(changed.Executor).IsEqualTo(new DBRef(9));
		await Assert.That(changed.Enactor).IsEqualTo(new DBRef(7));
		await Assert.That(root.Executor).IsEqualTo(new DBRef(7));
		await Assert.That(root.HookedCommand).IsNull();
		await Assert.That(changed.ForFunction("add", ArgumentList.Empty).HookedCommand!.ToPlainText()).IsEqualTo("look");
	}

	[Test]
	public async ValueTask AQueuedSnapshotOwnsItsCounters()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var snapshot = root.SnapshotForQueuedAction();

		await Assert.That(ReferenceEquals(snapshot.TotalInvocations, root.TotalInvocations)).IsFalse();
		await Assert.That(ReferenceEquals(snapshot.Registers, root.Registers)).IsFalse();
		await Assert.That(snapshot.CommandText).IsNull();
	}
}
