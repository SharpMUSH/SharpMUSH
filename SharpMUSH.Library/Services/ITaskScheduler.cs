using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface ITaskScheduler
{
	ValueTask WriteUserCommand(string handle, MString command, ParserState? state);
	ValueTask WriteCommand(MString command, ParserState? state);
	ValueTask WriteCommandList(MString command, ParserState? state);
	ValueTask WriteCommandList(MString command, ParserState? state, SemaphoreSlim semaphore);
	ValueTask WriteCommandList(MString command, ParserState? state, TimeSpan cron);
}