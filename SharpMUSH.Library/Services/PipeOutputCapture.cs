using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
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

	// @pipe outlives a queue entry, so it is kept by dbref rather than in the async flow.
	private readonly ConcurrentDictionary<int, AttributePipe> _attributePipes = new();

	public bool TryCapture(int dbref, MString text)
	{
		if (Frames.Value is { IsEmpty: false } frames && frames.Peek() is var (executor, buffer) && executor == dbref)
		{
			buffer.Append(text);
			return true;
		}

		// A full @pipe takes nothing more: the rest is shown, as when RhostMUSH turns a full pipe off. The
		// message that fills it is shown whole, so none of it is lost.
		if (!_attributePipes.TryGetValue(dbref, out var pipe) || pipe.Buffer.Truncated) return false;

		return pipe.Buffer.Append(text) && !pipe.Tee;
	}

	public bool Captures(int dbref)
		=> Frames.Value is { IsEmpty: false } frames && frames.Peek().Executor == dbref
			|| _attributePipes.TryGetValue(dbref, out var pipe) && !pipe.Buffer.Truncated;

	public bool StartAttributePipe(int dbref, AttributePipe pipe)
		=> _attributePipes.TryAdd(dbref, pipe);

	public AttributePipe? StopAttributePipe(int dbref)
		=> _attributePipes.TryRemove(dbref, out var pipe) ? pipe : null;

	public AttributePipe? AttributePipeOf(int dbref)
		=> _attributePipes.GetValueOrDefault(dbref);

	private sealed class CaptureScope(ImmutableStack<(int, PipeBuffer)> prior) : IDisposable
	{
		public void Dispose() => Frames.Value = prior;
	}
}
