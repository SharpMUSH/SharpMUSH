using System.Collections.Immutable;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Database.Lightning.Semantics;

internal enum WikiLegacyMutationKind { Create = 1, Body = 2, Title = 3, Metadata = 4, Delete = 5 }
internal sealed record IntegratedWikiReceipt(Guid MutationId, string RequestDigest, string PageId,
	long StateVersion, long CommitSequence, int BodyRevision, WikiLegacyMutationKind Kind);
internal sealed record IntegratedWikiCommitted(IntegratedWikiReceipt Receipt, WikiPage Page);
internal sealed record IntegratedWikiDeleted(IntegratedWikiReceipt Receipt);
internal sealed record IntegratedWikiReplayed(IntegratedWikiReceipt Receipt);
internal sealed record IntegratedWikiDuplicate(string Namespace, string Slug);
internal union IntegratedWikiCommitResult(IntegratedWikiCommitted, IntegratedWikiDeleted, IntegratedWikiReplayed, IntegratedWikiDuplicate, NotFound);

/// <summary>Closed immutable normalized request. The caller owns its one scope until the actual parent completes.</summary>
internal abstract class PreparedLegacyWikiMutation
{
	private PreparedLegacyWikiMutation(LegacyWikiPreparationScope scope, WikiLegacyMutationKind kind, string digest)
		=> (Scope, Kind, RequestDigest, MutationId) = (scope, kind, digest, Guid.NewGuid());
	internal LegacyWikiPreparationScope Scope { get; }
	internal WikiLegacyMutationKind Kind { get; }
	internal string RequestDigest { get; }
	internal Guid MutationId { get; private set; }
	// Internal recovery/test entry: restore request identity only after ordinary bounded preparation
	// has rebuilt all fields and the digest in a fresh live scope. Never reuse the old scope.
	internal static PreparedLegacyWikiMutation RestoreForReplay(LegacyWikiPreparationScope scope,
		PreparedLegacyWikiMutation request, Guid mutationId)
	{
		if (mutationId == Guid.Empty) throw new ArgumentException("A restored mutation needs an identity.", nameof(mutationId));
		PreparedLegacyWikiMutation restored = request switch
		{
			Create create => Create.Prepare(scope, create.Page, create.EditSummary),
			Body body => Body.Prepare(scope, body.Id, body.Value, body.Editor, body.EditSummary, body.At),
			Title title => Title.Prepare(scope, title.Id, title.Value),
			Metadata metadata => Metadata.Prepare(scope, metadata.Id, metadata.Categories, metadata.Published),
			Delete delete => Delete.Prepare(scope, delete.Id),
			_ => throw new ArgumentException("Unknown restored request.", nameof(request))
		};
		restored.MutationId = mutationId;
		return restored;
	}
	internal sealed class Delete : PreparedLegacyWikiMutation
	{
		private Delete(LegacyWikiPreparationScope scope, string id, string digest)
			: base(scope, WikiLegacyMutationKind.Delete, digest) => Id = id;
		internal string Id { get; }
		internal static Delete Prepare(LegacyWikiPreparationScope scope, string id)
		{
			id = scope.Target(id);
			return new(scope, id, scope.Digest(WikiLegacyMutationKind.Delete, [id, string.Empty]));
		}
	}
	internal sealed class Create : PreparedLegacyWikiMutation
	{
		private Create(LegacyWikiPreparationScope scope, WikiPage page, ImmutableArray<string> categories, string? summary, string digest)
			: base(scope, WikiLegacyMutationKind.Create, digest) => (Page, Categories, EditSummary) = (page, categories, summary);
		internal WikiPage Page { get; }
		internal ImmutableArray<string> Categories { get; }
		internal string? EditSummary { get; }
		internal static Create Prepare(LegacyWikiPreparationScope scope, WikiPage page, string? summary)
		{
			if (string.IsNullOrWhiteSpace(page.Slug) || string.IsNullOrWhiteSpace(page.Title) || string.IsNullOrWhiteSpace(page.Namespace))
				throw new WikiSemanticCapacityException("caller-metadata");
			foreach (var value in new[] { page.Slug, page.Title, page.Namespace, page.MarkdownSource, page.RenderedHtml, page.PlainText,
				page.AuthorDbref, page.LastEditorDbref, page.SourceLocale }) scope.Retain(value);
			if (summary is not null) scope.Retain(summary);
			var categories = scope.Categories(page.Categories);
			scope.Lifetime.ReserveAdditional(checked(1024 + 16L * categories.Length));
			// The frozen DTO retains no caller-owned category container or caller-selected not-yet-allocated ID.
			var frozen = page with { Id = string.Empty, Categories = categories };
			var fields = new List<string?> { page.Slug, page.Title, page.Namespace, page.MarkdownSource, page.RenderedHtml, page.PlainText,
				page.AuthorDbref, page.LastEditorDbref, page.SourceLocale, page.CreatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
				page.UpdatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture), page.Published ? "1" : "0", summary, string.Empty,
				categories.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) };
			fields.AddRange(categories);
			return new(scope, frozen, categories, summary, scope.Digest(WikiLegacyMutationKind.Create, fields));
		}
	}
	internal sealed class Body : PreparedLegacyWikiMutation
	{
		private Body(LegacyWikiPreparationScope scope, string id, WikiBody body, string editor, string? summary, DateTimeOffset at, string digest)
			: base(scope, WikiLegacyMutationKind.Body, digest) => (Id, Value, Editor, EditSummary, At) = (id, body, editor, summary, at);
		internal string Id { get; }
		internal WikiBody Value { get; }
		internal string Editor { get; }
		internal string? EditSummary { get; }
		internal DateTimeOffset At { get; }
		internal static Body Prepare(LegacyWikiPreparationScope scope, string id, WikiBody body, string editor, string? summary, DateTimeOffset at)
		{
			id = scope.Target(id);
			foreach (var value in new[] { body.Markdown, body.Html, body.PlainText, editor }) scope.Retain(value);
			if (summary is not null) scope.Retain(summary);
			return new(scope, id, body, editor, summary, at, scope.Digest(WikiLegacyMutationKind.Body,
				[id, body.Markdown, body.Html, body.PlainText, editor, summary, at.ToString("O", System.Globalization.CultureInfo.InvariantCulture), string.Empty]));
		}
	}
	internal sealed class Title : PreparedLegacyWikiMutation
	{
		private Title(LegacyWikiPreparationScope scope, string id, string value, string digest)
			: base(scope, WikiLegacyMutationKind.Title, digest) => (Id, Value) = (id, value);
		internal string Id { get; }
		internal string Value { get; }
		internal static Title Prepare(LegacyWikiPreparationScope scope, string id, string value)
		{
			if (string.IsNullOrWhiteSpace(value)) throw new WikiSemanticCapacityException("caller-title");
			id = scope.Target(id); scope.Retain(value);
			return new(scope, id, value, scope.Digest(WikiLegacyMutationKind.Title, [id, value, string.Empty]));
		}
	}
	internal sealed class Metadata : PreparedLegacyWikiMutation
	{
		private Metadata(LegacyWikiPreparationScope scope, string id, ImmutableArray<string> categories, bool published, string digest)
			: base(scope, WikiLegacyMutationKind.Metadata, digest) => (Id, Categories, Published) = (id, categories, published);
		internal string Id { get; }
		internal ImmutableArray<string> Categories { get; }
		internal bool Published { get; }
		internal static Metadata Prepare(LegacyWikiPreparationScope scope, string id, IReadOnlyList<string> categories, bool published)
		{
			id = scope.Target(id);
			var frozen = scope.Categories(categories);
			scope.Lifetime.ReserveAdditional(checked(256 + 16L * frozen.Length));
			return new(scope, id, frozen, published, scope.Digest(WikiLegacyMutationKind.Metadata,
				new[] { id, published ? "1" : "0", string.Empty, frozen.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(frozen)));
		}
	}
}
