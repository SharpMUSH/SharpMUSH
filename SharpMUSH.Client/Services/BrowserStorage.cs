using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>The two Web Storage areas a page can reach.</summary>
public enum BrowserStore
{
	/// <summary><c>localStorage</c>: per origin, survives the tab closing.</summary>
	Local,

	/// <summary><c>sessionStorage</c>: per tab, gone when the tab closes.</summary>
	Session,
}

/// <summary>
/// Web Storage through JS interop, with an unusable store read as an empty one.
/// </summary>
/// <remarks>
/// A browser can refuse storage outright — site data blocked, a sandboxed frame, a full quota — and
/// the call then throws a <see cref="JSException"/>. Nothing the portal keeps there is authoritative:
/// the account session, the locale, the theme and the terminal width all have a default the portal
/// already handles. So a read that fails is a key that is absent, and a write that fails leaves the
/// value in memory for this page's lifetime and reports <see langword="false"/>.
/// </remarks>
public static class BrowserStorage
{
	public static async ValueTask<string?> GetItemAsync(this IJSRuntime js, BrowserStore store, string key)
	{
		try
		{
			return await js.InvokeAsync<string?>($"{Area(store)}.getItem", key);
		}
		catch (JSException)
		{
			return null;
		}
	}

	public static async ValueTask<bool> SetItemAsync(this IJSRuntime js, BrowserStore store, string key, string value)
	{
		try
		{
			await js.InvokeVoidAsync($"{Area(store)}.setItem", key, value);
			return true;
		}
		catch (JSException)
		{
			return false;
		}
	}

	public static async ValueTask<bool> RemoveItemAsync(this IJSRuntime js, BrowserStore store, string key)
	{
		try
		{
			await js.InvokeVoidAsync($"{Area(store)}.removeItem", key);
			return true;
		}
		catch (JSException)
		{
			return false;
		}
	}

	private static string Area(BrowserStore store) => store switch
	{
		BrowserStore.Local => "localStorage",
		BrowserStore.Session => "sessionStorage",
		_ => throw new ArgumentOutOfRangeException(nameof(store), store, null),
	};
}
