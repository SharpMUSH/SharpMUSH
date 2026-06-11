using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Captures output emitted to the http_handler object while an inbound HTTP request is being
/// served, appending it to the response body — the SharpMUSH analog of PennMUSH's
/// <c>CONN_HTTP_BUFFER</c> descriptor flag plus the <c>active_http_request</c> global
/// (pennmush src/bsd.c <c>do_http_command</c> / src/notify.c <c>queue_newwrite</c>).
///
/// PennMUSH is single-threaded and uses a process global; SharpMUSH serves requests
/// concurrently, so the active frame lives in an <see cref="AsyncLocal{T}"/> instead. The
/// AsyncLocal flows across awaits within the request but NOT into queued work (<c>@wait</c>,
/// $-commands), which reproduces Penn's documented behavior: queued output is never sent to
/// the HTTP client.
/// </summary>
public interface IHttpOutputCapture
{
	/// <summary>
	/// Begins capturing output directed at <paramref name="handlerDbref"/> into
	/// <paramref name="context"/>.Body. Dispose the returned scope to stop capturing
	/// (restores any previously active frame).
	/// </summary>
	IDisposable BeginCapture(int handlerDbref, HttpResponseContext context);

	/// <summary>
	/// Offers a piece of output to the active capture frame. Returns <c>true</c> when the
	/// output was directed at the captured handler and has been appended to the response
	/// body (the caller should then skip normal connection delivery), <c>false</c> otherwise.
	/// </summary>
	bool TryCapture(int dbref, string text);
}
