namespace SharpMUSH.Configuration.Options;

/// <summary>
/// Configuration options for banned player names.
/// These names cannot be used when creating new player characters.
/// </summary>
public record BannedNamesOptions(
	[property: SharpConfig(
		Name = "banned_names",
		Category = "BannedNames",
		Description = "List of player names that are prohibited and cannot be created",
		Group = "Name Restrictions",
		Order = 1,
		Tooltip = "Players cannot create characters with these names")]
	string[] BannedNames
);

/// <summary>
/// Configuration options for sitelock rules.
/// Sitelocks control which sites can connect, create players, or use guests.
/// </summary>
public record SitelockRulesOptions(
	[property: SharpConfig(
		Name = "sitelock_rules",
		Category = "SitelockRules",
		Description = "Sitelock rules mapping host patterns to access options (e.g., !connect, !create, !guest, register, suspect)",
		Group = "Access Control",
		Order = 1,
		Tooltip = "Format: host pattern → access rules array")]
	Dictionary<string, string[]> Rules
);
