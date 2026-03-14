namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: SharpConfig(
		Name = "purge_interval",
		Category = "Dump",
		Description = "Time interval for purging destroyed objects",
		Group = "Database Maintenance",
		Order = 1,
		Tooltip = "Format: time value (e.g., '1h', '30m')")]
	string PurgeInterval
);
