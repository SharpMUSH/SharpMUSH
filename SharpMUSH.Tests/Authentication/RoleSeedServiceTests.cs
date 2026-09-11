using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Authentication;

public class RoleSeedServiceTests
{
	[Test]
	public async Task ExistingStaffReceiveMissingCapabilitiesWithoutReplacingEdits()
	{
		var roles = Substitute.For<IRoleRegistryService>();
		var existing = BuiltInRoles.All.ToDictionary(x => x.Slug, x => new SharpRole
		{
			Slug = x.Slug, Name = "Custom " + x.Name, IsSystem = x.IsSystem, Color = "#123456", Priority = 123,
			Permissions = new Dictionary<string, PermissionState> { [PortalPermission.QueueControl] = PermissionState.Deny, ["JOBS.MANAGE"] = PermissionState.Deny, [PortalPermission.QueueInspectOwn] = PermissionState.Inherit }
		});
		roles.GetRoleAsync(Arg.Any<string>()).Returns(c => Task.FromResult<Found<SharpRole>>(existing[c.Arg<string>()]));
		roles.UpsertRoleAsync(Arg.Any<SharpRole>()).Returns(c => { existing[c.Arg<SharpRole>().Slug] = c.Arg<SharpRole>(); return Task.CompletedTask; });
		var store = Substitute.For<IExpandedDataStore>();
		RoleSeedService.CapabilityMigration? migration = null;
		store.GetExpandedServerData<RoleSeedService.CapabilityMigration>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => migration);
		store.SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(c =>
		{
			migration = (RoleSeedService.CapabilityMigration)c[1];
			return ValueTask.CompletedTask;
		});
		var service = new RoleSeedService(roles, NullLogger<RoleSeedService>.Instance, store);
		await service.StartAsync(default);
		foreach (var slug in new[] { "god", "wizard" })
		{
			await Assert.That(existing[slug].Permissions.GetValueOrDefault(PortalPermission.QueueInspect)).IsEqualTo(PermissionState.Allow);
			await Assert.That(existing[slug].Permissions.GetValueOrDefault(PortalPermission.RealityAdmin)).IsEqualTo(PermissionState.Allow);
			await Assert.That(existing[slug].Permissions[PortalPermission.QueueControl]).IsEqualTo(PermissionState.Deny);
			await Assert.That(existing[slug].Permissions.ContainsKey(PortalPermission.JobsManage)).IsFalse();
			await Assert.That(new PermissionResolver().Resolve([existing[slug]]).Contains(PortalPermission.JobsManage)).IsFalse();
			await Assert.That(existing[slug].Permissions[PortalPermission.QueueInspectOwn]).IsEqualTo(PermissionState.Inherit);
			await Assert.That(existing[slug].Name).IsEqualTo("Custom " + (slug == "god" ? "God" : "Wizard"));
			await Assert.That(existing[slug].Priority).IsEqualTo(123);
		}
		await Assert.That(existing["player"].Permissions.ContainsKey(PortalPermission.RealityAdmin)).IsFalse();
		// An administrator can remove a migrated grant afterwards without restart undoing it.
		existing["god"].Permissions.Remove(PortalPermission.QueueInspect);
		await service.StartAsync(default);
		await Assert.That(existing["god"].Permissions.ContainsKey(PortalPermission.QueueInspect)).IsFalse();
	}
	[Test]
	public async Task FailedRoleWriteDoesNotMarkMigrationCompleteOrMutateReadState()
	{
		var roles = Substitute.For<IRoleRegistryService>();
		var oldRole = new SharpRole { Slug = "god", Name = "God", IsSystem = true };
		roles.GetRoleAsync(Arg.Any<string>()).Returns(Task.FromResult<Found<SharpRole>>(oldRole));
		roles.UpsertRoleAsync(Arg.Any<SharpRole>()).Returns(Task.FromException(new IOException("write rejected")));
		var store = Substitute.For<IExpandedDataStore>();
		var service = new RoleSeedService(roles, NullLogger<RoleSeedService>.Instance, store);
		await Assert.That(() => service.StartAsync(default)).Throws<IOException>();
		await Assert.That(oldRole.Permissions.Count).IsEqualTo(0);
		await store.DidNotReceive().SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task NewWorldSeedsDefaultsAndRecordsUpgradeOnlyOnce()
	{
		var roles = Substitute.For<IRoleRegistryService>();
		var saved = new Dictionary<string, SharpRole>();
		roles.GetRoleAsync(Arg.Any<string>()).Returns(c => Task.FromResult<Found<SharpRole>>(
			saved.TryGetValue(c.Arg<string>(), out var role) ? role : new NotFound()));
		roles.UpsertRoleAsync(Arg.Any<SharpRole>()).Returns(c => { saved[c.Arg<SharpRole>().Slug] = c.Arg<SharpRole>(); return Task.CompletedTask; });
		var store = Substitute.For<IExpandedDataStore>();
		RoleSeedService.CapabilityMigration? migration = null;
		store.GetExpandedServerData<RoleSeedService.CapabilityMigration>(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => migration);
		store.SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(c =>
		{
			migration = (RoleSeedService.CapabilityMigration)c[1];
			return ValueTask.CompletedTask;
		});
		var service = new RoleSeedService(roles, NullLogger<RoleSeedService>.Instance, store);
		await service.StartAsync(default);
		await service.StartAsync(default);
		await Assert.That(saved.Count).IsEqualTo(BuiltInRoles.All.Count);
		await roles.Received(BuiltInRoles.All.Count).UpsertRoleAsync(Arg.Any<SharpRole>());
		await store.Received(1).SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
	}

}
