namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Tells the portal pages that have an application open to fetch its data again. The message carries no
/// data: each page refetches through its own data route, so every permission check stays where it was.
/// </summary>
public interface IPortalApplicationNotifier
{
	/// <summary>
	/// Asks every open copy of <paramref name="slug"/> to refetch, or only the copies showing
	/// <paramref name="objid"/> when one is named. Several asks for one copy within a second become one refetch.
	/// </summary>
	Task RefreshAsync(string slug, string? objid);
}
