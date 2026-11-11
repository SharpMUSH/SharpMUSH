using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Whether pictures take the theme's tone (its <c>imagery</c> part: Directive's greys, Horror's red, Mystery's noir) in
/// this browser. On by default; the player turns it off from the account panel or the Theme settings page to see
/// banners, covers and gallery pictures in their own colours under any theme. Kept per browser in
/// <c>localStorage</c>, like the screen reader choice, because it is about the screen rather than the character.
/// Off is <c>data-image-filter="off"</c> on <c>&lt;html&gt;</c>, which <c>css/themes/imagery.css</c> reads;
/// <c>index.html</c> sets it from the same key before the runtime starts, so the first render is already in it.
/// </summary>
public sealed class ThemeImageFilter(IJSRuntime js)
{
	/// <summary>The <c>localStorage</c> key, also read by <c>index.html</c>'s boot script.</summary>
	public const string StorageKey = "theme.image-filter";

	private Task? _load;

	/// <summary>Pictures take the theme's tone.</summary>
	public bool On { get; private set; } = true;

	/// <summary>Raised when <see cref="On"/> changes, after it has.</summary>
	public event Action? Changed;

	/// <summary>Reads the kept choice once; later calls return the same task.</summary>
	public Task LoadAsync() => _load ??= LoadCoreAsync();

	public async Task SetAsync(bool on)
	{
		await LoadAsync();
		if (on == On) return;
		On = on;
		if (on) await js.RemoveItemAsync(BrowserStore.Local, StorageKey);
		else await js.SetItemAsync(BrowserStore.Local, StorageKey, "off");
		await ApplyAsync();
		Changed?.Invoke();
	}

	private async Task LoadCoreAsync()
	{
		var on = await js.GetItemAsync(BrowserStore.Local, StorageKey) != "off";
		if (on == On) return;
		On = on;
		Changed?.Invoke();
	}

	private async Task ApplyAsync()
	{
		try
		{
			await js.InvokeVoidAsync("sharpmushLayout.setImageFilter", On);
		}
		catch (JSException)
		{
			// No layout script: the choice is kept and index.html applies it on the next load.
		}
	}
}
