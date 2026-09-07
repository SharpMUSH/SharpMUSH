using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IWikiService"/>: wiki pages and their revision/translation history. Not ported yet — every
/// member throws <see cref="NotImplementedException"/> until a later task.
/// </summary>
public sealed partial class LightningDatabase
{
	public Task<OneOf<WikiPage, NotFound>> GetBySlugAsync(string slug, string? category, WikiNamespace ns = WikiNamespace.Main)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiPage, NotFound>> GetByIdAsync(string id)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiPage>> GetRecentChangesAsync(int count = 20)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiPage>> GetByNamespaceAsync(WikiNamespace ns, int skip = 0, int take = 50)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiPage>> GetAllPagesAsync(int skip = 0, int take = 50, WikiNamespace? ns = null)
		=> throw new NotImplementedException();

	public Task<int> CountPagesAsync(WikiNamespace? ns, bool includeDrafts)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiPage>> GetByCategoryAsync(string category, int skip = 0, int take = 50)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiPage>> GetByTagAsync(string tag, int skip = 0, int take = 50)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiPage, Error<string>>> CreateAsync(
		string title,
		string markdown,
		string authorDbref,
		WikiNamespace ns = WikiNamespace.Main,
		string? category = null,
		string? sourceLocale = null)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiPage, NotFound>> UpdateAsync(
		string id,
		string markdown,
		string editorDbref,
		string? editSummary = null)
		=> throw new NotImplementedException();

	public Task<OneOf<None, NotFound>> DeleteAsync(string id, string editorDbref)
		=> throw new NotImplementedException();

	public Task<OneOf<None, NotFound>> SetProtectionAsync(string id, bool isProtected)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiPage, NotFound>> SetMetadataAsync(
		string id,
		string? category,
		IReadOnlyList<string> tags,
		bool published)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsAsync(string pageId, int skip = 0, int take = 20)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiRevision, NotFound>> GetRevisionAsync(string pageId, int revisionNumber)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiTranslationSummary>> GetTranslationsAsync(string pageId)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiTranslation>> GetAllTranslationsAsync(int skip = 0, int take = 50)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiTranslation, NotFound>> GetTranslationAsync(string pageId, string locale)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiTranslation, WikiWriteConflict, Error<string>>> UpsertTranslationAsync(
		string pageId,
		string locale,
		string title,
		string markdown,
		string editorDbref,
		string? editSummary,
		bool published,
		int? expectedRevisionNumber)
		=> throw new NotImplementedException();

	public Task<OneOf<None, NotFound>> DeleteTranslationAsync(string pageId, string locale, string editorDbref)
		=> throw new NotImplementedException();

	public Task<IReadOnlyList<WikiRevision>> GetRevisionsForLocaleAsync(string pageId, string locale, int skip, int take)
		=> throw new NotImplementedException();

	public Task<OneOf<WikiRevision, NotFound>> GetRevisionForLocaleAsync(string pageId, string locale, int revisionNumber)
		=> throw new NotImplementedException();
}
