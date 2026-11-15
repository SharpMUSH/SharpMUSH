using Mediator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// A role change reaches the open portal tabs it affects, so they read their permissions again without a
/// page reload: a player's role (an approval, WIZARD) tells that player's account, a role's definition tells
/// everyone, and a refused change tells no one.
/// </summary>
public class PortalPermissionNoticeTests
{
	private sealed record World(
		IRoleManagementService Service,
		IHubClients<IGameHubClient> Clients,
		IGameHubClient Account,
		IGameHubClient Everyone);

	/// <summary><c>owner</c> plays #1; <c>pl</c> plays #4.</summary>
	private static World Build()
	{
		var account = Substitute.For<IGameHubClient>();
		var everyone = Substitute.For<IGameHubClient>();
		var clients = Substitute.For<IHubClients<IGameHubClient>>();
		clients.User(Arg.Any<string>()).Returns(account);
		clients.All.Returns(everyone);
		var hub = Substitute.For<IHubContext<GameHub, IGameHubClient>>();
		hub.Clients.Returns(clients);
		var notices = new PortalPermissionNotices(hub, NullLogger<PortalPermissionNotices>.Instance);

		var registry = InMemoryRoleRegistry.Seeded();
		var accounts = Substitute.For<IAccountService>();
		void Account(string id, SharpPlayer character)
		{
			var holder = new SharpAccount { Id = id, Username = id, PasswordHash = "" };
			accounts.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns(holder);
			accounts.GetCharactersAsync(id, Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([character]));
			accounts.GetAccountForCharacterAsync(character.Object.DBRef, Arg.Any<CancellationToken>()).Returns(holder);
		}

		Account("owner", AdministrativeCapabilityTests.Player(1));
		Account("pl", AdministrativeCapabilityTests.Player(4));

		var resolver = new PermissionResolver();
		var capabilities = new AdministrativeCapabilityService(accounts, registry, resolver);
		var invalidator = new AccountClaimsInvalidator(
			new FusionCache(new Microsoft.Extensions.Options.OptionsWrapper<FusionCacheOptions>(new FusionCacheOptions())),
			new ObjectVersions(), new Lazy<IEnumerable<IAccountChangeListener>>(() => [notices]));
		var inner = new RoleManagementService(registry, accounts, capabilities, resolver, invalidator, Substitute.For<IMediator>());
		return new World(new PermissionNoticeRoleManagementService(inner, notices), clients, account, everyone);
	}

	private static RoleActor Owner => new CapabilityActor("owner");

	private static AnySharpObject Character(int number) => AdministrativeCapabilityTests.Player(number);

	private static async Task Accepted<T>(Task<RoleOutcome<T>> outcome)
		=> await Assert.That((await outcome) is RoleRefusal).IsFalse();

	[Test]
	public async Task ARoleGivenToACharacterTellsItsAccount()
	{
		var world = Build();

		await Accepted(world.Service.AssignToObjectAsync(Owner, Character(4), BuiltInRoles.BuilderSlug));

		world.Clients.Received().User("pl");
		await world.Account.Received().ReceivePermissionsChanged();
		await world.Everyone.DidNotReceive().ReceivePermissionsChanged();
	}

	[Test]
	public async Task ARoleTakenFromACharacterTellsItsAccount()
	{
		var world = Build();
		await Accepted(world.Service.AssignToObjectAsync(Owner, Character(4), BuiltInRoles.BuilderSlug));

		await Accepted(world.Service.UnassignFromObjectAsync(Owner, Character(4), BuiltInRoles.BuilderSlug));

		world.Clients.Received(2).User("pl");
		await world.Account.Received(2).ReceivePermissionsChanged();
	}

	[Test]
	public async Task AnAccountsOwnRoleTellsThatAccount()
	{
		var world = Build();

		await Accepted(world.Service.AssignAsync(Owner, "pl", "helper"));

		world.Clients.Received().User("pl");
		await world.Account.Received().ReceivePermissionsChanged();
	}

	[Test]
	public async Task ARolesDefinitionTellsEveryone()
	{
		var world = Build();

		await Accepted(world.Service.SaveRoleAsync(Owner,
			new RoleDraft("st", "Story Teller", "Staff", null, 1, new Dictionary<string, PermissionState>())));

		await world.Everyone.Received(1).ReceivePermissionsChanged();
	}

	[Test]
	public async Task ARefusedChangeTellsNoOne()
	{
		var world = Build();

		var outcome = await world.Service.SaveRoleAsync(new CapabilityActor("pl"),
			new RoleDraft("st", "Story Teller", "Staff", null, 1, new Dictionary<string, PermissionState>()));

		await Assert.That(outcome is RoleRefusal).IsTrue();
		world.Clients.DidNotReceive().User(Arg.Any<string>());
		await world.Everyone.DidNotReceive().ReceivePermissionsChanged();
	}
}
