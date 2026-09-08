namespace SharpMUSH.Database.Lightning.Records;

/// <summary>One applied schema migration, keyed by <see cref="Id"/> in a dedicated migrations table.</summary>
public sealed record MigrationRecord
{
	public string Id { get; init; } = "";
	public long AppliedUnixMs { get; init; }
}
