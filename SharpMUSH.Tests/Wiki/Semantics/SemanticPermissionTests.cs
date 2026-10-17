using System.Collections.Immutable;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Authentication;
using SharpMUSH.Tools.WikiSemantics;

namespace SharpMUSH.Tests.Wiki.Semantics;

public class SemanticPermissionTests
{
	[Test]
	public async Task HiddenTargetsCannotAffectMembershipProjectionCountsFacetsOrLookahead()
	{
		var graph = SemanticReferenceTests.Graph(SemanticReferenceTests.Fact("a", "t:link", new PageRef("b")));
		var visibility = WikiVisibility.All with { ExcludedPageIds = new HashSet<string> { "b" } };
		var query = SemanticReferenceTests.Query with { Columns = ["t:link"], Facets = ["t:class"], PageSize = 3 };
		var result = SemanticReferenceTests.Success(graph, query, visibility);
		await Assert.That(result.Count).IsEqualTo(3L);
		await Assert.That(result.HasMore).IsFalse();
		await Assert.That(result.Rows[0].Cells[0].Values.IsEmpty).IsTrue();
		await Assert.That(result.Facets[0].Buckets.IsEmpty).IsTrue();
		var absent = SemanticReferenceTests.Success(graph with { Facts = new(1, []) }, query, visibility);
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(result)).IsEqualTo(System.Text.Json.JsonSerializer.Serialize(absent));
		var hidden = ReferenceEvaluator.Evaluate(graph, query with { Predicates = [new EqualPredicate("t:link", new PageRef("b"))] }, visibility).Expect<QueryFailure>();
		var missing = ReferenceEvaluator.Evaluate(graph, query with { Predicates = [new EqualPredicate("t:link", new PageRef("missing"))] }, visibility).Expect<QueryFailure>();
		await Assert.That(hidden).IsEqualTo(missing);
	}
	[Test]
	public async Task CollectionAndVisibilityFiltersApplyToBothEndpoints()
	{
		var graph = SemanticReferenceTests.Graph(SemanticReferenceTests.Fact("a", "t:link", new PageRef("b")));
		graph = graph with { Pages = [graph.Pages[0], graph.Pages[1] with { Collections = ["other"] }] };
		await Assert.That(SemanticReferenceTests.Success(graph, SemanticReferenceTests.Query with { Columns = ["t:link"] }).Rows.Single().Cells.Single().Values.IsEmpty).IsTrue();
		await Assert.That(SemanticReferenceTests.Success(graph, visibility: WikiVisibility.All with { IncludedPageIds = new HashSet<string>() }).Rows.IsEmpty).IsTrue();
	}
	[Test]
	public async Task RealAccessServiceControlsGlobalReadDraftsAndRestrictions()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var access = new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver());
		var admin = WikiReader.From([PortalPermission.WikiAdmin, PortalPermission.WikiRead], "#1");
		await access.SetRequirementsAsync(admin, "#1", WikiRuleTarget.ForCategory("secret"), new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiAdmin] });
		var graph = SemanticReferenceTests.Graph() with
		{
			Pages = [
			SemanticReferenceTests.Page("a"),
			SemanticReferenceTests.Page("b") with { Visibility = new("main", [], [], false, "#42") },
			SemanticReferenceTests.Page("c") with { Visibility = new("main", [], [], false, "#99") },
			SemanticReferenceTests.Page("d") with { Visibility = new("main", ["secret"], [], true, null) }]
		};
		var noRead = await access.VisibilityAsync(WikiReader.From([PortalPermission.WikiAdmin], "#1"));
		await Assert.That(SemanticReferenceTests.Success(graph, visibility: noRead).Rows.IsEmpty).IsTrue();
		var adminVisibility = await access.VisibilityAsync(admin);
		await Assert.That(string.Join(",", SemanticReferenceTests.Success(graph, visibility: adminVisibility).Rows.Select(r => r.Subject.Id))).IsEqualTo("a,d");
		var player = await access.VisibilityAsync(WikiReader.From([PortalPermission.WikiRead], "#42"));
		await Assert.That(string.Join(",", SemanticReferenceTests.Success(graph, visibility: player).Rows.Select(r => r.Subject.Id))).IsEqualTo("a,b");
	}
	[Test]
	public async Task SameTitleNamespacesAndDeletedIncarnationsNeverMerge()
	{
		var graph = SemanticReferenceTests.Graph(SemanticReferenceTests.Fact("a", "t:link", new PageRef("deleted"))) with { Pages = [SemanticReferenceTests.Page("a"), SemanticReferenceTests.Page("b", "help")] };
		var result = SemanticReferenceTests.Success(graph, SemanticReferenceTests.Query with { Columns = ["t:link"] });
		await Assert.That(string.Join(",", result.Rows.Select(r => r.Subject.Id))).IsEqualTo("a,b");
		await Assert.That(result.Rows[0].Cells[0].Values.IsEmpty).IsTrue();
	}
	[Test]
	public async Task NamespaceAndPageRequirementsAndExplicitRenamePreserveOpaqueIdentity()
	{
		var wiki = InMemoryWikiStore.CreateService();
		var access = new WikiAccessService(wiki, InMemoryRoleRegistry.Seeded(), new PermissionResolver());
		var admin = WikiReader.From([PortalPermission.WikiAdmin, PortalPermission.WikiRead], "#1");
		var rule = new Dictionary<WikiAction, IReadOnlyList<string>> { [WikiAction.Read] = [PortalPermission.WikiAdmin] };
		await access.SetRequirementsAsync(admin, "#1", WikiRuleTarget.ForNamespace("help"), rule);
		var restricted = (await wiki.CreateAsync("Restricted", "body", "#1")).Expect<WikiPage>();
		(await access.SetRequirementsAsync(admin, "#1", WikiRuleTarget.ForPage(restricted.Id), rule)).Expect<WikiRequirements>();
		var graph = SemanticReferenceTests.Graph() with { Pages = [SemanticReferenceTests.Page("a"), SemanticReferenceTests.Page("b", "help"), SemanticReferenceTests.Page(restricted.Id)] };
		var visibility = await access.VisibilityAsync(WikiReader.From([PortalPermission.WikiRead], "#42"));
		await Assert.That(SemanticReferenceTests.Success(graph, visibility: visibility).Rows.Single().Subject.Id).IsEqualTo("a");
		var renamed = graph.Pages[0] with { Key = new("pack", "main", "renamed"), Title = "Renamed" };
		var oldKey = graph.Pages[0].Key;
		await Assert.That(CanonicalFacts.Resolve(oldKey, [new(oldKey, renamed.Reference)], new HashSet<PageRef> { renamed.Reference }) is PageRef resolved && resolved.Id == "a").IsTrue();
		await Assert.That(SemanticReferenceTests.Success(graph with { Pages = [renamed] }, visibility: visibility).Rows.Single().Subject.Id).IsEqualTo("a");
	}

}
