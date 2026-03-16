namespace SharpMUSH.Library.ExpandedObjectData;

[Serializable]
public record UptimeData(
	DateTimeOffset StartTime,
	DateTimeOffset LastRebootTime,
	int Reboots,
	DateTimeOffset NextWarningTime,
	DateTimeOffset NextPurgeTime
	) : AbstractExpandedData;