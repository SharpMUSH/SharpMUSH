using Discord;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Always reads REST state: Discord.Net's socket cache does not change when a REST overwrite succeeds.</summary>
public sealed class DiscordPermissionChannel(ulong everyoneRoleId, ulong linkedRoleId,
	Func<CancellationToken, Task<IRole>> ensureLinked,
	Func<CancellationToken, Task<IGuildChannel>> readChannel,
	Func<ulong, CancellationToken, Task<IRole?>> readRole) : IDiscordPermissionChannel
{
	public ulong EveryoneRoleId => everyoneRoleId;
	public ulong LinkedRoleId { get; private set; } = linkedRoleId;
	public async Task EnsureLinkedRoleAsync(CancellationToken ct) => LinkedRoleId = (await ensureLinked(ct)).Id;
	public async Task<OverwritePermissions?> ReadAsync(ulong roleId, CancellationToken ct)
	{
		if (await readRole(roleId, ct) is not { } role) return null;
		return (await readChannel(ct)).GetPermissionOverwrite(role) ?? OverwritePermissions.InheritAll;
	}
	public async Task WriteAsync(ulong roleId, Func<OverwritePermissions, OverwritePermissions> change, CancellationToken ct)
	{
		// A deleted saved role has no overwrite left. Restoring it never creates a replacement role.
		if (await readRole(roleId, ct) is not { } role) return;
		var channel = await readChannel(ct);
		var current = channel.GetPermissionOverwrite(role) ?? OverwritePermissions.InheritAll;
		await channel.AddPermissionOverwriteAsync(role, change(current), new RequestOptions { CancelToken = ct, AuditLogReason = "SharpMUSH channel access" });
	}
}
