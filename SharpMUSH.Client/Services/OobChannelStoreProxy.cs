namespace SharpMUSH.Client.Services;

/// <summary>
/// A stable <see cref="IOobChannelStore"/> that forwards to a swappable inner store.
/// </summary>
/// <remarks>
/// Consumers capture the store object itself — <c>Play.razor</c> subscribes to
/// <c>PlayTerminal.OobChannels.ChannelUpdated</c> at init and holds that reference for its
/// lifetime. Handing out the inner store directly would leave those subscribers attached to a
/// dead object the moment the terminal is recreated, so the facade hands out this instead.
/// </remarks>
public sealed class OobChannelStoreProxy : IOobChannelStore
{
	private IOobChannelStore? _inner;

	public event Action<string>? ChannelUpdated;

	/// <summary>
	/// Swaps the backing store, re-pointing the forwarder without touching subscribers.
	/// </summary>
	/// <remarks>
	/// Drops the outgoing store's payloads first (while <see cref="Forward"/> is still wired to it),
	/// so every package it held raises <see cref="ChannelUpdated"/> one last time before the swap.
	/// Without this, a recreate (character switch) silently re-points at a fresh, empty store with no
	/// event at all — subscribers like <c>Play.razor</c>'s sidebar would keep rendering the PREVIOUS
	/// character's room contents/who-list until the new connection happened to push fresh OOB data.
	/// Reuses <see cref="IOobChannelStore.Clear"/> rather than inventing a second reset mechanism.
	/// </remarks>
	public void SetInner(IOobChannelStore inner)
	{
		if (_inner is not null)
		{
			_inner.Clear();
			_inner.ChannelUpdated -= Forward;
		}

		_inner = inner;
		_inner.ChannelUpdated += Forward;
	}

	private void Forward(string package) => ChannelUpdated?.Invoke(package);

	public void Set(string package, string dataJson) => _inner?.Set(package, dataJson);

	public string? Get(string package) => _inner?.Get(package);

	public IReadOnlyCollection<string> Packages => _inner?.Packages ?? [];

	public void Clear() => _inner?.Clear();
}
