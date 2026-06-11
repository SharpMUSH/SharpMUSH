using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Tests.Infrastructure;
using System.Net;
using System.Text.Json;

namespace SharpMUSH.Tests.Integration.Profile;

/// <summary>
/// HTTP-level integration tests for the default character-profile web behavior. These boot the
/// full server, so the default <c>HTTP`PROFILE`*</c> softcode is seeded onto the http_handler
/// (#4, which the config now defaults to) and the request travels the real ASP.NET pipeline +
/// MUSHcode evaluation.
///
/// Guards two regressions that crashed / 404'd the portal Character Page:
///  - the seeded schema must evaluate to valid JSON (ArgumentsOrdered numeric-order fix), and
///  - the GET route must resolve an existing character by name (pmatch global-player-match fix).
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class ProfileApiTests(ServerWebAppFactory factory)
{
	[Test]
	public async Task ProfileSchema_ReturnsValidJsonWithSections()
	{
		var http = factory.CreateHttpClient();

		var response = await http.GetAsync("api/profile-schema");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(json); // would throw if the handler returned #-1 / empty
		await Assert.That(doc.RootElement.TryGetProperty("sections", out var sections)).IsTrue();
		await Assert.That(sections.GetArrayLength()).IsEqualTo(4);
	}

	[Test]
	public async Task ProfileGet_ResolvesExistingCharacterByName()
	{
		// Resolve #1 (God)'s actual name from the engine so the test isn't tied to a literal.
		var mediator = factory.Services.GetRequiredService<IMediator>();
		var god = await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)));
		await Assert.That(god.IsNone).IsFalse();
		var name = god.Known.Object().Name;

		var http = factory.CreateHttpClient();
		var response = await http.GetAsync($"api/profile/{Uri.EscapeDataString(name)}");

		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var json = await response.Content.ReadAsStringAsync();
		using var doc = JsonDocument.Parse(json);

		// The handler's GET softcode resolves the character via pmatch(after(%1,profile/)). This 404'd
		// when after() included its delimiter (returning "profile/God" instead of "God") — see the
		// after() regression in StringFunctionTests — and again when pmatch was visibility-gated.
		await Assert.That(doc.RootElement.GetProperty("status").GetInt32()).IsEqualTo(200);
		await Assert.That(doc.RootElement.GetProperty("character").GetString()).IsEqualTo(name);
	}
}
