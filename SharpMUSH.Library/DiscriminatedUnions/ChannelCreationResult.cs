using System.Runtime.CompilerServices;

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
/// Every provider answers with one of these three, so a create the storage layer rejected can never
/// read as success to the caller.
/// </remarks>
[Union]
public sealed class ChannelCreationResult : IUnion
{
	public ChannelCreationResult(Success value) => Value = value;
	public ChannelCreationResult(ChannelNameTaken value) => Value = value;
	public ChannelCreationResult(Error<string> value) => Value = value;

	public object? Value { get; }

	public override bool Equals(object? obj) => obj is ChannelCreationResult other && Equals(Value, other.Value);

	public override int GetHashCode() => Value?.GetHashCode() ?? 0;

	public bool IsSuccess => Value is Success;
	public bool IsNameTaken => Value is ChannelNameTaken;
	public bool IsError => Value is Error<string>;

	public string AsError => Value is Error<string> error ? error.Value : throw UnionCase.Mismatch<Error<string>>(Value);
}
