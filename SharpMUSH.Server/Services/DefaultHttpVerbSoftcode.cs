namespace SharpMUSH.Server.Services;

/// <summary>
/// The stock HTTP verb attributes (GET/POST/…) seeded onto the default http_handler object (#4).
/// Each verb routes by URL path to a backtick-namespaced sub-attribute and is intentionally thin:
///
///   GET /api/users?name=Joe   ⇒   @include me/GET`API`USERS=&lt;body&gt;
///
/// The verb attribute (run by the engine with %0 = path+query, %1 = body — see help sharphttp):
///  1. formq()'s the query string so sub-handlers read %q&lt;form.*&gt; named parameters
///     (%q&lt;fields&gt; lists the names),
///  2. @asserts the mapped sub-attribute exists — a request with no matching route (including
///     unroutable paths like bare "/" or trailing slashes, whose mapped attribute names are
///     invalid) answers a clean "404 API NOT FOUND" and stops,
///  3. @includes the sub-attribute VERB`PATH`SEGMENTS, passing %0 = the raw request body
///     (the body stays raw — sub-handlers can formq(%0) or json_query(%0) themselves based on
///     %q&lt;hdr.content-type&gt;; the mapped path is in %q&lt;attrpath&gt;).
///
/// Seeded once and never overwritten — games can edit these attributes freely.
/// </summary>
public static class DefaultHttpVerbSoftcode
{
	private static readonly string[] Verbs = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD"];

	/// <summary>(attribute name, MUSHcode) pairs for each HTTP verb.</summary>
	public static readonly IReadOnlyList<(string Attribute, string Code)> Attributes =
		[.. Verbs.Select(verb => (verb, CodeFor(verb)))];

	/// <summary>The routing command list for one verb. Exposed so tests can seed it explicitly.</summary>
	// 1. formq() decodes the query string into %q<form.*>; setq keeps the returned name list in
	//    %q<fields> (a bare think formq(...) would leak the names into the response body).
	// 2. @assert guards the route: setr() captures the path→backtick mapping in %q<attrpath>,
	//    t() requires it to be non-empty (a bare "/" maps to an empty path, and VERB` with a
	//    trailing backtick would resolve back to the router itself), and hasattr checks the full
	//    VERB`-prefixed attribute. Both checks failing answers a clean 404 and halts the list.
	// 3. The sub-attribute receives %0 = the raw request body.
	public static string CodeFor(string verb) =>
		"think setq(fields,formq(after(%0,?))); " +
		$"@assert cand(t(setr(attrpath,edit(before(rest(%0,/),?),/,`))),hasattr(me,{verb}`%q<attrpath>))=@respond 404 API NOT FOUND; " +
		$"@include me/{verb}`%q<attrpath>=%1";
}
