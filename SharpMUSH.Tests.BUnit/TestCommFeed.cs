using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>An <see cref="ICommFeed"/> a test fills by hand, recording what the page marks read.</summary>
public sealed class TestCommFeed : ICommFeed
{
	private Action? _changed;

	public IReadOnlyList<CommChannel> ChannelList { get; set; } = [];
	public IReadOnlyList<CommConversation> ConversationList { get; set; } = [];
	public Dictionary<string, List<CommMessage>> Lines { get; } = new(StringComparer.Ordinal);
	public List<string> MarkedRead { get; } = [];
	public List<string> Loaded { get; } = [];

	public IReadOnlyList<CommChannel> Channels => ChannelList;
	public IReadOnlyList<CommConversation> Conversations => ConversationList;
	public string? Viewing { get; set; }
	public bool? PageLogging { get; set; }
	public int Listeners => _changed?.GetInvocationList().Length ?? 0;

	public IReadOnlyList<CommMessage> Messages(string key) => Lines.TryGetValue(key, out var lines) ? lines : [];

	public void MarkRead(string key) => MarkedRead.Add(key);

	public Task<bool> LoadHistoryAsync(string key)
	{
		Loaded.Add(key);
		return Task.FromResult(true);
	}

	public void Raise() => _changed?.Invoke();

	public event Action? Changed
	{
		add => _changed += value;
		remove => _changed -= value;
	}
}
