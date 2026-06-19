using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Packages;

/// <summary>
/// The bundled "Common Functions" package is delivered by the package manager
/// (create mode): a single owned thing carries the HEADER/FOOTER/LINE softcode
/// that the global functions header()/footer()/line() evaluate, registered by
/// the package's AINSTALL (once) and STARTUP (every boot). These assertions are
/// read-only / additive so they run safely alongside the other tests.
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
public class CommonFunctionsPackageTests(ServerWebAppFactory factory)
{
	private IPackageRegistryService Registry =>
		(IPackageRegistryService)factory.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task CommonFunctions_IsInstalled_WithObjectAndAttributes()
	{
		// The bootstrap installed the package at startup.
		var package = await Registry.GetInstalledPackageAsync("common-functions");
		await Assert.That(package.IsT0).IsTrue();
		await Assert.That(package.AsT0.Version).IsEqualTo("1.0.0");

		// Create mode: the package owns exactly one object (the functions thing).
		var objects = await Registry.GetPackageObjectsAsync("common-functions");
		await Assert.That(objects.Count).IsEqualTo(1);
		await Assert.That(objects.Single().Ref).IsEqualTo("functions");

		// The UI-helper softcode attributes are managed on that object.
		var attrs = (await Registry.GetManagedAttributesAsync("common-functions"))
			.Select(m => m.Attribute).ToList();
		await Assert.That(attrs).Contains("FUN`HEADER");
		await Assert.That(attrs).Contains("FUN`FOOTER");
		await Assert.That(attrs).Contains("FUN`LINE");
		await Assert.That(attrs).Contains("AINSTALL");
		await Assert.That(attrs).Contains("STARTUP");
	}

	/// <summary>
	/// Functional end-to-end check that header() resolves to a centered rule: the
	/// bundled-package bootstrap (<c>DefaultPackagesBootstrapService</c>) installs the
	/// package, whose AINSTALL registers header() as a global <c>@function</c>. The test
	/// enactor (God) reports no client width, so width(%#) falls back to 78.
	/// </summary>
	[Test]
	public async Task Header_RendersFullWidthCenteredRule()
	{
		var result = (await factory.FunctionParser.FunctionParse(MModule.single("header(Title)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(78);
		await Assert.That(result).Contains("Title");
		await Assert.That(result).Contains("=");
	}
}
