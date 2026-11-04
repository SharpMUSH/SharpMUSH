using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>The four attributes a built application keeps, and the object that holds them.</summary>
public sealed record BuiltSource(string? Schema, string? Sources, string? Save, string? Required, string? Holder = null);

/// <summary>What the builder saves: the object to write on (<c>me</c>, a dbref or an objid) and the four values.</summary>
public sealed record BuiltSaveRequest(string Object, string Schema, string? Sources, string? Save, string? Required);

/// <summary>
/// Client for the server's built-application API (<c>api/built/{slug}/source</c>): reads and writes what the
/// application builder makes, as the session's character, so it changes only what that character could change.
/// </summary>
public sealed class BuiltApplicationClient(IHttpClientFactory httpClientFactory)
{
	public Task<ApiResult<BuiltSource>> OpenAsync(string slug, string holder) =>
		httpClientFactory.CreateClient("api").GetApiAsync<BuiltSource>(
			$"api/built/{Uri.EscapeDataString(slug)}/source?object={Uri.EscapeDataString(holder)}", "The server sent nothing back.");

	public Task<ApiResult<BuiltSource>> SaveAsync(string slug, BuiltSaveRequest request) =>
		httpClientFactory.CreateClient("api").PutApiAsync<BuiltSaveRequest, BuiltSource>(
			$"api/built/{Uri.EscapeDataString(slug)}/source", request, "The server sent nothing back.");
}
