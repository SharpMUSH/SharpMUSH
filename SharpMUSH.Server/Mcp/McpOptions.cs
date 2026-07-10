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
}
