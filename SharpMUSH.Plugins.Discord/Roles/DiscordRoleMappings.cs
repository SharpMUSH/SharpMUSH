using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>What the bot does when someone on Discord gives or takes a mapped role by hand.</summary>
public enum DiscordRoleMode
{
	/// <summary>Records the change for staff to decide on, and leaves it.</summary>
	Report,

	/// <summary>Records the change and puts the game's answer back at once.</summary>
	Enforce,
}

/// <summary>
/// A game role mirrored onto a Discord role. A linked member holds the Discord role exactly when their game account, or
/// one of its characters, holds the game role. Keyed by the Discord role's id and the game role's slug, so renaming
/// either leaves it intact.
/// </summary>
/// <param name="RoleName">The Discord role's name when it was mapped, for listings once it is gone.</param>
/// <param name="MappedBy">The objid of who mapped it.</param>
public sealed record DiscordRoleMapping(
	ulong RoleId,
	ulong GuildId,
	string RoleName,
	string Slug,
	DiscordRoleMode Mode,
	DateTimeOffset MappedAt,
	string MappedBy);

/// <summary>A mapped role someone on Discord gave or took by hand, against the game's answer.</summary>
/// <param name="Added">True when the member was given the role, false when it was taken from them.</param>
public sealed record DiscordDrift(ulong UserId, string UserName, bool Added, DateTimeOffset At);

/// <summary>The open drift of one mapped role, at most one per member.</summary>
public sealed record DiscordDriftRecord(ulong RoleId, IReadOnlyList<DiscordDrift> Members);

/// <summary>Role mappings and their open drift, kept in the world so its backups carry them.</summary>
public interface IDiscordRoleStore
{
	IReadOnlyList<DiscordRoleMapping> Mappings();

	DiscordRoleMapping? Mapping(ulong roleId);

	ValueTask SaveAsync(DiscordRoleMapping mapping, CancellationToken cancellationToken = default);

	/// <summary>Removes the mapping and its open drift.</summary>
	/// <returns>Whether there was a mapping.</returns>
	ValueTask<bool> RemoveAsync(ulong roleId, CancellationToken cancellationToken = default);

	/// <summary>Every mapped role's open drift.</summary>
	IReadOnlyList<DiscordDriftRecord> Drift();

	/// <summary>Whether <paramref name="userId"/>'s hold on the role has open drift.</summary>
	bool Drifted(ulong roleId, ulong userId);

	/// <summary>Records the drift, replacing the member's earlier one on the same role.</summary>
	ValueTask RecordAsync(ulong roleId, DiscordDrift drift, CancellationToken cancellationToken = default);

	/// <summary>Settles the member's drift on the role, once the game's answer is applied again.</summary>
	ValueTask SettleAsync(ulong roleId, ulong userId, CancellationToken cancellationToken = default);
}

/// <summary>Two plugin-owned tables, <c>discord.role</c> and <c>discord.drift</c>, both by Discord role id.</summary>
public sealed class LightningDiscordRoleStore(ILightningStorageAccessor accessor) : IDiscordRoleStore
{
	private readonly DiscordTable<DiscordRoleMapping> _mappings = new(accessor, "discord.role");
	private readonly DiscordTable<DiscordDriftRecord> _drift = new(accessor, "discord.drift");

	/// <summary>A drift change reads the role's record and writes it back; two at once must not lose one.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	public IReadOnlyList<DiscordRoleMapping> Mappings() => _mappings.List();

	public DiscordRoleMapping? Mapping(ulong roleId) => _mappings.Get(roleId);

	public ValueTask SaveAsync(DiscordRoleMapping mapping, CancellationToken cancellationToken = default)
		=> _mappings.SaveAsync(mapping.RoleId, mapping, cancellationToken);

	public async ValueTask<bool> RemoveAsync(ulong roleId, CancellationToken cancellationToken = default)
	{
		await _drift.RemoveAsync(roleId, cancellationToken);
		return await _mappings.RemoveAsync(roleId, cancellationToken);
	}

	public IReadOnlyList<DiscordDriftRecord> Drift() => _drift.List();

	public bool Drifted(ulong roleId, ulong userId)
		=> _drift.Get(roleId)?.Members.Any(drift => drift.UserId == userId) == true;

	public ValueTask RecordAsync(ulong roleId, DiscordDrift drift, CancellationToken cancellationToken = default)
		=> ChangeAsync(roleId, members => [.. members.Where(member => member.UserId != drift.UserId), drift], cancellationToken);

	public ValueTask SettleAsync(ulong roleId, ulong userId, CancellationToken cancellationToken = default)
		=> Drifted(roleId, userId)
			? ChangeAsync(roleId, members => [.. members.Where(member => member.UserId != userId)], cancellationToken)
			: ValueTask.CompletedTask;

	private async ValueTask ChangeAsync(ulong roleId, Func<IReadOnlyList<DiscordDrift>, IReadOnlyList<DiscordDrift>> change,
		CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			var members = change(_drift.Get(roleId)?.Members ?? []);
			if (members.Count == 0)
			{
				await _drift.RemoveAsync(roleId, CancellationToken.None);
			}
			else
			{
				await _drift.SaveAsync(roleId, new DiscordDriftRecord(roleId, members), CancellationToken.None);
			}
		}
		finally
		{
			_changing.Release();
		}
	}
}
