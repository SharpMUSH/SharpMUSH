using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class CommandOutputCapture : ICommandOutputCapture
{
	// A stack, as in HttpOutputCapture, so a nested capture is well-defined; only the innermost frame copies.
	private static readonly AsyncLocal<ImmutableStack<(int Character, CommandTranscript Transcript)>> Frames = new();

	public IDisposable BeginCapture(int character, CommandTranscript transcript)
	{
		var prior = Frames.Value ?? ImmutableStack<(int, CommandTranscript)>.Empty;
		Frames.Value = prior.Push((character, transcript));
		return new CaptureScope(prior);
	}

	public void Offer(int dbref, string text)
	{
		if (Frames.Value is not { IsEmpty: false } frames) return;

		var (character, transcript) = frames.Peek();
		if (character == dbref) transcript.Append(text);
	}

	private sealed class CaptureScope(ImmutableStack<(int, CommandTranscript)> prior) : IDisposable
	{
		public void Dispose() => Frames.Value = prior;
	}
}
