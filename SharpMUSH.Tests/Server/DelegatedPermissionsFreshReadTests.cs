using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Tests.Server.Controllers;

namespace SharpMUSH.Tests.Server;

public class DelegatedPermissionsFreshReadTests
{
	[ClassDataSource<WikiLandingFreshnessFactory>(Shared = SharedType.PerTestSession)]
	public required WikiLandingFreshnessFactory Factory { get; init; }

	[Test]
	public async Task UncachedReadSeesNativeDraftRevocationWithoutReplacingOrUpdatingThePrimedRequestMemo()
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: true);
		var accessor = new HttpContextAccessor();
		var prior = accessor.HttpContext;
		var context = new DefaultHttpContext();
		accessor.HttpContext = context;
		try
		{
			var delegated = new DelegatedPermissions(LandingFreshNative.Capabilities(Factory), LandingFreshNative.Roles(Factory), accessor);
			var app = new DelegatedApp("draft-memo-" + Guid.NewGuid().ToString("N"), "Native draft memo", reader.Character.Object.DBRef, [DelegatedScopes.Content]);
			var before = await delegated.GrantedAsync(reader.AccountId, app);
			await Assert.That(before.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(before.Contains(PortalPermission.WikiDrafts)).IsTrue();
			var memoItems = context.Items.ToArray();
			var principal = context.User;
			await LandingFreshNative.Roles(Factory).SetAccountOverrideAsync(reader.AccountId, PortalPermission.WikiDrafts, PermissionState.Deny);
			var native = await LandingFreshNative.HeldAsync(Factory, reader);
			await Assert.That(native.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(native.Contains(PortalPermission.WikiDrafts)).IsFalse();
			var old = await delegated.GrantedAsync(reader.AccountId, app);
			await Assert.That(ReferenceEquals(before, old)).IsTrue();
			await Assert.That(old.Contains(PortalPermission.WikiDrafts)).IsTrue();
			var fresh = await delegated.GrantedUncachedAsync(reader.AccountId, app, CancellationToken.None);
			await Assert.That(fresh.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(fresh.Contains(PortalPermission.WikiDrafts)).IsFalse();
			await Assert.That(context.Items.Count).IsEqualTo(memoItems.Length);
			foreach (var item in memoItems)
				await Assert.That(context.Items.TryGetValue(item.Key, out var value) && ReferenceEquals(value, item.Value)).IsTrue();
			await Assert.That(ReferenceEquals(await delegated.GrantedAsync(reader.AccountId, app), before)).IsTrue();
			await Assert.That(ReferenceEquals(accessor.HttpContext, context)).IsTrue();
			await Assert.That(ReferenceEquals(context.User, principal)).IsTrue();
		}
		finally { accessor.HttpContext = prior; await LandingFreshNative.CleanupAsync(Factory, null, reader); }
	}

	[Test]
	public async Task UncachedReadRefreshesNativeCustomCategoryWhileTokenScopesAndHeldPermissionRemainUnchanged()
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: false);
		var suffix = Guid.NewGuid().ToString("N")[..12];
		var permission = "landing.fresh." + suffix;
		var oldCategory = "Landing Old " + suffix;
		var newCategory = "Landing New " + suffix;
		var roles = LandingFreshNative.Roles(Factory);
		var accessor = new HttpContextAccessor();
		var prior = accessor.HttpContext;
		var context = new DefaultHttpContext();
		accessor.HttpContext = context;
		try
		{
			await roles.UpsertCustomPermissionAsync(new(permission, oldCategory, "Native category refresh", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
			await roles.SetAccountOverrideAsync(reader.AccountId, permission, PermissionState.Allow);
			var app = new DelegatedApp("category-" + suffix, "Category memo", reader.Character.Object.DBRef,
				[DelegatedScopes.Content, DelegatedScopes.CategoryScope(oldCategory)]);
			var delegated = new DelegatedPermissions(LandingFreshNative.Capabilities(Factory), roles, accessor);
			var before = await delegated.GrantedAsync(reader.AccountId, app);
			await Assert.That(before.Contains(permission)).IsTrue();
			var stored = (await roles.GetCustomPermissionsAsync()).Single(item => item.Scope == permission);
			await roles.UpsertCustomPermissionAsync(stored with { Category = newCategory });
			await Assert.That((await roles.GetCustomPermissionsAsync()).Single(item => item.Scope == permission).Category).IsEqualTo(newCategory);
			await Assert.That((await LandingFreshNative.HeldAsync(Factory, reader)).Contains(permission)).IsTrue();
			await Assert.That((await delegated.GrantedAsync(reader.AccountId, app)).Contains(permission)).IsTrue();
			var fresh = await delegated.GrantedUncachedAsync(reader.AccountId, app);
			await Assert.That(fresh.Contains(permission)).IsFalse();
			await Assert.That(fresh.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(ReferenceEquals(await delegated.GrantedAsync(reader.AccountId, app), before)).IsTrue();
			await Assert.That(ReferenceEquals(accessor.HttpContext, context)).IsTrue();
		}
		finally
		{
			accessor.HttpContext = prior;
			await roles.RemoveCustomPermissionAsync(permission);
			await LandingFreshNative.CleanupAsync(Factory, null, reader);
		}
	}

	[Test]
	public async Task UncachedReadRetainsTokenIntersectionAndRejectsSameNumberWithDifferentCharacterCreationIdentity()
	{
		var reader = await LandingFreshNative.ReaderAsync(Factory, drafts: true);
		var roles = LandingFreshNative.Roles(Factory);
		var accessor = new HttpContextAccessor();
		var prior = accessor.HttpContext;
		var context = new DefaultHttpContext();
		accessor.HttpContext = context;
		try
		{
			await roles.SetAccountOverrideAsync(reader.AccountId, PortalPermission.QueueInspect, PermissionState.Allow);
			await Assert.That((await LandingFreshNative.HeldAsync(Factory, reader)).Contains(PortalPermission.QueueInspect)).IsTrue();
			var app = new DelegatedApp("identity-" + Guid.NewGuid().ToString("N"), "Exact identity", reader.Character.Object.DBRef, [DelegatedScopes.Content]);
			var delegated = new DelegatedPermissions(LandingFreshNative.Capabilities(Factory), roles, accessor);
			var memo = await delegated.GrantedAsync(reader.AccountId, app);
			var fresh = await delegated.GrantedUncachedAsync(reader.AccountId, app);
			await Assert.That(fresh.Contains(PortalPermission.WikiRead)).IsTrue();
			await Assert.That(fresh.Contains(PortalPermission.WikiDrafts)).IsTrue();
			await Assert.That(fresh.Contains(PortalPermission.QueueInspect)).IsFalse();
			var wrongIncarnation = app with { Character = new DBRef(app.Character.Number, reader.Character.Object.CreationTime + 1) };
			var invalid = await delegated.GrantedUncachedAsync(reader.AccountId, wrongIncarnation);
			await Assert.That(invalid).IsEmpty();
			await Assert.That(ReferenceEquals(await delegated.GrantedAsync(reader.AccountId, app), memo)).IsTrue();
			await Assert.That(ReferenceEquals(accessor.HttpContext, context)).IsTrue();
		}
		finally { accessor.HttpContext = prior; await LandingFreshNative.CleanupAsync(Factory, null, reader); }
	}
}
