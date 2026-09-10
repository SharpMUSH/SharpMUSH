using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class HttpOutputCapture : IHttpOutputCapture
{
	/// <summary>
	/// Maximum HTTP response body size, mirroring PennMUSH's BUFFER_LEN cap on the
	/// <c>http_request.response</c> buffer. Output past this point is silently dropped.
	/// </summary>
	public const int MaxBodyLength = 8192;

	// A stack (rather than a single frame) keeps nested captures well-defined if a handler's
	// softcode ever triggers another in-process dispatch; only the innermost frame captures.
	private static readonly AsyncLocal<ImmutableStack<(int Dbref, HttpResponseContext Context)>> Frames = new();

	public IDisposable BeginCapture(int handlerDbref, HttpResponseContext context)
	{
		var prior = Frames.Value ?? ImmutableStack<(int, HttpResponseContext)>.Empty;
		Frames.Value = prior.Push((handlerDbref, context));
		return new CaptureScope(prior);
	}

	public bool TryCapture(int dbref, string text)
	{
		var frames = Frames.Value;
		if (frames is null || frames.IsEmpty)
		{
			return false;
		}

		var (handlerDbref, context) = frames.Peek();
		if (handlerDbref != dbref)
		{
			return false;
		}

		// Penn appends each queued write verbatim; our notify layer hands us whole messages,
		// so terminate each with a newline to keep multi-think output line-shaped.
		var remaining = MaxBodyLength - context.Body.Length;
		if (remaining > text.Length)
		{
			context.Body.Append(text).Append('\n');
		}
		else if (remaining > 0)
		{
			context.Body.Append(text, 0, remaining);
		}

		// Even when the buffer is full we report captured: the output was directed at the
		// HTTP handler and must not leak to a connection that does not exist.
		return true;
	}

	private sealed class CaptureScope(ImmutableStack<(int, HttpResponseContext)> prior) : IDisposable
	{
		public void Dispose() => Frames.Value = prior;
	}
}
