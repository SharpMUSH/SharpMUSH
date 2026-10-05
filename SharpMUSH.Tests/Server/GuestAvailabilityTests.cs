using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// What the portal's anonymous Play offer is based on: a visitor can play as a guest only when logins and
/// guest logins are on AND the game has a guest character to hand out. <c>Net.Guests</c> alone sent every
/// visitor of a new game to "Sorry, there are no guest characters available."
/// </summary>
public class GuestAvailabilityTests
{
	private static IOptionsWrapper<SharpMUSHOptions> Options(bool logins = true, bool guests = true)
	{
		var options = new OptionsService(Substitute.For<ISharpDatabase>(), []).Create(string.Empty);
		options = options with { Net = options.Net with { Logins = logins, Guests = guests } };
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(options);
		return wrapper;
	}

	private static IMediator NoPlayers()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<GetAllPlayersQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => AsyncEnumerable.Empty<SharpPlayer>());
		return mediator;
	}

	[Test]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task GuestOrAllLoginsOff_IsUnavailable_WithoutReadingTheRoster(bool logins, bool guests)
	{
		var mediator = NoPlayers();
		var availability = new GuestAvailability(mediator, Options(logins, guests));

		await Assert.That(await availability.CanLogInAsync()).IsFalse();
		mediator.DidNotReceive().CreateStream(Arg.Any<GetAllPlayersQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task GuestsOn_ButNoGuestCharacter_IsUnavailable()
	{
		var availability = new GuestAvailability(NoPlayers(), Options());

		await Assert.That(await availability.CanLogInAsync()).IsFalse();
	}

	[Test]
	public async Task TheRosterIsReadOnce_UntilInvalidated()
	{
		var mediator = NoPlayers();
		var availability = new GuestAvailability(mediator, Options());

		await availability.CanLogInAsync();
		await availability.CanLogInAsync();
		mediator.Received(1).CreateStream(Arg.Any<GetAllPlayersQuery>(), Arg.Any<CancellationToken>());

		availability.Invalidate();
		await availability.CanLogInAsync();
		mediator.Received(2).CreateStream(Arg.Any<GetAllPlayersQuery>(), Arg.Any<CancellationToken>());
	}
}
