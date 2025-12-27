using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IMUSHCodeParser
{	
	ParserState CurrentState { get; }
	IImmutableStack<ParserState> State { get; }
	LibraryService<string, FunctionDefinition> FunctionLibrary {get;}
	LibraryService<string, CommandDefinition> CommandLibrary {get;}
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
	/// Uses two-stage parsing (SLL first, then LL) for better error messages.
	/// </summary>
	/// <param name="text">The text to parse.</param>
	/// <param name="parseType">The type of parsing to perform (Function, Command, etc.).</param>
	/// <returns>A list of parse errors, or an empty list if parsing succeeded.</returns>
	IReadOnlyList<ParseError> ValidateAndGetErrors(MString text, ParseType parseType = ParseType.Function);
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