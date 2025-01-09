namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[PennConfig(Name = "forking_dump")] bool ForkingDump,
	[PennConfig(Name = "dump_message")] string? DumpMessage,
	[PennConfig(Name = "dump_complete")] string? DumpComplete,
	[PennConfig(Name = "dump_warning_1m")] string? DumpWarning1Min,
	[PennConfig(Name = "dump_warning_5m")] string? DumpWarning5Min,
	[PennConfig(Name = "dump_interval")] string DumpInterval,
	[PennConfig(Name = "warning_interval")] string WarningInterval,
	[PennConfig(Name = "purge_interval")] string PurgeInterval,
	[PennConfig(Name = "database_check_interval")] string DatabaseCheckInterval
);