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
/// Every tool takes the softcode either inline via <c>code</c> or by a <c>documentId</c>
/// previously returned from <c>open_document</c>. Positions are 0-based (line, character).
///
/// The endpoint hosting these tools is authenticated per-request as a game character
/// (see <see cref="Authentication.MushBasicAuthenticationHandler"/>).
/// </summary>
[McpServerToolType]
public class MushTools(IMushCodeAnalyzer analyzer, McpDocumentStore documents)
{
	[McpServerTool(Name = "validate", UseStructuredContent = true)]
	[Description("Validate SharpMUSH softcode against the live parser and return any " +
	             "syntax errors, warnings, or hints. Ranges are 0-based (line, character).")]
	public IReadOnlyList<McpDiagnostic> Validate(
		[Description("The MUSH softcode to validate. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("How to parse the code: 'function' (default), 'commandlist' (a list of " +
		             "commands, e.g. an attribute's $-command actions), or 'command' (a single command).")]
		string parseType = "function",
		[Description("Id from open_document to validate instead of inline 'code'.")]
		string? documentId = null)
		=> analyzer.Validate(Resolve(code, documentId), ParseParseType(parseType))
			.Select(McpDiagnostic.From)
			.ToList();

	[McpServerTool(Name = "format")]
	[Description("Format SharpMUSH softcode with a consistent style: trims whitespace, " +
	             "inserts a space after commas, and a space between an @command and its first " +
	             "argument. Returns the formatted code. Line count is preserved.")]
	public string Format(
		[Description("The MUSH softcode to format. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("Id from open_document to format instead of inline 'code'.")]
		string? documentId = null)
		=> analyzer.Format(Resolve(code, documentId));

	[McpServerTool(Name = "hover", UseStructuredContent = true)]
	[Description("Return hover documentation (function/command signature, or a built-in " +
	             "substitution explanation) for the word at a 0-based line/character, or null.")]
	public McpHover? Hover(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The MUSH softcode. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("Id from open_document to use instead of inline 'code'.")]
		string? documentId = null)
	{
		var hover = analyzer.Hover(Resolve(code, documentId), line, character);
		return hover is null ? null : McpHover.From(hover);
	}

	[McpServerTool(Name = "complete", UseStructuredContent = true)]
	[Description("Return completion suggestions (functions, commands, and common " +
	             "substitutions) for the word prefix at a 0-based line/character.")]
	public IReadOnlyList<McpCompletion> Complete(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The MUSH softcode. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("Id from open_document to use instead of inline 'code'.")]
		string? documentId = null)
		=> analyzer.Complete(Resolve(code, documentId), line, character)
			.Select(McpCompletion.From)
			.ToList();

	[McpServerTool(Name = "signature_help", UseStructuredContent = true)]
	[Description("Return signature help for the function call surrounding a 0-based " +
	             "line/character, or null if the position is not inside a known function call.")]
	public McpSignature? SignatureHelp(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The MUSH softcode. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("Id from open_document to use instead of inline 'code'.")]
		string? documentId = null)
	{
		var signature = analyzer.SignatureHelp(Resolve(code, documentId), line, character);
		return signature is null ? null : McpSignature.From(signature);
	}

	[McpServerTool(Name = "document_symbols", UseStructuredContent = true)]
	[Description("Return an outline of the softcode: attribute definitions, @set attributes, " +
	             "function calls, and commands.")]
	public IReadOnlyList<McpSymbol> DocumentSymbols(
		[Description("The MUSH softcode. Omit if 'documentId' is given.")]
		string? code = null,
		[Description("Id from open_document to use instead of inline 'code'.")]
		string? documentId = null)
		=> analyzer.DocumentSymbols(Resolve(code, documentId))
			.Select(McpSymbol.From)
			.ToList();

	[McpServerTool(Name = "open_document")]
	[Description("Store softcode server-side and return a documentId so subsequent tool calls " +
	             "can reference it via 'documentId' instead of resending the text.")]
	public string OpenDocument(
		[Description("The MUSH softcode to store.")] string code)
		=> documents.Open(code);

	[McpServerTool(Name = "close_document")]
	[Description("Release a documentId previously returned by open_document. Returns true if it " +
	             "existed.")]
	public bool CloseDocument(
		[Description("The documentId to release.")] string documentId)
		=> documents.Close(documentId);

	private string Resolve(string? code, string? documentId)
	{
		if (!string.IsNullOrEmpty(documentId))
		{
			if (documents.TryGet(documentId, out var text))
			{
				return text;
			}

			throw new ArgumentException(
				$"Unknown documentId '{documentId}'. Open one with open_document first.");
		}

		return code ?? throw new ArgumentException("Provide either 'code' or a 'documentId'.");
	}

	private static ParseType ParseParseType(string parseType) => MushParseMode.FromName(parseType);
}

/// <summary>A diagnostic in the flat, JSON-friendly shape returned to MCP clients.</summary>
public record McpDiagnostic(
	string Severity, string Message,
	int StartLine, int StartCharacter, int EndLine, int EndCharacter,
	string? Code, string? Source)
{
	public static McpDiagnostic From(Diagnostic d) => new(
		d.Severity.ToString(), d.Message,
		d.Range.Start.Line, d.Range.Start.Character, d.Range.End.Line, d.Range.End.Character,
		d.Code, d.Source);
}

/// <summary>Hover markdown plus the 0-based range it covers.</summary>
public record McpHover(string Markdown, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
{
	public static McpHover From(HoverInfo h) => new(
		h.Markdown, h.Range.Start.Line, h.Range.Start.Character, h.Range.End.Line, h.Range.End.Character);
}

/// <summary>A completion suggestion.</summary>
public record McpCompletion(string Label, string Kind, string? Detail, string? Documentation, string? InsertText, bool IsSnippet)
{
	public static McpCompletion From(CompletionSuggestion c) =>
		new(c.Label, c.Kind, c.Detail, c.Documentation, c.InsertText, c.IsSnippet);
}

/// <summary>Signature help for a function call.</summary>
public record McpSignature(string Label, string Documentation, IReadOnlyList<McpParameter> Parameters, int ActiveParameter)
{
	public static McpSignature From(SignatureInfo s) => new(
		s.Label, s.Documentation,
		s.Parameters.Select(p => new McpParameter(p.Label, p.Documentation)).ToList(),
		s.ActiveParameter);
}

/// <summary>One parameter within an <see cref="McpSignature"/>.</summary>
public record McpParameter(string Label, string Documentation);

/// <summary>A document outline symbol.</summary>
public record McpSymbol(
	string Name, string Kind, string Detail,
	int StartLine, int StartCharacter, int EndLine, int EndCharacter)
{
	public static McpSymbol From(CodeSymbol s) => new(
		s.Name, s.Kind, s.Detail,
		s.Range.Start.Line, s.Range.Start.Character, s.Range.End.Line, s.Range.End.Character);
}
