namespace SharpMUSH.Tools.RhostExport;

public enum RhostBackend { LegacyQdbm, Mdbx }

public sealed record RhostExportOptions
{
	public required string SourceDirectory { get; init; }
	public required string DatabaseBase { get; init; }
	public required string Checkpoint { get; init; }
	public required string ExporterPath { get; init; }
	public required string ExporterProfilePath { get; init; }
	public required string OutputDirectory { get; init; }
	public required RhostBackend Backend { get; init; }
	public string? MailFile { get; init; }
	public string? FolderFile { get; init; }
	public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
	public long MaxSourceBytes { get; init; } = 1024L * 1024 * 1024;
	public long MaxOutputBytes { get; init; } = 100L * 1024 * 1024;
	public long MaxDiagnosticBytes { get; init; } = 64 * 1024;
}

public sealed record RhostSourceFile(string Name, long Length, string Sha256);
public sealed record RhostExportProgress(string Stage);
public sealed record RhostExportResult(string OutputDirectory, int ObjectCount, int AttributeCount,
	IReadOnlyList<string> Warnings, IReadOnlyList<RhostSourceFile> SourceInventory);
