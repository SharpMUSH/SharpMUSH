namespace SharpMUSH.Configuration.Options;

public record WarningOptions(
	[property: SharpConfig(
		Name = "warn_interval",
		Category = "Warning",
		Description = "Time interval between automatic warning checks. Set to 0 to disable automatic checking."
	)] string WarnInterval
);
