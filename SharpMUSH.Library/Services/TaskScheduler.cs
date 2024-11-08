using System.Collections.Concurrent;
using OneOf.Types;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Should be a Background Task
/// </summary>
public class TaskScheduler : ITaskScheduler
{
	[Flags]
	private enum TaskQueueType
	{
		Default = 0,
		Object = 1,
		Player = Object << 1,
		Socket = Player << 1,
		InPlace = Socket << 1,
		NoBreaks = InPlace <<1,
		PreserveQReg = NoBreaks <<1,
		ClearQReg = PreserveQReg <<1,
		PropagateQReg = ClearQReg <<1,
		NoList = PropagateQReg << 1,
		Break = NoList << 1,
		Retry = Break << 1,
		Debug = Retry << 1,
		NoDebug = Debug << 1,
		Priority = NoDebug << 1,
		DebugPrivileges = Priority << 1,
		Event = DebugPrivileges << 1,
		Recurse = InPlace | NoBreaks | PreserveQReg
	}

	private record TaskQueue(
		OneOf<SemaphoreSlim, string, None> WaitType,
		TaskQueueType Type,
		string? Handle,
		MString Command,
		ParserState? State);

	public async ValueTask WriteUserCommand(string handle, MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), TaskQueueType.Player | TaskQueueType.Socket, handle, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommand(MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), TaskQueueType.NoList, null, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state, SemaphoreSlim semaphore)
	{
		Pipe.Enqueue(new TaskQueue(semaphore, TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}
	
	public async ValueTask WriteCommandList(MString command, ParserState? state, string cron)
	{
		Pipe.Enqueue(new TaskQueue(cron, TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}

	private ConcurrentQueue<TaskQueue> Pipe { get; } = new();

	public async Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;
		if (!Pipe.TryDequeue(out var result)) return;

		if (result.WaitType.IsT0 || result.WaitType.IsT1)
		{
			throw new NotImplementedException();
		}
		
		if (result.State is not null)
		{
			switch (result)
			{
				case { Type: TaskQueueType.Socket, Handle: not null }:
					await parser.FromState(result.State).CommandParse(result.Handle, result.Command);
					break;
				case { Type: TaskQueueType.NoList }:
					await parser.FromState(result.State).CommandParse(result.Command);
					break;
				case { Type: TaskQueueType.Default }:
					await parser.FromState(result.State).CommandListParse(result.Command);
					break;
			}
		}
		else
		{
			switch (result)
			{
				case { Type: TaskQueueType.Socket, Handle: not null }:
					await parser.CommandParse(result.Handle, result.Command);
					break;
				case { Type: TaskQueueType.NoList }:
					await parser.CommandParse(result.Command);
					break;
				case { Type: TaskQueueType.Default }:
					await parser.CommandListParse(result.Command);
					break;
			}
		}
	}
}