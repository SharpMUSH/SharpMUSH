using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public class HttpOutputCapture : IHttpOutputCapture
{
	/// <summary>
	/// Maximum HTTP response body size in UTF-16 code units. HTTP capture shares the
	/// evaluator's output ceiling instead of PennMUSH's legacy BUFFER_LEN.
	/// </summary>
	public const int MaxBodyLength = FunctionLimits.MaxOutputCodeUnits;

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
		if (context.OutputLimitExceeded)
		{
			return true;
		}

		// Penn appends each queued write verbatim; our notify layer hands us whole messages,
		// so terminate each with a newline to keep multi-think output line-shaped.
		var required = text.Length + 1L;
		var remaining = MaxBodyLength - context.Body.Length;
		if (required <= remaining)
		{
			context.Body.Append(text).Append('\n');
		}
		else
		{
			context.OutputLimitExceeded = true;
			context.Body.Clear();
		}

		// Even after the limit is exceeded we report captured: the output was directed at the
		// HTTP handler and must not leak to a connection that does not exist.
		return true;
	}

	private sealed class CaptureScope(ImmutableStack<(int, HttpResponseContext)> prior) : IDisposable
	{
		public void Dispose() => Frames.Value = prior;
	}
}
