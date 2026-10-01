using Microsoft.AspNetCore.Components;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// Where a section sidebar is, as the app's own routes spell it: the path relative to the base href
/// (so a portal served under <c>/portal/</c> still sees <c>/settings/theme</c>), without a trailing
/// slash, and the query string. <c>new Uri(Nav.Uri).AbsolutePath</c> keeps the base's prefix and
/// never matches a route under a non-root base.
/// </summary>
public readonly record struct SectionPath(string Path, string Query)
{
	public static SectionPath Of(NavigationManager nav)
	{
		var relative = "/" + nav.ToBaseRelativePath(nav.Uri);
		var hash = relative.IndexOf('#');
		if (hash >= 0) relative = relative[..hash];
		var q = relative.IndexOf('?');
		var path = (q >= 0 ? relative[..q] : relative).TrimEnd('/');
		return new SectionPath(path.Length == 0 ? "/" : path, q >= 0 ? relative[q..] : string.Empty);
	}
}
