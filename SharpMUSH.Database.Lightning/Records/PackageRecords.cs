namespace SharpMUSH.Database.Lightning.Records;

/// <summary>Mirrors <c>SurrealDatabase.SysPackageDbRecord</c> / <c>InstalledPackageRecord</c>.</summary>
public sealed record InstalledPackageRecord
{
	public string PackageId { get; init; } = "";
	public string Version { get; init; } = "";
	public string SourceRepo { get; init; } = "";
	public string? SourcePath { get; init; }
	public string InstalledCommit { get; init; } = "";
	public string? PinnedBranch { get; init; }
	public string InstalledAt { get; init; } = "";
	public int CurrentRevision { get; init; }
	public string[]? DeployedFiles { get; init; }
}

/// <summary>Mirrors <c>SurrealDatabase.SysPackageObjectDbRecord</c> / <c>PackageObjectRecord</c>.</summary>
public sealed record PackageObjectRecord
{
	public string PackageId { get; init; } = "";
	public string RefName { get; init; } = "";
	public string Objid { get; init; } = "";
	public string ObjectType { get; init; } = "";
}

/// <summary>Mirrors <c>SurrealDatabase.SysManagedAttributeDbRecord</c> / <c>ManagedAttributeRecord</c>.</summary>
public sealed record ManagedAttributeRecord
{
	public string PackageId { get; init; } = "";
	public string Objid { get; init; } = "";
	public string Attribute { get; init; } = "";
	public string BaselineValue { get; init; } = "";
	public string BaselineHash { get; init; } = "";
	public string BaselineVersion { get; init; } = "";
}

/// <summary>Mirrors <c>SurrealDatabase.SysManagedStructureDbRecord</c> / <c>ManagedStructureRecord</c>.</summary>
public sealed record ManagedStructureRecord
{
	public string PackageId { get; init; } = "";
	public string Objid { get; init; } = "";
	public string StructureJson { get; init; } = "";
	public string BaselineVersion { get; init; } = "";
}

/// <summary>Mirrors <c>SurrealDatabase.SysPackageDependencyDbRecord</c> / <c>PackageDependencyRecord</c>.</summary>
public sealed record PackageDependencyRecord
{
	public string PackageId { get; init; } = "";
	public string DependsOnId { get; init; } = "";
	public string Constraint { get; init; } = "";
}

/// <summary>
/// Mirrors <c>SurrealDatabase.SysRemoteDbRecord</c> / <c>PackageRemoteRecord</c>. <c>Trust</c> is
/// <c>PackageRemoteTrust.ToString()</c>.
/// </summary>
public sealed record PackageRemoteRecord
{
	public string Name { get; init; } = "";
	public string Url { get; init; } = "";
	public string Trust { get; init; } = "";
	public string? Branch { get; init; }
}

/// <summary>
/// Mirrors <c>SurrealDatabase.SysPackageRevisionDbRecord</c> / <c>PackageRevisionRecord</c>.
/// <c>Kind</c> is <c>PackageRevisionKind.ToString()</c>.
/// </summary>
public sealed record PackageRevisionRecord
{
	public string PackageId { get; init; } = "";
	public int Revision { get; init; }
	public string Kind { get; init; } = "";
	public string Version { get; init; } = "";
	public string Commit { get; init; } = "";
	public string ManifestSnapshotJson { get; init; } = "";
	public string ConfigureAnswersJson { get; init; } = "";
	public string PreApplyValuesJson { get; init; } = "";
	public string AppliedAt { get; init; } = "";
}
