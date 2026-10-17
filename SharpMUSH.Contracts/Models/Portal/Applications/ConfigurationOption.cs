namespace SharpMUSH.Library.Models.Portal.Applications;

/// <summary>Shared field metadata. Never contains configured values, defaults or credential state.</summary>
public sealed record ConfigurationOption(
	string Key, string Label, string Description, IReadOnlyList<string>? Aliases = null,
	string? Permission = null, string? Route = null, bool RequiresRestart = false,
	string? ReadOnlyReason = null, string? LabelResourceKey = null, string? DescriptionResourceKey = null)
{
	public string Href(string defaultRoute) => (Route ?? defaultRoute) + "#config-option-" + Uri.EscapeDataString(Key);

	public bool IsValid() => ValidKey(Key) && !string.IsNullOrWhiteSpace(Label) && Label.Length <= 256
		&& Description is { Length: <= 4096 } && (Permission is null || !string.IsNullOrWhiteSpace(Permission) && Permission.Length <= 128)
		&& (Route is null || ConfigurationOptionRoutes.IsSafe(Route))
		&& (Aliases is null || Aliases.Count <= 16 && Aliases.All(alias => !string.IsNullOrWhiteSpace(alias) && alias.Length <= 128))
		&& (ReadOnlyReason is null || ReadOnlyReason.Length <= 4096)
		&& (LabelResourceKey is null || ValidKey(LabelResourceKey))
		&& (DescriptionResourceKey is null || ValidKey(DescriptionResourceKey));

	public bool Equals(ConfigurationOption? other) => other is not null
		&& Key == other.Key && Label == other.Label && Description == other.Description
		&& Permission == other.Permission && Route == other.Route && RequiresRestart == other.RequiresRestart
		&& ReadOnlyReason == other.ReadOnlyReason && LabelResourceKey == other.LabelResourceKey
		&& DescriptionResourceKey == other.DescriptionResourceKey && (Aliases ?? []).SequenceEqual(other.Aliases ?? []);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Key); hash.Add(Label); hash.Add(Description); hash.Add(Permission); hash.Add(Route);
		hash.Add(RequiresRestart); hash.Add(ReadOnlyReason); hash.Add(LabelResourceKey); hash.Add(DescriptionResourceKey);
		foreach (var alias in Aliases ?? []) hash.Add(alias);
		return hash.ToHashCode();
	}

	public static bool ValidKey(string? key) => key is { Length: > 0 and <= 128 }
		&& key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

/// <summary>Authorized option discovery; selecting it navigates, never executes a command.</summary>
public sealed record ConfigurationSearchEntry(
	string Id, string Source, string Section, string Route, ConfigurationOption Option)
{
	public string Href => Option.Href(Route);
}

/// <summary>Canonical destinations shared by the schema projection and configuration navigation.</summary>
public static class ConfigurationOptionRoutes
{
	public static string ForCategory(string category) => "/admin/config/" + (category switch
	{
		"Restriction" => "restrictions",
		"SitelockRules" => "sitelock",
		_ => category.ToLowerInvariant()
	});

	public static bool IsSafe(string? route) => !string.IsNullOrWhiteSpace(route)
		&& (route.StartsWith("/admin/", StringComparison.Ordinal) || route.StartsWith("/apps/", StringComparison.Ordinal))
		&& route.Split('/').Skip(1).All(segment => segment.Length > 0 && segment is not "." and not ".."
			&& segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'));
}
