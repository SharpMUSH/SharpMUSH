using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Plugins.Discord.Roles;

/// <summary>
/// Linking or unlinking a member changes the Linked role, and with it every mapped role: a member who links gets the
/// roles their account holds, and one who unlinks loses them.
/// </summary>
public sealed class DiscordLinkedMember(IDiscordLinkedRole linked, IDiscordMemberSync sync) : IDiscordLinkedRole
{
	public async Task GiveAsync(ulong userId, CancellationToken cancellationToken)
	{
		sync.UserChanged(userId);
		await linked.GiveAsync(userId, cancellationToken);
	}

	public async Task TakeAsync(ulong userId, CancellationToken cancellationToken)
	{
		sync.UserChanged(userId);
		await linked.TakeAsync(userId, cancellationToken);
	}
}
