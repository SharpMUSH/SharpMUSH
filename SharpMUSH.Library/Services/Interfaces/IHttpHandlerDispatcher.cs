using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Dispatches inbound portal HTTP requests to the configured <c>http_handler</c> object's
/// MUSHcode, mirroring PennMUSH's HTTP handler model. The handler attribute is evaluated with the
/// request bound to stack args — <c>%0</c>=method, <c>%1</c>=path, <c>%2</c>=query, <c>%3</c>=body,
/// <c>%4</c>=viewer dbref — and its evaluated return value is the response body (typically JSON).
/// The handler softcode owns all visibility/permission decisions.
/// </summary>
public interface IHttpHandlerDispatcher
{
	/// <summary>
	/// Evaluates <paramref name="attribute"/> (e.g. <c>HTTP`PROFILE`GET</c>) on the configured
	/// <c>http_handler</c> object and returns its evaluated body.
	/// Returns <see cref="NotFound"/> when no handler is configured, the handler object is missing,
	/// or the handler lacks the requested attribute.
	/// </summary>
	ValueTask<OneOf<string, NotFound>> DispatchAsync(
		string attribute,
		string method,
		string path,
		string query,
		string body,
		DBRef viewer,
		CancellationToken ct = default);
}
