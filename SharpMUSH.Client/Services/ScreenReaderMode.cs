using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Whether the player uses a screen reader in this browser, which no browser can detect: they say so in
/// Play's settings menu. Kept per browser in <c>localStorage</c>, since a screen reader belongs to the device rather than
/// the character. On, Play's single-letter exit keys are off (a letter typed to a screen reader must never walk
/// the character out of the room), and each terminal tells the game it is a screen reader, the way an MTTS
/// client does, so <c>terminfo()</c> says <c>screenreader</c> and softcode can leave out what only draws.
/// </summary>
public sealed class ScreenReaderMode(IJSRuntime js)
{
	private const string StorageKey = "a11y.screenreader";

	private Task? _load;

	/// <summary>The player said they use a screen reader here.</summary>
	public bool On { get; private set; }

	/// <summary>Raised when <see cref="On"/> changes, after it has.</summary>
	public event Action? Changed;

	/// <summary>Reads the kept choice once; later calls return the same task.</summary>
	public Task LoadAsync() => _load ??= LoadCoreAsync();

	public async Task SetAsync(bool on)
	{
		await LoadAsync();
		if (on == On) return;
		On = on;
		if (on) await js.SetItemAsync(BrowserStore.Local, StorageKey, "on");
		else await js.RemoveItemAsync(BrowserStore.Local, StorageKey);
		Changed?.Invoke();
	}

	private async Task LoadCoreAsync()
	{
		var on = await js.GetItemAsync(BrowserStore.Local, StorageKey) == "on";
		if (on == On) return;
		On = on;
		Changed?.Invoke();
	}
}
