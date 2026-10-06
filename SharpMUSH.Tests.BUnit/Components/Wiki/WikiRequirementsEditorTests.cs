using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Wiki;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;

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
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task ASaveSendsOnlyTheActionsChangedInTheForm()
	{
		var set = new WikiRequirementSetDto("category", "lore", "lore",
			new Dictionary<string, IReadOnlyList<string>> { ["read"] = ["lore.read"] }, null, null);
		var cut = Render<WikiRequirementsEditor>(p => p
			.Add(x => x.Scope, "category").Add(x => x.Key, "lore").Add(x => x.Set, set).Add(x => x.Editable, true));

		cut.FindAll("button").Single(b => b.TextContent.Contains("Change")).Click();
		// The fields are read, create, edit and delete, in that order.
		cut.FindAll("input")[2].Change("media.admin");
		cut.FindAll("button").Single(b => b.TextContent.Contains("Save")).Click();
		cut.WaitForAssertion(() =>
		{
			if (Server.Puts.Count != 1) throw new InvalidOperationException("no save yet");
		}, TimeSpan.FromSeconds(5));

		var sent = JsonDocument.Parse(Server.Puts[0]).RootElement.GetProperty("required");
		await Assert.That(sent.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(new[] { "edit" });
		await Assert.That(sent.GetProperty("edit")[0].GetString()).IsEqualTo("media.admin");
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
