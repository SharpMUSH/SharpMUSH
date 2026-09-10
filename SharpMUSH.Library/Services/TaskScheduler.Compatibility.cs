using Mediator;
using Microsoft.Extensions.Logging;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class TaskScheduler
{
	// Preserve the published constructor and ValueTask entry points for compiled plugins.
	// Engine code uses the additive admission methods to observe rejection and reservation PIDs.
	public TaskScheduler(IMUSHCodeParser parser, IConnectionService connectionService,
		ISchedulerFactory schedulerFactory, IAttributeService attributeService, IMediator mediator,
		ILogger<TaskScheduler> logger)
		: this(parser, connectionService, schedulerFactory, attributeService, mediator, logger, null, null) { }

	// Keep the budget-aware constructor slot used by compiled R4 consumers.
	public TaskScheduler(IMUSHCodeParser parser, IConnectionService connectionService,
		ISchedulerFactory schedulerFactory, IAttributeService attributeService, IMediator mediator,
		ILogger<TaskScheduler> logger, IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions>? configuration,
		INotifyService? notifyService)
		: this(parser, connectionService, schedulerFactory, attributeService, mediator, logger, configuration, notifyService, diagnostics: null) { }

	public async ValueTask WriteUserCommand(long handle, MString command, ParserState state)
	{ await AdmitUserCommand(handle, command, state); }
	public async ValueTask WriteCommandList(MString command, ParserState state)
	{ await AdmitCommandList(command, state); }
	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue)
	{ await AdmitCommandList(command, state, dbAttribute, oldValue); }
	public async ValueTask WriteCommandList(MString command, ParserState state, DbRefAttribute dbAttribute, int oldValue, TimeSpan timeout)
	{ await AdmitCommandList(command, state, dbAttribute, oldValue, timeout); }
	public async ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay)
	{ await AdmitCommandList(command, state, delay); }
	public async ValueTask WriteAsyncAttribute(Func<ValueTask<ParserState>> function, DbRefAttribute dbAttribute)
	{ await AdmitAsyncAttribute(function, dbAttribute); }
	public async ValueTask EnqueueWork(Func<ValueTask<CallState?>> action, string triggerName, string group)
	{ await AdmitWork(action, triggerName, group); }
	public async ValueTask Notify(DbRefAttribute dbAttribute, int oldValue, int count = 1)
	{ await NotifyCounted(dbAttribute, oldValue, count); }
	public async ValueTask NotifyAll(DbRefAttribute dbAttribute)
	{ await NotifyAllCounted(dbAttribute); }
}
