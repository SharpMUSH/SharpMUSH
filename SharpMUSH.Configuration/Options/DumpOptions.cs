namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	bool ForkingDump = true,
	string? DumpMessage = "Saving database. Game may freeze for a few moments.",
	string? DumpComplete = "Save complete.",
	string? DumpWarning1Min = "Database save in 1 minute.",
	string? DumpWarning5Min = "Database save in 5 minutes.",
	string DumpInterval = "4h",
	string WarningInterval = "1h",
	string PurgeInterval = "10m1s",
	string DatabaseCheckInterval = "9m59s"
);