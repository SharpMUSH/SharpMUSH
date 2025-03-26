using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface ITaskScheduler
{
	/// <summary>
	/// Write a user command to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="handle">The identifier for the handle to send to.</param>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	ValueTask WriteUserCommand(string handle, MString command, ParserState state);
	
	/// <summary>
	/// Write a single command to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	ValueTask WriteCommand(MString command, ParserState state);
	
	/// <summary>
	/// Write a commandlist to the scheduler, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	ValueTask WriteCommandList(MString command, ParserState state);

	/// <summary>
	/// Write a commandlist to the scheduler on a semaphore, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="semaphore">A semaphore to evaluate</param>
	ValueTask WriteCommandList(MString command, ParserState state, SemaphoreSlim semaphore);

	/// <summary>
	/// Write a commandlist to the scheduler on a semaphore with a timeout, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="semaphore">A semaphore to evaluate</param>
	/// <param name="timeout">Timeout after which the command is re-queued as a regular command to be immediately run.</param>
	ValueTask WriteCommandList(MString command, ParserState state, SemaphoreSlim semaphore, TimeSpan timeout);
	
	/// <summary>
	/// Write a commandlist to the scheduler on a semaphore with a timeout, to be immediately executed when the scheduler runs.
	/// </summary>
	/// <param name="command">The command to run.</param>
	/// <param name="state">A ParserState to ensure valid parsing.</param>
	/// <param name="delay">Timeout after which the command is re-queued as a regular command to be immediately run.</param>
	ValueTask WriteCommandList(MString command, ParserState state, TimeSpan delay);
}