using System.Net;

namespace SharpMUSH.Client.Services;

/// <summary>Why a call to the game's REST API did not produce a value.</summary>
public enum ApiFailureKind
{
	/// <summary>The server answered 404. The object or attribute is not there.</summary>
	NotFound,

	/// <summary>The engine refused: the acting character lacks the permission.</summary>
	Forbidden,

	/// <summary>The request never got an answer — unreachable server, dropped connection, timeout.</summary>
	Transport,

	/// <summary>An answer arrived but was not one we can use: 5xx, or a body we could not read.</summary>
	Unexpected
}

/// <summary>
/// The failed arm of a client API call.
/// </summary>
/// <remarks>
/// Kept distinct from a bare <see cref="OneOf.Types.Error"/> because the caller has to tell these
/// apart: "no such attribute" is an ordinary outcome the editor renders as an empty buffer, while
/// "you may not read that" and "the server is down" are both errors but say different things to the
/// user. Collapsing them into <see langword="null"/> is what this type replaced.
/// </remarks>
/// <param name="Kind">Which category of failure this was.</param>
/// <param name="Message">Human-readable detail — the engine's own refusal text where it sent one.</param>
/// <param name="Status">The HTTP status, when there was a response at all.</param>
public sealed record ApiFailure(ApiFailureKind Kind, string Message, HttpStatusCode? Status = null)
{
	public static ApiFailure Transport(Exception ex) =>
		new(ApiFailureKind.Transport, $"Could not reach the server: {ex.Message}");

	public static ApiFailure FromStatus(HttpStatusCode status, string? serverMessage) => status switch
	{
		HttpStatusCode.NotFound => new ApiFailure(ApiFailureKind.NotFound, serverMessage ?? "Not found.", status),
		HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
			new ApiFailure(ApiFailureKind.Forbidden, serverMessage ?? "Permission denied.", status),
		_ => new ApiFailure(ApiFailureKind.Unexpected, serverMessage ?? $"Request failed ({(int)status}).", status)
	};
}
