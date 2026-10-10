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
