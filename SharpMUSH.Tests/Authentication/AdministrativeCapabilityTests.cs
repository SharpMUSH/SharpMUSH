using System.Collections.Immutable;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Authentication;

public class AdministrativeCapabilityTests
{
	[Test]
	[Arguments(0, 100, PermissionState.Deny, false)]
	[Arguments(100, 0, PermissionState.Deny, false)]
	[Arguments(100, 100, PermissionState.Deny, false)]
	[Arguments(0, 100, PermissionState.Inherit, true)]
	[Arguments(100, 0, PermissionState.Allow, true)]
	public async Task ExplicitChildCeiling(int parentPriority, int childPriority, PermissionState state, bool expected)
	{
		var roles = new[] { Role("parent", parentPriority, PortalPermission.JobsManage, PermissionState.Allow),
			Role("child", childPriority, PortalPermission.JobsManageOwn, state) };
		await Assert.That(new PermissionResolver().Resolve(roles).Contains(PortalPermission.JobsManageOwn)).IsEqualTo(expected);
	}

	[Test]
	public async Task ExecutionRechecksRevocationAndAccountStatus()
	{
		var (service, accounts, registry, account) = Build();
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.SnapshotCapture)).IsTrue();
		registry.GetRolesForAccountAsync("a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>([]));
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.SnapshotCapture)).IsFalse();
		account.Status = AccountStatus.Disabled;
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.SnapshotCapture)).IsFalse();
	}

	[Test]
	public async Task ActiveCharacterMustBeLinkedAndExecuteAsItself()
	{
		var (service, accounts, _, _) = Build();
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([player]));
		var active = player.Object.DBRef;
		await Assert.That(await service.AuthorizeAsync(new("a", active, active), PortalPermission.SnapshotCapture)).IsTrue();
		await Assert.That(await service.AuthorizeAsync(new("a", active, new DBRef(8, 1)), PortalPermission.SnapshotCapture)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a", new DBRef(8, 1), new DBRef(8, 1)), PortalPermission.SnapshotCapture)).IsFalse();
		await Assert.That(await service.AuthorizeAsync(new("a", new DBRef(7), new DBRef(7)), PortalPermission.SnapshotCapture)).IsFalse();
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		await Assert.That(await service.AuthorizeAsync(new("a", active, active), PortalPermission.SnapshotCapture)).IsFalse();
	}

	[Test]
	public async Task LinkedWizardDoesNotElevateAnotherActiveCharacter()
	{
		var (service, accounts, registry, _) = Build();
		var wizard = Player(1);
		var player = Player(7);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([wizard, player]));
		registry.GetRoleAsync("god").Returns(BuiltInRoles.All.Single(r => r.Slug == "god"));
		await Assert.That(await service.AuthorizeAsync(new("a"), PortalPermission.SnapshotRestore)).IsTrue();
		await Assert.That(await service.AuthorizeAsync(new("a", player.Object.DBRef, player.Object.DBRef), PortalPermission.SnapshotRestore)).IsFalse();
	}

	[Test]
	public async Task GameAdapterRejectsUnlinkedDisabledAndBareReferences()
	{
		var (service, accounts, _, account) = Build();
		var executor = new DBRef(7, 1);
		await Assert.That(await service.GetGameActorAsync(executor)).IsNull();
		accounts.GetAccountForCharacterAsync(executor, Arg.Any<CancellationToken>()).Returns(account);
		await Assert.That(await service.GetGameActorAsync(executor)).IsNotNull();
		await Assert.That(await service.GetGameActorAsync(new DBRef(7))).IsNull();
		account.Status = AccountStatus.Disabled;
		await Assert.That(await service.GetGameActorAsync(executor)).IsNull();
	}

	[Test]
	public async Task HigherExplicitChildAllowOverridesLowerChildDeny()
	{
		var roles = new[] { Role("parent", 100, PortalPermission.JobsManage, PermissionState.Allow),
			Role("restricted", 1, PortalPermission.JobsManageOwn, PermissionState.Deny),
			Role("exception", 2, PortalPermission.JobsManageOwn, PermissionState.Allow) };
		var explanation = new PermissionResolver().Explain(roles, PortalPermission.JobsManageOwn);
		await Assert.That(explanation.Allowed).IsTrue();
		await Assert.That(explanation.Priority).IsEqualTo(2);
		await Assert.That(explanation.Roles.Single()).IsEqualTo("exception");
	}

	private static SharpPlayer Player(int number) => new()
	{
		PasswordHash = "", Quota = 0, Home = null!, Location = null!,
		Object = new()
		{
			Key = number, CreationTime = 1, Name = "player", Type = "PLAYER", Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = null!, Powers = null!, Attributes = null!, LazyAttributes = null!, AllAttributes = null!, LazyAllAttributes = null!,
			Flags = new(() => Array.Empty<SharpObjectFlag>().ToAsyncEnumerable()), Parent = null!, Zone = null!, Children = null!
		}
	};

	private static SharpRole Role(string slug, int priority, string scope, PermissionState state) => new()
	{ Slug = slug, Name = slug, Priority = priority, Permissions = new() { [scope] = state } };

	private static (AdministrativeCapabilityService, IAccountService, IRoleRegistryService, SharpAccount) Build()
	{
		var accounts = Substitute.For<IAccountService>();
		var registry = Substitute.For<IRoleRegistryService>();
		var account = new SharpAccount { Id = "a", Username = "a", PasswordHash = "" };
		accounts.GetByIdAsync("a", Arg.Any<CancellationToken>()).Returns(account);
		accounts.GetCharactersAsync("a", Arg.Any<CancellationToken>()).Returns(new ValueTask<IReadOnlyList<SharpPlayer>>([]));
		registry.GetRoleAsync(Arg.Any<string>()).Returns(new NotFound());
		registry.GetRolesForAccountAsync("a", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<SharpRole>>(
			[Role("operator", 5, PortalPermission.SnapshotCapture, PermissionState.Allow)]));
		return (new(accounts, registry, new RoleDerivationService(), new PermissionResolver()), accounts, registry, account);
	}
}
