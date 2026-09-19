using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
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

	private async Task<string> Eval(string expression) =>
		(await factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))?.Message?.ToString() ?? string.Empty;

	[Test]
	public async Task CommonFunctions_IsInstalled_WithObjectAndAttributes()
	{
		if (await Registry.GetInstalledPackageAsync("common-functions") is not InstalledPackageRecord package)
			throw new InvalidOperationException("common-functions is not installed.");
		await Assert.That(package.Version).IsEqualTo("2.0.0");

		var objects = await Registry.GetPackageObjectsAsync("common-functions");
		await Assert.That(objects.Count).IsEqualTo(1);
		await Assert.That(objects.Single().Ref).IsEqualTo("functions");

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
		var result = (await factory.FunctionParser.FunctionParse(MarkupText.Plain("header(Title)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(78);
		await Assert.That(result).Contains("Title");
		await Assert.That(result).Contains("=");
	}

	/// <summary>type=left brackets the title and pushes it to the left edge after a short border.</summary>
	[Test]
	public async Task Header_LeftType_BracketsAndLeftJustifies()
	{
		var result = (await factory.FunctionParser.FunctionParse(MarkupText.Plain("header(Test,40,left)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(40);
		await Assert.That(result).Contains("[ Test ]");
		await Assert.That(result).StartsWith("==");
	}

	/// <summary>type=right brackets the title and pushes it to the right edge before a short border.</summary>
	[Test]
	public async Task Footer_RightType_BracketsAndRightJustifies()
	{
		var result = (await factory.FunctionParser.FunctionParse(MarkupText.Plain("footer(Test,40,right)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(40);
		await Assert.That(result).Contains("[ Test ]");
		await Assert.That(result).EndsWith("==");
	}

	/// <summary>A title far wider than the rule is clipped to fit — never overflowing onto a new line.</summary>
	[Test]
	public async Task Header_LongTitle_IsClippedToWidth()
	{
		var huge = new string('x', 200);
		var result = (await factory.FunctionParser.FunctionParse(MarkupText.Plain($"header({huge},40,left)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(40)
			.Because("a long title must be truncated to fit, never wrapping past the width");
	}

	/// <summary>
	/// STARTUP walks the FUN` tree rather than listing the functions, so every branch head must come
	/// back registered. An unregistered name evaluates to its own call text.
	/// </summary>
	[Test]
	public async Task EveryFunctionInTheTree_IsRegistered()
	{
		var objid = (await Registry.GetPackageObjectsAsync("common-functions")).Single().Objid;
		var dbref = DBRef.Parse(objid);

		var heads = (await Eval($"lattr({dbref}/FUN`*)")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(heads.Length).IsGreaterThan(0);

		foreach (var name in heads.Select(head => head["FUN`".Length..].ToLowerInvariant()))
		{
			await Assert.That(await Eval($"{name}()")).IsNotEqualTo($"{name}()")
				.Because($"FUN`{name.ToUpperInvariant()} is in the tree, so {name}() must be registered");
		}
	}
}
