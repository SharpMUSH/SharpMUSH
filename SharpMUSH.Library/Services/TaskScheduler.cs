using OneOf;
using OneOf.Types;
using SharpMUSH.Library.ParserInterfaces;
using System.Collections.Concurrent;

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
		NoBreaks = InPlace << 1,
		PreserveQReg = NoBreaks << 1,
		ClearQReg = PreserveQReg << 1,
		PropagateQReg = ClearQReg << 1,
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
		OneOf<SemaphoreSlim, TimeSpan, None> WaitType,
		DateTimeOffset EntryTime,
		TaskQueueType Type,
		string? Handle,
		MString Command,
		ParserState? State);

	public async ValueTask WriteUserCommand(string handle, MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), DateTimeOffset.UtcNow, TaskQueueType.Player | TaskQueueType.Socket, handle, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommand(MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), DateTimeOffset.UtcNow, TaskQueueType.NoList, null, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue(new None(), DateTimeOffset.UtcNow, TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state, SemaphoreSlim semaphore)
	{
		Pipe.Enqueue(new TaskQueue(semaphore, DateTimeOffset.UtcNow, TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}

	public async ValueTask WriteCommandList(MString command, ParserState? state, TimeSpan time)
	{
		Pipe.Enqueue(new TaskQueue(time, DateTimeOffset.UtcNow, TaskQueueType.Default, null, command, state));
		await ValueTask.CompletedTask;
	}

	private ConcurrentQueue<TaskQueue> Pipe { get; } = new();

	public async Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;
		if (!Pipe.TryDequeue(out var result)) return;

		var skipStack = new ConcurrentStack<TaskQueue>();

		do
		{
			switch (result)
			{
				case { State: null, Type: TaskQueueType.Player | TaskQueueType.Socket, Handle: not null }:
					await parser.Empty().CommandParse(result.Handle, result.Command); // Direct user input.
					continue;
				case { State: null }:
					throw new Exception("This should never occur");
				case { WaitType.IsT1: true }:
					if (DateTimeOffset.UtcNow - result.EntryTime > result.WaitType.AsT1)
					{
						await parser.FromState(result.State).CommandListParse(result.Command);
					}
					continue;
				case { WaitType.IsT0: true }:
					// TODO: Implement Semaphore Wait
					throw new NotImplementedException("Implement Scheduled Semaphores.");
				case { Type: TaskQueueType.NoList }:
					await parser.FromState(result.State).CommandParse(result.Command);
					continue;
				case { Type: TaskQueueType.Default }:
					await parser.FromState(result.State).CommandListParse(result.Command);
					continue;
			}

			skipStack.Push(result);
		} while (Pipe.TryDequeue(out result) && !stoppingToken.IsCancellationRequested);

		foreach (var item in skipStack)
		{
			Pipe.Enqueue(item);
		}
	}
}