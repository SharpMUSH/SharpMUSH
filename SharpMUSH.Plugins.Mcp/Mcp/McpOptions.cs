namespace SharpMUSH.Server.Mcp;

/// <summary>
/// Configuration for the in-server Model Context Protocol (MCP) endpoint.
/// Bound from the "Mcp" section of appsettings.
/// </summary>
public class McpOptions
{
	public const string Section = "Mcp";

	/// <summary>
	/// When false (the production default) the MCP endpoint is not mapped and all
	/// requests to <see cref="Path"/> return 404.
	/// </summary>
	public bool Enabled { get; set; }

	/// <summary>
	/// The route the MCP Streamable-HTTP endpoint is mapped at.
	/// </summary>
	public string Path { get; set; } = "/mcp";
	/// <summary>UTF-8 code bytes allowed in one stored or inline document.</summary>
	public int MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;
	public int MaxDocumentsPerPrincipal { get; set; } = 64;
	public long MaxBytesPerPrincipal { get; set; } = 32L * 1024 * 1024;
	public int MaxStoredDocuments { get; set; } = 1024;
	public long MaxStoredBytes { get; set; } = 128L * 1024 * 1024;
	public TimeSpan DocumentIdleTimeout { get; set; } = TimeSpan.FromMinutes(15);
	/// <summary>Envelope budget includes worst-case JSON escaping of a 5 MiB document.</summary>
	public long MaxRequestBodyBytes { get; set; } = 32L * 1024 * 1024;
	public int MaxConcurrentRequests { get; set; } = 4;
	public TimeSpan BodyReadTimeout { get; set; } = TimeSpan.FromSeconds(10);
	public int MaxConcurrentAnalyses { get; set; } = 2;
	public int MaxQueuedAnalyses { get; set; } = 8;
	/// <summary>Elapsed deadline covers queue wait and cooperative analysis work.</summary>
	public TimeSpan AnalysisTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
