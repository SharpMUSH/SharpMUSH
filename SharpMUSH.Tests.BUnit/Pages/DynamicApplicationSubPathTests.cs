using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Serves a jobs application whose routes carry <c>{path}</c>, recording every request.</summary>
file sealed class JobsAppHandler(string? permission) : HttpMessageHandler
{
	public ConcurrentQueue<string> Requests { get; } = new();

	private string AppDto => $$"""
	{"slug":"jobs","displayName":"Jobs","icon":"Work","kind":"Page",
	 "schemaUrl":"http/jobs/schema?view={path}","dataUrl":"http/jobs/data/{path}","submitRoute":null,
	 "minimumRole":"Guest","navPlacement":"Play","zones":[],"order":0,
	 "permission":{{(permission is null ? "null" : $"\"{permission}\"")}}}
	""";

	private const string Schema = """
	{"kind":"view","schema_version":1,"title":"Jobs","pages":[{"key":"p","order":1,"sections":[
	  {"name":"Jobs","order":1,"elements":[{"kind":"field","key":"which","label":"Which","type":"text"}]}]}]}
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var uri = request.RequestUri!;
		Requests.Enqueue(uri.PathAndQuery);

		string? body = uri.AbsolutePath switch
		{
			"/api/applications/jobs" => AppDto,
			"/http/jobs/schema" => Schema,
			var path when path.StartsWith("/http/jobs/data", StringComparison.Ordinal) =>
				"{\"fields\":{\"which\":{\"value\":\"" + uri.PathAndQuery.Replace("\"", "") + "\",\"visible\":true}}}",
			_ => null
		};

		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// <c>/apps/{slug}/{**rest}</c>: the sub-path fills <c>{path}</c> in the app's routes and the page's query
/// rides along on both, a navigation within the app reloads its schema and data, and an app that names a
/// permission is refused to a viewer who does not hold it.
/// </summary>
public class DynamicApplicationSubPathTests : TrackingBunitContext, IAsyncDisposable
{
	private ConcurrentQueue<string> _requests = new();
	private BunitAuthorizationContext _auth = default!;

	private void Seed(string? permission = null)
	{
		var handler = new JobsAppHandler(permission);
		_requests = handler.Requests;
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") }));

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new PluginComponentLoader(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<PluginComponentLoader>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		_auth = AddAuthorization();
	}

	private IRenderedComponent<DynamicApplication> RenderAt(string address)
	{
		Services.GetRequiredService<NavigationManager>().NavigateTo(address);
		var rest = address.Split('?')[0]["/apps/jobs".Length..].TrimStart('/');
		return Render<DynamicApplication>(p => p.Add(c => c.Slug, "jobs").Add(c => c.Rest, rest));
	}

	private void WaitFor(IRenderedComponent<DynamicApplication> cut, string text) =>
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains(text, StringComparison.Ordinal)) throw new InvalidOperationException($"'{text}' not rendered yet");
		}, TimeSpan.FromSeconds(5));

	[Test]
	public async Task The_sub_path_fills_the_path_token_escaped_and_the_query_rides_along()
	{
		Seed();

		var cut = RenderAt("/apps/jobs/12/a%20b?status=open");
		WaitFor(cut, "/http/jobs/data/12/a%20b?status=open");

		await Assert.That(_requests).Contains("/http/jobs/schema?view=12/a%20b&status=open");
		await Assert.That(_requests).Contains("/http/jobs/data/12/a%20b?status=open");
	}

	[Test]
	public async Task With_no_sub_path_the_token_is_empty()
	{
		Seed();

		var cut = RenderAt("/apps/jobs");
		WaitFor(cut, "/http/jobs/data/");

		await Assert.That(_requests).Contains("/http/jobs/schema?view=");
		await Assert.That(_requests).Contains("/http/jobs/data/");
	}

	[Test]
	public async Task Navigating_within_the_app_reloads_its_schema_and_data()
	{
		Seed();
		var cut = RenderAt("/apps/jobs");
		WaitFor(cut, "/http/jobs/data/");
		var nav = Services.GetRequiredService<NavigationManager>();

		nav.NavigateTo("/apps/jobs/12");
		WaitFor(cut, "/http/jobs/data/12");
		await Assert.That(_requests).Contains("/http/jobs/schema?view=12");

		nav.NavigateTo("/apps/jobs/12?x=1");
		WaitFor(cut, "/http/jobs/data/12?x=1");
		await Assert.That(_requests).Contains("/http/jobs/schema?view=12&x=1");

		// One fetch of each per address, however many ways the navigation was announced.
		await Assert.That(_requests.Count(r => r == "/http/jobs/data/12")).IsEqualTo(1);
	}

	[Test]
	public async Task A_permission_gated_app_is_refused_without_the_permission()
	{
		Seed(permission: "jobs.staff");
		_auth.SetAuthorized("player");

		var cut = Render<DynamicApplication>(p => p.Add(c => c.Slug, "jobs"));
		WaitFor(cut, "NavNoApplicationAccess");

		await Assert.That(_requests.Any(r => r.StartsWith("/http/", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task A_permission_gated_app_opens_for_a_holder_of_the_permission()
	{
		Seed(permission: "jobs.staff");
		_auth.SetAuthorized("staffer");
		_auth.SetClaims(new Claim(PortalPermission.ClaimType, "JOBS.staff"));

		var cut = Render<DynamicApplication>(p => p.Add(c => c.Slug, "jobs"));
		WaitFor(cut, "/http/jobs/data/");

		await Assert.That(cut.Markup).DoesNotContain("NavNoApplicationAccess");
	}
}

/// <summary>The nav hides a permission-gated application from a viewer who does not hold the permission.</summary>
public class PermissionGatedNavTests
{
	private static PortalApplication App(string slug, string? permission) =>
		new(slug, slug, null, "Page", $"http/{slug}/schema", null, null, "Player", "Play", [], 0, Permission: permission);

	private static ClaimsPrincipal Viewer(params string[] permissions) => new(new ClaimsIdentity(
		[new Claim(ClaimTypes.Role, "Player"), .. permissions.Select(p => new Claim(PortalPermission.ClaimType, p))], "test"));

	[Test]
	public async Task Only_holders_of_the_permission_see_the_entry()
	{
		PortalApplication[] apps = [App("jobs", null), App("queue", "jobs.staff")];

		var player = PortalNavSections.AppsForSection(apps, Viewer(), "Play").Select(a => a.Slug).ToList();
		var staff = PortalNavSections.AppsForSection(apps, Viewer("jobs.staff"), "Play").Select(a => a.Slug).ToList();
		var byRoleAlone = PortalNavSections.AppsForSection(apps, PortalRole.God, "Play").Select(a => a.Slug).ToList();

		await Assert.That(player).IsEquivalentTo(new[] { "jobs" });
		await Assert.That(staff).IsEquivalentTo(new[] { "jobs", "queue" });
		await Assert.That(byRoleAlone).IsEquivalentTo(new[] { "jobs" });
	}
}
