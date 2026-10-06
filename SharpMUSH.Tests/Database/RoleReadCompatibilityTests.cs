using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class RoleReadCompatibilityTests
{
	[Test]
	[Arguments(typeof(IRoleRegistryService))]
	[Arguments(typeof(LightningDatabase))]
	public async Task PublishedSingleArgumentRoleReadRemainsAvailable(Type contract)
	{
		await Assert.That(contract.GetMethod(nameof(IRoleRegistryService.GetRoleAsync), [typeof(string)])).IsNotNull();
	}

	[Test]
	public async Task TokenAwareRoleReadHasDefaultImplementationForExistingProviders()
	{
		var overload = typeof(IRoleRegistryService).GetMethod(nameof(IRoleRegistryService.GetRoleAsync), [typeof(string), typeof(CancellationToken)]);
		await Assert.That(overload).IsNotNull();
		await Assert.That(overload!.IsAbstract).IsFalse();
	}
	private sealed class LegacyRegistry : IRoleRegistryService
	{
		public TaskCompletionSource<Found<SharpRole>> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public int Reads { get; private set; }
		public Task<Found<SharpRole>> GetRoleAsync(string slug) { Reads++; return Result.Task; }
		public Task UpsertRoleAsync(SharpRole role) => throw new NotSupportedException();
		public Task<IReadOnlyList<SharpRole>> GetRolesAsync(CancellationToken token = default) => throw new NotSupportedException();
		public Task RemoveRoleAsync(string slug) => throw new NotSupportedException();
		public Task AssignRoleToAccountAsync(string accountId, string slug) => throw new NotSupportedException();
		public Task RemoveRoleFromAccountAsync(string accountId, string slug) => throw new NotSupportedException();
		public Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken token = default) => throw new NotSupportedException();
		public Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string slug) => throw new NotSupportedException();
		public Task<IReadOnlyDictionary<string, PermissionState>> GetAccountOverridesAsync(string accountId, CancellationToken token = default) => throw new NotSupportedException();
		public Task SetAccountOverrideAsync(string accountId, string scope, PermissionState state) => throw new NotSupportedException();
		public Task<IReadOnlyList<string>> GetObjectRolesAsync(int number, CancellationToken token = default) => throw new NotSupportedException();
		public Task AssignRoleToObjectAsync(int number, string slug) => throw new NotSupportedException();
		public Task RemoveRoleFromObjectAsync(int number, string slug) => throw new NotSupportedException();
		public Task<IReadOnlyList<int>> GetObjectsForRoleAsync(string slug, CancellationToken token = default) => throw new NotSupportedException();
		public Task<IReadOnlyDictionary<string, PermissionState>> GetObjectOverridesAsync(int number, CancellationToken token = default) => throw new NotSupportedException();
		public Task SetObjectOverrideAsync(int number, string scope, PermissionState state) => throw new NotSupportedException();
		public Task<IReadOnlyList<CustomPermission>> GetCustomPermissionsAsync(CancellationToken token = default) => throw new NotSupportedException();
		public Task UpsertCustomPermissionAsync(CustomPermission permission) => throw new NotSupportedException();
		public Task<PermissionOverrideHolders> GetOverridesOfAsync(string scope, CancellationToken token = default) => throw new NotSupportedException();
		public Task RemoveCustomPermissionAsync(string scope) => throw new NotSupportedException();
		public Task<IReadOnlyList<RoleCategory>> GetCategoriesAsync(CategoryKind kind, CancellationToken token = default) => throw new NotSupportedException();
		public Task UpsertCategoryAsync(CategoryKind kind, RoleCategory category) => throw new NotSupportedException();
		public Task RenameCategoryAsync(CategoryKind kind, string name, RoleCategory renamed) => throw new NotSupportedException();
		public Task RemoveCategoryAsync(CategoryKind kind, string name) => throw new NotSupportedException();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task DefaultReadObservesOriginalCancellationToken(bool cancelBeforeRead)
	{
		var provider = new LegacyRegistry();
		IRoleRegistryService contract = provider;
		using var cancel = new CancellationTokenSource();
		if (cancelBeforeRead) cancel.Cancel();
		var pending = contract.GetRoleAsync("wizard", cancel.Token);
		cancel.Cancel();
		OperationCanceledException? cancellation = null;
		try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); }
		catch (OperationCanceledException ex) { cancellation = ex; }
		finally { provider.Result.TrySetResult(new NotFound()); }
		await Assert.That(cancellation).IsNotNull();
		await Assert.That(cancellation!.CancellationToken).IsEqualTo(cancel.Token);
		await Assert.That(provider.Reads).IsEqualTo(cancelBeforeRead ? 0 : 1);
	}

	[Test]
	public async Task DefaultReadDelegatesToLegacyProvider()
	{
		var provider = new LegacyRegistry();
		provider.Result.SetResult(new NotFound());
		IRoleRegistryService contract = provider;
		await Assert.That((await contract.GetRoleAsync("wizard", CancellationToken.None)).Value).IsTypeOf<NotFound>();
		await Assert.That(provider.Reads).IsEqualTo(1);
	}

}
