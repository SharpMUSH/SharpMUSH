namespace SharpMUSH.Configuration.Options;

/// <summary>
/// Configuration options for banned player names.
/// These names cannot be used when creating new player characters.
/// </summary>
public record BannedNamesOptions(
	[property: SharpConfig(
		Name = "banned_names",
		Description = "List of player names that are prohibited and cannot be created",
		Category = "Banned Names")]
	string[] BannedNames
);

/// <summary>
/// Configuration options for sitelock rules.
/// Sitelocks control which sites can connect, create players, or use guests.
/// </summary>
public record SitelockRulesOptions(
	[property: SharpConfig(
		Name = "sitelock_rules",
		Description = "Sitelock rules mapping host patterns to access options (e.g., !connect, !create, !guest, register, suspect)",
		Category = "Sitelock Rules")]
	Dictionary<string, string[]> Rules
);
