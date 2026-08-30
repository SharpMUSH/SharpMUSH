using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpChannel
{
	public record MemberAndStatus(AnySharpObject Member, SharpChannelStatus Status);

	[JsonIgnore] public string? Id { get; set; }
	public required MString Name { get; set; }
	public MString Description { get; set; } = MModule.empty();
	/// <summary>
	/// Who owns the channel, or <see langword="null"/> if nothing does.
	/// </summary>
	/// <remarks>
	/// Nullable because the data can genuinely say so: deleting an object detaches every relationship on
	/// it, channel ownership included, so a channel outlives an owner deleted through storage rather than
	/// through <c>ObjectDestructionService</c> (which re-owns first). A stale channel list resolving a
	/// since-deleted channel lands in the same place. Every provider used to throw out of the row access
	/// instead, and since <c>@channel/add</c> resolves the owner of every channel to count its own, one
	/// ownerless channel broke channel creation for everybody.
	/// </remarks>
	public required AsyncLazy<SharpPlayer?> Owner { get; set; }
	public required Lazy<IAsyncEnumerable<MemberAndStatus>> Members { get; set; }
	public required string[] Privs { get; set; }
	public string JoinLock { get; set; } = string.Empty;
	public string SpeakLock { get; set; } = string.Empty;
	public string SeeLock { get; set; } = string.Empty;
	public string HideLock { get; set; } = string.Empty;
	public string ModLock { get; set; } = string.Empty;
	public int Buffer { get; set; } = 0;
	public string Mogrifier { get; set; } = string.Empty;
}

public record SharpChannelStatus(bool? Combine, bool? Gagged, bool? Hide, bool? Mute, MString? Title);