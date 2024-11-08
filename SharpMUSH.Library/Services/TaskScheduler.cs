using System.Collections.Concurrent;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Should be a Background Task
/// </summary>
public class TaskScheduler : ITaskScheduler
{
	private record TaskQueue(
		string Handle, 
		MString Command, 
		ParserState? State);
	
	public async ValueTask Write(string handle, MString command, ParserState? state)
	{
		Pipe.Enqueue(new TaskQueue (handle, command, state));
		await ValueTask.CompletedTask;
	}

	private ConcurrentQueue<TaskQueue> Pipe { get; } = new();

	public async Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;
		if (!Pipe.TryDequeue(out var result)) return;
		
		if (result.State is not null)
		{
			await parser.FromState(result.State).CommandParse(result.Handle, result.Command);
		}
		else
		{
			await parser.CommandParse(result.Handle, result.Command);
		}
	}
}