using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Storage for admin-customized portal layouts. A <em>scope</em> is a named layout — for example
/// <c>"global"</c> (the chrome zones), <c>"home"</c>, <c>"wiki-index"</c>, or <c>"profile"</c> — and
/// each scope owns one <see cref="LayoutConfiguration"/>. Implemented by every database provider;
/// system data, never visible to softcode, travels with backups.
/// </summary>
/// <remarks>
/// The whole <see cref="LayoutConfiguration"/> is persisted as a single JSON blob keyed by scope, so
/// no provider has to model the nested zone/placement structure. A missing record means the scope has
/// never been customized; callers fall back to a code-supplied default.
///
/// Single-fetch methods return <c>OneOf&lt;T, NotFound&gt;</c> rather than null, matching
/// <see cref="IApplicationRegistryService"/> conventions.
/// </remarks>
public interface ILayoutRegistryService
{
	/// <summary>Creates or replaces the stored layout for a scope.</summary>
	Task UpsertLayoutAsync(string scope, LayoutConfiguration layout);

	/// <summary>Fetches the stored layout for a scope, or <see cref="NotFound"/> if it was never customized.</summary>
	Task<OneOf<LayoutConfiguration, NotFound>> GetLayoutAsync(string scope);

	/// <summary>Lists the scopes that have a stored (customized) layout, sorted by scope name.</summary>
	Task<IReadOnlyList<string>> GetCustomizedScopesAsync();

	/// <summary>Removes the stored layout for a scope, reverting it to its code default. Does not error if absent.</summary>
	Task RemoveLayoutAsync(string scope);
}
