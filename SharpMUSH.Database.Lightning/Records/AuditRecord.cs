namespace SharpMUSH.Database.Lightning.Records;

/// <summary>One staff action in the audit log, keyed by when it was taken (see <c>LightningDatabase.Audit.cs</c>).</summary>
public sealed record AuditRecord
{
	public long AtMs { get; init; }
	public string Action { get; init; } = "";
	public string Source { get; init; } = "";
	public string? ActorAccountId { get; init; }
	public string? ActorAccountName { get; init; }
	public string? ActorObjid { get; init; }
	public string? ActorName { get; init; }
	public string? TargetKind { get; init; }
	public string? TargetId { get; init; }
	public string? TargetName { get; init; }
	public string? Details { get; init; }
}
