namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: PennConfig(Name = "forking_dump", Description = "Use a separate process for database dumps to avoid blocking", DefaultValue = "yes")] bool ForkingDump,
	[property: PennConfig(Name = "dump_message", Description = "Message displayed when database dump begins", DefaultValue = "Saving Database. Game may freeze for a moment.")] string? DumpMessage,
	[property: PennConfig(Name = "dump_complete", Description = "Message displayed when database dump completes", DefaultValue = "Save complete.")] string? DumpComplete,
	[property: PennConfig(Name = "dump_warning_1min", Description = "Warning message shown 1 minute before dump", DefaultValue = "Database Save in 1 minute.")] string? DumpWarning1Min,
	[property: PennConfig(Name = "dump_warning_5min", Description = "Warning message shown 5 minutes before dump", DefaultValue = "Database Save in 5 minutes.")] string? DumpWarning5Min,
	[property: PennConfig(Name = "dump_interval", Description = "Time interval between automatic database dumps", DefaultValue = "3600")] string DumpInterval,
	[property: PennConfig(Name = "warn_interval", Description = "Time interval for dump warning messages", DefaultValue = "3540")] string WarningInterval,
	[property: PennConfig(Name = "purge_interval", Description = "Time interval for purging destroyed objects", DefaultValue = "600")] string PurgeInterval,
	[property: PennConfig(Name = "dbck_interval", Description = "Time interval for database consistency checks", DefaultValue = "300")] string DatabaseCheckInterval
);