namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: PennConfig(Name = "forking_dump")] bool ForkingDump,
	[property: PennConfig(Name = "dump_message")] string? DumpMessage,
	[property: PennConfig(Name = "dump_complete")] string? DumpComplete,
	[property: PennConfig(Name = "dump_warning_1m")] string? DumpWarning1Min,
	[property: PennConfig(Name = "dump_warning_5m")] string? DumpWarning5Min,
	[property: PennConfig(Name = "dump_interval")] string DumpInterval,
	[property: PennConfig(Name = "warning_interval")] string WarningInterval,
	[property: PennConfig(Name = "purge_interval")] string PurgeInterval,
	[property: PennConfig(Name = "database_check_interval")] string DatabaseCheckInterval
);