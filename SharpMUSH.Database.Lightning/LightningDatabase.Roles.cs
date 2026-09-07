using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="Library.Services.Interfaces.IRoleRegistryService"/>: portal roles (Discord-style RBAC)
/// and account-role assignments. Not ported yet — every member throws
/// <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public Task UpsertRoleAsync(SharpRole role)
		=> throw new NotImplementedException();

	public Task<OneOf<SharpRole, NotFound>> GetRoleAsync(string slug)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<SharpRole>> GetRolesAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public Task RemoveRoleAsync(string slug)
		=> throw new NotImplementedException();

	public Task AssignRoleToAccountAsync(string accountId, string roleSlug)
		=> throw new NotImplementedException();

	public Task RemoveRoleFromAccountAsync(string accountId, string roleSlug)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<SharpRole>> GetRolesForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<string>> GetAccountIdsForRoleAsync(string roleSlug)
		=> throw new NotImplementedException();
}
