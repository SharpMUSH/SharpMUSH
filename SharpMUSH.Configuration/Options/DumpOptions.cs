namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: PennConfig(Name = "forking_dump")] bool ForkingDump,
	[property: PennConfig(Name = "dump_message")] string? DumpMessage,
	[property: PennConfig(Name = "dump_complete")] string? DumpComplete,
	[property: PennConfig(Name = "dump_warning_1min")] string? DumpWarning1Min,
	[property: PennConfig(Name = "dump_warning_5min")] string? DumpWarning5Min,
	[property: PennConfig(Name = "dump_interval")] string DumpInterval,
	[property: PennConfig(Name = "warn_interval")] string WarningInterval,
	[property: PennConfig(Name = "purge_interval")] string PurgeInterval,
	[property: PennConfig(Name = "dbck_interval")] string DatabaseCheckInterval
);