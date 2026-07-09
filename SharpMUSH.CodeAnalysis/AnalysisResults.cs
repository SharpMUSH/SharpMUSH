using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.CodeAnalysis;

/// <summary>Hover information for a symbol at a position: markdown plus the covered range.</summary>
public record HoverInfo(string Markdown, Range Range);

/// <summary>A single completion suggestion.</summary>
/// <param name="Kind">"Function", "Keyword", or "Variable".</param>
/// <param name="IsSnippet">When true, <paramref name="InsertText"/> is a snippet (uses $0 tab stops).</param>
public record CompletionSuggestion(
	string Label,
	string Kind,
	string? Detail,
	string? Documentation,
	string? InsertText,
	bool IsSnippet);

/// <summary>One parameter within a <see cref="SignatureInfo"/>.</summary>
public record ParameterInfo(string Label, string Documentation);

/// <summary>Signature help for the function call surrounding a position.</summary>
public record SignatureInfo(
	string Label,
	string Documentation,
	IReadOnlyList<ParameterInfo> Parameters,
	int ActiveParameter);

/// <summary>A document outline symbol.</summary>
/// <param name="Kind">"Property", "Function", or "Method".</param>
public record CodeSymbol(
	string Name,
	string Kind,
	string Detail,
	Range Range,
	Range SelectionRange);
