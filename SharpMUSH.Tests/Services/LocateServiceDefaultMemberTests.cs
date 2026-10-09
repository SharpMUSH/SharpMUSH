using SharpMUSH.Library.Authorization;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The locate shapes on <see cref="ILocateService"/> beyond <see cref="ILocateService.Locate"/>,
/// <see cref="ILocateService.LocateAndNotifyIfInvalid"/> and <see cref="ILocateService.Room"/> are default
/// members: each is the silent or the noisy primitive with a fixed flag set or a different return type.
/// These drive them through a fake that implements only the primitives, so a shape cannot grow its own
/// notify path again.
/// </summary>
public class LocateServiceDefaultMemberTests
{
	private const LocateFlags PlayerFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.EnglishStyleMatching |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.AbsoluteMatch;

	private static readonly IMUSHCodeParser Parser = Substitute.For<IMUSHCodeParser>();
	private static readonly AnySharpObject Looker = new(Room(0, "Looker"));

	/// <summary>Implements only the primitives, and records which one each call reached and with what.</summary>
	private sealed class PrimitivesOnly(AnyOptionalSharpObjectOrError answer) : ILocateService
	{
		public List<(string Primitive, string Name, LocateFlags Flags)> Calls { get; } = [];

		public ValueTask<AnyOptionalSharpObjectOrError> LocateAndNotifyIfInvalid(IMUSHCodeParser parser,
			AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
		{
			Calls.Add(("noisy", name, flags));
			return ValueTask.FromResult(answer);
		}

		public ValueTask<AnyOptionalSharpObjectOrError> Locate(IMUSHCodeParser parser,
			AnySharpObject looker, AnySharpObject executor, string name, LocateFlags flags)
		{
			Calls.Add(("silent", name, flags));
			return ValueTask.FromResult(answer);
		}

		public ValueTask<AnySharpContainer> Room(AnySharpObject content) => throw new NotSupportedException();
	}

	[Test]
	public async Task CallStateShape_OnAMiss_ReportsThroughTheNoisyPrimitiveAndReturnsNoMatch()
	{
		var fake = new PrimitivesOnly(new None());
		ILocateService locate = fake;

		var result = await locate.LocateAndNotifyIfInvalidWithCallState(Parser, Looker, Looker, "nothing",
			LocateFlags.All);

		await Assert.That(result is Error<CallState> { Value: var e } && e.Message.ToPlainText() == ErrorMessages.Returns.NoMatch)
			.IsTrue();
		await Assert.That(fake.Calls).IsEquivalentTo(new[] { ("noisy", "nothing", LocateFlags.All) });
	}

	[Test]
	public async Task CallStateShape_KeepsTheErrorStringTheLocateAnswered()
	{
		ILocateService locate = new PrimitivesOnly(new Error<string>(ErrorMessages.Returns.AmbiguousMatch));

		var result = await locate.LocateAndNotifyIfInvalidWithCallStateFunction(Parser, Looker, Looker, "both",
			LocateFlags.All, _ => new CallState("found"));

		await Assert.That(result.Message.ToPlainText()).IsEqualTo(ErrorMessages.Returns.AmbiguousMatch);
	}

	[Test]
	public async Task FunctionShape_OnAHit_HandsTheObjectToTheContinuation()
	{
		ILocateService locate = new PrimitivesOnly(Looker);

		var result = await locate.LocateAndNotifyIfInvalidWithCallStateFunction(Parser, Looker, Looker, "me",
			LocateFlags.All, found => ValueTask.FromResult(new CallState(found.Object().Name)));

		await Assert.That(result.Message.ToPlainText()).IsEqualTo("Looker");
	}

	[Test]
	public async Task PlayerShapes_UseThePlayerFlagSet_NoisyAndSilent()
	{
		var fake = new PrimitivesOnly(new None());
		ILocateService locate = fake;

		await locate.LocatePlayerAndNotifyIfInvalid(Parser, Looker, Looker, "Alice");
		await locate.LocatePlayerAndNotifyIfInvalidWithCallState(Parser, Looker, Looker, "Bob");
		await locate.LocatePlayer(Parser, Looker, Looker, "Carol");
		await locate.LocateConnectionTarget(Parser, Looker, Looker, "me");

		await Assert.That(fake.Calls).IsEquivalentTo(new[]
		{
			("noisy", "Alice", PlayerFlags),
			("noisy", "Bob", PlayerFlags),
			("silent", "Carol", PlayerFlags),
			// lookup_desc's MAT_ME, and silent: a connection function answers with a string, not a notify.
			("silent", "me", PlayerFlags | LocateFlags.MatchMeForLooker)
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task PlayerFunctionShape_RefusesANonPlayerMatch()
	{
		ILocateService locate = new PrimitivesOnly(Looker);

		await Assert.That(async () => await locate.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(Parser, Looker,
				Looker, "#0", _ => ValueTask.FromResult(new CallState("player"))))
			.Throws<InvalidOperationException>();
	}

	private static SharpRoom Room(int key, string name) =>
		new()
		{
			Id = $"test-room-{key}",
			Object = new SharpObject
			{
				Key = key,
				CreationTime = 0L,
				Name = name,
				Type = "Room",
				Locks = ImmutableDictionary<string, SharpLockData>.Empty,
				Owner = new(_ => Task.FromResult<SharpPlayer>(null!)),
				Grants = new(_ => Task.FromResult(ObjectGrants.None)),
				Powers = new(AsyncEnumerable.Empty<SharpPower>),
				Attributes = new(AsyncEnumerable.Empty<SharpAttribute>),
				LazyAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
				AllAttributes = new(AsyncEnumerable.Empty<SharpAttribute>),
				LazyAllAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
				Flags = new(AsyncEnumerable.Empty<SharpObjectFlag>),
				Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
				Zone = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
				Children = new(AsyncEnumerable.Empty<SharpObject>)
			},
			Location = new(_ => Task.FromResult<AnyOptionalSharpContainer>(new None()))
		};
}
