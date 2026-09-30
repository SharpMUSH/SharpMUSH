using System.Net;
using System.Net.Http.Json;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// Startup used to await the application catalog before building the host, so every visitor's first
/// frame waited on a GET api/applications (up to its five-second timeout). It loads alongside the
/// first render now; these pin that it does not block, and that readers can still wait for it.
/// </summary>
public class ApplicationCatalogTests
{
	private static readonly Uri Api = new("https://localhost:8081/");

	private static HttpResponseMessage Apps() => new(HttpStatusCode.OK)
	{
		Content = JsonContent.Create(new[]
		{
			new
			{
				slug = "character-header", displayName = "Character Header", icon = "Badge", kind = "Widget",
				schemaUrl = "http/profile/schema", dataUrl = (string?)null, submitRoute = (string?)null,
				minimumRole = "Guest", navPlacement = (string?)null, zones = new[] { "MainContent" }, order = 0
			}
		})
	};

	[Test]
	public async Task StartLoading_ReturnsBeforeTheRegistryAnswers()
	{
		using var handler = new GatedHttpHandler(_ => Apps());

		var catalog = ApplicationCatalog.StartLoading(Api, handler: handler);

		await Assert.That(catalog.Loaded.IsCompleted).IsFalse()
			.Because("the catalog must not hold up startup while the request is out");
		await Assert.That(catalog.All).IsEmpty();

		handler.Release();
		await catalog.Loaded;

		await Assert.That(catalog.Get("character-header")?.DisplayName).IsEqualTo("Character Header");
		await Assert.That(catalog.WidgetApps.Count).IsEqualTo(1);
	}

	[Test]
	public async Task OnLoaded_RunsWithTheSnapshot()
	{
		using var handler = new GatedHttpHandler(_ => Apps());
		IReadOnlyList<string>? seen = null;

		var catalog = ApplicationCatalog.StartLoading(Api,
			c => seen = c.WidgetApps.Select(a => a.Slug).ToList(), handler);
		handler.Release();
		await catalog.Loaded;

		await Assert.That(seen).IsEquivalentTo(["character-header"]);
	}

	[Test]
	public async Task AFailedLoad_CompletesEmpty_AndSkipsOnLoaded()
	{
		using var handler = new GatedHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
		var called = false;

		var catalog = ApplicationCatalog.StartLoading(Api, _ => called = true, handler);
		handler.Release();
		await catalog.Loaded;

		await Assert.That(catalog.Loaded.IsCompletedSuccessfully).IsTrue()
			.Because("readers await Loaded; a failed fetch must degrade to empty, not fault them");
		await Assert.That(catalog.All).IsEmpty();
		await Assert.That(called).IsFalse();
	}
}
