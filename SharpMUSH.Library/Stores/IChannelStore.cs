using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Chat channels and their memberships.
/// </summary>
public interface IChannelStore
{
	IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken cancellationToken = default);

	ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// The channels owned by <paramref name="owner"/>. Callers used to walk every channel and resolve
	/// each owner, which cost a round trip per channel.
	/// </summary>
	IAsyncEnumerable<SharpChannel> GetChannelsOwnedByAsync(DBRef owner, CancellationToken cancellationToken = default);

	IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a channel, its owner edge and its owner's membership edge, atomically with respect to the
	/// channel name — which is a global namespace, so a create that loses the race must be refused rather
	/// than duplicating or overwriting the winner.
	/// </summary>
	/// <returns>
	/// <see cref="OneOf.Types.Success"/>, <see cref="ChannelNameTaken"/> when the name is already in use, or
	/// <see cref="OneOf.Types.Error{T}"/> carrying the storage layer's message. Never silence: the caller
	/// tells a player which of the three happened.
	/// </returns>
	ValueTask<ChannelCreationResult> CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default);

	ValueTask UpdateChannelAsync(SharpChannel channel,
		MString? name,
		MString? description,
		string[]? privs,
		string? joinLock,
		string? speakLock,
		string? seeLock,
		string? hideLock,
		string? modLock,
		string? mogrifier,
		int? buffer, CancellationToken cancellationToken = default);

	ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default);

	ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default);

	ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);

	ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default);

	ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default);
}
