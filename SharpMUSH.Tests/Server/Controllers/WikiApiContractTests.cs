using System.Text.Json;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The wiki wire contract. These records used to be declared twice — once nested in
/// <c>WikiController</c> and once privately in the browser's <c>WikiService</c> — and the two copies
/// had already drifted: the client had <c>Tags</c> nullable and the four locale fields positional
/// where the server had them init-only. Both ends now bind
/// <see cref="SharpMUSH.Library.API.WikiPageDto"/>, and these tests pin the JSON it produces so a
/// rename cannot pass a compile on one side and silently stop binding on the other.
/// </summary>
public class WikiApiContractTests
{
	/// <summary>The options Blazor's <c>GetFromJsonAsync</c> and ASP.NET Core's JSON output both default to.</summary>
	private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

	private static WikiPageDto SamplePage() => new(
		Id: "page/1", Slug: "welcome", Title: "Welcome", Namespace: "main",
		MarkdownSource: "# Hi", RenderedHtml: "<h1>Hi</h1>", PlainText: "Hi",
		CreatedAt: DateTimeOffset.UnixEpoch, UpdatedAt: DateTimeOffset.UnixEpoch,
		IsProtected: false, RevisionNumber: 3,
		Category: "general", Tags: ["intro", "start"], Published: true)
	{
		Locale = "fr",
		RequestedLocale = "fr-CA",
		IsFallback = true,
		AvailableLocales = ["en", "fr"],
	};

	/// <summary>
	/// Field by field rather than record equality: the two list members compare by reference, so a
	/// perfectly round-tripped DTO is never <c>Equals</c> to the one it came from.
	/// </summary>
	[Test]
	public async Task PageDtoRoundTripsEveryField()
	{
		var original = SamplePage();

		var round = JsonSerializer.Deserialize<WikiPageDto>(JsonSerializer.Serialize(original, Web), Web)!;

		await Assert.That(round with { Tags = [], AvailableLocales = [] })
			.IsEqualTo(original with { Tags = [], AvailableLocales = [] });
		await Assert.That(round.Tags).IsEquivalentTo(original.Tags);
		await Assert.That(round.AvailableLocales).IsEquivalentTo(original.AvailableLocales);
	}

	/// <summary>
	/// The four localization fields are init-only, not constructor parameters. A positional
	/// redeclaration on the far side used to bind them by luck of case-insensitive matching; this
	/// asserts they survive the round trip on their own terms.
	/// </summary>
	[Test]
	public async Task LocalizationFieldsSurviveTheRoundTrip()
	{
		var round = JsonSerializer.Deserialize<WikiPageDto>(JsonSerializer.Serialize(SamplePage(), Web), Web)!;

		await Assert.That(round.Locale).IsEqualTo("fr");
		await Assert.That(round.RequestedLocale).IsEqualTo("fr-CA");
		await Assert.That(round.IsFallback).IsTrue();
		await Assert.That(round.AvailableLocales).IsEquivalentTo(new[] { "en", "fr" });
	}

	/// <summary>
	/// A payload that omits the localization fields binds their defaults rather than nulls — that is
	/// what every non-localized endpoint sends, and the reader renders those values directly.
	/// </summary>
	[Test]
	public async Task OmittedLocalizationFieldsBindDefaults()
	{
		const string json = """
			{"id":"page/1","slug":"welcome","title":"Welcome","namespace":"main",
			 "markdownSource":"# Hi","renderedHtml":"<h1>Hi</h1>","plainText":"Hi",
			 "createdAt":"1970-01-01T00:00:00+00:00","updatedAt":"1970-01-01T00:00:00+00:00",
			 "isProtected":false,"revisionNumber":1,"category":"general","tags":[],"published":true}
			""";

		var page = JsonSerializer.Deserialize<WikiPageDto>(json, Web)!;

		await Assert.That(page.Locale).IsEqualTo(string.Empty);
		await Assert.That(page.RequestedLocale).IsEqualTo(string.Empty);
		await Assert.That(page.IsFallback).IsFalse();
		await Assert.That(page.AvailableLocales).IsEmpty();
	}

	/// <summary>
	/// <c>Tags</c> is the one array bound through the constructor, so an absent or explicitly null
	/// array would otherwise hand the browser a null through a non-nullable property and fault the
	/// page render. It normalises to empty.
	/// </summary>
	[Test]
	[Arguments("""{"id":"i","slug":"s","title":"t","namespace":"main","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"1970-01-01T00:00:00+00:00","updatedAt":"1970-01-01T00:00:00+00:00","isProtected":false,"revisionNumber":1,"category":null,"published":true}""")]
	[Arguments("""{"id":"i","slug":"s","title":"t","namespace":"main","markdownSource":"","renderedHtml":"","plainText":"","createdAt":"1970-01-01T00:00:00+00:00","updatedAt":"1970-01-01T00:00:00+00:00","isProtected":false,"revisionNumber":1,"category":null,"tags":null,"published":true}""")]
	public async Task MissingTagsBindsEmptyNotNull(string json)
	{
		var page = JsonSerializer.Deserialize<WikiPageDto>(json, Web)!;

		await Assert.That(page.Tags).IsNotNull();
		await Assert.That(page.Tags).IsEmpty();
	}

	[Test]
	public async Task RevisionAndTranslationDtosRoundTrip()
	{
		var revision = new WikiRevisionDto(4, "#7:1", DateTimeOffset.UnixEpoch, "typo", "# Hi");
		var translation = new WikiTranslationSummaryDto("fr", "Bienvenue", true, DateTimeOffset.UnixEpoch, 2);

		await Assert.That(JsonSerializer.Deserialize<WikiRevisionDto>(
			JsonSerializer.Serialize(revision, Web), Web)).IsEqualTo(revision);
		await Assert.That(JsonSerializer.Deserialize<WikiTranslationSummaryDto>(
			JsonSerializer.Serialize(translation, Web), Web)).IsEqualTo(translation);
	}

	/// <summary>
	/// The batch endpoints answer with this shape; the admin page reads <c>Succeeded</c>/<c>Failed</c>
	/// straight out of it to build its snackbar line.
	/// </summary>
	[Test]
	public async Task BatchResultRoundTrips()
	{
		var result = new WikiBatchResult(["main/general/a"], ["main/general/b"]);

		var round = JsonSerializer.Deserialize<WikiBatchResult>(JsonSerializer.Serialize(result, Web), Web)!;

		await Assert.That(round.Succeeded).IsEquivalentTo(new[] { "main/general/a" });
		await Assert.That(round.Failed).IsEquivalentTo(new[] { "main/general/b" });
	}
}
