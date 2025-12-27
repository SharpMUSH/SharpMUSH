namespace SharpMUSH.Library.Models;

/// <summary>
/// Diagnostic severity levels compatible with LSP DiagnosticSeverity.
/// </summary>
public enum DiagnosticSeverity
{
	/// <summary>
	/// Reports an error.
	/// </summary>
	Error = 1,

	/// <summary>
	/// Reports a warning.
	/// </summary>
	Warning = 2,

	/// <summary>
	/// Reports an information.
	/// </summary>
	Information = 3,

	/// <summary>
	/// Reports a hint.
	/// </summary>
	Hint = 4
}

/// <summary>
/// Represents a diagnostic, such as a compiler error or warning.
/// Compatible with the Language Server Protocol (LSP) Diagnostic type.
/// </summary>
public record Diagnostic
{
	/// <summary>
	/// The range at which the message applies.
	/// </summary>
	public required Range Range { get; init; }

	/// <summary>
	/// The diagnostic's severity. Can be omitted. If omitted it is up to the
	/// client to interpret diagnostics as error, warning, info or hint.
	/// </summary>
	public DiagnosticSeverity Severity { get; init; } = DiagnosticSeverity.Error;

	/// <summary>
	/// The diagnostic's code, which might appear in the user interface.
	/// </summary>
	public string? Code { get; init; }

	/// <summary>
	/// A human-readable string describing the source of this diagnostic,
	/// e.g. 'SharpMUSH Parser' or 'ANTLR'.
	/// </summary>
	public string? Source { get; init; }

	/// <summary>
	/// The diagnostic's message.
	/// </summary>
	public required string Message { get; init; }

	/// <summary>
	/// Additional metadata about the diagnostic.
	/// </summary>
	public DiagnosticTag[]? Tags { get; init; }

	/// <summary>
	/// An array of related diagnostic information, e.g. when symbol-names within
	/// a scope collide all definitions can be marked via this property.
	/// </summary>
	public DiagnosticRelatedInformation[]? RelatedInformation { get; init; }

	/// <summary>
	/// The offending token text, if available.
	/// </summary>
	public string? OffendingToken { get; init; }

	/// <summary>
	/// The expected tokens, if available.
	/// </summary>
	public IReadOnlyList<string>? ExpectedTokens { get; init; }

	public override string ToString()
	{
		var severityStr = Severity switch
		{
			DiagnosticSeverity.Error => "Error",
			DiagnosticSeverity.Warning => "Warning",
			DiagnosticSeverity.Information => "Info",
			DiagnosticSeverity.Hint => "Hint",
			_ => "Diagnostic"
		};

		var msg = $"{severityStr} at {Range}: {Message}";

		if (OffendingToken is not null)
		{
			msg += $"\n  Unexpected token: '{OffendingToken}'";
		}

		if (ExpectedTokens is not null && ExpectedTokens.Count > 0)
		{
			msg += $"\n  Expected one of: {string.Join(", ", ExpectedTokens)}";
		}

		return msg;
	}
}

/// <summary>
/// Diagnostic tags for categorizing diagnostics.
/// Compatible with LSP DiagnosticTag.
/// </summary>
public enum DiagnosticTag
{
	/// <summary>
	/// Unused or unnecessary code.
	/// Clients are allowed to render diagnostics with this tag faded out instead of having
	/// an error squiggle.
	/// </summary>
	Unnecessary = 1,

	/// <summary>
	/// Deprecated or obsolete code.
	/// Clients are allowed to rendered diagnostics with this tag strike through.
	/// </summary>
	Deprecated = 2
}

/// <summary>
/// Represents a related message and source code location for a diagnostic.
/// Compatible with LSP DiagnosticRelatedInformation.
/// </summary>
public record DiagnosticRelatedInformation
{
	/// <summary>
	/// The location of this related diagnostic information.
	/// </summary>
	public required DiagnosticLocation Location { get; init; }

	/// <summary>
	/// The message of this related diagnostic information.
	/// </summary>
	public required string Message { get; init; }
}

/// <summary>
/// Represents a location inside a resource, such as a line inside a text file.
/// Compatible with LSP Location.
/// </summary>
public record DiagnosticLocation
{
	/// <summary>
	/// The URI of the document.
	/// </summary>
	public required string Uri { get; init; }

	/// <summary>
	/// The range within the document.
	/// </summary>
	public required Range Range { get; init; }
}
