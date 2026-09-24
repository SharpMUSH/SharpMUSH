using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.BUnit.Controllers;

/// <summary>
/// Proves the <c>allow_browser_code</c> gate on the applications catalog: a <c>RenderKind=Component</c>
/// (browser-loaded compiled component) app is OMITTED from <c>GET /api/applications</c> and 404s on
/// <c>GET /api/applications/{slug}</c> when the gate is off, and is present when the gate is on. Schema apps
/// are always present regardless of the gate.
/// </summary>
public class ApplicationsGateTests
{
	private static RegisteredApplication SchemaApp(string slug) =>
		new(slug, slug, null, ApplicationKind.Page, "http/x/schema", null, null,
			PortalRole.Guest, "Build", null, 1);

	private static RegisteredApplication ComponentApp(string slug) =>
		new(slug, slug, null, ApplicationKind.Page, "http/x/schema", null, null,
			PortalRole.Guest, "Plugins", null, 2,
			OwningPackage: null,
			RenderKind: ApplicationRenderKind.Component,
			ComponentAssemblyUrl: "api/plugins/p/ui/Comp.dll",
			ComponentTypeName: "Comp.Widget");

	private static ApplicationsController CreateController(bool allowBrowserCode, params RegisteredApplication[] apps)
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns(Task.FromResult<IReadOnlyList<RegisteredApplication>>(apps.ToList()));
		registry.GetApplicationAsync(Arg.Any<string>()).Returns(ci =>
		{
			var slug = ci.Arg<string>();
			var match = apps.FirstOrDefault(a => string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
			return Task.FromResult<Found<RegisteredApplication>>(
				match is null ? new NotFound() : match);
		});

		var dispatcher = Substitute.For<IHttpHandlerCommandDispatcher>();
		var wrapper = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		wrapper.CurrentValue.Returns(OptionsWith(allowBrowserCode));
		var logger = Substitute.For<ILogger<ApplicationsController>>();
		return new ApplicationsController(registry, dispatcher, wrapper, logger);
	}

	[TUnit.Core.Test]
	public async Task List_OmitsComponentApps_WhenGateOff()
	{
		var controller = CreateController(allowBrowserCode: false, SchemaApp("schema-one"), ComponentApp("comp-one"));

		var result = await controller.List();
		var ok = (OkObjectResult)result.Result!;
		var dtos = (IReadOnlyList<ApplicationsController.ApplicationDto>)ok.Value!;

		await Assert.That(dtos.Select(d => d.Slug)).Contains("schema-one");
		await Assert.That(dtos.Any(d => d.Slug == "comp-one")).IsFalse()
			.Because("a Component-kind app is hidden from the catalog when allow_browser_code is off");
	}

	[TUnit.Core.Test]
	public async Task List_IncludesComponentApps_WhenGateOn()
	{
		var controller = CreateController(allowBrowserCode: true, SchemaApp("schema-one"), ComponentApp("comp-one"));

		var result = await controller.List();
		var ok = (OkObjectResult)result.Result!;
		var dtos = (IReadOnlyList<ApplicationsController.ApplicationDto>)ok.Value!;

		await Assert.That(dtos.Select(d => d.Slug)).Contains("comp-one");
		var comp = dtos.First(d => d.Slug == "comp-one");
		await Assert.That(comp.RenderKind).IsEqualTo(ApplicationRenderKind.Component);
		await Assert.That(comp.ComponentAssemblyUrl).IsEqualTo("api/plugins/p/ui/Comp.dll");
		await Assert.That(comp.ComponentTypeName).IsEqualTo("Comp.Widget");
	}

	[TUnit.Core.Test]
	public async Task Get_ComponentApp_NotFound_WhenGateOff()
	{
		var controller = CreateController(allowBrowserCode: false, ComponentApp("comp-one"));

		var result = await controller.Get("comp-one");

		await Assert.That(result.Result).IsTypeOf<NotFoundResult>()
			.Because("a Component-kind app is invisible when the gate is off");
	}

	[TUnit.Core.Test]
	public async Task Get_ComponentApp_Ok_WhenGateOn()
	{
		var controller = CreateController(allowBrowserCode: true, ComponentApp("comp-one"));

		var result = await controller.Get("comp-one");

		await Assert.That(result.Result).IsTypeOf<OkObjectResult>();
	}

	// SharpMUSHOptions with only the Database.AllowBrowserCode flag varied; the rest mirror the proven
	// defaults in ConfigurationControllerTests so the positional records bind correctly.
	private static SharpMUSHOptions OptionsWith(bool allowBrowserCode) => TestOptions.Create(allowBrowserCode);
}
