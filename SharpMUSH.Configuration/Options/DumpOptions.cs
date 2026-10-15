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
		Name = "dump_interval",
		Category = "Dump",
		Description = "Time interval between the scheduled saves that raise the dump events, while compatibility_events is on. Set to 0 to turn them off",
		Group = "Database Maintenance",
		Order = 2,
		Tooltip = "Format: time value (e.g., '1h', '30m')")]
	string DumpInterval,

	[property: SharpConfig(
		Name = "dbck_interval",
		Category = "Dump",
		Description = "Time interval between the scheduled checks that raise db`dbck, while compatibility_events is on. Set to 0 to turn them off",
		Group = "Database Maintenance",
		Order = 3,
		Tooltip = "Format: time value (e.g., '10m')")]
	string DbckInterval,

	[property: SharpConfig(
		Name = "dump_message",
		Category = "Dump",
		Description = "Shown to every connected player when a save starts, while compatibility_events is on. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 4)]
	string? DumpMessage,

	[property: SharpConfig(
		Name = "dump_complete",
		Category = "Dump",
		Description = "Shown to every connected player when a save finishes, while compatibility_events is on. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 5)]
	string? DumpComplete,

	[property: SharpConfig(
		Name = "dump_warning_5min",
		Category = "Dump",
		Description = "Shown to every connected player five minutes before a scheduled save, while compatibility_events is on. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 6)]
	string? DumpWarning5Min,

	[property: SharpConfig(
		Name = "dump_warning_1min",
		Category = "Dump",
		Description = "Shown to every connected player one minute before a scheduled save, while compatibility_events is on. Empty shows nothing",
		Group = "Database Maintenance",
		Order = 7)]
	string? DumpWarning1Min
);
