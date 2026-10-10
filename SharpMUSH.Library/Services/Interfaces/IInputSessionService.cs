using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IInputSessionService
{
	InputSession? GetCapturing(long handle);
	InputCaptureSnapshot CapturePendingInput(long handle);
	/// <summary>Opaque transport revision retained after capture ends; changes on each successful start.</summary>
	Guid GetCaptureGeneration(long handle);
	/// <summary>
	/// Opens a session whose callback gets every line typed on the connection. A line equal to
	/// <paramref name="exit"/> (trimmed, ignoring case) ends it and runs the callback once more with <c>exit</c> in %1.
	/// </summary>
	ValueTask<string?> StartAsync(IMUSHCodeParser parser, DBRef target, string attribute, MString prompt, string exit, TimeSpan timeout);
	ValueTask<string?> PromptAsync(IMUSHCodeParser parser, MString prompt);
	ValueTask<string?> CancelAsync(IMUSHCodeParser parser);
	ValueTask<bool> TryEscapeAsync(long handle, string? transportSessionId, MString input, Guid? expectedCapture = null);
	/// <summary>Moves every open session of <paramref name="character"/> to its timeout now; returns how many.</summary>
	ValueTask<int> RescueAsync(DBRef character);
	IReadOnlyList<InputSession> TakeExpired();
	void Discard(InputSession session);
	/// <summary>
	/// Takes down whatever prompt a WebSocket client on <paramref name="handle"/> shows, unless a session is
	/// capturing its lines. The check and the clear's place in the handle's order are taken together, so a
	/// session starting meanwhile publishes its prompt after the clear.
	/// </summary>
	ValueTask ClearPromptUnlessCapturingAsync(long handle);
	ValueTask<CallState?> DeliverAsync(IMUSHCodeParser parser, InputSession session, MString input, bool timeout = false);
}
