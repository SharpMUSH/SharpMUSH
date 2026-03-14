namespace SharpMUSH.Configuration.Options;

public record WarningOptions(
	[property: SharpConfig(
		Name = "warn_interval",
		Category = "Warning",
		Description = "Time interval between automatic warning checks. Set to 0 to disable automatic checking.",
		Group = "Warning System",
		Order = 1,
		Tooltip = "Format: time value (e.g., '5m', '1h', '0' to disable)")]
	string WarnInterval
);
