using SharpMUSH.Library.Models;
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

	/// <summary>
	/// Get all Tasks currently running on the scheduler, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, with either a <see cref="string"/> handle or <see cref="DBRef"/>, and the time/date they are expected to run by.</returns>
	IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf.OneOf<string, DBRef>)[])> GetAllTasks();

	/// <summary>
	/// Get all Tasks currently running on the scheduler for a handle, when they are due, and the handle they are associated with.
	/// Normally, these should only be immediate tasks in the case of a handle.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, and the time/date they are expected to run by.</returns>
	IAsyncEnumerable<(string Group, DateTimeOffset[])> GetTasks(string handle);

	/// <summary>
	/// Get all Tasks currently running on the scheduler for a DBref, when they are due, and the handle they are associated with.
	/// </summary>
	/// <returns>An AsyncEnumerable grouped by type, and the time/date they are expected to run by.</returns>
	IAsyncEnumerable<(string Group, DateTimeOffset[])> GetTasks(DBRef obj);
}