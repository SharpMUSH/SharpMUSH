using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
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
	ValueTask CommandParse(long handle, IConnectionService connectionService, MString text);
	ValueTask CommandParse(MString text);
	ValueTask<CallState?> CommandSingleArgParse(MString text);
	ValueTask<CallState?> FunctionParse(MString text);
	IMUSHCodeParser Empty();
	IMUSHCodeParser Push(ParserState state);
	IMUSHCodeParser FromState(ParserState state);
	Option<ParserState> StateHistory(uint index);
}