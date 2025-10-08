namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: SharpConfig(Name = "purge_interval", Category = "Dump", Description = "Time interval for purging destroyed objects")] string PurgeInterval
);