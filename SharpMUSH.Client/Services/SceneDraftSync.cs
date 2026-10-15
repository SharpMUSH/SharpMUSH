using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Keeps the live scene page's unsent pose in two places: this browser (so a reload or a lost connection loses
/// nothing) and the server (so the character's other devices open the same draft). One instance belongs to one
/// page; <see cref="Key"/> names the character and the scene.
/// </summary>
/// <remarks>
/// <para>The browser's copy records the server time it last agreed with (<see cref="Kept.SyncedAt"/>) and whether
/// it was written since (<see cref="Kept.Dirty"/>). Times are compared only with times the server gave, so a device
/// whose clock is wrong still syncs.</para>
/// <para>A pose sent from one device empties the draft, and the server keeps that empty draft with its time: the
/// copy another device still holds is older, so that device empties too instead of putting the sent pose back.</para>
/// </remarks>
public sealed class SceneDraftSync(SceneService scenes, IJSRuntime js) : IDisposable
{
	/// <summary>A draft as this browser keeps it.</summary>
	/// <param name="SyncedAt">The server time of the copy this one last agreed with; 0 when it never has.</param>
	/// <param name="Dirty">Written here since then, and not yet on the server.</param>
	public sealed record Kept(string Mode, string Text, string? Markup, long SyncedAt, bool Dirty);

	/// <summary>Where the draft is, for the line beside Send.</summary>
	public enum SyncState
	{
		/// <summary>Nothing to keep, or not read yet.</summary>
		Idle,

		/// <summary>Written here; on its way to the server.</summary>
		Saving,

		/// <summary>The server holds this draft: the character's other devices open it.</summary>
		Saved,

		/// <summary>The server could not take it; it is kept in this browser and tried again.</summary>
		DeviceOnly,
	}

	/// <summary>How long typing pauses before the draft goes to the server.</summary>
	public static readonly TimeSpan PushDelay = TimeSpan.FromMilliseconds(800);

	/// <summary>The browser storage key: the character and the scene.</summary>
	public string Key { get; private set; } = "";

	private string _sceneId = "";
	private Kept? _kept;
	private CancellationTokenSource? _pending;
	private bool _pushing;
	private bool _again;
	private bool _serverRefused;

	/// <summary>Where the draft is now.</summary>
	public SyncState State { get; private set; } = SyncState.Idle;

	/// <summary>Raised when <see cref="State"/> changes.</summary>
	public event Action? StateChanged;

	/// <summary>
	/// What to show, given this browser's copy and the server's, and whether this browser's copy has to go to the
	/// server. The server's wins when it was written after the copy here last agreed with it: another device wrote
	/// since. Otherwise the copy here is as new or newer, and goes up when it was written since.
	/// </summary>
	public static (Kept? Show, bool Push) Choose(Kept? local, SceneDraftCopy? server)
	{
		if (server is null) return (local, local?.Dirty == true);
		var fromServer = new Kept(server.Mode, server.Text, server.Markup, server.UpdatedAt, false);
		if (local is null || server.UpdatedAt > local.SyncedAt) return (fromServer, false);
		return (local, local.Dirty);
	}

	/// <summary>
	/// Reads the draft for <paramref name="sceneId"/> kept under <paramref name="key"/>: this browser's copy and the
	/// server's, whichever <see cref="Choose"/> picks. Null when there is none.
	/// </summary>
	public async Task<Kept?> LoadAsync(string key, string sceneId)
	{
		CancelPending();
		Key = key;
		_sceneId = sceneId;
		_serverRefused = false;
		var local = await ReadLocalAsync(key);
		var server = await scenes.GetDraftAsync(sceneId) switch
		{
			SceneDraftCopy copy => copy,
			ApiFailure failure => Unavailable(failure),
		};
		var (show, push) = Choose(local, server);
		_kept = show;
		if (show is not null && show != local) await WriteLocalAsync(show);
		SetState(server is not null ? Settled(show) : show is { Dirty: true } && !_serverRefused ? SyncState.DeviceOnly : SyncState.Idle);
		if (push) await PushAsync();
		return show;
	}

	/// <summary>
	/// Keeps what the field holds now: in this browser straight away, on the server once typing pauses for
	/// <see cref="PushDelay"/>, or at once when <paramref name="now"/> (a pose just sent).
	/// </summary>
	public async Task KeepAsync(string mode, string text, string? markup, bool now = false)
	{
		if (Key.Length == 0) return;
		var kept = new Kept(mode, text, markup, _kept?.SyncedAt ?? 0, true);
		var unchanged = _kept is { } same && same with { Dirty = true } == kept;
		if (unchanged && (!now || _kept is { Dirty: false })) return;
		if (!unchanged)
		{
			_kept = kept;
			await WriteLocalAsync(kept);
		}
		CancelPending();
		if (_serverRefused)
		{
			// A guest, or a scene this character may not see: the copy here is all there is, and nothing to report.
			SetState(SyncState.Idle);
			return;
		}
		SetState(SyncState.Saving);
		if (now)
		{
			await PushAsync();
			return;
		}
		var pending = _pending = new CancellationTokenSource();
		try
		{
			await Task.Delay(PushDelay, pending.Token);
		}
		catch (TaskCanceledException)
		{
			return;
		}
		await PushAsync();
	}

	/// <summary>
	/// The page came back into view: a draft written here goes up, and otherwise one another device wrote since is
	/// read. Returns that newer draft (empty when it was sent from there), or null when nothing changed.
	/// </summary>
	public async Task<Kept?> RefreshAsync()
	{
		if (Key.Length == 0 || _serverRefused) return null;
		if (_kept is { Dirty: true })
		{
			await PushAsync();
			return null;
		}
		if (await scenes.GetDraftAsync(_sceneId) is not SceneDraftCopy server) return null;
		var (show, _) = Choose(_kept, server);
		if (show is null || show == _kept) return null;
		_kept = show;
		await WriteLocalAsync(show);
		SetState(Settled(show));
		return show;
	}

	private async Task PushAsync()
	{
		if (_pushing)
		{
			_again = true;
			return;
		}
		_pushing = true;
		try
		{
			do
			{
				_again = false;
				if (_kept is not { Dirty: true } sending) return;
				var key = Key;
				switch (await scenes.SaveDraftAsync(_sceneId, sending.Mode, sending.Text, sending.Markup))
				{
					case SceneDraftCopy saved when key == Key:
						// Typed on while it was on its way: what went up is still the copy the server agrees with.
						_kept = _kept == sending ? sending with { SyncedAt = saved.UpdatedAt, Dirty = false } : _kept with { SyncedAt = saved.UpdatedAt };
						await WriteLocalAsync(_kept);
						SetState(_kept.Dirty ? SyncState.Saving : Settled(_kept));
						if (_kept.Dirty) _again = true;
						break;
					case SceneDraftCopy:
						return;
					case ApiFailure failure:
						Unavailable(failure);
						SetState(_serverRefused ? SyncState.Idle : SyncState.DeviceOnly);
						return;
				}
			}
			while (_again);
		}
		finally
		{
			_pushing = false;
		}
	}

	/// <summary>
	/// The server did not answer. A refusal (a guest, a scene this character may not see) stops asking until the
	/// next scene; anything else is tried again on the next change or when the page comes back into view.
	/// </summary>
	private SceneDraftCopy? Unavailable(ApiFailure failure)
	{
		if (failure.Kind is ApiFailureKind.Unauthenticated or ApiFailureKind.Forbidden or ApiFailureKind.NotFound)
			_serverRefused = true;
		return null;
	}

	private static SyncState Settled(Kept? kept) => kept is { Text.Length: > 0 } ? SyncState.Saved : SyncState.Idle;

	private void SetState(SyncState state)
	{
		if (state == State) return;
		State = state;
		StateChanged?.Invoke();
	}

	private void CancelPending()
	{
		_pending?.Cancel();
		_pending = null;
	}

	private async Task<Kept?> ReadLocalAsync(string key)
	{
		if (await js.GetItemAsync(BrowserStore.Local, key) is not { Length: > 0 } json) return null;
		try
		{
			return JsonSerializer.Deserialize<Kept>(json) is { } kept ? kept with { Text = kept.Text ?? "", Mode = kept.Mode ?? "" } : null;
		}
		catch (JsonException)
		{
			// Not a draft this page wrote: start empty.
			return null;
		}
	}

	/// <summary>An empty draft the server already holds needs no copy here; any other is kept.</summary>
	private async Task WriteLocalAsync(Kept kept)
	{
		if (kept is { Text.Length: 0, Dirty: false })
		{
			await js.RemoveItemAsync(BrowserStore.Local, Key);
			return;
		}
		await js.SetItemAsync(BrowserStore.Local, Key, JsonSerializer.Serialize(kept));
	}

	public void Dispose() => CancelPending();
}
