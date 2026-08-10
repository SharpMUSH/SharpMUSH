using OneOf;
using OneOf.Types;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A channel with the requested name already exists, so the create was refused.
/// </summary>
/// <remarks>
/// Distinct from <see cref="Error{T}"/> because it is the one failure a caller can explain to a player.
/// Channel names are a global namespace (PennMUSH <c>ok_channel_name</c>, <c>src/extchat.c:1855-1870</c>),
/// so this is reachable without any race whenever two players pick the same name.
/// </remarks>
public readonly record struct ChannelNameTaken;

/// <summary>
/// The outcome of a channel create: it worked, the name was taken, or the storage layer failed.
/// </summary>
/// <remarks>
/// <c>CreateChannelAsync</c> used to return <c>ValueTask</c>, and ArangoDB's implementation caught every
/// exception, aborted its transaction and returned normally — so a create that failed reported success to
/// the caller. Every provider now answers with one of these three, and none of them is silence.
/// </remarks>
public class ChannelCreationResult(OneOf<Success, ChannelNameTaken, Error<string>> input)
	: OneOfBase<Success, ChannelNameTaken, Error<string>>(input)
{
	public static implicit operator ChannelCreationResult(Success x) => new(x);
	public static implicit operator ChannelCreationResult(ChannelNameTaken x) => new(x);
	public static implicit operator ChannelCreationResult(Error<string> x) => new(x);

	public bool IsSuccess => IsT0;
	public bool IsNameTaken => IsT1;
	public bool IsError => IsT2;

	public string AsError => AsT2.Value;
}
