using Mediator;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Authentication;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <see cref="StaffLevelService"/>: the setup wizard's choice between SharpMUSH's starter roles and RhostMUSH's levels,
/// made as the owner's account through <see cref="RoleManagementService"/> on a world of its own.
/// </summary>
public class StaffLevelServiceTests
{
	private static readonly CapabilityActor Owner = new("owner");

	private sealed record World(StaffLevelService Service, InMemoryRoleRegistry Registry, IExpandedObjectDataService ServerData);

	private static World Build()
	{
		var registry = InMemoryRoleRegistry.Seeded();
		var accounts = Substitute.For<IAccountService>();
		accounts.GetByIdAsync("owner", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "owner", Username = "owner", PasswordHash = "" });
		accounts.GetCharactersAsync("owner", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([AdministrativeCapabilityTests.Player(1)]));

		var resolver = new PermissionResolver();
		var management = new RoleManagementService(registry, accounts, new AdministrativeCapabilityService(accounts, registry, resolver),
			resolver, Substitute.For<IAccountClaimsInvalidator>(), Substitute.For<IMediator>());
		var serverData = Substitute.For<IExpandedObjectDataService>();
		return new World(new StaffLevelService(registry, management, serverData), registry, serverData);
	}

	private static async Task<HashSet<string>> SlugsAsync(InMemoryRoleRegistry registry)
		=> (await registry.GetRolesAsync()).Select(role => role.Slug).ToHashSet();

	[Test]
	public async Task ANewWorldUsesSharpMUSHsRoles()
	{
		var state = await Build().Service.StateAsync(CancellationToken.None);

		await Assert.That(state.Set).IsEqualTo(StaffLevelSets.SharpMUSH);
		await Assert.That(state.Kept).IsEmpty();
	}

	/// <summary>A RhostMUSH import leaves its levels in the world, so a game that has not chosen starts on them.</summary>
	[Test]
	public async Task AWorldWithRhostLevelsStartsOnThem()
	{
		var world = Build();
		world.Registry.Add(BuiltInRoles.RhostLevels[2]);

		var state = await world.Service.StateAsync(CancellationToken.None);

		await Assert.That(state.Set).IsEqualTo(StaffLevelSets.RhostMUSH);
		await Assert.That(state.Kept).IsEquivalentTo(["helper", "moderator"]);
	}

	[Test]
	public async Task ChoosingRhostLevelsMakesThemAndRemovesTheUnheldStarters()
	{
		var world = Build();

		await Assert.That(await world.Service.ApplyAsync(Owner, StaffLevelSets.RhostMUSH, CancellationToken.None) is Success).IsTrue();

		var slugs = await SlugsAsync(world.Registry);
		await Assert.That(slugs).Contains("guildmaster").And.Contains("architect").And.Contains("councilor").And.Contains("immortal");
		await Assert.That(slugs).DoesNotContain("helper").And.DoesNotContain("moderator");
		await Assert.That(slugs).Contains(BuiltInRoles.WizardSlug).And.Contains(BuiltInRoles.RoyaltySlug);
		await world.ServerData.Received(1).SetExpandedServerDataAsync(
			Arg.Is<StaffLevelChoice>(choice => choice.Set == StaffLevelSets.RhostMUSH), Arg.Any<bool>());

		var immortal = (await world.Registry.GetRoleAsync("immortal")).Expect<SharpRole>();
		var wizard = BuiltInRoles.All.Single(role => role.Slug == BuiltInRoles.WizardSlug);
		await Assert.That(immortal.Priority).IsGreaterThan(wizard.Priority);
		await Assert.That(immortal.Permissions).ContainsKey(PortalPermission.ServerAdmin);
	}

	/// <summary>A role someone holds is never deleted by the choice; it is reported instead.</summary>
	[Test]
	public async Task AHeldRoleOfTheOtherSetStays()
	{
		var world = Build();
		await world.Registry.AssignRoleToObjectAsync(7, "moderator");

		await world.Service.ApplyAsync(Owner, StaffLevelSets.RhostMUSH, CancellationToken.None);

		var slugs = await SlugsAsync(world.Registry);
		await Assert.That(slugs).Contains("moderator").And.DoesNotContain("helper");
		world.ServerData.GetExpandedServerDataAsync<StaffLevelChoice>()
			.Returns(new ValueTask<StaffLevelChoice?>(new StaffLevelChoice { Set = StaffLevelSets.RhostMUSH }));
		await Assert.That((await world.Service.StateAsync(CancellationToken.None)).Kept).IsEquivalentTo(["moderator"]);
	}

	/// <summary>Going back to SharpMUSH's roles brings the starters back and removes the unheld levels; a held one stays.</summary>
	[Test]
	public async Task ChoosingSharpMUSHAgainRestoresTheStarters()
	{
		var world = Build();
		await world.Service.ApplyAsync(Owner, StaffLevelSets.RhostMUSH, CancellationToken.None);
		await world.Registry.AssignRoleToAccountAsync("owner", "architect");

		await Assert.That(await world.Service.ApplyAsync(Owner, StaffLevelSets.SharpMUSH, CancellationToken.None) is Success).IsTrue();

		var slugs = await SlugsAsync(world.Registry);
		await Assert.That(slugs).Contains("helper").And.Contains("moderator").And.Contains("architect");
		await Assert.That(slugs).DoesNotContain("guildmaster").And.DoesNotContain("councilor").And.DoesNotContain("immortal");
	}

	[Test]
	public async Task AnUnknownSetIsRefused()
		=> await Assert.That(await Build().Service.ApplyAsync(Owner, "tinymux", CancellationToken.None) is Error<string>).IsTrue();
}
