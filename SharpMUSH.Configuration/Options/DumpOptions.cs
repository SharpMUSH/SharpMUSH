namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: PennConfig(Name = "forking_dump", Description = "Use a separate process for database dumps to avoid blocking")] bool ForkingDump,
	[property: PennConfig(Name = "dump_message", Description = "Message displayed when database dump begins")] string? DumpMessage,
	[property: PennConfig(Name = "dump_complete", Description = "Message displayed when database dump completes")] string? DumpComplete,
	[property: PennConfig(Name = "dump_warning_1min", Description = "Warning message shown 1 minute before dump")] string? DumpWarning1Min,
	[property: PennConfig(Name = "dump_warning_5min", Description = "Warning message shown 5 minutes before dump")] string? DumpWarning5Min,
	[property: PennConfig(Name = "dump_interval", Description = "Time interval between automatic database dumps")] string DumpInterval,
	[property: PennConfig(Name = "warn_interval", Description = "Time interval for dump warning messages")] string WarningInterval,
	[property: PennConfig(Name = "purge_interval", Description = "Time interval for purging destroyed objects")] string PurgeInterval,
	[property: PennConfig(Name = "dbck_interval", Description = "Time interval for database consistency checks")] string DatabaseCheckInterval
);