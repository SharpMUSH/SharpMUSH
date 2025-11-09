namespace SharpMUSH.Configuration.Options;

/// <summary>
/// Configuration options for sitelock rules and banned player names.
/// Sitelocks control which sites can connect, create players, or use guests.
/// </summary>
public record SitelockOptions(
	[property: SharpConfig(
		Name = "banned_names",
		Description = "List of banned player names that cannot be created",
		Category = "Sitelock")]
	string[] BannedNames,

	[property: SharpConfig(
		Name = "rules",
		Description = "Sitelock rules mapping host patterns to access options (e.g., !connect, !create, !guest, register, suspect)",
		Category = "Sitelock")]
	Dictionary<string, string[]> Rules
);
