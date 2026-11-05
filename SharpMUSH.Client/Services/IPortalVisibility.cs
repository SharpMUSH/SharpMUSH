namespace SharpMUSH.Client.Services;

/// <summary>Whether the portal's tab is in view, so a page refreshes its data only while someone can see it.</summary>
public interface IPortalVisibility
{
	/// <summary>The tab is in view, as last reported for <c>@push</c>; before any report, it counts as in view.</summary>
	bool IsVisible { get; }
}
