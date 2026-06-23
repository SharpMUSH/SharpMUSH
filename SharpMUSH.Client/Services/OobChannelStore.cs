using System.Collections.Concurrent;

namespace SharpMUSH.Client.Services;

public sealed class OobChannelStore : IOobChannelStore
{
	private readonly ConcurrentDictionary<string, string> _channels = new();

	public event Action<string>? ChannelUpdated;

	public void Set(string package, string dataJson)
	{
		if (string.IsNullOrEmpty(package)) return;
		_channels[package] = dataJson;
		ChannelUpdated?.Invoke(package);
	}

	public string? Get(string package) =>
		_channels.TryGetValue(package, out var v) ? v : null;

	public IReadOnlyCollection<string> Packages => _channels.Keys.ToArray();
}
