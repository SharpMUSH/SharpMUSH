using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Remembers, per section, whether its page sidebar is collapsed to the 62px strip (README §3, Q3:
/// per section, in localStorage like the theme preset). Collapsing the wiki's sidebar leaves the
/// configuration's alone. Storage failures are silent: a browser that refuses storage keeps the
/// choice for the tab's lifetime.
/// </summary>
public sealed class SidebarCollapseService(IJSRuntime js)
{
	private const string KeyPrefix = "sharpmush.sidebar.";

	private readonly Dictionary<string, bool> _known = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Raised with the section key after its state changes.</summary>
	public event Action<string>? Changed;

	/// <summary>Whether <paramref name="section"/> is collapsed; false until remembered otherwise.</summary>
	public async Task<bool> IsCollapsedAsync(string section)
	{
		if (_known.TryGetValue(section, out var known))
		{
			return known;
		}

		var stored = await js.GetItemAsync(BrowserStore.Local, KeyPrefix + section);
		var collapsed = string.Equals(stored, "1", StringComparison.Ordinal);
		_known[section] = collapsed;
		return collapsed;
	}

	/// <summary>Sets and remembers <paramref name="section"/>'s state.</summary>
	public async Task SetCollapsedAsync(string section, bool collapsed)
	{
		_known[section] = collapsed;
		await js.SetItemAsync(BrowserStore.Local, KeyPrefix + section, collapsed ? "1" : "0");
		Changed?.Invoke(section);
	}
}
