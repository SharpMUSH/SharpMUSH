using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.Assertions.Enums;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// A compiled lock is a composition of async leaf delegates, and the boolean operators are ordinary
/// C# <c>&amp;&amp;</c>, <c>||</c> and <c>!</c> over awaited operands. That gives two properties worth
/// pinning independently of how the composition is built:
///
/// <list type="number">
///   <item><description>The truth table is right — including the asymmetric cases, where an operand
///     transposition is the difference between deny and allow.</description></item>
///   <item><description>The short-circuit is real — <c>a&amp;b</c> must not evaluate <c>b</c> when
///     <c>a</c> is false, and <c>a|b</c> must not evaluate <c>b</c> when <c>a</c> is true. Every leaf
///     that matters reads the database, so an operator that evaluates both sides is not merely
///     wasteful: on the single-threaded command queue it is a read nobody asked for.</description></item>
/// </list>
///
/// <para>Both were what <c>DotNext.Metaprogramming</c>'s <c>AsyncLambda</c> got wrong when it was
/// measured as an alternative to this composition (issue #873): it transposed the operands of
/// <c>AndAlso</c>/<c>OrElse</c> and evaluated the right-hand side unconditionally, so every compound
/// lock in the game inverted. These tests hold regardless of implementation, and are the reason to
/// re-check any rewriter before it is reached for again.</para>
///
/// <para>The leaves here are eval locks (<c>ATTR/yes</c>) over a recording
/// <see cref="ILockEvaluationServices"/>: each one names itself when evaluated, so both the order and
/// the fact of evaluation are observable.</para>
///
/// <para>Every case runs twice, against a leaf that suspends and a leaf that answers synchronously.
/// The combinators take a different route through each — a leaf that is already complete skips the
/// async state machine entirely — and the short-circuit has to survive both. A cache hit is the
/// synchronous case, which is to say the common one.</para>
/// </summary>
public class LockShortCircuitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// A lock leaf that answers <c>true</c>. Written as an eval lock so the recording seam below sees it.
	/// </summary>
	private const string Passes = "PASSES/yes";

	/// <summary>A lock leaf that answers <c>false</c>.</summary>
	private const string Fails = "FAILS/yes";

	[Arguments($"{Passes} & {Passes}", true)]
	[Arguments($"{Passes} & {Fails}", false)]
	[Arguments($"{Fails} & {Passes}", false)]
	[Arguments($"{Fails} & {Fails}", false)]
	[Arguments($"{Passes} | {Passes}", true)]
	[Arguments($"{Passes} | {Fails}", true)]
	[Arguments($"{Fails} | {Passes}", true)]
	[Arguments($"{Fails} | {Fails}", false)]
	[Arguments($"!{Passes}", false)]
	[Arguments($"!{Fails}", true)]
	[Test]
	public async Task TruthTableOverAsyncLeaves(string lockString, bool expected)
	{
		foreach (var leavesSuspend in BothRoutes)
		{
			var (parser, services) = Build(leavesSuspend);
			var one = await God();

			await Assert.That(await parser.Compile(lockString)(one, one))
				.IsEqualTo(expected)
				.Because($"{lockString} is {expected} ({Route(leavesSuspend)})");
			await Assert.That(services.Evaluated).IsNotEmpty();
		}
	}

	[Arguments($"({Fails} | {Passes}) & {Passes}", true)]
	[Arguments($"({Fails} | {Passes}) & {Fails}", false)]
	[Arguments($"({Passes} & {Fails}) | {Passes}", true)]
	[Arguments($"({Passes} & {Fails}) | {Fails}", false)]
	[Arguments($"!({Fails} | {Passes})", false)]
	[Arguments($"!({Passes} & {Fails})", true)]
	// AND binds tighter than OR, so this is (F & P) | P, not F & (P | P).
	[Arguments($"{Fails} & {Passes} | {Passes}", true)]
	[Test]
	public async Task NestingDoesNotTransposeOperands(string lockString, bool expected)
	{
		foreach (var leavesSuspend in BothRoutes)
		{
			var (parser, _) = Build(leavesSuspend);
			var one = await God();

			await Assert.That(await parser.Compile(lockString)(one, one))
				.IsEqualTo(expected)
				.Because($"{lockString} is {expected} ({Route(leavesSuspend)})");
		}
	}

	[Test]
	public async Task AndDoesNotEvaluateItsRightOperandWhenTheLeftIsFalse()
	{
		foreach (var leavesSuspend in BothRoutes)
		{
			var (parser, services) = Build(leavesSuspend);
			var one = await God();

			await Assert.That(await parser.Compile($"{Fails} & {Passes}")(one, one)).IsFalse();
			await Assert.That(services.Evaluated).IsEquivalentTo(new[] { "FAILS" })
				.Because($"the answer was settled by the left operand, so the right one names a database read nobody needs ({Route(leavesSuspend)})");
		}
	}

	[Test]
	public async Task OrDoesNotEvaluateItsRightOperandWhenTheLeftIsTrue()
	{
		foreach (var leavesSuspend in BothRoutes)
		{
			var (parser, services) = Build(leavesSuspend);
			var one = await God();

			await Assert.That(await parser.Compile($"{Passes} | {Fails}")(one, one)).IsTrue();
			await Assert.That(services.Evaluated).IsEquivalentTo(new[] { "PASSES" })
				.Because($"the answer was settled by the left operand, so the right one names a database read nobody needs ({Route(leavesSuspend)})");
		}
	}

	[Arguments($"{Passes} & {Fails}", "PASSES", "FAILS")]
	[Arguments($"{Fails} | {Passes}", "FAILS", "PASSES")]
	[Test]
	public async Task AnOperandTheAnswerStillDependsOnIsEvaluated_InSourceOrder(
		string lockString, string first, string second)
	{
		foreach (var leavesSuspend in BothRoutes)
		{
			var (parser, services) = Build(leavesSuspend);
			var one = await God();

			_ = await parser.Compile(lockString)(one, one);

			// CollectionOrdering.Matching, because IsEquivalentTo ignores order by default and this test
			// is entirely about order — without it a transposition passes.
			await Assert.That(services.Evaluated).IsEquivalentTo(new[] { first, second }, CollectionOrdering.Matching)
				.Because($"the left operand did not settle the answer, so both sides run — left to right ({Route(leavesSuspend)})");
		}
	}

	/// <summary>
	/// A leaf that suspends and a leaf that answers synchronously take different routes through the
	/// combinators — the completed one skips the async state machine — and every property here has to
	/// hold on both. A cache hit is the synchronous route, which is to say the common one.
	/// </summary>
	private static readonly bool[] BothRoutes = [true, false];

	private static string Route(bool leavesSuspend) => leavesSuspend ? "suspending leaves" : "completed leaves";

	private async ValueTask<AnySharpObject> God()
		=> (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

	/// <summary>
	/// A parser per case, over its own cache: these tests compile the same lock text under both
	/// completion routes, and a shared compiled-expression cache would hand the second one the first
	/// one's delegate.
	/// </summary>
	private (BooleanExpressionParser Parser, RecordingEvaluationServices Services) Build(bool leavesSuspend)
	{
		var services = new RecordingEvaluationServices(leavesSuspend, "PASSES");
		var cache = new FusionCache(new FusionCacheOptions());
		_caches.Add(cache);
		return (new BooleanExpressionParser(services, Substitute.For<IMediator>(), cache), services);
	}

	private readonly List<FusionCache> _caches = [];

	[After(Test)]
	public void DisposeCaches()
	{
		foreach (var cache in _caches)
		{
			cache.Dispose();
		}

		_caches.Clear();
	}

	/// <summary>
	/// Answers eval-lock leaves by name, recording each one as it is asked, so a leaf that runs out of
	/// order — or runs at all when it should not have — shows up in <see cref="Evaluated"/>.
	/// <para>
	/// <paramref name="suspend"/> picks which route through the combinators the answer takes: a leaf
	/// that yields drives the async slow path, one that answers synchronously drives the completed-
	/// operand fast path. Both have to short-circuit.
	/// </para>
	/// </summary>
	private sealed class RecordingEvaluationServices(bool suspend, params string[] passing) : ILockEvaluationServices
	{
		private readonly List<string> _evaluated = [];

		public IReadOnlyList<string> Evaluated
		{
			get { lock (_evaluated) { return _evaluated.ToArray(); } }
		}

		public async ValueTask<OneOf<string, LockEvaluationFailure>> EvaluateAttributeAsync(
			AnySharpObject gated, AnySharpObject unlocker, string attributeName)
		{
			lock (_evaluated)
			{
				_evaluated.Add(attributeName);
			}

			if (suspend)
			{
				await Task.Yield();
			}

			return passing.Contains(attributeName, StringComparer.OrdinalIgnoreCase) ? "yes" : "no";
		}

		public ValueTask<AnyOptionalSharpObjectOrError> LocateAsync(AnySharpObject looker, AnySharpObject executor,
			string name, LocateFlags flags)
			=> throw new NotSupportedException("These locks are eval locks only.");

		public ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj,
			string attribute, IAttributeService.AttributeMode mode, bool parent = true)
			=> throw new NotSupportedException("These locks are eval locks only.");

		public ValueTask<bool> EvaluateLock(string lockString, AnySharpObject gated, AnySharpObject unlocker)
			=> throw new NotSupportedException("These locks are eval locks only.");
	}
}
