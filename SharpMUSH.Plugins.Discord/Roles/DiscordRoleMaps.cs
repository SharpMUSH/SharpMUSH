using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>A mapping and what it is doing now.</summary>
/// <param name="Problem">Why the bot cannot apply it, or null when it does.</param>
public sealed record DiscordRoleMappingStatus(DiscordRoleMapping Mapping, string? Problem, int Drift);

/// <summary>
/// Maps game roles onto Discord roles, under the rule that governs assigning roles: only a role below your own highest,
/// never one held implicitly or one that allows <c>administrator</c>, and <c>wizard</c> or <c>royalty</c> only when
/// confirmed, since a mapping shows publicly who holds the role.
/// </summary>
/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordRoleMaps(IDiscordRoleStore store, IDiscordRoles discord, IServiceProvider services, TimeProvider time)
{
	public IReadOnlyList<DiscordRoleMappingStatus> List()
	{
		var drift = store.Drift().ToDictionary(record => record.RoleId, record => record.Members.Count);
		return [.. store.Mappings()
			.OrderBy(mapping => mapping.Slug, StringComparer.OrdinalIgnoreCase)
			.Select(mapping => new DiscordRoleMappingStatus(mapping, discord.Problem(mapping), drift.GetValueOrDefault(mapping.RoleId)))];
	}

	/// <param name="actor">What the one mapping holds, for the hierarchy rule.</param>
	/// <param name="actorId">Their objid, kept with the mapping.</param>
	/// <param name="confirmed">Whether they confirmed mapping a staff role.</param>
	public async Task<Result<DiscordRoleMappingStatus>> MapAsync(PermissionContext actor, string actorId, string slug,
		string discordRole, bool confirmed, CancellationToken cancellationToken)
	{
		var registry = services.GetRequiredService<IRoleRegistryService>();
		if (await registry.GetRoleAsync(slug.Trim(), cancellationToken) is not SharpRole role)
		{
			return new Error<string>($"No game role is called {slug}. @role lists them.");
		}

		if (Refusal(actor, role, confirmed) is { } refusal)
		{
			return new Error<string>(refusal);
		}

		return discord.Find(discordRole.Trim()) switch
		{
			DiscordRoleInfo found => await MapToAsync(role, found, actorId, cancellationToken),
			Error<string> error => error,
		};
	}

	/// <summary>Removes the mapping and its drift. Members keep the Discord role as it is.</summary>
	public async Task<Result<DiscordRoleMapping>> UnmapAsync(string discordRole, CancellationToken cancellationToken)
		=> Lookup(discordRole) is { } mapping && await store.RemoveAsync(mapping.RoleId, cancellationToken)
			? mapping
			: new Error<string>($"{discordRole} is not a mapped Discord role. @discord/map lists them.");

	/// <summary>Switches the mapping between reporting drift and putting it back.</summary>
	public async Task<Result<DiscordRoleMapping>> ModeAsync(string discordRole, DiscordRoleMode mode, CancellationToken cancellationToken)
	{
		if (Lookup(discordRole) is not { } mapping)
		{
			return new Error<string>($"{discordRole} is not a mapped Discord role. @discord/map lists them.");
		}

		var changed = mapping with { Mode = mode };
		await store.SaveAsync(changed, cancellationToken);
		if (mode == DiscordRoleMode.Enforce)
		{
			discord.Settle(mapping.RoleId);
		}

		return changed;
	}

	/// <summary>A mapping by its Discord role's id, or by the name it had when mapped.</summary>
	public DiscordRoleMapping? Lookup(string discordRole)
	{
		var text = discordRole.Trim().TrimStart('@');
		var mappings = store.Mappings();
		return ulong.TryParse(text, out var id)
			? mappings.FirstOrDefault(mapping => mapping.RoleId == id)
			: mappings.Where(mapping => string.Equals(mapping.RoleName, text, StringComparison.OrdinalIgnoreCase)).ToList() is [var only]
				? only
				: null;
	}

	/// <summary>Why <paramref name="actor"/> may not map <paramref name="role"/>, or null when they may.</summary>
	public static string? Refusal(PermissionContext actor, SharpRole role, bool confirmed)
	{
		if (BuiltInRoles.IsImplicit(role.Slug))
		{
			return $"{role.Name} is held implicitly, so it cannot be mapped.";
		}

		if (PermissionResolver.StateOf(role.Permissions, PortalPermission.Administrator) == PermissionState.Allow)
		{
			return $"{role.Name} allows administrator, so it cannot be mapped.";
		}

		if (!RoleHierarchy.Outranks(actor, role.Priority))
		{
			return $"{role.Name} is not below your highest role, so you cannot map it.";
		}

		return !confirmed && role.Slug is BuiltInRoles.WizardSlug or BuiltInRoles.RoyaltySlug
			? $"Mapping {role.Name} shows on Discord who is staff. @discord/map/confirm maps it anyway."
			: null;
	}

	private async Task<Result<DiscordRoleMappingStatus>> MapToAsync(SharpRole role, DiscordRoleInfo found, string actorId,
		CancellationToken cancellationToken)
	{
		if (found.Everyone || found.Managed || found.Linked)
		{
			return new Error<string>(found switch
			{
				{ Everyone: true } => "@everyone cannot be mapped.",
				{ Linked: true } => $"{found.Name} is the bot's Linked role, which it gives every linked member.",
				_ => $"{found.Name} belongs to an integration or to Server Boosting, so Discord does not let the bot give it.",
			});
		}

		if (store.Mapping(found.RoleId) is { } existing)
		{
			return string.Equals(existing.Slug, role.Slug, StringComparison.OrdinalIgnoreCase)
				? new DiscordRoleMappingStatus(existing, discord.Problem(existing), 0)
				: new Error<string>($"{found.Name} is already mapped to {existing.Slug}. @discord/unmap {found.RoleId} first.");
		}

		var mapping = new DiscordRoleMapping(found.RoleId, found.GuildId, found.Name, role.Slug, DiscordRoleMode.Report,
			time.GetUtcNow(), actorId);
		await store.SaveAsync(mapping, cancellationToken);
		discord.Pass(found.GuildId);
		return new DiscordRoleMappingStatus(mapping, found.Problem, 0);
	}
}
