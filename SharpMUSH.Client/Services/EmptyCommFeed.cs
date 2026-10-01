namespace SharpMUSH.Client.Services;

/// <summary>
/// No channels and no pages: what the Play sidebar shows until a game sends <c>comm.channels</c> and
/// <c>comm.message</c> (README §7.3). The terminal still carries every line.
/// </summary>
public sealed class EmptyCommFeed : ICommFeed
{
	public IReadOnlyList<CommChannel> Channels => [];

	public IReadOnlyList<CommConversation> Conversations => [];

	public IReadOnlyList<CommMessage> Messages(string key) => [];

	public void MarkRead(string key)
	{
	}

	/// <summary>Never raised: nothing here changes.</summary>
	public event Action? Changed
	{
		add { }
		remove { }
	}
}
