using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>The game's side of role mapping: which game roles an account holds.</summary>
public interface IDiscordRoleHolders
{
	/// <summary>
	/// The slugs of the roles the account holds, itself or through any of its characters, read through the same grants
	/// the engine uses; null when the account is gone or not active (disabled, banned or closed), which holds none.
	/// </summary>
	Task<IReadOnlySet<string>?> HeldAsync(string accountId, CancellationToken cancellationToken);

	/// <summary>
	/// The slugs of the game roles that allow <c>administrator</c> now. Such a role cannot be mapped, and one changed to
	/// allow it after it was mapped is no longer applied.
	/// </summary>
	Task<IReadOnlySet<string>> AdministrativeAsync(CancellationToken cancellationToken);
}

/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordRoleHolders(IServiceProvider services) : IDiscordRoleHolders
{
	public async Task<IReadOnlySet<string>?> HeldAsync(string accountId, CancellationToken cancellationToken)
	{
		if (await services.GetRequiredService<IAccountService>().GetByIdAsync(accountId, cancellationToken) is not { IsActive: true })
		{
			return null;
		}

		var context = await services.GetRequiredService<IAdministrativeCapabilityService>()
			.GetContextAsync(new CapabilityActor(accountId), cancellationToken);
		return context.Roles.Select(role => role.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	public async Task<IReadOnlySet<string>> AdministrativeAsync(CancellationToken cancellationToken)
		=> (await services.GetRequiredService<IRoleRegistryService>().GetRolesAsync(cancellationToken))
			.Where(role => PermissionResolver.StateOf(role.Permissions, PortalPermission.Administrator) == PermissionState.Allow)
			.Select(role => role.Slug)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
