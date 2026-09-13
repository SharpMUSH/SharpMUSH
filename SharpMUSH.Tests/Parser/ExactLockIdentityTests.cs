using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Parser;

public class ExactLockIdentityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IBooleanExpressionParser Locks => Factory.Services.GetRequiredService<IBooleanExpressionParser>();

	private async Task<AnySharpObject> Create(string prefix)
	{
		var created = await Factory.FunctionParser.FunctionParse(MarkupText.Plain($"create({prefix}_{Guid.NewGuid():N})"));
		return (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created!.Message!.ToPlainText())))).Expect<AnySharpObject>();
	}

	[Test]
	[Arguments("=", false)]
	[Arguments("=", true)]
	[Arguments("", false)]
	[Arguments("", true)]
	[Arguments("+", false)]
	[Arguments("+", true)]
	public async Task KeyIdentityAndDirectCarryRemainDistinct(string prefix, bool stamped)
	{
		var key = await Create("IdentityKey");
		var carrier = await Create("IdentityCarrier");
		var unrelated = await Create("IdentityOther");
		await Mediator.Send(new MoveObjectCommand(key.MinusRoom(), carrier.AsContainer, (await key.Where()).Object().DBRef));
		var identity = key.Object().DBRef;
		var reference = stamped ? identity.ToString() : $"#{identity.Number}";
		var expression = prefix + reference;
		await Assert.That(Locks.Validate(expression, unrelated)).IsTrue();
		await Assert.That(Locks.Normalize(expression)).IsEqualTo(expression);
		var predicate = Locks.Compile(expression);
		await Assert.That(await predicate(unrelated, key)).IsEqualTo(prefix != "+");
		await Assert.That(await predicate(unrelated, unrelated)).IsFalse();
		await Assert.That(await predicate(unrelated, carrier)).IsEqualTo(prefix != "=")
			.Because("only ordinary and carry keys admit a different object holding the key");
		var stale = Locks.Compile($"{prefix}#{identity.Number}:{identity.CreationMilliseconds - 1}");
		await Assert.That(await stale(unrelated, key)).IsFalse();
		await Assert.That(await stale(unrelated, carrier)).IsFalse();
		var normalizedName = Locks.Normalize(prefix + key.Object().Name, carrier);
		await Assert.That(normalizedName).IsEqualTo($"{prefix}#{identity.Number}");
		await Assert.That(await Locks.Compile(normalizedName)(unrelated, key)).IsEqualTo(prefix != "+");
		await Assert.That(await Locks.Compile(normalizedName)(unrelated, carrier)).IsEqualTo(prefix != "=");
	}

	[Test]
	public async Task ExactKeyDoesNotReadInventoryAfterIdentityMismatch()
	{
		var key = await Create("NoReadKey");
		var other = await Create("NoReadOther");
		var mediator = Substitute.For<IMediator>();
		using var cache = new FusionCache(new FusionCacheOptions());
		var parser = new BooleanExpressionParser(Substitute.For<ILockEvaluationServices>(), mediator, cache);
		await Assert.That(await parser.Compile($"={key.Object().DBRef}")(other, other)).IsFalse();
		_ = mediator.DidNotReceive().CreateStream(Arg.Any<GetContentsQuery>(), Arg.Any<CancellationToken>());
	}
}
