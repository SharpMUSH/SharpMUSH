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
	public required AsyncLazy<SharpPlayer> Owner { get; set; }
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