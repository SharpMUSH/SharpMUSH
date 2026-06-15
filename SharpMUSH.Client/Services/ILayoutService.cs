using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Loads and saves admin-customized portal layouts, keyed by <em>scope</em> (a named layout such as
/// <c>"global"</c>, <c>"home"</c>, <c>"wiki-index"</c>, or <c>"profile"</c>). Layouts are stored
/// server-side (DB-backed) and shared across all visitors; a scope that has never been customized
/// resolves to a code-supplied default.
/// </summary>
public interface ILayoutService
{
	/// <summary>Raised (with the changed scope) after a successful save/reset so components can re-render.</summary>
	event Action<string>? OnLayoutChanged;

	/// <summary>Loads the stored layout for a scope; returns the scope default when nothing is stored or on error.</summary>
	Task<LayoutConfiguration> GetLayoutAsync(string scope);

	/// <summary>Persists a layout for a scope (requires layout.admin). Returns true on success.</summary>
	Task<bool> SaveLayoutAsync(string scope, LayoutConfiguration layout);

	/// <summary>Resets a scope to its code default by removing the stored layout (requires layout.admin).</summary>
	Task<bool> ResetLayoutAsync(string scope);

	/// <summary>Returns the built-in default layout for a scope.</summary>
	LayoutConfiguration GetDefaultLayout(string scope);

	/// <summary>Lists the scopes that currently have a stored (customized) layout. Empty on error.</summary>
	Task<IReadOnlyList<string>> GetCustomizedScopesAsync();
}
