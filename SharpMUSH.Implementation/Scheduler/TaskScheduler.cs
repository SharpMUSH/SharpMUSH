using System.Collections.Concurrent;

namespace SharpMUSH.Implementation.Scheduler;

/// <summary>
/// Should be a Background Task
/// </summary>
public class TaskScheduler : ITaskScheduler
{
	public TaskScheduler()
	{
		Pipe = new ConcurrentQueue<string>();
	}

	public void Write(string str)
	{
		Pipe.Enqueue(str);
	}

	private ConcurrentQueue<string> Pipe { get; }

	public void ExecuteAsync()
	{
		while (true)
		{
			if (!Pipe.TryDequeue(out var result)) continue;

			Console.WriteLine(result);
		}
	}
}