using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IChannelStore"/>: chat channels and memberships. Not ported yet — every member throws
/// <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public IAsyncEnumerable<SharpChannel> GetAllChannelsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpChannel?> GetChannelAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpChannel> GetChannelsOwnedByAsync(DBRef owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpChannel> GetMemberChannelsAsync(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<ChannelCreationResult> CreateChannelAsync(MString name, string[] privs, SharpPlayer owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelAsync(SharpChannel channel,
		MString? name,
		MString? description,
		string[]? privs,
		string? joinLock,
		string? speakLock,
		string? seeLock,
		string? hideLock,
		string? modLock,
		string? mogrifier,
		int? buffer, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelOwnerAsync(SharpChannel channel, SharpPlayer newOwner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask DeleteChannelAsync(SharpChannel channel, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask AddUserToChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask RemoveUserFromChannelAsync(SharpChannel channel, AnySharpObject obj, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UpdateChannelUserStatusAsync(SharpChannel channel, AnySharpObject obj, SharpChannelStatus status, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
