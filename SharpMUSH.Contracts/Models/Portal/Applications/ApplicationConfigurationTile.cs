namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>Discovery metadata only; configuration values remain behind the destination's authorization.</summary>
public sealed record ApplicationConfigurationTile(
	string Id, string Title, string Description, string? Icon, string Route, string Permission)
{
	/// <summary>Only canonical same-origin admin/application paths, without query strings or escapes.</summary>
	public bool HasSafeRoute() =>
		!string.IsNullOrWhiteSpace(Route) && (Route.StartsWith("/admin/", StringComparison.Ordinal) || Route.StartsWith("/apps/", StringComparison.Ordinal))
		&& Route.Split('/').Skip(1).All(segment => segment.Length > 0 && segment is not "." and not ".."
			&& segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
}
