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
/// A registered application's document kept beside its route on the <c>http_handler</c> (<c>DATA`PROFILE`SCHEMA</c> for
/// <c>http/profile/schema</c>): where it is, whether the caller may change it, and the package that installed it.
/// </summary>
public sealed record StoredDocument(string Slug, string Name, string Kind, string Holder, string HolderName,
	string Attribute, bool Controls, string? Package, bool Changed);

/// <summary>One stored document and its text.</summary>
public sealed record StoredDocumentSource(StoredDocument Document, string Schema);

/// <summary>The document to save over a stored one.</summary>
public sealed record StoredDocumentSave(string Schema);

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

	/// <summary>The registered applications whose document is kept as it is beside its route, so the builder can edit it.</summary>
	public Task<ApiResult<IReadOnlyList<StoredDocument>>> DocumentsAsync() =>
		httpClientFactory.CreateClient("api").GetApiAsync<IReadOnlyList<StoredDocument>>("api/built-documents", "The server sent nothing back.");

	public Task<ApiResult<StoredDocumentSource>> OpenDocumentAsync(string slug) =>
		httpClientFactory.CreateClient("api").GetApiAsync<StoredDocumentSource>(
			$"api/built-documents/{Uri.EscapeDataString(slug)}", "The server sent nothing back.");

	public Task<ApiResult<StoredDocumentSource>> SaveDocumentAsync(string slug, string schema) =>
		httpClientFactory.CreateClient("api").PutApiAsync<StoredDocumentSave, StoredDocumentSource>(
			$"api/built-documents/{Uri.EscapeDataString(slug)}", new StoredDocumentSave(schema), "The server sent nothing back.");
}
