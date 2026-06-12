using OneOf;
using OneOf.Types;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The assembled result of running an inbound HTTP request through the http_handler's
/// <c>&lt;METHOD&gt;</c> attribute — the outbound half of PennMUSH's <c>struct http_request</c>
/// (code / ctype / headers / response).
/// </summary>
/// <param name="Status">HTTP status code (default 200).</param>
/// <param name="ReasonPhrase">Status reason text (default "OK"); set by <c>@respond &lt;code&gt; &lt;text&gt;</c>.</param>
/// <param name="ContentType">Content-Type (default "text/plain"); set by <c>@respond/type</c>.</param>
/// <param name="Headers">Additional headers added by <c>@respond/header</c>.</param>
/// <param name="Body">Everything the handler emitted to itself (think/@pemit/…) during execution.</param>
public record HttpHandlerResult(
	int Status,
	string ReasonPhrase,
	string ContentType,
	IReadOnlyList<(string Name, string Value)> Headers,
	string Body);

/// <summary>
/// Dispatches an inbound HTTP request to the configured <c>http_handler</c> object by running its
/// <c>&lt;METHOD&gt;</c> attribute (GET/POST/…) <b>as a command list</b> — the equivalent of
/// PennMUSH's invisible-login + <c>@include #handler/&lt;method&gt;</c> (src/cque.c
/// <c>run_http_command</c>). <c>%0</c> = path (including query string), <c>%1</c> = request body,
/// headers arrive as <c>%q&lt;hdr.name&gt;</c> q-registers plus a <c>%q&lt;headers&gt;</c> name list.
/// Output the handler emits to itself becomes the response body; <c>@respond</c> shapes the
/// status line, content type, and headers.
///
/// This supersedes the function-evaluation model of <see cref="IHttpHandlerDispatcher"/> for raw
/// HTTP routes; see help sharphttp.
/// </summary>
public interface IHttpHandlerCommandDispatcher
{
	/// <summary>
	/// Runs the request through the handler. Returns <see cref="NotFound"/> when no
	/// <c>http_handler</c> is configured or the handler has no <c>&lt;METHOD&gt;</c> attribute
	/// (SharpMUSH deviates from PennMUSH's 200-empty here by design — see help sharphttp).
	/// </summary>
	/// <param name="method">HTTP method (case-insensitive; matched to the attribute name uppercased).</param>
	/// <param name="path">Request path including the query string, e.g. <c>/foo?bar=baz</c>. Becomes <c>%0</c>.</param>
	/// <param name="body">Raw request body. Becomes <c>%1</c>.</param>
	/// <param name="headers">Request headers; duplicates are joined with <c>%r</c> in one q-register.</param>
	ValueTask<OneOf<HttpHandlerResult, NotFound>> DispatchAsync(
		string method,
		string path,
		string body,
		IEnumerable<(string Name, string Value)> headers,
		CancellationToken ct = default);
}
