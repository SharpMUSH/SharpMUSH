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
	public async Task QueuedArgumentsRetainValuesWithoutRetainingDeferredParsers()
	{
		var argument = new CallState(MarkupText.Plain("captured"), 5, null,
			() => throw new InvalidOperationException("Retained the submitting parser"));
		var state = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(new() { ["0"] = argument });
		var queued = state.SnapshotForQueuedAction();
		await Assert.That((await queued.Arguments["0"].GetParsedResultAsync()).Message.ToPlainText()).IsEqualTo("captured");
		await Assert.That((await queued.EnvironmentRegisters["0"].GetParsedResultAsync()).Message.ToPlainText()).IsEqualTo("captured");
	}

	[Test]
	public async Task QueuedArgumentsAreDetachedFromCallerMutation()
	{
		var arguments = new Dictionary<string, CallState> { ["0"] = new("before") };
		var state = ParserState.RootFor(new DBRef(7)).ForAttributeArguments(arguments) with { CallerArguments = arguments };
		var queued = state.SnapshotForQueuedAction();
		arguments["0"] = new("after");
		await Assert.That(queued.Arguments["0"].Message.ToPlainText()).IsEqualTo("before");
		await Assert.That(queued.CallerArguments!["0"].Message.ToPlainText()).IsEqualTo("before");
	}

	[Test]
	public async Task AttributeInvocationBinderReplacesParentPresenceAndExcludesGeneratedKeys()
	{
		var parentArguments = new Dictionary<string, CallState> { ["parent"] = new("old") };
		var parent = ParserState.RootFor(new DBRef(7)) with { SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(parentArguments) };
		var arguments = new AttributeArgumentEnvironment { ["0"] = new(""), ["itemscount"] = new("1") };
		arguments.GeneratedKeys.Add("itemscount");
		var invocation = parent.ForAttributeArguments(arguments);
		await Assert.That(invocation.SuppliedArgumentKeys!.Contains("0")).IsTrue();
		await Assert.That(invocation.SuppliedArgumentKeys.Contains("itemscount")).IsFalse();
		await Assert.That(invocation.SuppliedArgumentKeys.Contains("parent")).IsFalse();
		await Assert.That(invocation.EnvironmentRegisters.ContainsKey("itemscount")).IsTrue();
		await Assert.That(parent.ForAttributeArguments([]).SuppliedArgumentKeys!.Count).IsEqualTo(0);
	}

	[Test]
	public async Task QueuedPresenceIsImmutableAndDoesNotForceValues()
	{
		var environment = new Dictionary<string, CallState> { ["who"] = new CallState(MarkupText.Empty, 0, null, () => throw new InvalidOperationException("Presence forced an argument")) };
		var state = ParserState.RootFor(new DBRef(7)) with { EnvironmentRegisters = environment, SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(environment) };
		var queued = state.SnapshotForQueuedAction();
		environment.Clear();
		await Assert.That(queued.SuppliedArgumentKeys!.Contains("WHO")).IsTrue();
		await Assert.That(queued.ForFunction("hasarg", []).SuppliedArgumentKeys!.Contains("who")).IsTrue();
		await Assert.That(queued.EnvironmentRegisters.ContainsKey("who")).IsTrue();
	}

	[Test]
	public async ValueTask AFunctionFrameSharesItsCallersContext()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var frame = root.ForFunction("add", new Dictionary<string, CallState>());

		await Assert.That(ReferenceEquals(frame.Context, root.Context)).IsTrue();
		await Assert.That(frame.ParserFunctionDepth).IsEqualTo(1);
		await Assert.That(frame.Function).IsEqualTo("add");
	}

	[Test]
	public async ValueTask AFunctionFrameKeepsTheTypedLineAndSession()
	{
		var typed = ParserState.ForTypedLine(new DBRef(7), handle: 3, session: "session-a", outputLimit: 100, line: "think [add(1,2)]");
		var frame = typed.ForFunction("add", new Dictionary<string, CallState>());

		await Assert.That(frame.TypedLine).IsEqualTo("think [add(1,2)]");
		await Assert.That(frame.ConnectionSessionId).IsEqualTo("session-a");
	}

	[Test]
	public async ValueTask ReplacingASharedValueCopiesTheContextForThatFrameOnly()
	{
		var root = ParserState.RootFor(new DBRef(7));
		var environment = new Dictionary<string, CallState>();
		var localized = root with { EnvironmentRegisters = environment };

		await Assert.That(ReferenceEquals(localized.Context, root.Context)).IsFalse();
		await Assert.That(ReferenceEquals(localized.EnvironmentRegisters, environment)).IsTrue();
		await Assert.That(ReferenceEquals(root.EnvironmentRegisters, environment)).IsFalse();
		// Everything it did not replace is still the evaluation's.
		await Assert.That(ReferenceEquals(localized.TotalInvocations, root.TotalInvocations)).IsTrue();
		await Assert.That(ReferenceEquals(localized.Registers, root.Registers)).IsTrue();
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
