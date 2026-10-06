using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

/// <summary>
/// The requirement editors: a save sends only the actions changed in the form, so it cannot undo another
/// admin's change to an action this form never touched; and the permissions card never shows, or lets an
/// admin change, the rules of a page the reader has already left.
/// </summary>
public class WikiRequirementsEditorTests : TrackingBunitContext
{
	private sealed class Handler : HttpMessageHandler
	{
		public TaskCompletionSource FirstPageReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public List<string> Puts { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			var path = request.RequestUri!.AbsolutePath;
			if (request.Method == HttpMethod.Put)
			{
				var body = await request.Content!.ReadAsStringAsync(ct);
				Puts.Add(body);
				return Json(Set("category", "lore", """{"read":["lore.read"],"edit":["media.admin"]}"""));
			}

			switch (path)
			{
				case "/api/roles/permissions":
					return Json("""[{"scope":"lore.read","category":"Wiki","description":"Read the lore pages","createdAt":0}]""");
				case "/api/wiki/first/requirements":
					await FirstPageReleased.Task;
					return Json($$"""{"page":{{Set("page", "wiki_page/1", """{"edit":["first.scope"]}""")}},"inherited":[]}""");
				case "/api/wiki/second/requirements":
					return Json($$"""{"page":{{Set("page", "wiki_page/2", """{"edit":["second.scope"]}""")}},"inherited":[]}""");
				default:
					return new HttpResponseMessage(HttpStatusCode.NotFound);
			}
		}

		private static string Set(string scope, string key, string required)
			=> $$"""{"scope":"{{scope}}","key":"{{key}}","label":"{{key}}","required":{{required}},"updatedBy":null,"updatedAt":null}""";

		private static HttpResponseMessage Json(string body)
			=> new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}

	private Handler Server { get; } = new();

	public WikiRequirementsEditorTests()
	{
		var client = Track(new HttpClient(Server) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(_ => new WikiService(factory, NullLogger<WikiService>.Instance))
			.AddSingleton(_ => new RoleRegistryClient(factory, NullLogger<RoleRegistryClient>.Instance))
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization().SetAuthorized("admin").SetPolicies(PortalPermission.RolesAdmin);
	}

	[Test]
	public async Task ASaveSendsOnlyTheActionsChangedInTheForm()
	{
		var set = new WikiRequirementSetDto("category", "lore", "lore",
			new Dictionary<string, IReadOnlyList<string>> { ["read"] = ["lore.read"] }, null, null);
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "category").Add(x => x.Key, "lore").Add(x => x.Set, set).Add(x => x.Editable, true));

		cut.Find("button.wiki-req-edit").Click();
		// The pickers are read, create, edit and delete, in that order.
		var pickers = cut.FindComponents<MudAutocomplete<WikiRequirementsEditor.PermissionOption>>();
		await cut.InvokeAsync(() => pickers[2].Instance.ValueChanged.InvokeAsync(new("media.admin", "Image Library · Manage", "")));
		cut.FindAll("button").Single(b => b.TextContent.Contains("Save")).Click();
		cut.WaitForAssertion(() =>
		{
			if (Server.Puts.Count != 1) throw new InvalidOperationException("no save yet");
		}, TimeSpan.FromSeconds(5));

		var sent = JsonDocument.Parse(Server.Puts[0]).RootElement.GetProperty("required");
		await Assert.That(sent.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(new[] { "edit" });
		await Assert.That(sent.GetProperty("edit")[0].GetString()).IsEqualTo("media.admin");
	}

	/// <summary>
	/// A picker searches the game's own permissions and the built-in ones by name, scope or description, and
	/// leaves out what the action already requires.
	/// </summary>
	[Test]
	public async Task APickerSearchesCustomAndBuiltInPermissions()
	{
		var set = new WikiRequirementSetDto("category", "lore", "lore",
			new Dictionary<string, IReadOnlyList<string>> { ["read"] = ["wiki.read"] }, null, null);
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "category").Add(x => x.Key, "lore").Add(x => x.Set, set).Add(x => x.Editable, true));

		cut.Find("button.wiki-req-edit").Click();
		var read = cut.FindComponents<MudAutocomplete<WikiRequirementsEditor.PermissionOption>>()[0].Instance;
		cut.WaitForAssertion(() =>
		{
			if (!read.SearchFunc!("lore", default).Result.Any()) throw new InvalidOperationException("custom permissions not loaded");
		}, TimeSpan.FromSeconds(5));

		var lore = (await read.SearchFunc!("lore", default)).Select(o => o.Scope).ToArray();
		var wiki = (await read.SearchFunc!("wiki.", default)).Select(o => o.Scope).ToArray();
		await Assert.That(lore).Contains("lore.read");
		await Assert.That(wiki).Contains("wiki.edit");
		await Assert.That(wiki).DoesNotContain("wiki.read");
	}

	/// <summary>A chosen permission is removed with a labelled button, so a keyboard can reach it.</summary>
	[Test]
	public async Task AChosenPermissionIsRemovedWithItsButton()
	{
		var set = new WikiRequirementSetDto("category", "lore", "lore",
			new Dictionary<string, IReadOnlyList<string>> { ["read"] = ["lore.read"] }, null, null);
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "category").Add(x => x.Key, "lore").Add(x => x.Set, set).Add(x => x.Editable, true));

		cut.Find("button.wiki-req-edit").Click();
		cut.Find(".wiki-req-chip button[aria-label$='lore.read']").Click();
		cut.FindAll("button").Single(b => b.TextContent.Contains("Save")).Click();
		cut.WaitForAssertion(() =>
		{
			if (Server.Puts.Count != 1) throw new InvalidOperationException("no save yet");
		}, TimeSpan.FromSeconds(5));

		var sent = JsonDocument.Parse(Server.Puts[0]).RootElement.GetProperty("required");
		await Assert.That(sent.GetProperty("read").GetArrayLength()).IsEqualTo(0);
	}

	/// <summary>A roles.admin holder can reach the Permissions tab from the form, outside the pickers' lists.</summary>
	[Test]
	public async Task TheFormLinksToDefiningANewPermission()
	{
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "category").Add(x => x.Key, "lore").Add(x => x.Editable, true));

		cut.Find("button.wiki-req-edit").Click();
		await Assert.That(cut.Find(".wiki-req-actions a.wiki-req-define").GetAttribute("href")).IsEqualTo("/admin/roles?tab=permissions");
	}

	/// <summary>Each target is a block of its own: what it is, its name, what it holds, and a labelled edit button.</summary>
	[Test]
	public async Task ATargetIsABlockWithItsNameAndPurpose()
	{
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "namespace").Add(x => x.Key, "help").Add(x => x.Editable, true)
			.Add(x => x.Kicker, "Namespace").Add(x => x.Heading, "Help").Add(x => x.Sub, "Help pages for players."));

		var block = cut.Find("section.wiki-req");
		await Assert.That(block.QuerySelector(".wiki-req-kicker")!.TextContent).IsEqualTo("Namespace");
		await Assert.That(block.QuerySelector("h3.wiki-req-title")!.TextContent).IsEqualTo("Help");
		await Assert.That(block.QuerySelector(".wiki-req-sub")!.TextContent).IsEqualTo("Help pages for players.");
		await Assert.That(block.QuerySelector("button.wiki-req-edit")!.GetAttribute("aria-label")).IsEqualTo("Change");
		await Assert.That(block.QuerySelector(".wiki-req-none")).IsNotNull();
	}

	[Test]
	public async Task ThePermissionsCardDropsTheRulesOfAPageTheReaderLeft()
	{
		var cut = Render<WikiPermissionsCard>(p => p.Add(x => x.Slug, "first"));
		cut.Render(p => p.Add(x => x.Slug, "second"));
		cut.WaitForAssertion(() => cut.Find(".wiki-permissions"), TimeSpan.FromSeconds(5));

		Server.FirstPageReleased.SetResult();
		// Give the first page's answer the chance to land on the card it no longer belongs to.
		await Task.Delay(200);
		var text = await cut.InvokeAsync(() => cut.Find(".wiki-permissions").TextContent);
		await Assert.That(text).Contains("second.scope");
		await Assert.That(text).DoesNotContain("first.scope");
	}
}
