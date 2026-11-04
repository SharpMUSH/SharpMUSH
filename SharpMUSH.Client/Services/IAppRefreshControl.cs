namespace SharpMUSH.Client.Services;

/// <summary>
/// The pages that have an application open, as the game connection knows them, so softcode's
/// <c>portalrefresh()</c> can ask them to fetch their data again. An app is <c>&lt;slug&gt;</c>, or
/// <c>&lt;slug&gt;:&lt;objid&gt;</c> for the copy showing one character. Nothing but the app rides on a refresh:
/// the page refetches through its own data route.
/// </summary>
public interface IAppRefreshControl
{
	/// <summary>Raised with the app the server asked to refresh.</summary>
	event Action<string>? OnAppRefresh;

	/// <summary>Whether the tab is in view, as last reported for <c>@push</c>.</summary>
	bool IsVisible { get; }

	/// <summary>
	/// Joins an app's group. Several pages may join one app; it is left when the last of them leaves, and
	/// joined again after a reconnect. Best-effort: a failed join leaves the page on its own timer.
	/// </summary>
	Task JoinAppAsync(string app);

	/// <summary>Leaves an app's group once no page holds it; see <see cref="JoinAppAsync"/>.</summary>
	Task LeaveAppAsync(string app);
}
