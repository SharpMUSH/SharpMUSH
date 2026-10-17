using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
namespace SharpMUSH.Tests.Authentication.External;

public class AccountRecoveryHierarchyTests
{
	[Test]
	[Arguments("lower", true)]
	[Arguments("equal", false)]
	[Arguments("higher", false)]
	[Arguments("disabled-higher", false)]
	[Arguments("closed-higher", false)]
	[Arguments("deleted-higher", false)]
	[Arguments("character-higher", false)]
	[Arguments("disabled-character-higher", false)]
	[Arguments("owner-target", false)]
	[Arguments("reserved", false)]
	[Arguments("self", false)]
	[Arguments("implicit-player", false)]
	public async Task NonownerActsOnlyBelowPersistedTargetAuthorityEvenWhenInactive(string scenario, bool allowed)
	{
		var accounts = Substitute.For<IAccountService>(); var registry = InMemoryRoleRegistry.Seeded();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.PlayersModerate, Arg.Any<CancellationToken>()).Returns(true);
		var actor = new SharpAccount { Id = "actor", Username = "actor" };
		var target = new SharpAccount { Id = scenario == "self" ? "actor" : "target", Username = scenario == "reserved" ? "system" : "target", Status = scenario.StartsWith("disabled") ? AccountStatus.Disabled : scenario.StartsWith("closed") ? AccountStatus.Closed : scenario.StartsWith("deleted") ? AccountStatus.Deleted : AccountStatus.Active };
		accounts.GetByIdAsync("actor", Arg.Any<CancellationToken>()).Returns(actor);
		if (scenario != "self") accounts.GetByIdAsync("target", Arg.Any<CancellationToken>()).Returns(target);
		accounts.GetCharactersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[]);
		var actorRole = new SharpRole { Slug = "actor-rank", Name = "Actor", Priority = scenario == "implicit-player" ? 0 : 100 };
		capabilities.GetContextAsync(new CapabilityActor("actor"), Arg.Any<CancellationToken>()).Returns(new PermissionContext([actorRole], new Dictionary<string, PermissionState>(), false));
		var priority = scenario == "lower" ? 50 : scenario == "equal" ? 100 : 150;
		if (scenario is not ("owner-target" or "reserved" or "self" or "implicit-player"))
		{
			registry.Add(new SharpRole { Slug = "target-rank", Name = "Target", Priority = priority });
			if (scenario.Contains("character"))
			{ accounts.GetCharactersAsync("target", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[AdministrativeCapabilityTests.Player(7)]); await registry.AssignRoleToObjectAsync(7, "target-rank"); }
			else await registry.AssignRoleToAccountAsync("target", "target-rank");
		}
		if (scenario == "owner-target") accounts.GetCharactersAsync("target", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[AdministrativeCapabilityTests.Player(1)]);
		if (scenario == "implicit-player") accounts.GetCharactersAsync("target", Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SharpPlayer>)[AdministrativeCapabilityTests.Player(7)]);
		var result = await new AccountRecoveryHierarchy(accounts, registry, capabilities).AuthorizeAsync("actor", target.Id!, default);
		await Assert.That(result is SharpAccount).IsEqualTo(allowed);
	}
	[Test]
	public async Task OwnerBypassesHierarchyAndMissingTargetsStayNotFound()
	{
		var accounts = Substitute.For<IAccountService>(); var registry = InMemoryRoleRegistry.Seeded(); var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.PlayersModerate, Arg.Any<CancellationToken>()).Returns(true);
		var owner = new SharpAccount { Id = "owner", Username = "owner" }; var reserved = new SharpAccount { Id = "reserved", Username = "system", Status = AccountStatus.Disabled };
		accounts.GetByIdAsync("owner", Arg.Any<CancellationToken>()).Returns(owner); accounts.GetByIdAsync("reserved", Arg.Any<CancellationToken>()).Returns(reserved);
		capabilities.GetContextAsync(new CapabilityActor("owner"), Arg.Any<CancellationToken>()).Returns(new PermissionContext([], new Dictionary<string, PermissionState>(), true));
		var policy = new AccountRecoveryHierarchy(accounts, registry, capabilities);
		await Assert.That((await policy.AuthorizeAsync("owner", "reserved", default)).Expect<SharpAccount>().Status).IsEqualTo(AccountStatus.Disabled);
		(await policy.AuthorizeAsync("owner", "missing", default)).Expect<NotFound>();
		await Assert.That((await policy.AuthorizeAsync("owner", "owner", default)).Expect<SharpAccount>().Id).IsEqualTo("owner");
	}
}
