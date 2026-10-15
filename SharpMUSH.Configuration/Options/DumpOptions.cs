namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: SharpConfig(
		Name = "purge_interval",
		Category = "Dump",
		Description = "Time interval for purging destroyed objects",
		Group = "Database Maintenance",
		Order = 1,
		Tooltip = "Format: time value (e.g., '1h', '30m')")]
	string PurgeInterval,

	[property: SharpConfig(
		Name = "dump_message",
		Category = "Dump",
		Description = "Shown to every connected player when a backup of the world starts (@backup, @dump or the backup schedule). Empty shows nothing",
		Group = "Database Maintenance",
		Order = 2)]
	string? DumpMessage,

	[property: SharpConfig(
		Name = "dump_complete",
		Category = "Dump",
		Description = "Shown to every connected player when a backup of the world is written. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 3)]
	string? DumpComplete,

	[property: SharpConfig(
		Name = "dump_warning_5min",
		Category = "Dump",
		Description = "Shown to every connected player five minutes before a scheduled backup. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 4)]
	string? DumpWarning5Min,

	[property: SharpConfig(
		Name = "dump_warning_1min",
		Category = "Dump",
		Description = "Shown to every connected player one minute before a scheduled backup. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 5)]
	string? DumpWarning1Min
);
