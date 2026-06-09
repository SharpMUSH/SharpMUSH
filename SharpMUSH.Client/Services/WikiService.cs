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
		int RevisionNumber);

	// ── Request bodies (mirrors WikiController request records) ───────────────

	private record CreatePageRequest(string Title, string Markdown, string? Namespace);
	private record UpdatePageRequest(string Markdown, string? EditSummary);

	// ── Read ─────────────────────────────────────────────────────────────────

	public async ValueTask<OneOf<WikiArticle, None>> GetWikiArticle(string slug)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var dto = await http.GetFromJsonAsync<WikiPageDto>($"api/wiki/{Uri.EscapeDataString(slug)}");
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
	/// Saves updated markdown for an existing page identified by its server-side ID.
	/// Returns the updated <see cref="WikiArticle"/> or a string error message.
	/// </summary>
	public async ValueTask<OneOf<WikiArticle, string>> UpdatePageAsync(
		string id,
		string markdown,
		string? editSummary = null)
	{
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.PutAsJsonAsync(
				$"api/wiki/{Uri.EscapeDataString(id)}",
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
			logger.LogError(ex, "UpdatePageAsync failed for id={Id}", id);
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
		};
}
