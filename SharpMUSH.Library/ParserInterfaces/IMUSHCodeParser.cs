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
	CallState? CommandCommaArgsParse(MString text);
	CallState? CommandEqSplitArgsParse(MString text);
	CallState? CommandEqSplitParse(MString text);
	CallState? CommandListParse(MString text);
	Task CommandParse(string handle, MString text);
	CallState? CommandSingleArgParse(MString text);
	CallState? FunctionParse(MString text);
	IMUSHCodeParser Pop();
	IMUSHCodeParser Push(ParserState state);
}