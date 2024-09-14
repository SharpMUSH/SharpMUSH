using SharpMUSH.Library.Services;
using System.Collections.Immutable;

namespace SharpMUSH.Library.ParserInterfaces;

public partial interface IMUSHCodeParser
{
	IConnectionService ConnectionService { get; }
	ParserState CurrentState { get; }
	ISharpDatabase Database { get; }
	INotifyService NotifyService { get; }
	IPasswordService PasswordService { get; }
	IPermissionService PermissionService { get; }
	ILocateService LocateService { get; }
	IQueueService QueueService { get; }
	IImmutableStack<ParserState> State { get; }
	ValueTask<CallState?> CommandCommaArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitParse(MString text);
	ValueTask<CallState?> CommandListParse(MString text);
	ValueTask CommandParse(string handle, MString text);
	ValueTask<CallState?> CommandSingleArgParse(MString text);
	ValueTask<CallState?> FunctionParse(MString text);
	IMUSHCodeParser Pop();
	IMUSHCodeParser Push(ParserState state);
}