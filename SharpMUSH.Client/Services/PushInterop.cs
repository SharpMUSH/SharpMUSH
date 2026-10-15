using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SharpMUSH.Client.Services;

/// <summary>What this browser can do about notifications and installing the portal (<c>SharpMUSH.Push.state</c>).</summary>
/// <param name="Supported">Whether it has service workers, push and notifications at all.</param>
/// <param name="Permission"><c>default</c>, <c>granted</c>, <c>denied</c> or <c>unsupported</c>.</param>
/// <param name="Endpoint">This browser's push endpoint when it is subscribed, else null.</param>
/// <param name="Standalone">Whether the portal is running as an installed app.</param>
/// <param name="Ios">Whether this is an iPhone or iPad, where push needs the portal added to the home screen.</param>
/// <param name="CanInstall">Whether the browser offered its install prompt.</param>
public sealed record PushBrowserState(bool Supported, string Permission, string? Endpoint, bool Standalone, bool Ios, bool CanInstall)
{
	public static readonly PushBrowserState Unsupported = new(false, "unsupported", null, false, false, false);
}

/// <summary>What <c>SharpMUSH.Push.subscribe</c> resolved to: the subscription's JSON, or why there is none.</summary>
public sealed record PushSubscribeResult(string? Subscription, string? Error);

/// <summary>
/// The page side of web push (<c>wwwroot/js/push.js</c>): the service worker, subscribing this browser, the install
/// prompt, and two things the portal reports as it runs: whether the tab is in view (to the game hub, so
/// <c>@push</c> leaves a player at the screen alone) and which character it plays (to the service worker, so a
/// notification for that character focuses this tab). A tab opened from a notification carries the character in
/// its <c>as</c> query and switches to it once signed in.
/// </summary>
public sealed class PushInterop(IJSRuntime js, ConnectionStateService connection, AccountAuthService auth,
	NavigationManager navigation) : IDisposable
{
	private DotNetObjectReference<PushInterop>? _self;
	private string? _pendingCharacter;

	/// <summary>Starts watching. Called once at startup; nothing waits for it.</summary>
	public async Task StartAsync()
	{
		_pendingCharacter = new Uri(navigation.Uri).Query.TrimStart('?').Split('&')
			.Select(pair => pair.Split('=', 2))
			.Where(pair => pair is ["as", { Length: > 0 }])
			.Select(pair => Uri.UnescapeDataString(pair[1]))
			.FirstOrDefault();

		try
		{
			_self = DotNetObjectReference.Create(this);
			await js.InvokeVoidAsync("SharpMUSH.Push.watch", _self);
		}
		catch (JSException)
		{
			// push.js did not load: no visibility reports, which only means @push treats the tab as out of view.
		}

		auth.ActiveCharacterChanged += OnCharacterChanged;
		auth.AuthStateChanged += OnCharacterChanged;
		await auth.InitAsync();
		OnCharacterChanged();
	}

	/// <summary>The tab was shown or hidden.</summary>
	[JSInvokable]
	public Task VisibilityChanged(bool visible) => connection.ReportVisibilityAsync(visible);

	/// <summary>A notification for this tab's character was clicked: show its page here.</summary>
	[JSInvokable]
	public void Open(string path)
	{
		if (path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal))
		{
			navigation.NavigateTo(path);
		}
	}

	public async Task<PushBrowserState> StateAsync()
	{
		try
		{
			return await js.InvokeAsync<PushBrowserState>("SharpMUSH.Push.state");
		}
		catch (JSException)
		{
			return PushBrowserState.Unsupported;
		}
	}

	public async Task<PushSubscribeResult> SubscribeAsync(string publicKey)
	{
		try
		{
			return await js.InvokeAsync<PushSubscribeResult>("SharpMUSH.Push.subscribe", publicKey);
		}
		catch (JSException ex)
		{
			return new PushSubscribeResult(null, ex.Message);
		}
	}

	/// <summary>Unsubscribes this browser; the endpoint it had, or null.</summary>
	public async Task<string?> UnsubscribeAsync()
	{
		try
		{
			return await js.InvokeAsync<string?>("SharpMUSH.Push.unsubscribe");
		}
		catch (JSException)
		{
			return null;
		}
	}

	/// <summary>Shows the browser's install prompt; true when the player installed the portal.</summary>
	public async Task<bool> InstallAsync()
	{
		try
		{
			return await js.InvokeAsync<bool>("SharpMUSH.Push.install");
		}
		catch (JSException)
		{
			return false;
		}
	}

	private async void OnCharacterChanged()
	{
		try
		{
			var active = auth.ActiveCharacter is { } character ? $"#{character.DbrefNumber}:{character.CreationTime}" : null;
			await js.InvokeVoidAsync("SharpMUSH.Push.setCharacter", active);

			if (_pendingCharacter is not { } wanted || !auth.IsLoggedIn || auth.Characters.Count == 0)
			{
				return;
			}

			_pendingCharacter = null;
			navigation.NavigateTo(navigation.GetUriWithQueryParameter("as", (string?)null), replace: true);
			if (wanted != active
				&& auth.Characters.FirstOrDefault(c => $"#{c.DbrefNumber}:{c.CreationTime}" == wanted) is { } target)
			{
				await auth.SwitchCharacterAsync(target);
			}
		}
		catch (JSException)
		{
			// push.js is missing or failed: a notification click opens a new tab instead of focusing this one.
		}
		catch (InvalidOperationException)
		{
			// The JS runtime is not ready during prerender-like test hosts; nothing to report.
		}
	}

	public void Dispose()
	{
		auth.ActiveCharacterChanged -= OnCharacterChanged;
		auth.AuthStateChanged -= OnCharacterChanged;
		_self?.Dispose();
	}
}
