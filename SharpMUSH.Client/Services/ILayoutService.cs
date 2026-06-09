using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Manages the portal layout configuration (which widgets live in which zones).
/// Persists to and restores from browser localStorage.
/// </summary>
public interface ILayoutService
{
	/// <summary>
	/// Raised when the layout changes so components can re-render.
	/// </summary>
	event Action? OnLayoutChanged;

	/// <summary>
	/// Loads the layout from localStorage; returns the default layout when nothing is stored.
	/// </summary>
	Task<LayoutConfiguration> GetLayoutAsync();

	/// <summary>
	/// Persists the layout to localStorage and fires <see cref="OnLayoutChanged"/>.
	/// </summary>
	Task SaveLayoutAsync(LayoutConfiguration layout);

	/// <summary>Returns the built-in default layout.</summary>
	LayoutConfiguration GetDefaultLayout();
}
