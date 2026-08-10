using System.Net;

namespace SharpMUSH.Client.Services;

/// <summary>Why a call to the game's REST API did not produce a value.</summary>
public enum ApiFailureKind
{
	/// <summary>The server answered 404. The object or attribute is not there.</summary>
	NotFound,

	/// <summary>
	/// Nobody is signed in — the session is missing or expired. Distinct from
	/// <see cref="Forbidden"/> because the remedy is to authenticate again, not to give up.
	/// </summary>
	Unauthenticated,

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
/// Kept distinct from a bare <see cref="OneOf.Types.Error"/> so a caller CAN tell these apart:
/// "no such attribute", "you may not read that", "you are not signed in" and "the server is down"
/// are four different facts, and collapsing them into <see langword="null"/> is what this type
/// replaced. <see cref="Message"/> is what the editor currently renders; <see cref="Kind"/> is
/// there for callers that need to act differently — re-authenticating on
/// <see cref="ApiFailureKind.Unauthenticated"/>, for instance — rather than only report.
/// </remarks>
/// <param name="Kind">Which category of failure this was.</param>
/// <param name="Message">Human-readable detail — the engine's own refusal text where it sent one.</param>
/// <param name="Status">The HTTP status, when there was a response at all.</param>
public sealed record ApiFailure(ApiFailureKind Kind, string Message, HttpStatusCode? Status = null)
{
	public static ApiFailure Transport(Exception ex) =>
		new(ApiFailureKind.Transport, $"Could not reach the server: {ex.Message}");

	/// <summary>A response arrived but could not be used — an unreadable or malformed body.</summary>
	public static ApiFailure Malformed(Exception ex, HttpStatusCode? status = null) =>
		new(ApiFailureKind.Unexpected, $"The server's response could not be read: {ex.Message}", status);

	public static ApiFailure FromStatus(HttpStatusCode status, string? serverMessage) => status switch
	{
		HttpStatusCode.NotFound => new ApiFailure(ApiFailureKind.NotFound, serverMessage ?? "Not found.", status),
		HttpStatusCode.Unauthorized =>
			new ApiFailure(ApiFailureKind.Unauthenticated, serverMessage ?? "Your session has expired.", status),
		HttpStatusCode.Forbidden =>
			new ApiFailure(ApiFailureKind.Forbidden, serverMessage ?? "Permission denied.", status),
		_ => new ApiFailure(ApiFailureKind.Unexpected, serverMessage ?? $"Request failed ({(int)status}).", status)
	};
}
