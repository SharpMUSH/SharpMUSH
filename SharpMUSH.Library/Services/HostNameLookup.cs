using System.Collections.Concurrent;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class HostNameLookup(
	IConnectionService connections,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	IHostNameResolver resolver) : IHostNameLookup
{
	/// <summary>Set on a connection once its address has been looked up, found or not.</summary>
	public const string LookedUpMetadata = "HostNameLookedUp";

	private readonly ConcurrentDictionary<long, Lazy<Task>> _running = new();

	/// <inheritdoc />
	public Task LookUpAsync(long handle)
	{
		if (!configuration.CurrentValue.Net.UseDns
				|| connections.Get(handle) is not { } connection
				|| connection.Metadata.ContainsKey(LookedUpMetadata)
				|| connection.HostName != connection.InternetProtocolAddress)
		{
			return Task.CompletedTask;
		}

		var address = connection.InternetProtocolAddress;
		return _running.GetOrAdd(handle, _ => new Lazy<Task>(() => RunAsync(handle, address))).Value;
	}

	private async Task RunAsync(long handle, string address)
	{
		try
		{
			var name = await resolver.ResolveAsync(address);
			if (connections.Get(handle) is { InternetProtocolAddress: var current } && current == address)
			{
				if (name is not null) connections.Update(handle, "HostName", name);
				connections.Update(handle, LookedUpMetadata, "1");
			}
		}
		finally
		{
			_running.TryRemove(handle, out _);
		}
	}
}
