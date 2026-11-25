using Microsoft.JSInterop;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>Whether the browser lets the page raise notifications.</summary>
public enum NotificationPermission
{
	/// <summary>Not asked yet.</summary>
	Default,
	Granted,
	Denied,

	/// <summary>The browser has no notifications (or none outside a service worker).</summary>
	Unsupported
}

/// <summary>
/// The sounds and browser notifications Play's highlight rules raise, through <c>js/terminal-alerts.js</c>.
/// </summary>
/// <remarks>
/// A burst of matching lines plays one sound, and leaves one notification per rule (the script's
/// <c>minGapMs</c>, and the rule's id as the notification's tag). A notification shows only while the tab is
/// not in front: in front, the line and its sound are already there.
/// </remarks>
public sealed class TerminalAlerts(IJSRuntime js)
{
	/// <summary>The longest notification text; a longer line is cut with an ellipsis.</summary>
	public const int MaxBodyLength = 180;

	/// <summary>Plays the rule's sound and raises its notification, as <paramref name="hit"/> says.</summary>
	/// <param name="who">The character the line came to, which titles the notification.</param>
	public async Task AlertAsync(HighlightHit hit, string text, string? who)
	{
		if (hit.Sound != HighlightSound.None)
			await CallAsync<bool>("SharpMUSH.Alerts.play", hit.Sound.ToString());
		if (hit.Notify)
			await CallAsync<bool>("SharpMUSH.Alerts.notify", string.IsNullOrWhiteSpace(who) ? "Play" : who, Body(text), $"sharpmush-hl-{hit.Rule.Id}");
	}

	/// <summary>Plays <paramref name="sound"/> now, for the rule editor.</summary>
	public Task PreviewAsync(HighlightSound sound) =>
		sound == HighlightSound.None ? Task.CompletedTask : CallAsync<bool>("SharpMUSH.Alerts.preview", sound.ToString());

	public async Task<NotificationPermission> PermissionAsync() =>
		Parse(await CallAsync<string?>("SharpMUSH.Alerts.permission"));

	/// <summary>Asks the browser for permission to notify. Run from a click, or the browser ignores it.</summary>
	public async Task<NotificationPermission> RequestPermissionAsync() =>
		Parse(await CallAsync<string?>("SharpMUSH.Alerts.requestPermission"));

	/// <summary>The line as a notification shows it: one line, cut at <see cref="MaxBodyLength"/>.</summary>
	public static string Body(string text)
	{
		var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		return flat.Length <= MaxBodyLength ? flat : flat[..(MaxBodyLength - 1)] + "…";
	}

	private static NotificationPermission Parse(string? permission) => permission switch
	{
		"granted" => NotificationPermission.Granted,
		"denied" => NotificationPermission.Denied,
		"default" => NotificationPermission.Default,
		_ => NotificationPermission.Unsupported
	};

	private async Task<T?> CallAsync<T>(string identifier, params object?[] args)
	{
		try
		{
			return await js.InvokeAsync<T>(identifier, args);
		}
		catch (JSException)
		{
			// A sound or a notification the browser would not give is left out; the line is still marked.
			return default;
		}
	}
}
