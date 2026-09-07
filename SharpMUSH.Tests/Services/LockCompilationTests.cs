using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Where a compiled lock expression is cached, and by what.
/// </summary>
/// <remarks>
/// <see cref="IBooleanExpressionParser.Compile"/> already caches, keyed by the expression text, in the
/// dedicated bounded cache Startup registers for exactly this. LockService cached the delegate a
/// second time in the engine cache, keyed by object and lock type — which cannot be right: the
/// delegate depends on the lock string and nothing else, so an object-keyed entry outlives the text
/// that produced it.
/// </remarks>
public class LockCompilationTests
{
	private readonly TestObjectFactory _factory = new();
	private readonly IBooleanExpressionParser _parser = Substitute.For<IBooleanExpressionParser>();

	/// <summary>Compiles to a delegate that answers with whether the text was the one given here.</summary>
	private void CompilesTo(string text, bool answer)
		=> _parser.Compile(text).Returns(_ => (AnySharpObject _, AnySharpObject _) => answer);

	private AnySharpObject LockedWith(int number, string lockString)
	{
		var obj = _factory.CreateThing(number, $"Thing {number}");
		obj.Object().Locks = ImmutableDictionary<string, SharpLockData>.Empty
			.Add(LockType.Basic.ToString(), new SharpLockData { LockString = lockString });
		return obj;
	}

	[Test]
	public async Task ChangingTheLockStringChangesTheAnswer()
	{
		var service = new LockService(_parser);
		var unlocker = _factory.CreateThing(1, "Unlocker");
		CompilesTo("=#1", true);
		CompilesTo("=#2", false);

		var gated = LockedWith(7, "=#1");
		await Assert.That(service.Evaluate(LockType.Basic, gated, unlocker)).IsTrue();

		gated.Object().Locks = ImmutableDictionary<string, SharpLockData>.Empty
			.Add(LockType.Basic.ToString(), new SharpLockData { LockString = "=#2" });

		await Assert.That(service.Evaluate(LockType.Basic, gated, unlocker)).IsFalse()
			.Because("the delegate belongs to the lock text, so a new lock string cannot reach an old one");
	}
}
