namespace SharpMUSH.CodeAnalysis.Catalog;

/// <summary>
/// A transport's explicit analysis contract. The byte limit must also be enforced by its adapter;
/// this descriptor alone neither limits existing APIs nor advertises a new endpoint.
/// </summary>
public sealed class AnalysisCapabilities(string source, int maxDocumentBytes, IEnumerable<string> features)
{
	public int SchemaVersion => 1;
	public string Source { get; } = source;
	public string PositionEncoding => "utf-16";
	public int MaxDocumentBytes { get; } = maxDocumentBytes > 0
		? maxDocumentBytes : throw new ArgumentOutOfRangeException(nameof(maxDocumentBytes));
	public IReadOnlyList<string> Features { get; } = Array.AsReadOnly(features.ToArray());
	public IReadOnlyList<MushAnalysisMode> Modes { get; } = Array.AsReadOnly(Enum.GetValues<MushAnalysisMode>());

	/// <summary>Initial adapter budget preserves the owner's 5 MiB buffer requirement.</summary>
	public static AnalysisCapabilities Offline { get; } = new("offline", 5 * 1024 * 1024,
		["validate", "hover", "complete", "signature-help", "document-symbols"]);
}
