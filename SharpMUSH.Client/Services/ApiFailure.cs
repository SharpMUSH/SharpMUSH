using System.Net;
using System.Text.Json;

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
/// Kept distinct from a bare <see cref="SharpMUSH.Library.DiscriminatedUnions.Error"/> so a caller CAN tell these apart:
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

	/// <param name="body">
	/// The refusal as the server sent it. Reduced to its sentence by <see cref="ServerSentence"/>; when
	/// it carries none, the status's own sentence stands in.
	/// </param>
	public static ApiFailure FromStatus(HttpStatusCode status, string? body) =>
		ForStatus(status, ServerSentence(body));

	private static ApiFailure ForStatus(HttpStatusCode status, string? serverMessage) => status switch
	{
		HttpStatusCode.NotFound => new ApiFailure(ApiFailureKind.NotFound, serverMessage ?? "Not found.", status),
		HttpStatusCode.Unauthorized =>
			new ApiFailure(ApiFailureKind.Unauthenticated, serverMessage ?? "Your session has expired.", status),
		HttpStatusCode.Forbidden =>
			new ApiFailure(ApiFailureKind.Forbidden, serverMessage ?? "Permission denied.", status),
		_ => new ApiFailure(ApiFailureKind.Unexpected, serverMessage ?? $"Request failed ({(int)status}).", status)
	};

	/// <summary>
	/// The sentence a refusal body carries, or <see langword="null"/> when it carries none.
	/// </summary>
	/// <remarks>
	/// The server words a refusal three ways, and the pages put this straight into a snackbar: an
	/// <c>{ "error": "…" }</c> object, plain text (or a JSON string), and — from a bare
	/// <c>NotFound()</c> or <c>Forbid()</c> — ProblemDetails or no body at all. ProblemDetails'
	/// <c>title</c> is only the status restated, so it is not a reason; its <c>detail</c> is. An
	/// object naming no reason yields <see langword="null"/> rather than its JSON.
	/// </remarks>
	public static string? ServerSentence(string? body)
	{
		if (string.IsNullOrWhiteSpace(body)) return null;

		var trimmed = body.Trim();
		if (trimmed[0] is not ('{' or '"')) return trimmed;

		try
		{
			using var document = JsonDocument.Parse(trimmed);
			var root = document.RootElement;
			if (root.ValueKind == JsonValueKind.String) return NonBlank(root.GetString());
			if (root.ValueKind != JsonValueKind.Object) return trimmed;

			return Property(root, "error") ?? Property(root, "detail");
		}
		catch (JsonException)
		{
			return trimmed;
		}
	}

	private static string? Property(JsonElement root, string name) =>
		root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? NonBlank(value.GetString())
			: null;

	private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
