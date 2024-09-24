using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public interface ITaskScheduler
{
	Task ExecuteAsync(IMUSHCodeParser parser, CancellationToken stoppingToken);
	ValueTask Write(string handle, MString command, ParserState? state);
}