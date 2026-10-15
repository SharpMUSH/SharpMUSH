using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// PLAYER`LINK and PLAYER`UNLINK are raised by <see cref="AccountService"/>, which every route to a link goes
/// through, and only when the store's write reports that the link changed.
/// </summary>
public class AccountLinkEventTests
{
	private static readonly DBRef Character = new(7, 777L);

	private static (AccountService Service, ISharpDatabase Database, IEventService Events) Build(
		CharacterLinkWrite linked = default, bool unlinked = false)
	{
		var database = Substitute.For<ISharpDatabase>();
		var events = Substitute.For<IEventService>();
		database.LinkCharacterToAccountAsync(Arg.Any<string>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(linked);
		database.UnlinkCharacterFromAccountAsync(Arg.Any<string>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(unlinked);
		var service = new AccountService(database, database, Substitute.For<IPasswordService>(),
			Substitute.For<IAccountSessionStore>(), events: new Lazy<IEventService>(() => events));
		return (service, database, events);
	}

	[Test]
	public async Task LinkingRaisesPlayerLinkWithTheObjidAndAccount()
	{
		var (service, _, events) = Build(linked: new Linked());

		await service.LinkCharacterAsync("node_accounts/1", Character);

		await events.Received(1).TriggerEventAsync(SharpEvents.PlayerLink, null, "#7:777", "node_accounts/1");
	}

	[Test]
	public async Task LinkingACharacterTheAccountAlreadyHoldsRaisesNothing()
	{
		var (service, _, events) = Build(linked: new AlreadyLinked());

		await service.LinkCharacterAsync("node_accounts/1", Character);

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, Array.Empty<string>());
	}

	[Test]
	public async Task UnlinkingRaisesPlayerUnlinkForTheAccountItLeft()
	{
		var (service, _, events) = Build(unlinked: true);

		await service.UnlinkCharacterAsync("node_accounts/1", Character);

		await events.Received(1).TriggerEventAsync(SharpEvents.PlayerUnlink, null, "#7:777", "node_accounts/1");
	}

	[Test]
	public async Task UnlinkingACharacterTheAccountDoesNotHoldRaisesNothing()
	{
		var (service, _, events) = Build(unlinked: false);

		await service.UnlinkCharacterAsync("node_accounts/1", Character);

		await events.DidNotReceiveWithAnyArgs().TriggerEventAsync(default!, default, Array.Empty<string>());
	}
}
