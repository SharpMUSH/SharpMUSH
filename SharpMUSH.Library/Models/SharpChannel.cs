namespace SharpMUSH.Library.Models;

public class SharpChannel
{
	public required string Name { get; set; }
	public string Description { get; set; } = string.Empty;	
	public required Lazy<SharpPlayer> Owner { get; set; }
	public required Func<IEnumerable<SharpObject>> Members { get; set; }
	public required string[] Privs { get; set; }
	public string JoinLock { get; set; } = string.Empty;
	public string SpeakLock { get; set; } = string.Empty;
	public string SeeLock { get; set; } = string.Empty;
	public string HideLock { get; set; } = string.Empty;
	public string ModLock { get; set; } = string.Empty;
}

public class ChannelStatus
{
	public required bool Gagged { get; set; } = false;
	public required bool Mute { get; set; } = false;
	public required bool Hide { get; set; } = false;
	public required bool Combine { get; set; } = false;
	public required string Title { get; set; } = string.Empty;
}