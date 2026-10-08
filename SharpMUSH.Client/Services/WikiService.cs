using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Client.Models;
using System.Net.Http.Json;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Client-side wiki service. All reads and writes go through the server REST API
/// (GET/POST/PUT/DELETE /api/wiki/...) so data survives page reloads and is backed
/// by the same database as the rest of the server.
/// </summary>
public class WikiService(IHttpClientFactory httpClientFactory, ILogger<WikiService> logger)
{
	/// <summary>Concurrent reads of one recent-changes list (the stats tile and the activity widget) share a request.</summary>
	private readonly SingleFlight<string, IReadOnlyList<WikiPageSummary>> _recentFlight = new();

	/// <summary>The sidebar, the index and the article all ask for the category names at once; they share a request.</summary>
	private readonly SingleFlight<string, IReadOnlyDictionary<string, string>> _categoryNamesFlight = new();

	public async ValueTask<FoundResult<WikiArticle>> GetWikiArticle(
		string slug, string? ns = null, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var url = $"api/wiki/ns/{Uri.EscapeDataString(ns ?? "main")}/{Uri.EscapeDataString(slug)}{LangQuery(lang, first: true)}";
			var dto = await http.GetFromJsonAsync<WikiPageDto>(url);
			return dto is null ? new NotFound() : ToArticle(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return new NotFound();
		}
		catch (Exception ex)
		{
			// Not "no such page": the server did not answer (a restart, a dropped connection) or answered
			// wrongly. The page says so and offers to try again, rather than offering to create it.
			logger.LogError(ex, "GetWikiArticle failed for slug={Slug} lang={Lang}", LogSanitizer.Sanitize(slug), LogSanitizer.Sanitize(lang ?? string.Empty));
			return new Error<string>(ex.Message);
		}
	}

	/// <summary>
	/// Returns the most recently updated pages, newest first.
	/// Failures (network, server error) return an empty list — the index UI
	/// simply shows nothing rather than breaking the whole page.
	/// </summary>
	public ValueTask<IReadOnlyList<WikiPageSummary>> GetRecentChangesAsync(int count = 20, string? lang = null)
	{
		var url = $"api/wiki/recent?count={count}{LangQuery(lang, first: false)}";
		return new ValueTask<IReadOnlyList<WikiPageSummary>>(_recentFlight.RunAsync(url, () => FetchRecentChangesAsync(url)));
	}

	private async Task<IReadOnlyList<WikiPageSummary>> FetchRecentChangesAsync(string url)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageSummaryDto>>(url);
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRecentChangesAsync failed");
			return [];
		}
	}

	/// <summary>
	/// The same list with the failure in the type: "no recent changes" and "the server would not say"
	/// are different facts, and the recent-changes page tells them apart.
	/// </summary>
	public async ValueTask<ApiResult<IReadOnlyList<WikiPageSummary>>> GetRecentChangesResultAsync(int count = 20, string? lang = null)
	{
		var result = await httpClientFactory.CreateClient("api")
			.GetApiAsync<List<WikiPageSummaryDto>>($"api/wiki/recent?count={count}{LangQuery(lang, first: false)}", "The server returned no recent changes.");
		return result switch
		{
			List<WikiPageSummaryDto> dtos => dtos.Select(ToSummary).ToList(),
			ApiFailure failure => failure,
		};
	}

	/// <summary>
	/// Lists pages within a namespace, ordered by title. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetNamespacePagesAsync(
		string ns, int skip = 0, int take = 50, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageSummaryDto>>(
				$"api/wiki/ns/{Uri.EscapeDataString(ns)}?skip={skip}&take={take}{LangQuery(lang, first: false)}");
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
		int skip = 0, int take = 50, string? ns = null, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			using var response = await http.GetAsync($"api/wiki/pages?skip={skip}&take={take}{NsQuery(ns, first: false)}{LangQuery(lang, first: false)}");
			response.EnsureSuccessStatusCode();

			var dtos = await response.Content.ReadFromJsonAsync<List<WikiPageSummaryDto>>() ?? [];
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
	/// Page counts by state (published, draft, restricted), counted by the server without listing a page.
	/// The server counts drafts only for a caller who may see them.
	/// </summary>
	public async ValueTask<ApiResult<WikiPageCountsDto>> GetCountsAsync() =>
		await httpClientFactory.CreateClient("api")
			.GetApiAsync<WikiPageCountsDto>("api/wiki/counts", "The server returned no wiki counts.");

	/// <summary>
	/// Lists pages in a category. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetByCategoryAsync(
		string category, int skip = 0, int take = 50, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiPageSummaryDto>>(
				$"api/wiki/category/{Uri.EscapeDataString(category)}?skip={skip}&take={take}{LangQuery(lang, first: false)}");
			return dtos?.Select(ToSummary).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetByCategoryAsync failed for category={Category}", category);
			return [];
		}
	}

	/// <summary>Batch size for <see cref="GetAllByCategoryAsync"/>.</summary>
	private const int CategoryBatch = 200;

	/// <summary>
	/// Every page in a category, read in batches. The server pages over the rows this caller may see,
	/// so a batch shorter than the one asked for is the last. A failed request ends the listing too,
	/// with what was read so far.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiPageSummary>> GetAllByCategoryAsync(string category, string? lang = null)
	{
		var all = new List<WikiPageSummary>();
		for (var skip = 0; ; skip += CategoryBatch)
		{
			var batch = await GetByCategoryAsync(category, skip, CategoryBatch, lang);
			all.AddRange(batch);
			if (batch.Count < CategoryBatch) return all;
		}
	}

	/// <summary>
	/// Returns the revision history for a page, newest first. Failures return an empty list.
	/// </summary>
	public async ValueTask<IReadOnlyList<WikiRevisionInfo>> GetRevisionsAsync(
		string slug, int skip = 0, int take = 20, string? ns = null, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiRevisionDto>>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/revisions?skip={skip}&take={take}&ns={Uri.EscapeDataString(ns ?? "main")}{LangQuery(lang, first: false)}");
			return dtos?.Select(ToRevision).ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRevisionsAsync failed for slug={Slug}", LogSanitizer.Sanitize(slug));
			return [];
		}
	}

	/// <summary>
	/// Returns a single revision snapshot (with full markdown) or None when missing.
	/// </summary>
	public async ValueTask<Maybe<WikiRevisionInfo>> GetRevisionAsync(
		string slug, int revisionNumber, string? ns = null, string? lang = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dto = await http.GetFromJsonAsync<WikiRevisionDto>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/revisions/{revisionNumber}{KeyQuery(ns)}{LangQuery(lang, first: false)}");
			return dto is null ? new None() : ToRevision(dto);
		}
		catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
		{
			return new None();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetRevisionAsync failed for slug={Slug} rev={Rev}", LogSanitizer.Sanitize(slug), revisionNumber);
			return new None();
		}
	}

	/// <summary>Locales this reader can read the page in, excluding drafts they may not see.</summary>
	public async ValueTask<IReadOnlyList<WikiTranslationInfo>> GetTranslationsAsync(
		string slug, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dtos = await http.GetFromJsonAsync<List<WikiTranslationSummaryDto>>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations{KeyQuery(ns)}");
			return dtos?
				.Select(d => new WikiTranslationInfo(d.Locale, d.Title, d.Published, d.UpdatedAt, d.RevisionNumber))
				.ToList() ?? [];
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "GetTranslationsAsync failed for slug={Slug}", LogSanitizer.Sanitize(slug));
			return [];
		}
	}

	/// <summary>
	/// Creates or updates one locale's translation. <paramref name="expectedRevisionNumber"/> is the revision
	/// the editor loaded; null means create-only.
	/// </summary>
	/// <remarks>
	/// A 409 from the server means somebody else saved first. It comes back as a
	/// <see cref="WikiTranslationSaveError"/> with <c>NeedsReload</c> set, and the caller must offer a reload
	/// rather than retrying — a retry re-sends this editor's stale markdown over the winner's, which is the
	/// data loss the whole compare-and-swap exists to prevent.
	/// </remarks>
	public async ValueTask<TranslationSaveResult> UpsertTranslationAsync(
		string slug, string locale, string title, string markdown, bool published,
		int? expectedRevisionNumber, string? editSummary = null, string? ns = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			using var response = await http.PutAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations/{Uri.EscapeDataString(locale)}{KeyQuery(ns)}",
				new UpsertTranslationRequest(title, markdown, editSummary, published, expectedRevisionNumber));

			if (!response.IsSuccessStatusCode)
				return new WikiTranslationSaveError(
					await response.Content.ReadAsStringAsync(),
					NeedsReload: response.StatusCode == System.Net.HttpStatusCode.Conflict);

			var dto = await response.Content.ReadFromJsonAsync<WikiTranslationSummaryDto>();
			return dto is null
				? new WikiTranslationSaveError("The server returned no translation.", NeedsReload: false)
				: new WikiTranslationInfo(dto.Locale, dto.Title, dto.Published, dto.UpdatedAt, dto.RevisionNumber);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "UpsertTranslationAsync failed for slug={Slug} locale={Locale}", slug, locale);
			return new WikiTranslationSaveError(ex.Message, NeedsReload: false);
		}
	}

	/// <summary>Removes one locale's translation. The page and every other locale are untouched.</summary>
	public async ValueTask<ApiResult<Success>> DeleteTranslationAsync(
		string slug, string locale, string? ns = null) =>
		Logged(await Http.DeleteApiAsync(
				$"api/wiki/{Uri.EscapeDataString(slug)}/translations/{Uri.EscapeDataString(locale)}{KeyQuery(ns)}"),
			"DeleteTranslationAsync", slug);

	/// <summary>
	/// Creates a new wiki page on the server.
	/// Returns the created <see cref="WikiArticle"/>, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<WikiArticle>> CreatePageAsync(
		string title,
		string markdown,
		string? ns = null,
		IReadOnlyList<string>? categories = null) =>
		Logged(Article(await Http.PostApiAsync<CreatePageRequest, WikiPageDto>(
				"api/wiki", new CreatePageRequest(title, markdown, ns, categories), EmptyResponse)),
			"CreatePageAsync", title);

	/// <summary>
	/// Saves updated markdown for an existing page, identified by its URL slug.
	/// Returns the updated <see cref="WikiArticle"/>, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<WikiArticle>> UpdatePageAsync(
		string slug,
		string markdown,
		string? editSummary = null,
		string? ns = null) =>
		Logged(Article(await Http.PutApiAsync<UpdatePageRequest, WikiPageDto>(
				$"api/wiki/{Uri.EscapeDataString(slug)}{KeyQuery(ns)}",
				new UpdatePageRequest(markdown, editSummary), EmptyResponse)),
			"UpdatePageAsync", slug);

	/// <summary>
	/// Sets the categories and published flag on a page identified by slug.
	/// Returns the updated <see cref="WikiArticle"/>, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<WikiArticle>> SetMetadataAsync(
		string slug,
		IReadOnlyList<string> categories,
		bool published,
		string? ns = null) =>
		Logged(Article(await Http.PutApiAsync<SetMetadataRequest, WikiPageDto>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/metadata{KeyQuery(ns)}",
				new SetMetadataRequest(categories, published), EmptyResponse)),
			"SetMetadataAsync", slug);

	/// <summary>
	/// Restores the page body from an earlier revision. The restore is a normal
	/// edit (new revision), so rollbacks are themselves recorded in history.
	/// Returns the updated <see cref="WikiArticle"/>, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<WikiArticle>> RollbackAsync(
		string slug,
		int revisionNumber,
		string? ns = null) =>
		Logged(Article(await Http.PostApiAsync<RollbackRequest, WikiPageDto>(
				$"api/wiki/{Uri.EscapeDataString(slug)}/rollback{KeyQuery(ns)}",
				new RollbackRequest(revisionNumber), EmptyResponse)),
			"RollbackAsync", slug);

	/// <summary>
	/// Each category's name in <paramref name="lang"/> (null: the reader's default), keyed by category key:
	/// the title of the category's page, translated where it is. Pass it to <c>WikiHelpers.CategoryLabel</c>;
	/// a category with no page, or an unreachable or malformed answer, shows its key.
	/// </summary>
	public ValueTask<IReadOnlyDictionary<string, string>> GetCategoryNamesAsync(string? lang = null)
	{
		var url = $"api/wiki/category-names{LangQuery(lang, first: true)}";
		return new ValueTask<IReadOnlyDictionary<string, string>>(_categoryNamesFlight.RunAsync(url, () => FetchCategoryNamesAsync(url)));
	}

	private async Task<IReadOnlyDictionary<string, string>> FetchCategoryNamesAsync(string url)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			return await http.GetFromJsonAsync<Dictionary<string, string>>(url) ?? new Dictionary<string, string>();
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
		{
			logger.LogError(ex, "GetCategoryNamesAsync failed");
			return new Dictionary<string, string>();
		}
	}

	/// <summary>The keys of the categories pinned to the wiki home; only these get a card there.</summary>
	public async ValueTask<ApiResult<List<string>>> GetPinnedCategoriesAsync() =>
		await httpClientFactory.CreateClient("api")
			.GetApiAsync<List<string>>("api/wiki/pinned-categories", "The server returned no pinned wiki categories.");

	/// <summary>Pins <paramref name="category"/> to the wiki home or takes it off (wiki.admin); returns the pinned keys.</summary>
	public async ValueTask<ApiResult<List<string>>> SetCategoryPinnedAsync(string category, bool pinned) =>
		Logged(await Http.PutApiAsync<SetCategoryPinnedRequest, List<string>>(
				$"api/wiki/categories/{Uri.EscapeDataString(category)}/pin",
				new SetCategoryPinnedRequest(pinned), EmptyResponse),
			"SetCategoryPinnedAsync", category);

	/// <summary>Every category the reader can see, with its name and page count, ordered by name.</summary>
	public async ValueTask<ApiResult<List<WikiCategorySummaryDto>>> GetCategoriesAsync(string? lang = null) =>
		await httpClientFactory.CreateClient("api")
			.GetApiAsync<List<WikiCategorySummaryDto>>($"api/wiki/categories{LangQuery(lang, first: true)}",
				"The server returned no wiki categories.");

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
			using var response = await http.PostAsJsonAsync("api/wiki/exists", new ExistsRequest(refArray));
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
	/// Protects or unprotects several pages at once: a page requirement of wiki.admin to edit and delete them.
	/// Returns the per-slug outcome, or the <see cref="ApiFailure"/> that stopped the request.
	/// </summary>
	public async ValueTask<ApiResult<WikiBatchResult>> BatchProtectAsync(
		IEnumerable<string> refs, bool isProtected) =>
		Logged(await Http.PostApiAsync<BatchProtectRequest, WikiBatchResult>(
				"api/wiki/batch/protect", new BatchProtectRequest(refs.ToArray(), isProtected), EmptyResponse),
			"BatchProtectAsync", string.Empty);

	/// <summary>
	/// Deletes multiple pages at once (Wizard only).
	/// Returns the per-slug outcome, or the <see cref="ApiFailure"/> that stopped the request.
	/// </summary>
	public async ValueTask<ApiResult<WikiBatchResult>> BatchDeleteAsync(
		IEnumerable<string> refs) =>
		Logged(await Http.PostApiAsync<BatchDeleteRequest, WikiBatchResult>(
				"api/wiki/batch/delete", new BatchDeleteRequest(refs.ToArray()), EmptyResponse),
			"BatchDeleteAsync", string.Empty);

	/// <summary>
	/// Deletes a single page identified by slug (Wizard only).
	/// Returns <see cref="Success"/>, or the <see cref="ApiFailure"/> that stopped it.
	/// </summary>
	public async ValueTask<ApiResult<Success>> DeletePageAsync(string slug, string? ns = null) =>
		Logged(await Http.DeleteApiAsync($"api/wiki/{Uri.EscapeDataString(slug)}{KeyQuery(ns)}"),
			"DeletePageAsync", slug);

	/// <summary>Every namespace, category and page requirement set (wiki.admin).</summary>
	public async ValueTask<ApiResult<List<WikiRequirementSetDto>>> GetAllRequirementsAsync() =>
		await Http.GetApiAsync<List<WikiRequirementSetDto>>("api/wiki/requirements", EmptyResponse);

	/// <summary>What one namespace, category or page requires; <paramref name="scope"/> is <c>namespace</c>, <c>category</c> or <c>page</c>.</summary>
	public async ValueTask<ApiResult<WikiRequirementSetDto>> GetRequirementsAsync(string scope, string key) =>
		await Http.GetApiAsync<WikiRequirementSetDto>(RequirementsUrl(scope, key), EmptyResponse);

	/// <summary>A page's own requirements and those its namespace and categories add.</summary>
	public async ValueTask<ApiResult<WikiPageRequirementsDto>> GetPageRequirementsAsync(string slug, string? ns = null) =>
		await Http.GetApiAsync<WikiPageRequirementsDto>($"api/wiki/{Uri.EscapeDataString(slug)}/requirements{KeyQuery(ns)}", EmptyResponse);

	/// <summary>
	/// Replaces what the named actions require on a namespace, category or page (wiki.admin); an action with
	/// an empty list requires nothing again, and actions left out keep what they had.
	/// </summary>
	public async ValueTask<ApiResult<WikiRequirementSetDto>> SetRequirementsAsync(
		string scope, string key, IReadOnlyDictionary<string, IReadOnlyList<string>> required) =>
		Logged(await Http.PutApiAsync<SetRequirementsRequest, WikiRequirementSetDto>(
				RequirementsUrl(scope, key), new SetRequirementsRequest(required), EmptyResponse),
			"SetRequirementsAsync", $"{scope} {key}");

	private static string RequirementsUrl(string scope, string key) =>
		$"api/wiki/requirements/{Uri.EscapeDataString(scope)}/{Uri.EscapeDataString(key)}";

	private const string EmptyResponse = "Server returned an empty response.";

	private HttpClient Http => httpClientFactory.CreateClient("api");

	private static ApiResult<WikiArticle> Article(ApiResult<WikiPageDto> result) => result switch
	{
		WikiPageDto dto => ToArticle(dto),
		ApiFailure failure => failure,
	};

	/// <summary>Logs a failed write, naming the call and what it was about, and hands the result on.</summary>
	private ApiResult<T> Logged<T>(ApiResult<T> result, string call, string subject)
	{
		if (result is ApiFailure failure)
			logger.LogError("{Call} failed for {Subject}: {Reason}", call, LogSanitizer.Sanitize(subject), failure.Message);
		return result;
	}

	private static WikiArticle ToArticle(WikiPageDto dto) =>
		new(
			title: dto.Title,
			content: dto.MarkdownSource,
			image: dto.Image,
			renderedHtml: dto.RenderedHtml
		)
		{
			Id = dto.Id,
			LastEditedBy = dto.LastEditedBy,
			UpdatedAt = dto.UpdatedAt,
			Slug = dto.Slug,
			Categories = dto.Categories.ToList(),
			Published = dto.Published,
			IsRestricted = dto.IsRestricted,
			Access = dto.Access,
			Locale = dto.Locale,
			RequestedLocale = dto.RequestedLocale,
			IsFallback = dto.IsFallback,
			AvailableLocales = dto.AvailableLocales.ToList(),
			// The SERVED row's revision number — the translation's when a translation was served, the page's
			// otherwise. WikiEdit passes it back as expectedRevisionNumber, which is why it must come from the
			// same DTO field the server resolved rather than from a separate page lookup.
			RevisionNumber = dto.RevisionNumber,
		};

	/// <summary>Builds the optional <c>?ns=</c> / <c>&amp;ns=</c> query suffix for namespaced requests.</summary>
	private static string NsQuery(string? ns, bool first = true) =>
		ns is null ? string.Empty : $"{(first ? '?' : '&')}ns={Uri.EscapeDataString(ns)}";

	/// <summary>
	/// Builds the <c>ns</c> query suffix that, with the slug, identifies a page for slug-keyed
	/// mutation/revision routes. It is always sent, defaulting to <c>main</c>.
	/// </summary>
	private static string KeyQuery(string? ns) =>
		$"?ns={Uri.EscapeDataString(ns ?? "main")}";

	/// <summary>
	/// Builds the optional <c>lang</c> query suffix. Null or blank sends nothing at all, which the server
	/// reads as "use the configured default" — sending an empty <c>lang=</c> would mean the same thing but
	/// makes the prerender cache key and the browser history noisier for no gain.
	/// </summary>
	private static string LangQuery(string? lang, bool first) =>
		string.IsNullOrWhiteSpace(lang)
			? string.Empty
			: $"{(first ? '?' : '&')}lang={Uri.EscapeDataString(lang)}";

	private static WikiPageSummary ToSummary(WikiPageSummaryDto dto) =>
		new(dto.Slug, dto.Title, dto.Namespace, dto.UpdatedAt, dto.RevisionNumber)
		{
			Categories = dto.Categories,
			Published = dto.Published,
			IsRestricted = dto.IsRestricted,
			Locale = dto.Locale,
			IsFallback = dto.IsFallback,
			Image = dto.Image,
			LastEditedBy = dto.LastEditedBy,
		};

	private static WikiRevisionInfo ToRevision(WikiRevisionDto dto) =>
		new(dto.RevisionNumber, dto.EditorDbref, dto.Timestamp, dto.EditSummary, dto.MarkdownSource) { EditorName = dto.EditorName };
}
