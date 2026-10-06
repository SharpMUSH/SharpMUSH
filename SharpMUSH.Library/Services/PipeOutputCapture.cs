using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class PipeOutputCapture : IPipeOutputCapture
{
	// A stack, as in HttpOutputCapture: a piped command whose own action list pipes again takes the inner
	// command's output into the inner frame, and only the innermost frame captures.
	private static readonly AsyncLocal<ImmutableStack<(int Executor, PipeBuffer Buffer)>> Frames = new();

	public IDisposable BeginCapture(int executor, PipeBuffer buffer)
	{
		var prior = Frames.Value ?? ImmutableStack<(int, PipeBuffer)>.Empty;
		Frames.Value = prior.Push((executor, buffer));
		return new CaptureScope(prior);
	}

	public bool TryCapture(int dbref, MString text)
	{
		if (Frames.Value is not { IsEmpty: false } frames) return false;

		var (executor, buffer) = frames.Peek();
		if (executor != dbref) return false;

		buffer.Append(text);
		return true;
	}

	public bool Captures(int dbref)
		=> Frames.Value is { IsEmpty: false } frames && frames.Peek().Executor == dbref;

	private sealed class CaptureScope(ImmutableStack<(int, PipeBuffer)> prior) : IDisposable
	{
		public void Dispose() => Frames.Value = prior;
	}
}
