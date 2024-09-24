using System.Collections.Concurrent;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Should be a Background Task
/// </summary>
public class TaskScheduler : ITaskScheduler
{
	public async ValueTask Write(string handle, MString command, ParserState? state)
	{
		Pipe.Enqueue((handle, command, state));
		await ValueTask.CompletedTask;
	}

	private ConcurrentQueue<(string, MString, ParserState?)> Pipe { get; } = new();

	public async Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;
		if (!Pipe.TryDequeue(out var result)) return;
		
		if (result.Item3 is not null)
		{
			await parser.FromState(result.Item3).CommandParse(result.Item1, result.Item2);
		}
		else
		{
			await parser.CommandParse(result.Item1, result.Item2);
		}
	}
}