using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>The four attributes a built application keeps, and the object that holds them.</summary>
public sealed record BuiltSource(string? Schema, string? Sources, string? Save, string? Required, string? Holder = null);

/// <summary>What the builder saves: the object to write on (<c>me</c>, a dbref or an objid) and the four values.</summary>
public sealed record BuiltSaveRequest(string Object, string Schema, string? Sources, string? Save, string? Required);

/// <summary>An object the builder can save on, as the caller sees it, and the applications it holds.</summary>
public sealed record BuiltShelf(string Holder, string Name, bool Controls, IReadOnlyList<BuiltEntry> Apps);

/// <summary>One application on an object, and which of its leaves (<c>SOURCES</c>, <c>SAVE</c>, <c>REQUIRED</c>) are set.</summary>
public sealed record BuiltEntry(string Slug, string Title, string Kind, IReadOnlyList<string> Parts);

/// <summary>A map of key → attribute to read, as the caller, from an object (<c>me</c> when none is named).</summary>
public sealed record BuiltValuesRequest(string? Object, IReadOnlyDictionary<string, string> Sources);

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

	/// <summary>The applications <paramref name="holder"/> holds, and whether the caller controls it.</summary>
	public Task<ApiResult<BuiltShelf>> ShelfAsync(string holder) =>
		httpClientFactory.CreateClient("api").GetApiAsync<BuiltShelf>(
			$"api/built?object={Uri.EscapeDataString(holder)}", "The server sent nothing back.");

	/// <summary>What each attribute of <paramref name="sources"/> holds on <paramref name="holder"/>, read as the caller.</summary>
	public Task<ApiResult<SchemaData>> ValuesAsync(string? holder, IReadOnlyDictionary<string, string> sources) =>
		httpClientFactory.CreateClient("api").PostApiAsync<BuiltValuesRequest, SchemaData>(
			"api/built/values", new BuiltValuesRequest(holder, sources), "The server sent nothing back.");
}
