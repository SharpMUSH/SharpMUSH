namespace SharpMUSH.Server.Services;

/// <summary>
/// The stock HTTP verb attributes (GET/POST/…) seeded onto the default http_handler object (#4).
/// Each verb routes by URL path to a backtick-namespaced sub-attribute and is intentionally thin:
///
///   GET /api/users?name=Joe   ⇒   @include me/GET`API`USERS=name=Joe,&lt;body&gt;
///
/// The verb attribute (run by the engine with %0 = path+query, %1 = body — see help sharphttp):
///  1. splits %0 into the path (leading slash and query stripped, trailing slashes trimmed) and
///     the raw query string,
///  2. formq()'s the query string so sub-handlers can read %q&lt;form.*&gt; named parameters
///     (%q&lt;fields&gt; lists the names; the body stays raw in %1 — sub-handlers can formq(%1) or
///     json_query(%1) themselves based on %q&lt;hdr.content-type&gt;),
///  3. @includes the sub-attribute named VERB`PATH`SEGMENTS, passing %0 = query string and
///     %1 = body.
///
/// Penn-raw by design: there is no existence guard, so a request with no matching sub-attribute
/// (or an unroutable path like bare "/", whose mapped attribute name is invalid) returns 200 with
/// @include's error text as the body — exactly what naive PennMUSH softcode would do. Games that
/// want clean 404s can edit these attributes (they are seeded once and never overwritten).
/// </summary>
public static class DefaultHttpVerbSoftcode
{
	private static readonly string[] Verbs = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD"];

	/// <summary>(attribute name, MUSHcode) pairs for each HTTP verb.</summary>
	public static readonly IReadOnlyList<(string Attribute, string Code)> Attributes =
		[.. Verbs.Select(verb => (verb, CodeFor(verb)))];

	/// <summary>The routing command list for one verb. Exposed so tests can seed it explicitly.</summary>
	// Note: fields uses rest(%0,?) directly rather than %q<params> — setq pre-evaluates all its
	// arguments, so %q<params> would still be empty while the same call is setting it.
	public static string CodeFor(string verb) =>
		"think setq(path,trim(before(rest(%0,/),?),/,r),params,rest(%0,?),fields,formq(rest(%0,?))); " +
		$"@include me/{verb}`[edit(%q<path>,/,`)]=%q<params>,%1";
}
