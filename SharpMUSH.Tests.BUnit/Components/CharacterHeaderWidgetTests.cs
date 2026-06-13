using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components;

file sealed class HeaderStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Serves the profile pipeline the way the in-game http_handler does: the directory (for name→objid),
/// the kind:"view" Portal Schema Document, and the per-character data. Mirrors the seeded
/// profile-handler softcode shape (Area 21).
/// </summary>
file sealed class ProfilePipelineHandler : HttpMessageHandler
{
	private const string Roster = """[{"name":"Gandalf","objid":"#5:1000","created":1000,"category":""}]""";

	private const string Schema = """
	{"kind":"view","schema_version":1,"pages":[{"key":"profile","order":1,"sections":[
	  {"name":"Demographics","order":1,"elements":[
	    {"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"},
	    {"kind":"field","key":"secret","label":"Secret","type":"text","visible_to":"public"}]}]}]}
	""";

	private const string Data = """
	{"character":"Gandalf","objid":"#5:1000","dbref":"5","fields":{
	  "fullname":{"value":"Gandalf the Grey","visible":true},
	  "secret":{"value":"Olorin","visible":false}}}
	""";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
	{
		var path = request.RequestUri!.AbsolutePath;
		var body = path switch
		{
			"/http/characters" => Roster,
			"/http/profile/schema" => Schema,
			"/http/profile" => Data,
			_ => null
		};

		return Task.FromResult(body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(body, Encoding.UTF8, "application/json")
			});
	}
}

/// <summary>
/// Confirms the character profile renders through the shared schema view renderer (Area 21):
/// section + field from the schema, the value from the data, the name/dbref header, and that a
/// softcode-hidden field stays hidden.
/// </summary>
public class CharacterHeaderWidgetTests : BunitContext
{
	public CharacterHeaderWidgetTests()
	{
		var apiClient = new HttpClient(new ProfilePipelineHandler()) { BaseAddress = new Uri("https://localhost:8081/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddSingleton(apiClient)
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new SchemaAppService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<SchemaAppService>.Instance))
			.AddSingleton(sp => new CharacterDirectoryService(sp.GetRequiredService<IHttpClientFactory>(), NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton<IStringLocalizer<SharedResource>, HeaderStubLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[TUnit.Core.Test]
	public async Task RendersSchemaDrivenProfile_WithHeaderAndVisibilityRules()
	{
		var cut = Render<CharacterHeaderWidget>(p => p.Add(x => x.CharacterName, "Gandalf"));

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Gandalf the Grey"))
			{
				throw new InvalidOperationException("profile not loaded yet");
			}
		}, TimeSpan.FromSeconds(5));

		var markup = cut.Markup;
		await Assert.That(markup).Contains("Gandalf");          // header name
		await Assert.That(markup).Contains("#5");               // dbref from objid
		await Assert.That(markup).Contains("Demographics");     // section from schema
		await Assert.That(markup).Contains("Full Name");        // field label from schema
		await Assert.That(markup).Contains("Gandalf the Grey"); // value from data
		// The softcode-hidden field is not rendered.
		await Assert.That(markup).DoesNotContain("Olorin");
	}
}
