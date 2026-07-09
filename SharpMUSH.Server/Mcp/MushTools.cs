using System.ComponentModel;
using ModelContextProtocol.Server;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Server.Mcp;

/// <summary>
/// MCP tools exposing SharpMUSH's live code intelligence to MCP clients
/// (Claude Code, editors, tooling). Each tool is a thin JSON adapter over the shared
/// <see cref="IMushCodeAnalyzer"/>, which runs in-process against the live world's
/// registered function and command libraries.
///
/// The endpoint hosting these tools is authenticated per-request as a game character
/// (see <see cref="Authentication.MushBasicAuthenticationHandler"/>).
/// </summary>
[McpServerToolType]
public class MushTools(IMushCodeAnalyzer analyzer)
{
	[McpServerTool(Name = "validate", UseStructuredContent = true)]
	[Description("Validate SharpMUSH softcode against the live parser and return any " +
	             "syntax errors, warnings, or hints. Ranges are 0-based (line, character).")]
	public IReadOnlyList<McpDiagnostic> Validate(
		[Description("The MUSH softcode to validate.")]
		string code,
		[Description("How to parse the code: 'function' (default) or 'command'.")]
		string parseType = "function")
		=> analyzer.Validate(code, ParseParseType(parseType))
			.Select(McpDiagnostic.From)
			.ToList();

	[McpServerTool(Name = "format")]
	[Description("Format SharpMUSH softcode with a consistent style: trims whitespace, " +
	             "inserts a space after commas, and a space between an @command and its first " +
	             "argument. Returns the formatted code. Line count is preserved.")]
	public string Format(
		[Description("The MUSH softcode to format.")]
		string code)
		=> analyzer.Format(code);

	private static ParseType ParseParseType(string parseType)
		=> parseType.Trim().ToLowerInvariant() switch
		{
			"command" => ParseType.Command,
			_ => ParseType.Function
		};
}

/// <summary>
/// A diagnostic in the flat, JSON-friendly shape returned to MCP clients.
/// </summary>
public record McpDiagnostic(
	string Severity,
	string Message,
	int StartLine,
	int StartCharacter,
	int EndLine,
	int EndCharacter,
	string? Code,
	string? Source)
{
	public static McpDiagnostic From(Diagnostic d) => new(
		d.Severity.ToString(),
		d.Message,
		d.Range.Start.Line,
		d.Range.Start.Character,
		d.Range.End.Line,
		d.Range.End.Character,
		d.Code,
		d.Source);
}
