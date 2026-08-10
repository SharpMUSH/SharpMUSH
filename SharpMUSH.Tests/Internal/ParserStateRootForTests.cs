using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Internal;

/// <summary>
/// Contract tests for <see cref="ParserState.RootFor"/>, the shared "fresh root state acting as
/// one object" constructor. Three callers used to hand-roll this block — the boot @STARTUP pass,
/// the package lifecycle runner, and the HTTP handler dispatcher — and a fourth (the portal's
/// engine command invoker) needs it too.
///
/// The distinction that matters is against <see cref="ParserState.Empty"/>, whose register stack
/// is genuinely empty. A root state must ship one q-register frame, or the first setq() in the
/// code it runs has nowhere to write.
/// </summary>
public class ParserStateRootForTests
{
	private static readonly DBRef Actor = new(42, 1700000000);

	[Test]
	public async Task RootFor_BindsExecutorEnactorAndCaller_ToTheSameObject()
	{
		var state = ParserState.RootFor(Actor);

		await Assert.That(state.Executor).IsEqualTo(Actor);
		await Assert.That(state.Enactor).IsEqualTo(Actor);
		await Assert.That(state.Caller).IsEqualTo(Actor);
	}

	[Test]
	public async Task RootFor_SeedsExactlyOneRegisterFrame()
	{
		var state = ParserState.RootFor(Actor);

		await Assert.That(state.Registers.Count).IsEqualTo(1)
			.Because("code running from a root state must be able to setq() without pushing a frame first");
	}

	[Test]
	public async Task RootFor_HasNoConnectionAndNoHttpResponse()
	{
		var state = ParserState.RootFor(Actor);

		await Assert.That(state.Handle).IsNull();
		await Assert.That(state.HttpResponse).IsNull();
		await Assert.That(state.ParseMode).IsEqualTo(ParseMode.Default);
	}

	[Test]
	public async Task RootFor_ProvidesTheLimitCounters()
	{
		var state = ParserState.RootFor(Actor);

		await Assert.That(state.CallDepth).IsNotNull();
		await Assert.That(state.TotalInvocations).IsNotNull();
		await Assert.That(state.LimitExceeded).IsNotNull();
		await Assert.That(state.FunctionRecursionDepths).IsNotNull();
	}

	[Test]
	public async Task RootFor_StartsWithNoArgumentsOrEnvironmentRegisters()
	{
		var state = ParserState.RootFor(Actor);

		await Assert.That(state.Arguments).IsEmpty();
		await Assert.That(state.EnvironmentRegisters).IsEmpty();
	}

	[Test]
	public async Task RootFor_ReturnsAnIndependentStatePerCall()
	{
		var first = ParserState.RootFor(Actor);
		var second = ParserState.RootFor(Actor);

		first.Arguments["0"] = new CallState("mine");

		await Assert.That(second.Arguments).IsEmpty()
			.Because("two invocations sharing a mutable dictionary would leak arguments between callers");
	}
}
