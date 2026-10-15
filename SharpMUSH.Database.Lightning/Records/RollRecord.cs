namespace SharpMUSH.Database.Lightning.Records;

/// <summary>One recorded roll, keyed by its id (see <c>LightningDatabase.Rolls.cs</c>).</summary>
public sealed record RollRecord
{
	public long AtMs { get; init; }
	public string RollerObjid { get; init; } = "";
	public string RollerName { get; init; } = "";
	public string? ObjectObjid { get; init; }
	public string Formula { get; init; } = "";
	public decimal Total { get; init; }
	public string Text { get; init; } = "";
	public string Json { get; init; } = "";
}
