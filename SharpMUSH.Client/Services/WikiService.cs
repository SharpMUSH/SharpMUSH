using OneOf;
using OneOf.Types;
using SharpMUSH.Client.Models;
using System.Net.Http.Json;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side wiki service. All reads and writes go through the server REST API
/// (GET/POST/PUT/DELETE /api/wiki/...) so data survives page reloads and is backed
/// by the same database as the rest of the server.
/// </summary>
public class WikiService(IHttpClientFactory httpClientFactory, ILogger<WikiService> logger)
{
	// ── Server DTO (mirrors WikiController.WikiPageDto) ───────────────────────

	private record WikiPageDto(
		string Id,
		string Slug,
		string Title,
		string Namespace,
		string MarkdownSource,
		string RenderedHtml,
		string PlainText,
		DateTimeOffset CreatedAt,
		DateTimeOffset UpdatedAt,
		bool IsProtected,
		int RevisionNumber,
		string? Category,
		IReadOnlyList<string>? Tags,
		bool Published);

	private record WikiRevisionDto(
		int RevisionNumber,
		string EditorDbref,
		DateTimeOffset Timestamp,
		string? EditSummary,
		string MarkdownSource);

	// ── Request bodies (mirrors WikiController request records) ───────────────

	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record UpdatePageRequest(string Markdown, string? EditSummary);
	private record SetMetadataRequest(string? Category, string[] Tags, bool Published);
	private record RollbackRequest(int RevisionNumber);
	private record ExistsRequest(string[] Refs);
	private record BatchProtectRequest(string[] Slugs, string? Ns, bool IsProtected);
	private record BatchDeleteRequest(string[] Slugs, string? Ns);

	/// <summary>Per-slug outcome of a batch operation (mirrors WikiController.BatchResult).</summary>
	public record WikiBatchResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);

	// ── Read ─────────────────────────────────────────────────────────────────

	public async ValueTask<OneOf<WikiArticle, None>> GetWikiArticle(string slug, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var url = ns is null
				? $"api/wiki/{Uri.EscapeDataString(slug)}"
				: $"api/wiki/ns/{Uri.EscapeDataString(ns)}/{Uri.EscapeDataString(slug)}";
			var dto = await http.GetFromJsonAsync<WikiPageDto>(url);
			return dto is null ? new None() : ToArticle(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return new None();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetWikiArticle failed for slug={Slug}", slug);
			return new None();
		}
	}

	/// <summary>
	/// Returns the most recently updated pages, newest first.
	/// Failures (network, server error) return an empty list — the index UI
	/// simply shows nothing rather than breaking the whole page.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetRecentChangesAsync(int count = 20)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageDto>>($"api/wiki/recent?count={count}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRecentChangesAsync failed");
			return [];
		}
	}

	/// <summary>
	/// Lists pages within a namespace, ordered by title. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetNamespacePagesAsync(
		string ns, int skip = 0, int take = 50)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageDto>>(
				$"api/wiki/ns/{Uri.EscapeDataString(ns)}?skip={skip}&take={take}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetNamespacePagesAsync failed for ns={Namespace}", ns);
			return [];
		}
	}

	/// <summary>
	/// Paginated listing of all wiki pages (optionally restricted to a namespace),
	/// plus the total unpaginated count read from the X-Total-Count response header.
	/// Failures return an empty list with a zero total.
	/// </summary>
	public async ValueTask<(IReadOnlyList<WikiPageSummary> Items, int Total)> GetAllPagesAsync(
		int skip = 0, int take = 50, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync($"api/wiki/pages?skip={skip}&take={take}{NsQuery(ns, first: false)}");
			response.EnsureSuccessStatusCode();

			var dtos = await response.Content.ReadFromJsonAsync<List<WikiPageDto>>() ?? [];
			var total = response.Headers.TryGetValues("X-Total-Count", out var values)
				&& int.TryParse(values.FirstOrDefault(), out var parsed)
				? parsed
				: dtos.Count;
			return (dtos.Select(ToSummary).ToList(), total);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetAllPagesAsync failed (skip={Skip} take={Take} ns={Namespace})", skip, take, ns);
			return ([], 0);
		}
	}

	/// <summary>
	/// Lists pages in a category. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetByCategoryAsync(
		string category, int skip = 0, int take = 50)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageDto>>(
				$"api/wiki/category/{Uri.EscapeDataString(category)}?skip={skip}&take={take}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetByCategoryAsync failed for category={Category}", category);
			return [];
		}
	}

	/// <summary>
	/// Lists pages carrying a tag. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetByTagAsync(
		string tag, int skip = 0, int take = 50)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageDto>>(
				$"api/wiki/tag/{Uri.EscapeDataString(tag)}?skip={skip}&take={take}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetByTagAsync failed for tag={Tag}", tag);
			return [];
		}
	}

	/// <summary>
	/// Returns the revision history for a page, newest first. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiRevisionInfo>> GetRevisionsAsync(
		string slug, int skip = 0, int take = 20, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiRevisionDto>>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/revisions?skip={skip}&take={take}{NsQuery(ns, first: false)}");
			return dtos?.Select(ToRevision).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRevisionsAsync failed for slug={Slug}", slug);
			return [];
		}
	}

	/// <summary>
	/// Returns a single revision snapshot (with full markdown) or None when missing.
	/// </summary>
	public async ValueTask<OneOf<WikiRevisionInfo, None>> GetRevisionAsync(string slug, int revisionNumber, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dto = await http.GetFromJsonAsync<WikiRevisionDto>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/revisions/{revisionNumber}{NsQuery(ns)}");
			return dto is null ? new None() : ToRevision(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return new None();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRevisionAsync failed for slug={Slug} rev={Rev}", slug, revisionNumber);
			return new None();
		}
	}

	// ── Write ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Creates a new wiki page on the server.
	/// Returns the created <see cref="WikiArticle"/> or a string error message.
	/// </summary>
	public async ValueTask<OneOf<WikiArticle, string>> CreatePageAsync(
		string title,
		string markdown,
		string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/wiki", new CreatePageRequest(title, markdown, ns));

			if (response.IsSuccessStatusCode)
			{
				var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
				return dto is null
					? OneOf<WikiArticle, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiArticle, string>.FromT0(ToArticle(dto));
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Create failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "CreatePageAsync failed for title={Title}", title);
			return ex.Message;
		}
	}

	/// <summary>
	/// Saves updated markdown for an existing page, identified by its URL slug.
	/// Slug is used (not the internal DB ID) because ArangoDB IDs contain a '/'
	/// that cannot safely survive URL-encoding through ASP.NET Core routing.
	/// Returns the updated <see cref="WikiArticle"/> or a string error message.
	/// </summary>
	public async ValueTask<OneOf<WikiArticle, string>> UpdatePageAsync(
		string slug,
		string markdown,
		string? editSummary = null,
		string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}{NsQuery(ns)}",
				new UpdatePageRequest(markdown, editSummary));

			if (response.IsSuccessStatusCode)
			{
				var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
				return dto is null
					? OneOf<WikiArticle, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiArticle, string>.FromT0(ToArticle(dto));
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Update failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "UpdatePageAsync failed for slug={Slug}", slug);
			return ex.Message;
		}
	}

	/// <summary>
	/// Sets the category, tags and published flag on a page identified by slug.
	/// Returns the updated <see cref="WikiArticle"/> or a string error message.
	/// </summary>
	public async ValueTask<OneOf<WikiArticle, string>> SetMetadataAsync(
		string slug,
		string? category,
		IEnumerable<string> tags,
		bool published,
		string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/metadata{NsQuery(ns)}",
				new SetMetadataRequest(category, tags.ToArray(), published));

			if (response.IsSuccessStatusCode)
			{
				var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
				return dto is null
					? OneOf<WikiArticle, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiArticle, string>.FromT0(ToArticle(dto));
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Metadata update failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "SetMetadataAsync failed for slug={Slug}", slug);
			return ex.Message;
		}
	}

	/// <summary>
	/// Restores the page body from an earlier revision. The restore is a normal
	/// edit (new revision), so rollbacks are themselves recorded in history.
	/// Returns the updated <see cref="WikiArticle"/> or a string error message.
	/// </summary>
	public async ValueTask<OneOf<WikiArticle, string>> RollbackAsync(
		string slug,
		int revisionNumber,
		string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/rollback{NsQuery(ns)}",
				new RollbackRequest(revisionNumber));

			if (response.IsSuccessStatusCode)
			{
				var dto = await response.Content.ReadFromJsonAsync<WikiPageDto>();
				return dto is null
					? OneOf<WikiArticle, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiArticle, string>.FromT0(ToArticle(dto));
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Rollback failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "RollbackAsync failed for slug={Slug} rev={Rev}", slug, revisionNumber);
			return ex.Message;
		}
	}

	/// <summary>
	/// Batch page-existence check used for redlink rendering. Refs use URL-path
	/// form: "slug" for main-namespace pages, "ns/slug" otherwise. Failures return
	/// an empty map — links simply stay unmarked rather than breaking the page.
	/// </summary>
	public async ValueTask<IReadOnlyDictionary<string, bool>> CheckExistsAsync(IEnumerable<string> refs)
	{
		var refArray = refs.Distinct(StringComparer.Ordinal).ToArray();
		if (refArray.Length == 0)
			return new Dictionary<string, bool>();

		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync("api/wiki/exists", new ExistsRequest(refArray));
			if (!response.IsSuccessStatusCode)
				return new Dictionary<string, bool>();

			var map = await response.Content.ReadFromJsonAsync<Dictionary<string, bool>>();
			return map ?? new Dictionary<string, bool>();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "CheckExistsAsync failed for {Count} refs", refArray.Length);
			return new Dictionary<string, bool>();
		}
	}

	/// <summary>
	/// Sets or clears the protection flag on multiple pages at once (Wizard only).
	/// Returns the per-slug outcome, or a string error message on transport failure.
	/// </summary>
	public async ValueTask<OneOf<WikiBatchResult, string>> BatchProtectAsync(
		IEnumerable<string> slugs, bool isProtected, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync(
				"api/wiki/batch/protect",
				new BatchProtectRequest(slugs.ToArray(), ns, isProtected));

			if (response.IsSuccessStatusCode)
			{
				var result = await response.Content.ReadFromJsonAsync<WikiBatchResult>();
				return result is null
					? OneOf<WikiBatchResult, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiBatchResult, string>.FromT0(result);
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Batch protect failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "BatchProtectAsync failed");
			return ex.Message;
		}
	}

	/// <summary>
	/// Deletes multiple pages at once (Wizard only).
	/// Returns the per-slug outcome, or a string error message on transport failure.
	/// </summary>
	public async ValueTask<OneOf<WikiBatchResult, string>> BatchDeleteAsync(
		IEnumerable<string> slugs, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PostAsJsonAsync(
				"api/wiki/batch/delete",
				new BatchDeleteRequest(slugs.ToArray(), ns));

			if (response.IsSuccessStatusCode)
			{
				var result = await response.Content.ReadFromJsonAsync<WikiBatchResult>();
				return result is null
					? OneOf<WikiBatchResult, string>.FromT1("Server returned an empty response.")
					: OneOf<WikiBatchResult, string>.FromT0(result);
			}

			var body = await response.Content.ReadAsStringAsync();
			return $"Batch delete failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "BatchDeleteAsync failed");
			return ex.Message;
		}
	}

	/// <summary>
	/// Deletes a single page identified by slug (Wizard only).
	/// Returns None on success or a string error message.
	/// </summary>
	public async ValueTask<OneOf<None, string>> DeletePageAsync(string slug, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.DeleteAsync($"api/wiki/{Uri.EscapeDataString(slug)}{NsQuery(ns)}");

			if (response.IsSuccessStatusCode)
				return new None();

			var body = await response.Content.ReadAsStringAsync();
			return $"Delete failed ({(int)response.StatusCode}): {body}";
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "DeletePageAsync failed for slug={Slug}", slug);
			return ex.Message;
		}
	}

	// ── Projection ─────────────────────────────────────────────────────────────

	private static WikiArticle ToArticle(WikiPageDto dto) =>
		new(
			title: dto.Title,
			content: dto.MarkdownSource,
			image: null,
			renderedHtml: dto.RenderedHtml
		)
		{
			Id = dto.Id,
			Slug = dto.Slug,
			Category = dto.Category,
			Tags = dto.Tags?.ToList() ?? [],
			Published = dto.Published,
		};

	/// <summary>Builds the optional <c>?ns=</c> / <c>&amp;ns=</c> query suffix for namespaced requests.</summary>
	private static string NsQuery(string? ns, bool first = true) =>
		ns is null ? string.Empty : $"{(first ? '?' : '&')}ns={Uri.EscapeDataString(ns)}";

	private static WikiPageSummary ToSummary(WikiPageDto dto) =>
		new(dto.Slug, dto.Title, dto.Namespace, dto.UpdatedAt, dto.RevisionNumber)
		{
			Category = dto.Category,
			Tags = dto.Tags ?? [],
			Published = dto.Published,
			IsProtected = dto.IsProtected,
		};

	private static WikiRevisionInfo ToRevision(WikiRevisionDto dto) =>
		new(dto.RevisionNumber, dto.EditorDbref, dto.Timestamp, dto.EditSummary, dto.MarkdownSource);
}
