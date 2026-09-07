using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OneOf;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// An eval lock (<c>ATTR/value</c>) evaluates an attribute as MUSHcode and compares the result to the
/// lock's pattern. When that evaluation throws there is no result to compare, and the question is what
/// the lock does about it.
///
/// <para>PennMUSH answers unambiguously. <c>check_attrib_lock()</c> (src/boolexp.c) returns 0 for every
/// way the evaluation can fail — empty attribute name, empty comparison string, no such attribute — and
/// <c>pennlock.hlp</c> says of a permission failure inside the evaluation that "the person will
/// automatically fail to pass the lock". Failure denies.</para>
///
/// <para>These tests pin both halves: the verdict is deny, and the failure arrives as a failure rather
/// than as a value that happens to compare unequal.</para>
/// </summary>
public class EvalLockEvaluationFailureTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	/// <summary>
	/// The evaluation seam must report a failed evaluation as <see cref="LockEvaluationFailure"/>. Returning a
	/// string — any string, including the empty one — would put the failure back into the same channel
	/// as a result, which is what let a broken evaluation reach the lock's comparison.
	/// </summary>
	[Test]
	public async Task EvaluationThatThrows_IsReportedAsAFailure_NotAsAValue()
	{
		var one = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		var attributeService = Substitute.For<IAttributeService>();
		attributeService.EvaluateAttributeFunctionAsync(
				Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
				Arg.Any<Dictionary<string, CallState>>(), Arg.Any<bool>(), Arg.Any<bool>())
			.ThrowsAsync(new InvalidOperationException("evaluation exploded"));

		var parser = Substitute.For<IMUSHCodeParser>();
		parser.Push(Arg.Any<ParserState>()).Returns(parser);

		var services = new LockEvaluationServices(
			new Lazy<ILocateService>(() => Substitute.For<ILocateService>()),
			new Lazy<IAttributeService>(() => attributeService),
			new Lazy<ILockService>(() => Substitute.For<ILockService>()),
			new Lazy<IMUSHCodeParser>(() => parser),
			NullLogger<LockEvaluationServices>.Instance);
		var result = await services.EvaluateAttributeAsync(one, one, "BOOM");

		await Assert.That(result.IsT1)
			.IsTrue()
			.Because("a failed evaluation must not be indistinguishable from an evaluated value");
		await Assert.That(result.AsT1.AttributeName).IsEqualTo("BOOM");
		await Assert.That(result.AsT1.Reason).Contains("evaluation exploded");
	}

	/// <summary>
	/// And the verdict on that failure is deny, matching PennMUSH. A lock that cannot be evaluated is a
	/// lock that has not been passed.
	/// </summary>
	[Test]
	public async Task EvalLock_WhoseEvaluationFailed_DoesNotPass()
	{
		var one = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		var services = Substitute.For<ILockEvaluationServices>();
		services.EvaluateAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>())
			.Returns(new ValueTask<OneOf<string, LockEvaluationFailure>>(
				new LockEvaluationFailure("FAILING", "evaluation exploded")));

		var parser = new BooleanExpressionParser(services, Substitute.For<IMediator>(), new FusionCache(new FusionCacheOptions()));

		await Assert.That(parser.Compile("FAILING/expected")(one, one))
			.IsFalse()
			.Because("PennMUSH's check_attrib_lock() returns 0 when the evaluation cannot produce a value");
	}

	/// <summary>
	/// The control: an evaluation that does produce the pattern still passes, so the deny above is the
	/// failure path and not the eval lock being broken outright.
	/// </summary>
	[Test]
	public async Task EvalLock_WhoseEvaluationMatched_Passes()
	{
		var one = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		var services = Substitute.For<ILockEvaluationServices>();
		services.EvaluateAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>())
			.Returns(new ValueTask<OneOf<string, LockEvaluationFailure>>("expected"));

		var parser = new BooleanExpressionParser(services, Substitute.For<IMediator>(), new FusionCache(new FusionCacheOptions()));

		await Assert.That(parser.Compile("MATCHING/expected")(one, one)).IsTrue();
	}
}
