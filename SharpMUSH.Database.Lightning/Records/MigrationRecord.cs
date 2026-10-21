namespace SharpMUSH.Database.Lightning.Records;

/// <summary>One applied schema migration, stored under <c>mig:&lt;id&gt;</c> in the core metadata table.</summary>
public sealed record MigrationRecord
{
	public string Id { get; init; } = "";
	public long AppliedUnixMs { get; init; }
}
