using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class SchemaAppStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>Serves the profile pipeline (roster for name→objid, the view schema, and the per-character data).</summary>
file sealed class ProfileAppHandler : HttpMessageHandler
{
	private const string Roster = """[{"name":"Gandalf","objid":"#5:1000","created":1000,"category":""}]""";

	private const string Schema = """
	{"kind":"view","schema_version":1,"pages":[{"key":"profile","order":1,"sections":[
	  {"name":"Demographics","order":1,"elements":[
	    {"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"}]}]}]}
	""";

	private const string Data = """
	{"character":"Gandalf","objid":"#5:1000","dbref":"5","fields":{
	  "fullname":{"value":"Gandalf the Grey","visible":true}}}
	""";

	// The application DTO returned by GET /api/applications/{slug} (camelCase, as MVC serializes it).
	private const string AppDto = """
	{"slug":"character-header","displayName":"Character Header","icon":"Badge","kind":"Widget",
	 "schemaUrl":"http/profile/schema","dataUrl":"http/profile?objid={objid}","submitRoute":null,
	 "minimumRole":"Guest","navPlacement":null,"zones":["MainContent"],"order":0,"owningPackage":null}
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var body = request.RequestUri!.AbsolutePath switch
		{
			"/http/characters" => Roster,
			"/http/profile/schema" => Schema,
			"/http/profile" => Data,
			"/api/applications/character-header" => AppDto,
			_ => null
		};
		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
	}
}

/// <summary>
/// Confirms SchemaWidget, when used as an application-backed widget, resolves its schema/data routes
/// from the application catalog by slug and fills the {objid} token from the cascading profile page
/// context — i.e. the Character Header application renders the routed character.
/// </summary>
public class SchemaWidgetApplicationTests : BunitContext
{
	public SchemaWidgetApplicationTests()
	{
		var apiClient = new HttpClient(new ProfileAppHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		var characterHeaderApp = new PortalApplication(
			"character-header", "Character Header", "Badge", "Widget",
			"http/profile/schema", "http/profile?objid={objid}", null, "Guest", null,
			["MainContent"], 0);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([characterHeaderApp]))
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, SchemaAppStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task ApplicationBacked_ResolvesRoutesAndContext_RendersCharacter()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "character-header")));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
				throw new InvalidOperationException("profile data not loaded yet");
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Demographics");        // section from schema
		await Assert.That(markup).Contains("Full Name");           // field label from schema
		await Assert.That(markup).Contains("Gandalf the Grey");    // data resolved via {objid} substitution
	}
}

/// <summary>
/// Confirms the robustness fallback: with an EMPTY startup catalog, an application-backed SchemaWidget
/// still renders by lazily fetching its application by slug (GET /api/applications/{slug}). This is the
/// path that fixes "the header shows nothing" when the startup snapshot was empty.
/// </summary>
public class SchemaWidgetLazyFetchTests : BunitContext
{
	public SchemaWidgetLazyFetchTests()
	{
		var apiClient = new HttpClient(new ProfileAppHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(new ApplicationCatalog([]))   // empty — forces the lazy fetch path
			.AddSingleton(sp => new ApplicationRegistryClient(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<ApplicationRegistryClient>.Instance))
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, SchemaAppStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task EmptyCatalog_LazyFetchesAppBySlug_RendersCharacter()
	{
		var cut = Render<CascadingValue<ProfilePageContext>>(p => p
			.Add(x => x.Value, new ProfilePageContext("Gandalf", false))
			.Add(x => x.IsFixed, true)
			.AddChildContent<SchemaWidget>(cp => cp.Add(w => w.WidgetName, "character-header")));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
				throw new InvalidOperationException("profile data not loaded yet");
		}, TimeSpan.FromSeconds(5));

		await Assert.That(cut.Markup).Contains("Gandalf the Grey");
	}
}
