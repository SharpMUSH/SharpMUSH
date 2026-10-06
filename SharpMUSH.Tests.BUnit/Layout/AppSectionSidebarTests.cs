using System.Net;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Admin;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// A page application in a section of its own may serve its sidebar from its nav_url: the section
/// sidebar draws those groups and counts in place of the app's single link, marks the link whose path
/// and query match the address, and falls back to the single link when the route has nothing usable.
/// </summary>
public class AppSectionSidebarTests : TrackingBunitContext
{
	private sealed class Handler(string nav, HttpStatusCode navStatus) : HttpMessageHandler
	{
		public int NavReads;

		/// <summary>When set, a nav read waits for it: the answer to an earlier viewer's read is in hand, the next is not.</summary>
		public TaskCompletionSource? NavGate;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			const string apps = """
				[{"slug":"jobs","displayName":"Jobs","icon":"support_agent","kind":"Page","schemaUrl":"x","dataUrl":null,"submitRoute":null,
				  "minimumRole":"Guest","navPlacement":"Support","zones":[],"order":1,"owningPackage":null,"navUrl":"http/jobs/nav"},
				 {"slug":"faq","displayName":"FAQ","icon":null,"kind":"Page","schemaUrl":"x","dataUrl":null,"submitRoute":null,
				  "minimumRole":"Guest","navPlacement":"Support","zones":[],"order":2,"owningPackage":null}]
				""";
			var path = request.RequestUri!.AbsolutePath;
			if (path == "/http/jobs/nav")
			{
				Interlocked.Increment(ref NavReads);
				if (NavGate is { } gate)
				{
					await gate.Task.WaitAsync(cancellationToken);
				}
			}

			return path switch
			{
				"/api/applications" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(apps, Encoding.UTF8, "application/json") },
				"/http/jobs/nav" => new HttpResponseMessage(navStatus) { Content = new StringContent(nav, Encoding.UTF8, "application/json") },
				_ => new HttpResponseMessage(HttpStatusCode.NotFound)
			};
		}
	}

	private const string StaffNav = """
		{"groups":[
		  {"label":"","items":[
		    {"label":"Needs attention","path":"/apps/jobs","icon":"inbox","count":3},
		    {"label":"Handled by me","path":"/apps/jobs?filter=mine","icon":"person","count":1},
		    {"label":"Elsewhere","path":"https://example.com/","icon":"link"},
		    {"label":"Sneaky","path":"//example.com/x"}]},
		  {"label":"Buckets","items":[
		    {"label":"Bugs","path":"/apps/jobs?bucket=bugs","icon":"folder_open","count":2}]},
		  {"label":"Nothing usable","items":[{"label":"Out","path":"javascript:alert(1)"}]}]}
		""";

	private Handler Setup(string nav, HttpStatusCode status = HttpStatusCode.OK)
	{
		var handler = new Handler(nav, status);
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(new SchemaAppService(factory, NullLogger<SchemaAppService>.Instance))
			.AddLocalization();
		JSInterop.Mode = JSRuntimeMode.Loose;
		_auth = AddAuthorization();
		_auth.SetAuthorized("player");
		return handler;
	}

	private BunitAuthorizationContext _auth = null!;

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	[Test]
	public async Task The_served_links_replace_the_apps_own_link()
	{
		Setup(StaffNav);
		Nav.NavigateTo("/apps/jobs?filter=mine");
		var cut = Render<AppSectionSidebar>(p => p.Add(x => x.Section, "Support"));
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/jobs?bucket=bugs']"), TimeSpan.FromSeconds(5));

		var rows = cut.FindAll("a.kit-row").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(rows).IsEquivalentTo(new[] { "/apps/jobs", "/apps/jobs?filter=mine", "/apps/jobs?bucket=bugs", "/apps/faq" },
				TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("links that leave the portal are dropped, a group left empty goes, and an app without a nav_url keeps its link");
		await Assert.That(cut.Find("a.kit-row[href='/apps/jobs']").TextContent).Contains("3");
		await Assert.That(cut.Markup).Contains("Buckets").And.DoesNotContain("Nothing usable");
		await Assert.That(cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/apps/jobs?filter=mine" })
			.Because("the queue's own link is current only without a query");
	}

	[Test]
	public async Task The_links_are_read_again_on_each_move()
	{
		var handler = Setup(StaffNav);
		Nav.NavigateTo("/apps/jobs");
		var cut = Render<AppSectionSidebar>(p => p.Add(x => x.Section, "Support"));
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/jobs?bucket=bugs']"), TimeSpan.FromSeconds(5));
		var before = handler.NavReads;

		Nav.NavigateTo("/apps/jobs?bucket=bugs");
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/jobs?bucket=bugs'][aria-current='page']"), TimeSpan.FromSeconds(5));
		await Assert.That(handler.NavReads).IsGreaterThan(before).Because("counts move as jobs are worked");
	}

	[Test]
	[Arguments("""{"groups":[]}""", HttpStatusCode.OK)]
	[Arguments("not json", HttpStatusCode.OK)]
	[Arguments("{}", HttpStatusCode.InternalServerError)]
	public async Task Nothing_usable_leaves_the_apps_own_link(string nav, HttpStatusCode status)
	{
		Setup(nav, status);
		Nav.NavigateTo("/apps/jobs");
		var cut = Render<AppSectionSidebar>(p => p.Add(x => x.Section, "Support"));
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/faq']"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll("a.kit-row").Select(a => a.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/apps/jobs", "/apps/faq" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_new_viewer_does_not_see_the_last_viewers_links_while_theirs_load()
	{
		var handler = Setup(StaffNav);
		Nav.NavigateTo("/apps/jobs");
		var cut = Render<AppSectionSidebar>(p => p.Add(x => x.Section, "Support"));
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/jobs?bucket=bugs']"), TimeSpan.FromSeconds(5));

		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		handler.NavGate = gate;
		_auth.SetAuthorized("someone-else");

		cut.WaitForState(() => cut.FindAll("a.kit-row[href='/apps/jobs?bucket=bugs']").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("a.kit-row").Select(a => a.GetAttribute("href")))
			.IsEquivalentTo(new[] { "/apps/jobs", "/apps/faq" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the app keeps its own link until the new viewer's answer arrives");

		gate.SetResult();
		cut.WaitForAssertion(() => cut.Find("a.kit-row[href='/apps/jobs?bucket=bugs']"), TimeSpan.FromSeconds(5));
		await Assert.That(handler.NavReads).IsGreaterThanOrEqualTo(2);
	}
}
