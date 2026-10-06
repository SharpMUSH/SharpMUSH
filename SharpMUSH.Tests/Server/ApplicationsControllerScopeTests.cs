using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Shared;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The layout scope and OOB package of an application (README §7.4) travel through
/// <c>/api/applications</c> in both directions: an upsert stores them, and a read returns them.
/// </summary>
public class ApplicationsControllerScopeTests
{
	private static ApplicationsController NewController(IApplicationRegistryService registry)
	{
		var controller = new ApplicationsController(registry, new JsonSchemaDispatcher(),
			new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create(allowBrowserCode: true)),
			NullLogger<ApplicationsController>.Instance);
		controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
		return controller;
	}

	private static ApplicationsController.ApplicationDto WeatherDto(string? scope, string? oobPackage) =>
		new("weather", "Weather", null, "Widget", "http/weather/schema", null, null, "Player", null,
			["RightSidebar"], 30, Scope: scope, OobPackage: oobPackage);

	[Test]
	public async Task Upsert_StoresScopeAndOobPackage_Trimmed()
	{
		var registry = new FakeApplicationRegistry();

		var result = await NewController(registry).Upsert(WeatherDto(" play ", " weather.now "));

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		var stored = (await registry.GetApplicationAsync("weather")).Expect<RegisteredApplication>();
		await Assert.That(stored.Scope).IsEqualTo("play");
		await Assert.That(stored.OobPackage).IsEqualTo("weather.now");
	}

	[Test]
	public async Task Upsert_BlankScopeAndOobPackage_StoreNull()
	{
		var registry = new FakeApplicationRegistry();

		await NewController(registry).Upsert(WeatherDto("  ", ""));

		var stored = (await registry.GetApplicationAsync("weather")).Expect<RegisteredApplication>();
		await Assert.That(stored.Scope).IsNull();
		await Assert.That(stored.OobPackage).IsNull();
	}

	[Test]
	public async Task Get_ReturnsScopeAndOobPackage()
	{
		var registry = new FakeApplicationRegistry();
		await registry.UpsertApplicationAsync(new RegisteredApplication(
			"weather", "Weather", null, ApplicationKind.Widget, "http/weather/schema", null, null,
			PortalRole.Player, null, null, 30, Scope: "play", OobPackage: "weather.now"));

		var ok = (await NewController(registry).Get("weather")).Result as OkObjectResult;
		var dto = (ApplicationsController.ApplicationDto)ok!.Value!;

		await Assert.That(dto.Scope).IsEqualTo("play");
		await Assert.That(dto.OobPackage).IsEqualTo("weather.now");
	}

	/// <summary>Answers every schema-validation GET with a parseable JSON document.</summary>
	private sealed class JsonSchemaDispatcher : IHttpHandlerCommandDispatcher
	{
		private static readonly HttpHandlerResult Schema = new(200, "OK", "application/json", [], """{"kind":"view"}""");

		public ValueTask<Found<HttpHandlerResult>> DispatchAsync(
			string method, string path, string body, IEnumerable<(string Name, string Value)> headers,
			CancellationToken ct = default) => ValueTask.FromResult<Found<HttpHandlerResult>>(Schema);

		public ValueTask<Found<HttpHandlerResult>> DispatchAsync(
			string method, string path, string body, IEnumerable<(string Name, string Value)> headers,
			string clientIp, DBRef? viewer, CancellationToken ct = default) => ValueTask.FromResult<Found<HttpHandlerResult>>(Schema);
	}
}
