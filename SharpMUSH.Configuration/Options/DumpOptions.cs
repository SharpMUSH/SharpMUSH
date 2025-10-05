namespace SharpMUSH.Configuration.Options;

public record DumpOptions(
	[property: SharpConfig(Name = "purge_interval", Description = "Time interval for purging destroyed objects")] string PurgeInterval
);