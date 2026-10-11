using System.ComponentModel;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.Mcp;

/// <summary>Live analysis tools with host-owned document identity and bounded cooperative work.</summary>
[McpServerToolType]
public class MushTools(IAnalysisSessionFactory sessions, IHttpContextAccessor context, McpDocumentStore documents, McpAnalysisExecutor analysis)
{
	[McpServerTool(Name = "validate", UseStructuredContent = true)]
	[Description("Validate SharpMUSH softcode against the live parser. Ranges are 0-based (line, character).")]
	public Task<IReadOnlyList<McpDiagnostic>> Validate(
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Parse mode: function, commandsperline, commandlist or command.")] string parseType = "function",
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze<IReadOnlyList<McpDiagnostic>>(code, documentId, cancellationToken,
			(analyzer, text) => analyzer.Validate(text, MushParseMode.FromName(parseType)).Select(McpDiagnostic.From).ToList());

	[McpServerTool(Name = "format")]
	[Description("Format softcode with consistent whitespace. Preserves line count.")]
	public Task<string> Format(
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze(code, documentId, cancellationToken, (analyzer, text) => analyzer.Format(text));

	[McpServerTool(Name = "hover", UseStructuredContent = true)]
	[Description("Return hover documentation at a 0-based line/character, or null.")]
	public Task<McpHover?> Hover(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze<McpHover?>(code, documentId, cancellationToken, (analyzer, text) =>
		{
			var result = analyzer.Hover(text, line, character);
			return result is null ? null : McpHover.From(result);
		});

	[McpServerTool(Name = "complete", UseStructuredContent = true)]
	[Description("Return completion suggestions at a 0-based line/character.")]
	public Task<IReadOnlyList<McpCompletion>> Complete(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze<IReadOnlyList<McpCompletion>>(code, documentId, cancellationToken,
			(analyzer, text) => analyzer.Complete(text, line, character).Select(McpCompletion.From).ToList());

	[McpServerTool(Name = "signature_help", UseStructuredContent = true)]
	[Description("Return surrounding function signature help at a 0-based line/character, or null.")]
	public Task<McpSignature?> SignatureHelp(
		[Description("Line (0-based).")] int line,
		[Description("Character (0-based).")] int character,
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze<McpSignature?>(code, documentId, cancellationToken, (analyzer, text) =>
		{
			var result = analyzer.SignatureHelp(text, line, character);
			return result is null ? null : McpSignature.From(result);
		});

	[McpServerTool(Name = "document_symbols", UseStructuredContent = true)]
	[Description("Return the softcode outline: attribute definitions, calls and commands.")]
	public Task<IReadOnlyList<McpSymbol>> DocumentSymbols(
		[Description("The softcode; omit if documentId is given.")] string? code = null,
		[Description("Id from open_document.")] string? documentId = null,
		CancellationToken cancellationToken = default)
		=> Analyze<IReadOnlyList<McpSymbol>>(code, documentId, cancellationToken,
			(analyzer, text) => analyzer.DocumentSymbols(text).Select(McpSymbol.From).ToList());

	[McpServerTool(Name = "open_document")]
	[Description("Store softcode and return a principal-owned temporary documentId.")]
	public string OpenDocument([Description("The softcode to store.")] string code) => documents.Open(code);

	[McpServerTool(Name = "close_document")]
	[Description("Release an owned documentId. Returns true if it existed.")]
	public bool CloseDocument([Description("The documentId to release.")] string documentId) => documents.Close(documentId);

	private Task<T> Analyze<T>(string? code, string? documentId, CancellationToken cancellationToken, Func<IMushCodeAnalyzer, string, T> work)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var text = Resolve(code, documentId);
		var session = sessions.CreateSession(context.HttpContext?.User ?? throw new UnauthorizedAccessException("HTTP analysis identity is required."));
		return analysis.ExecuteAsync(async () => work(await session.CreateAnalyzerAsync(SharpMUSH.Library.ParserInterfaces.ExecutionBudget.CurrentToken), text), cancellationToken);
	}

	private string Resolve(string? code, string? documentId)
	{
		if (!string.IsNullOrEmpty(documentId))
		{
			if (documents.TryGet(documentId, out var text)) return text;
			throw new ArgumentException($"Unknown documentId '{documentId}'. Open one with open_document first.");
		}
		var inline = code ?? throw new ArgumentException("Provide either 'code' or a 'documentId'.");
		documents.ValidateText(inline);
		return inline;
	}
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
