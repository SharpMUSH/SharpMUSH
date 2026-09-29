using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The locate shapes in <see cref="LocateServiceExtensions"/> are not operations of their own: each is
/// the silent or the noisy primitive on <see cref="ILocateService"/> with a fixed flag set or a different
/// return type. These pin that down, so a shape cannot grow its own notify path again.
/// </summary>
public class LocateServiceExtensionsTests
{
	private const LocateFlags PlayerFlags =
		LocateFlags.PlayersPreference | LocateFlags.OnlyMatchTypePreference | LocateFlags.EnglishStyleMatching |
		LocateFlags.MatchOptionalWildCardForPlayerName | LocateFlags.AbsoluteMatch;

	private static readonly IMUSHCodeParser Parser = Substitute.For<IMUSHCodeParser>();
	private static readonly AnySharpObject Looker = new(Room(0, "Looker"));

	private static ILocateService Noisy(AnyOptionalSharpObjectOrError answer)
	{
		var locate = Substitute.For<ILocateService>();
		locate.LocateAndNotifyIfInvalid(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(),
				Arg.Any<AnySharpObject>(), Arg.Any<string>(), Arg.Any<LocateFlags>())
			.Returns(ValueTask.FromResult(answer));
		return locate;
	}

	[Test]
	public async Task CallStateShape_OnAMiss_ReportsThroughTheNoisyPrimitiveAndReturnsNoMatch()
	{
		var locate = Noisy(new None());

		var result = await locate.LocateAndNotifyIfInvalidWithCallState(Parser, Looker, Looker, "nothing",
			LocateFlags.All);

		await Assert.That(result is Error<CallState> { Value: var e } && e.Message!.ToPlainText() == ErrorMessages.Returns.NoMatch)
			.IsTrue();
		await locate.Received(1).LocateAndNotifyIfInvalid(Parser, Looker, Looker, "nothing", LocateFlags.All);
		await locate.DidNotReceiveWithAnyArgs().Locate(default!, default!, default!, default!, default);
	}

	[Test]
	public async Task CallStateShape_KeepsTheErrorStringTheLocateAnswered()
	{
		var locate = Noisy(new Error<string>(ErrorMessages.Returns.AmbiguousMatch));

		var result = await locate.LocateAndNotifyIfInvalidWithCallStateFunction(Parser, Looker, Looker, "both",
			LocateFlags.All, _ => new CallState("found"));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.AmbiguousMatch);
	}

	[Test]
	public async Task FunctionShape_OnAHit_HandsTheObjectToTheContinuation()
	{
		var locate = Noisy(Looker);

		var result = await locate.LocateAndNotifyIfInvalidWithCallStateFunction(Parser, Looker, Looker, "me",
			LocateFlags.All, found => ValueTask.FromResult(new CallState(found.Object().Name)));

		await Assert.That(result.Message!.ToPlainText()).IsEqualTo("Looker");
	}

	[Test]
	public async Task PlayerShapes_UseThePlayerFlagSet_NoisyAndSilent()
	{
		var locate = Noisy(new None());
		locate.Locate(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(),
				Arg.Any<string>(), Arg.Any<LocateFlags>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObjectOrError>(new None()));

		await locate.LocatePlayerAndNotifyIfInvalid(Parser, Looker, Looker, "Alice");
		await locate.LocatePlayerAndNotifyIfInvalidWithCallState(Parser, Looker, Looker, "Bob");
		await locate.LocatePlayer(Parser, Looker, Looker, "Carol");
		await locate.LocateConnectionTarget(Parser, Looker, Looker, "me");

		await locate.Received(1).LocateAndNotifyIfInvalid(Parser, Looker, Looker, "Alice", PlayerFlags);
		await locate.Received(1).LocateAndNotifyIfInvalid(Parser, Looker, Looker, "Bob", PlayerFlags);
		await locate.Received(1).Locate(Parser, Looker, Looker, "Carol", PlayerFlags);
		// lookup_desc's MAT_ME, and silent: a connection function answers with a string, not a notify.
		await locate.Received(1).Locate(Parser, Looker, Looker, "me", PlayerFlags | LocateFlags.MatchMeForLooker);
		await locate.DidNotReceive().LocateAndNotifyIfInvalid(Arg.Any<IMUSHCodeParser>(), Arg.Any<AnySharpObject>(),
			Arg.Any<AnySharpObject>(), "me", Arg.Any<LocateFlags>());
	}

	[Test]
	public async Task PlayerFunctionShape_RefusesANonPlayerMatch()
	{
		var locate = Noisy(Looker);

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
