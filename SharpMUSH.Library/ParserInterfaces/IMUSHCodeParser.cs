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
	IQueueService QueueService { get; }
	IImmutableStack<ParserState> State { get; }
	CallState? CommandCommaArgsParse(string text);
	CallState? CommandEqSplitArgsParse(string text);
	CallState? CommandEqSplitParse(string text);
	CallState? CommandListParse(string text);
	Task CommandParse(string handle, string text);
	CallState? CommandSingleArgParse(string text);
	CallState? FunctionParse(string text);
	IMUSHCodeParser Pop();
	IMUSHCodeParser Push(ParserState state);
}