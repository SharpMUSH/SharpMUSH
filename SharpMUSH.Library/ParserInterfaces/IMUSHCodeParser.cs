using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IMUSHCodeParser
{
	IServiceProvider ServiceProvider { get; }
	ParserState CurrentState { get; }
	IImmutableStack<ParserState> State { get; }
	LibraryService<string, FunctionDefinition> FunctionLibrary { get; }
	LibraryService<string, CommandDefinition> CommandLibrary { get; }
	ValueTask<CallState?> CommandCommaArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitParse(MString text);
	ValueTask<CallState?> CommandListParse(MString text);
	Func<ValueTask<CallState?>> CommandListParseVisitor(MString text);
	ValueTask<CallState> CommandParse(long handle, IConnectionService connectionService, MString text);
	ValueTask<CallState> CommandParse(MString text);
	ValueTask<CallState?> CommandSingleArgParse(MString text);
	ValueTask<CallState?> FunctionParse(MString text);
	IMUSHCodeParser Empty();
	IMUSHCodeParser Push(ParserState state);
	IMUSHCodeParser FromState(ParserState state);
	Option<ParserState> StateHistory(uint index);

	/// <summary>
	/// Tokenizes the input text and returns token information for syntax highlighting.
	/// </summary>
	/// <param name="text">The text to tokenize.</param>
	/// <returns>A list of tokens with their types, positions, and text.</returns>
	IReadOnlyList<TokenInfo> Tokenize(MString text);

	/// <summary>
	/// Parses the input text and returns any errors encountered.
	/// Uses the configured prediction mode (SLL or LL) for parsing.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="parseType">The type of parsing to perform (Function, Command, etc.).</param>
	/// <returns>A list of parse errors, or an empty list if parsing succeeded.</returns>
	IReadOnlyList<ParseError> ValidateAndGetErrors(MString text, ParseType parseType = ParseType.Function);

	/// <summary>
	/// Parses the input text and returns diagnostics (LSP-compatible errors/warnings).
	/// This is the LSP-compatible version of ValidateAndGetErrors with error ranges.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="parseType">The type of parsing to perform (Function, Command, etc.).</param>
	/// <returns>A list of diagnostics, or an empty list if parsing succeeded.</returns>
	IReadOnlyList<Diagnostic> GetDiagnostics(MString text, ParseType parseType = ParseType.Function);

	/// <summary>
	/// Performs semantic analysis on the input text and returns semantic tokens.
	/// Semantic tokens provide information about the meaning of code elements beyond syntax.
	/// </summary>
	/// <param name="text">The text to analyze.</param>
	/// <param name="parseType">The type of parsing to perform (Function, Command, etc.).</param>
	/// <returns>A list of semantic tokens with type and modifier information.</returns>
	IReadOnlyList<SemanticToken> GetSemanticTokens(MString text, ParseType parseType = ParseType.Function);

	/// <summary>
	/// Performs semantic analysis and returns tokens in LSP delta-encoded format.
	/// This is optimized for transmission over the network in LSP scenarios.
	/// </summary>
	/// <param name="text">The text to analyze.</param>
	/// <param name="parseType">The type of parsing to perform (Function, Command, etc.).</param>
	/// <returns>Semantic tokens data in LSP delta-encoded format.</returns>
	SemanticTokensData GetSemanticTokensData(MString text, ParseType parseType = ParseType.Function);
}

/// <summary>
/// The type of parsing to perform for validation.
/// </summary>
public enum ParseType
{
	Function,
	Command,
	CommandList,
	CommandSingleArg,
	CommandCommaArgs,
	CommandEqSplitArgs,
	CommandEqSplit
}