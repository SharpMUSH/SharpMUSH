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
		var created = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain($"create({prefix}_{Guid.NewGuid():N})"));
		return (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(created.ToPlainText())))).Expect<AnySharpObject>();
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
		await Mediator.Send(new MoveObjectCommand(key.AsOptionalContent.Expect<AnySharpContent>(), carrier.AsOptionalContainer.Expect<AnySharpContainer>(), (await key.Where()).Object().DBRef));
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
		var normalizedName = (await Locks.BindAsync(prefix + key.Object().Name, carrier)).Expect<string>();
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

	private async Task<AnySharpObject> Parse(string expression)
	{
		var made = await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression));
		return (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(made.ToPlainText())))).Expect<AnySharpObject>();
	}

	/// <summary>
	/// A carry key asks whether the key is in the unlocker's contents. A room's contents list the exits
	/// leading out of it, and never a room — not even one whose drop-to is the unlocker, though a drop-to
	/// is stored where a location is. The key's own location answers the same question as the inventory.
	/// </summary>
	[Test]
	[Arguments("")]
	[Arguments("+")]
	public async Task CarryKeyMatchesTheContentsListForExitsAndRooms(string prefix)
	{
		var room = await Parse($"dig(CarryRoom_{Guid.NewGuid():N})");
		var elsewhere = await Parse($"dig(CarryElsewhere_{Guid.NewGuid():N})");
		var exit = await Parse($"open(CarryExit_{Guid.NewGuid():N},#{elsewhere.Object().DBRef.Number},#{room.Object().DBRef.Number})");
		var droppingRoom = await Parse($"dig(CarryDropping_{Guid.NewGuid():N})");
		await Factory.FunctionParser.FunctionParse(MarkupText.Plain(
			$"link(#{droppingRoom.Object().DBRef.Number},#{room.Object().DBRef.Number})"));
		var unrelated = await Create("CarryGate");

		var contents = await Mediator.CreateStream(new GetContentsQuery(room.AsOptionalContainer.Expect<AnySharpContainer>()))
			.Select(item => item.Object().DBRef.Number).ToListAsync();
		await Assert.That(contents).Contains(exit.Object().DBRef.Number)
			.Because("precondition: a room's contents list the exits leading out of it");
		await Assert.That(contents).DoesNotContain(droppingRoom.Object().DBRef.Number)
			.Because("precondition: a room whose drop-to is this room is not in its contents");

		await Assert.That(await Locks.Compile($"{prefix}#{exit.Object().DBRef.Number}")(unrelated, room)).IsTrue();
		await Assert.That(await Locks.Compile($"{prefix}{exit.Object().DBRef}")(unrelated, room)).IsTrue();
		await Assert.That(await Locks.Compile($"{prefix}#{exit.Object().DBRef.Number}")(unrelated, elsewhere)).IsFalse();
		await Assert.That(await Locks.Compile($"{prefix}#{droppingRoom.Object().DBRef.Number}")(unrelated, room)).IsFalse()
			.Because("a drop-to is not carrying");
	}
}
