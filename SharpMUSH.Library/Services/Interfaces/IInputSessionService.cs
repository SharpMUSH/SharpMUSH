using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IInputSessionService
{
	InputSession? GetCapturing(long handle);
	ValueTask<string?> StartAsync(IMUSHCodeParser parser, DBRef target, string attribute, MString prompt, TimeSpan timeout);
	ValueTask<string?> PromptAsync(IMUSHCodeParser parser, MString prompt);
	ValueTask<string?> CancelAsync(IMUSHCodeParser parser);
	ValueTask<bool> TryEscapeAsync(long handle, string? transportSessionId, MString input);
	IReadOnlyList<InputSession> TakeExpired();
	void Discard(InputSession session);
	ValueTask<CallState?> DeliverAsync(IMUSHCodeParser parser, InputSession session, MString input, bool timeout = false);
}
