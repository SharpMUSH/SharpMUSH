namespace SharpMUSH.Library.ExpandedObjectData;

public record UptimeData(
	DateTimeOffset StartTime,
	DateTimeOffset LastRebootTime,
	int Reboots,
	DateTimeOffset NextWarningTime,
	DateTimeOffset NextPurgeTime
	): AbstractExpandedData;