using DotNext.Threading;
using Newtonsoft.Json;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models;

public class SharpChannel
{
	[JsonIgnore]
	public string? Id { get; set; }
	public required MString Name { get; set; }
	public MString Description { get; set; } = MModule.empty();	
	public required AsyncLazy<SharpPlayer> Owner { get; set; }
	public required AsyncLazy<IEnumerable<AnySharpObject>> Members { get; set; }
	public required string[] Privs { get; set; }
	public string JoinLock { get; set; } = string.Empty;
	public string SpeakLock { get; set; } = string.Empty;
	public string SeeLock { get; set; } = string.Empty;
	public string HideLock { get; set; } = string.Empty;
	public string ModLock { get; set; } = string.Empty;
}

public class SharpChannelStatus
{
	public bool? Gagged { get; set; }
	public bool? Mute { get; set; }
	public bool? Hide { get; set; }
	public bool? Combine { get; set; }
	public string? Title { get; set; }
}