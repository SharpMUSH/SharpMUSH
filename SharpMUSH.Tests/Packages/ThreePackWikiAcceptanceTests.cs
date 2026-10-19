using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Tests.Packages;

public sealed class ThreePackWikiWorld : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

/// <summary>
/// Owned synthetic packs exercise the current package contract through the actual host. The explicit
/// oracle names pages by fixture role, never by reusing the production collection/visibility algorithm.
/// Families vary authored prose; this suite covers generic metadata, content visibility and lifecycle,
/// not ordered manual execution or semantic secret graphs. Semantic storage is not activated.
/// </summary>
public class ThreePackWikiAcceptanceTests
{
	[ClassDataSource<ThreePackWikiWorld>(Shared = SharedType.PerClass)]
	public required ThreePackWikiWorld World { get; init; }

	private IPackageInstallService Installer => World.Services.GetRequiredService<IPackageInstallService>();
	private IWikiService Wiki => World.Services.GetRequiredService<IWikiService>();
	private IPackageRegistryService Registry => World.Services.GetRequiredService<IPackageRegistryService>();

	// Immutable authored source. Random run IDs qualify slugs, never human-facing titles or labels.
	// Retain these bytes when extending the fixture so corpus changes are visible in review.
	private const string RulesSource = """
		{"label":"Lantern Rules","appendix":"Lantern Options","group":"Actions","intro":"Choose an action, then read its cost and resolution.","body":"Spend one token.\nResolve the check.\nRecover the token at dawn.","translation":"Vue des actions","private":"Unreleased option balance notes."}
		""";
	private const string ManualSource = """
		{"label":"Workshop Manual","appendix":"Workshop Service Notes","group":"Inspection","intro":"Inspect the lathe before choosing an ordered procedure.","body":"Disconnect power.\nInspect the belt.\nVerify the guard before reconnecting.","translation":"Vue de l’inspection","private":"Staff-only isolation procedure."}
		""";
	private const string LoreSource = """
		{"label":"Glass Harbor Gazetteer","appendix":"Glass Harbor Referee Notes","group":"Council","intro":"Meet the public council before reading the district timeline.","body":"The council meets at noon.\nIts public seal is blue.\nThe next festival begins in spring.","translation":"Vue du conseil","private":"The councillor secretly serves the drowned court."}
		""";

	private sealed record AuthoredSource(string Label, string Appendix, string Group, string Intro, string Body,
		string Translation, string Private);

	private sealed record Fixture(string Id, AuthoredSource Source, string SourceSha256)
	{
		public string Primary => Id + "-guide";
		public string Supplement => Id + "-appendix";
		public string Group => Id + "_group";
		public string Overview => Id + "_overview";
		public string Appendix => Id + "_appendix";
		public string Draft => Id + "_draft";
		public string Restricted => Id + "_restricted";
		public string HiddenGroup => Id + "_hidden";
		public string DraftGroup => Id + "_unreleased";
	}

	private static Fixture NewFixture(string family)
	{
		var bytes = family switch
		{
			"rules" => RulesSource,
			"manual" => ManualSource,
			"lore" => LoreSource,
			_ => throw new ArgumentOutOfRangeException(nameof(family))
		};
		var source = JsonSerializer.Deserialize<AuthoredSource>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
			?? throw new InvalidOperationException("The owned fixture source is invalid.");
		return new("three-" + family + "-" + Guid.NewGuid().ToString("N")[..10], source,
			Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bytes))));
	}

	private static PackageManifest Manifest(string id, string label, string version, string pages)
	{
		var parsed = new PackageManifestService().ParseManifest($$"""
			format: 1.13
			package: {{id}}
			version: {{version}}
			locale: en
			wiki_collection_name: {{JsonSerializer.Serialize(label)}}
			wiki:
			{{pages}}
			""").Expect<ParsedPackageManifest>();
		if (parsed.Warnings.Count > 0)
			throw new InvalidOperationException($"Owned fixture '{id}' has manifest warnings: {string.Join("; ", parsed.Warnings.Select(warning => warning.ToString()))}");
		return parsed.Manifest;
	}

	private static string Entry(string slug, string title, string body, string? category = null,
		bool published = true, bool restricted = false, string ns = "main") => $$"""
		  - page: {{ns}}:{{slug}}
		    title: {{JsonSerializer.Serialize(title)}}
		    published: {{(published ? "true" : "false")}}
		    {{(category is null ? "" : "categories: [" + category + "]")}}
		    {{(restricted ? "requires: {view: [wiki.admin]}" : "")}}
		    markdown: {{JsonSerializer.Serialize(body)}}
		""";

	private static PackageManifest Primary(Fixture fixture, string version = "1.0.0", string? body = null)
		=> Manifest(fixture.Primary, fixture.Source.Label, version,
			Entry(fixture.Overview, "Overview", body ?? fixture.Source.Body, fixture.Group) + "\n"
			+ Entry(fixture.Group, fixture.Source.Group, fixture.Source.Intro, ns: "category"));

	private static PackageManifest Supplement(Fixture fixture)
		=> Manifest(fixture.Supplement, fixture.Source.Appendix, "1.0.0",
			Entry(fixture.Appendix, "Overview", "Supplemental public guidance.", fixture.Group) + "\n"
			+ Entry(fixture.Draft, "Working Notes", "DRAFT_SENTINEL", fixture.DraftGroup, published: false) + "\n"
			+ Entry(fixture.Restricted, "Private Notes", fixture.Source.Private, fixture.HiddenGroup, restricted: true));

	private Task<Result<PackageApplyResult>> Apply(PackageManifest manifest, Fixture fixture)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://example.invalid/owned-synthetic-packs", manifest.Name + "/", fixture.SourceSha256, "main"),
			new Dictionary<string, string>(), []));

	private async Task Install(Fixture fixture)
	{
		(await Apply(Primary(fixture), fixture)).Expect<PackageApplyResult>();
		(await Apply(Supplement(fixture), fixture)).Expect<PackageApplyResult>();
	}

	private async Task<WikiPage> Page(string slug, WikiNamespace ns = WikiNamespace.Main)
		=> (await Wiki.GetBySlugAsync(slug, ns)).Expect<WikiPage>();

	private async Task<T> WithReader<T>(Func<WikiCollectionsController, Task<T>> request, params string[] permissions)
	{
		await using var scope = World.Services.CreateAsyncScope();
		var controller = ActivatorUtilities.CreateInstance<WikiCollectionsController>(scope.ServiceProvider);
		var user = permissions.Length == 0 ? new ClaimsPrincipal(new ClaimsIdentity())
			: new ClaimsPrincipal(new ClaimsIdentity(permissions.Select(scope => new Claim(PortalPermission.ClaimType, scope))
				.Append(new Claim(GameHub.CharacterDbrefClaim, "#42")), "fixture-request"));
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext { User = user, RequestServices = scope.ServiceProvider }
		};
		return await request(controller);
	}

	private static T Value<T>(IActionResult response) => response is OkObjectResult { Value: T value }
		? value : throw new InvalidOperationException($"Expected {typeof(T).Name}, received {response.GetType().Name}.");

	private async Task Cleanup(Fixture fixture, string? rival = null)
	{
		foreach (var id in new[] { fixture.Primary, fixture.Supplement, rival }.OfType<string>())
			if (await Registry.GetInstalledPackageAsync(id) is InstalledPackageRecord)
				(await Installer.UninstallAsync(id)).Expect<Success>();
		// A locally edited page deliberately survives uninstall; remove that test-owned survivor too.
		foreach (var (slug, ns) in new[] { (fixture.Overview, WikiNamespace.Main), (fixture.Appendix, WikiNamespace.Main),
			(fixture.Draft, WikiNamespace.Main), (fixture.Restricted, WikiNamespace.Main), (fixture.Group, WikiNamespace.Category) })
			if (await Wiki.GetBySlugAsync(slug, ns) is WikiPage page)
				(await Wiki.DeleteAsync(page.Id, "#1")).Expect<None>();
	}

	[Test]
	[Arguments("rules")]
	[Arguments("manual")]
	[Arguments("lore")]
	public async Task TwoPackagesKeepAuthoredLabelsAndStableOwnershipAcrossUpdateAndRemoval(string family)
	{
		var fixture = NewFixture(family);
		try
		{
			await Install(fixture);
			var original = await Page(fixture.Overview);
			var category = await Page(fixture.Group, WikiNamespace.Category);
			var appendix = await Page(fixture.Appendix);
			await Assert.That(original.Title).IsEqualTo(appendix.Title);
			await Assert.That(original.Id == appendix.Id).IsFalse();
			await Assert.That(category.MarkdownSource).IsEqualTo(fixture.Source.Intro);
			var navigation = Value<WikiNavigationDto>(await WithReader(reader => reader.Navigation("package:" + fixture.Primary)));
			await Assert.That(navigation.Total).IsEqualTo(2);
			await Assert.That(navigation.Collections.Single(row => row.Id == "package:" + fixture.Primary).Name).IsEqualTo(fixture.Source.Label);
			await Assert.That(navigation.Categories.Single(row => row.Key == fixture.Group).Name).IsEqualTo(fixture.Source.Group);
			await Assert.That(navigation.Categories.Single(row => row.Key == fixture.Group).Pages).IsEqualTo(1);
			var staffBody = string.Join('\n', fixture.Source.Body.Split('\n')
				.Select((line, index) => index == 1 ? line + " Local annotation." : line));
			(await Wiki.UpdateAsync(original.Id, staffBody, "#1", "owned fixture local edit")).Expect<WikiPage>();
			(await Apply(Primary(fixture, "1.1.0", fixture.Source.Body + "\nEdition two clarification."), fixture)).Expect<PackageApplyResult>();
			var updated = await Page(fixture.Overview);
			await Assert.That(updated.Id).IsEqualTo(original.Id);
			await Assert.That(updated.MarkdownSource).IsEqualTo(staffBody + "\nEdition two clarification.");
			var ownership = await Registry.GetPackageWikiPagesAsync(fixture.Primary);
			await Assert.That(ownership.Select(row => row.PageId).ToArray()).IsEquivalentTo(new[] { original.Id, category.Id });
			await Assert.That(ownership.Single(row => row.PageId == original.Id).Markdown).IsEqualTo(fixture.Source.Body + "\nEdition two clarification.");
			(await Installer.UninstallAsync(fixture.Primary)).Expect<Success>();
			await Assert.That((await Page(fixture.Overview)).MarkdownSource).IsEqualTo(staffBody + "\nEdition two clarification.");
			await Assert.That(await Wiki.GetBySlugAsync(fixture.Group, WikiNamespace.Category) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(fixture.Primary)).IsEmpty();
			var game = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("game", search: "Overview")));
			await Assert.That(game.Single(row => row.Id == original.Id).CollectionId).IsEqualTo("game");
			var retained = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement)));
			await Assert.That(retained.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { appendix.Id });
			(await Installer.UninstallAsync(fixture.Supplement)).Expect<Success>();
			await Assert.That(await Wiki.GetBySlugAsync(fixture.Appendix, WikiNamespace.Main) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(fixture.Supplement)).IsEmpty();
		}
		finally { await Cleanup(fixture); }
	}

	[Test]
	[Arguments("rules")]
	[Arguments("manual")]
	[Arguments("lore")]
	public async Task AuthorizedCollectionRowsMatchTheExplicitOracleAndPublishedTranslation(string family)
	{
		var fixture = NewFixture(family);
		try
		{
			await Install(fixture);
			var overview = await Page(fixture.Overview);
			var category = await Page(fixture.Group, WikiNamespace.Category);
			var appendix = await Page(fixture.Appendix);
			var draft = await Page(fixture.Draft);
			var restricted = await Page(fixture.Restricted);
			(await Wiki.UpsertTranslationAsync(overview.Id, "fr", fixture.Source.Translation, "Traduction publiée.", "#1", null, true, null)).Expect<WikiTranslation>();
			(await Wiki.UpsertTranslationAsync(appendix.Id, "fr", "TRANSLATION_DRAFT_SENTINEL", "Traduction en cours.", "#1", null, false, null)).Expect<WikiTranslation>();
			// Explicit fixture oracle: source category and article in primary; only the public article in supplement.
			foreach (var (package, ids) in new[] { (fixture.Primary, new[] { overview.Id, category.Id }), (fixture.Supplement, new[] { appendix.Id }) })
			{
				var (rows, total) = await WithReader(async reader =>
				{
					var response = await reader.Pages("package:" + package, lang: "fr");
					return (Value<List<WikiPageSummaryDto>>(response), reader.Response.Headers["X-Total-Count"].ToString());
				});
				await Assert.That(rows.Select(row => row.Id).ToArray()).IsEquivalentTo(ids);
				await Assert.That(total).IsEqualTo(ids.Length.ToString());
				await Assert.That(rows.All(row => row.CollectionId == "package:" + package)).IsTrue();
				var navigation = Value<WikiNavigationDto>(await WithReader(reader => reader.Navigation("package:" + package)));
				await Assert.That(navigation.Total).IsEqualTo(ids.Length);
				await Assert.That(navigation.Categories.Select(row => row.Key).ToArray()).IsEquivalentTo(new[] { fixture.Group });
				await Assert.That(navigation.Categories.Single().Pages).IsEqualTo(1);
			}
			var translated = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Primary, search: fixture.Source.Translation, lang: "fr")));
			await Assert.That(translated.Single().Id).IsEqualTo(overview.Id);
			await Assert.That(translated.Single().Title).IsEqualTo(fixture.Source.Translation);
			await Assert.That(translated.Single().CollectionName).IsEqualTo(fixture.Source.Label);
			await Assert.That(Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement, search: fixture.Source.Translation, lang: "fr")))).IsEmpty();
			var fallback = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement, lang: "fr")));
			await Assert.That(fallback.Single().Title).IsEqualTo("Overview");
			foreach (var search in new[] { "Private Notes", "Working Notes", "TRANSLATION_DRAFT_SENTINEL", "ABSENT_SENTINEL" })
				await Assert.That(Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement, search: search, lang: "fr")))).IsEmpty();
			// Fresh controller per request mirrors request-scoped reader resolution. No authentication service is replaced.
			var ordinary = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement), PortalPermission.WikiRead));
			await Assert.That(ordinary.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { appendix.Id });
			// Draft visibility and restricted-page authority are independent permissions.
			foreach (var expected in new[]
			{
				(Permissions: new[] { PortalPermission.WikiRead, PortalPermission.WikiDrafts },
					Ids: new[] { appendix.Id, draft.Id }, Categories: new[] { fixture.Group, fixture.DraftGroup }),
				(Permissions: new[] { PortalPermission.WikiRead, PortalPermission.WikiAdmin },
					Ids: new[] { appendix.Id, restricted.Id }, Categories: new[] { fixture.Group, fixture.HiddenGroup })
			})
			{
				var (rows, total) = await WithReader(async reader =>
				{
					var response = await reader.Pages("package:" + fixture.Supplement);
					return (Value<List<WikiPageSummaryDto>>(response), reader.Response.Headers["X-Total-Count"].ToString());
				}, expected.Permissions);
				await Assert.That(rows.Select(row => row.Id).ToArray()).IsEquivalentTo(expected.Ids);
				await Assert.That(total).IsEqualTo(expected.Ids.Length.ToString());
				var navigation = Value<WikiNavigationDto>(await WithReader(reader => reader.Navigation("package:" + fixture.Supplement), expected.Permissions));
				await Assert.That(navigation.Total).IsEqualTo(expected.Ids.Length);
				await Assert.That(navigation.Collections.Single(row => row.Id == "package:" + fixture.Supplement).Pages).IsEqualTo(expected.Ids.Length);
				await Assert.That(navigation.Categories.Select(row => row.Key).ToArray()).IsEquivalentTo(expected.Categories);
				await Assert.That(navigation.Categories.All(row => row.Pages == 1)).IsTrue();
			}
			var privileged = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement),
				PortalPermission.WikiRead, PortalPermission.WikiAdmin, PortalPermission.WikiDrafts));
			await Assert.That(privileged.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { appendix.Id, draft.Id, restricted.Id });
			var nextRequest = Value<List<WikiPageSummaryDto>>(await WithReader(reader => reader.Pages("package:" + fixture.Supplement), PortalPermission.WikiRead));
			await Assert.That(nextRequest.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { appendix.Id });
		}
		finally { await Cleanup(fixture); }
	}

	[Test]
	[Arguments("rules")]
	[Arguments("manual")]
	[Arguments("lore")]
	public async Task SameCanonicalPageInAnotherPackageIsReviewedAndRefusedWithoutOverwrite(string family)
	{
		var fixture = NewFixture(family);
		var rival = fixture.Id + "-rival";
		try
		{
			await Install(fixture);
			var before = await Page(fixture.Overview);
			var revisions = (await Wiki.GetRevisionsAsync(before.Id)).Count;
			var challenger = Manifest(rival, "Independent Overview", "1.0.0",
				Entry(fixture.Overview, "Overview", "COLLISION_MUST_NOT_OVERWRITE", fixture.Group));
			var plan = await Installer.PlanAsync(challenger);
			await Assert.That(plan.IsBlocked).IsTrue();
			await Assert.That(plan.Wiki!.Single().Action).IsEqualTo(PackageWikiAction.Blocked);
			await Assert.That(plan.Wiki!.Single().Detail!).Contains(fixture.Primary);
			await Assert.That((await Apply(challenger, fixture)).Expect<Error<string>>().Value).Contains("Plan is blocked");
			var after = await Page(fixture.Overview);
			await Assert.That(after.Id).IsEqualTo(before.Id);
			await Assert.That(after.MarkdownSource).IsEqualTo(before.MarkdownSource);
			await Assert.That((await Wiki.GetRevisionsAsync(before.Id)).Count).IsEqualTo(revisions);
			await Assert.That(await Registry.GetInstalledPackageAsync(rival) is NotFound).IsTrue();
			await Assert.That(await Registry.GetPackageWikiPagesAsync(rival)).IsEmpty();
			await Assert.That((await Registry.GetPackageWikiPagesAsync(fixture.Primary)).Single(row => row.PageId == before.Id).Markdown)
				.IsEqualTo(before.MarkdownSource);
			await Assert.That(await WithReader(reader => reader.Navigation("package:" + rival)) is NotFoundResult).IsTrue();
		}
		finally { await Cleanup(fixture, rival); }
	}
}
