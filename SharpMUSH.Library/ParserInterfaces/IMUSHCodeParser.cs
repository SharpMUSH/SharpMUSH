using Mediator;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IMUSHCodeParser
{
	IOptionsMonitor<PennMUSHOptions> Configuration { get; }
	IConnectionService ConnectionService { get; }
	ParserState CurrentState { get; }
	IAttributeService AttributeService { get; }
	INotifyService NotifyService { get; }
	IPasswordService PasswordService { get; }
	IPermissionService PermissionService { get; }
	ILocateService LocateService { get; }
	IExpandedObjectDataService ObjectDataService { get; }
	ITaskScheduler Scheduler { get; }
	ICommandDiscoveryService CommandDiscoveryService { get; }
	IImmutableStack<ParserState> State { get; }
	IMediator Mediator { get; }
	ValueTask<CallState?> CommandCommaArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitArgsParse(MString text);
	ValueTask<CallState?> CommandEqSplitParse(MString text);
	ValueTask<CallState?> CommandListParse(MString text);
	Func<ValueTask<CallState?>> CommandListParseVisitor(MString text);
	ValueTask CommandParse(string handle, MString text);
	ValueTask CommandParse(MString text);
	ValueTask<CallState?> CommandSingleArgParse(MString text);
	ValueTask<CallState?> FunctionParse(MString text);
	IMUSHCodeParser Empty();
	IMUSHCodeParser Push(ParserState state);
	IMUSHCodeParser FromState(ParserState state);
}