using SharpMUSH.Client.Models;

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

	public event Action? RoomChanged;

	public event Action<OobFrame>? FrameReceived;

	public event Action<OobSessionSignal>? SessionSignaled;

	/// <summary>
	/// Swaps the backing store, re-pointing the forwarder without touching subscribers.
	/// </summary>
	/// <remarks>
	/// Drops the outgoing store's payloads first (while <see cref="Forward"/> is still wired to it),
	/// so every package it held raises <see cref="ChannelUpdated"/> one last time before the swap.
	/// Without this, a recreate (character switch) silently re-points at a fresh, empty store with no
	/// event at all — subscribers like <c>Play.razor</c>'s sidebar would keep rendering the PREVIOUS
	/// character's room contents/who-list until the new connection happened to push fresh OOB data.
	/// Reuses <see cref="IOobChannelStore.Clear"/> rather than inventing a second reset mechanism; the
	/// same clear empties the typed <see cref="Room"/> and raises <see cref="RoomChanged"/> once.
	/// </remarks>
	public void SetInner(IOobChannelStore inner)
	{
		_inner?.Clear();
		Point(inner);
	}

	/// <summary>
	/// Points at <paramref name="inner"/>, another live store, leaving the outgoing one as it is: the play terminal of
	/// the character the tab acts as (<see cref="FocusedPlayTerminal"/>) moves to another character's, whose
	/// connection and payloads stay. Every package either store holds raises <see cref="ChannelUpdated"/>, and
	/// <see cref="RoomChanged"/> is raised, so whatever reads through this one reads again.
	/// </summary>
	public void Follow(IOobChannelStore inner)
	{
		var before = Packages.ToList();
		Point(inner);
		foreach (var package in before.Union(inner.Packages, StringComparer.Ordinal)) ChannelUpdated?.Invoke(package);
		RoomChanged?.Invoke();
	}

	private void Point(IOobChannelStore inner)
	{
		if (_inner is not null)
		{
			_inner.ChannelUpdated -= Forward;
			_inner.RoomChanged -= ForwardRoom;
			_inner.FrameReceived -= ForwardFrame;
			_inner.SessionSignaled -= ForwardSignal;
		}

		_inner = inner;
		_inner.ChannelUpdated += Forward;
		_inner.RoomChanged += ForwardRoom;
		_inner.FrameReceived += ForwardFrame;
		_inner.SessionSignaled += ForwardSignal;
	}

	private void Forward(string package) => ChannelUpdated?.Invoke(package);

	private void ForwardRoom() => RoomChanged?.Invoke();

	private void ForwardFrame(OobFrame frame) => FrameReceived?.Invoke(frame);

	private void ForwardSignal(OobSessionSignal signal) => SessionSignaled?.Invoke(signal);

	public void Set(string package, string dataJson) => _inner?.Set(package, dataJson);

	public void Receive(OobFrame frame) => _inner?.Receive(frame);

	public void Signal(OobSessionSignal signal) => _inner?.Signal(signal);

	public string? Get(string package) => _inner?.Get(package);

	public IReadOnlyCollection<string> Packages => _inner?.Packages ?? [];

	public RoomState Room => _inner?.Room ?? RoomState.Empty;

	public void Clear() => _inner?.Clear();
}
