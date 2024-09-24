using System.Collections.Concurrent;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Should be a Background Task
/// </summary>
public class TaskScheduler : ITaskScheduler
{
	public async ValueTask Write(string handle, MString command, IMUSHCodeParser parser)
	{
		Pipe.Enqueue((handle, command, parser));
		await ValueTask.CompletedTask;
	}

	private ConcurrentQueue<(string, MString, IMUSHCodeParser)> Pipe { get; } = new();

	public async Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken)
	{
		if (stoppingToken.IsCancellationRequested) return;
		if (!Pipe.TryDequeue(out var result)) return;
		
		// await parser.FromState(result.Item3).CommandParse(result.Item1, result.Item2);
		
		await result.Item3.CommandParse(result.Item1, result.Item2);

		Console.WriteLine(result);
	}
}