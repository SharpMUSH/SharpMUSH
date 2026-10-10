using System.Text.RegularExpressions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Models.InputSessions;

/// <summary>A generation-bound capture, carrying full identities rather than reusable object numbers.</summary>
/// <param name="Routes">
/// What each line runs, tried in order; the first is the exit, which ends the session before its
/// attribute runs, and is also what runs at the timeout.
/// </param>
public sealed record InputSession(
	Guid Id,
	IConnectionService.ConnectionData Connection,
	string? TransportSessionId,
	DBRef Character,
	DBRef Executor,
	DBRef Owner,
	IReadOnlyList<InputRoute> Routes,
	DateTimeOffset ExpiresAt)
{
	/// <summary>The route whose match ends the session.</summary>
	public InputRoute Exit => Routes[0];
}

/// <summary>How a session's patterns match a line: exactly (trimmed, any case), as wildcards, or as regexps.</summary>
public enum InputMatch { Exact, Wild, Regex }

/// <summary>A pattern as <c>@input/start</c> was given it, and the attribute a matching line runs.</summary>
public readonly record struct InputRouteSpec(string Pattern, DBRef Target, string Attribute);

/// <summary>
/// One pattern of a session and the attribute a line it matches runs, on <paramref name="Target"/>,
/// owned by <paramref name="TargetOwner"/> when the session started. <paramref name="Matcher"/> is the
/// compiled wildcard or regexp (<paramref name="IsRegex"/> says which, for numbering its captures); it is
/// null for an exact pattern and for <c>*</c>, which takes any line.
/// </summary>
public sealed record InputRoute(string Pattern, Regex? Matcher, bool IsRegex, DBRef Target, DBRef TargetOwner, string Attribute)
{
	public bool IsAny => Pattern == "*";
}
