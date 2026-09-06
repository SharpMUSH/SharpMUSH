using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
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

	private IConnectionService ConnectionService => factory.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	private async Task<string> Eval(string expression) =>
		(await factory.FunctionParser.FunctionParse(MModule.single(expression)))?.Message?.ToString() ?? string.Empty;

	/// <summary>
	/// A connected player whose client does or does not announce Pueblo. Handles are picked by hand
	/// across this project and <c>Register</c> KEEPS an existing live handle, so a collision would
	/// silently drop this player's Pueblo metadata; refuse one already registered instead.
	/// </summary>
	private async Task<string> CreatePlayerAsync(string name, long handle, bool pueblo)
	{
		if (ConnectionService.Get(handle) is not null)
		{
			throw new InvalidOperationException(
				$"Handle {handle} is already registered; pick one no other test in this project uses.");
		}

		await factory.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@pcreate {name}=pw-{Tag}"));
		var dbref = (await factory.CommandParser.CommandParse(1, ConnectionService, MModule.single($"think [pmatch({name})]")))
			.Message!.ToPlainText().Trim();
		if (!DBRef.TryParse(dbref, out var parsed) || parsed is null)
		{
			throw new InvalidOperationException($"Failed to create {name}; pmatch returned '{dbref}'.");
		}

		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
			pueblo
				? new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
					new Dictionary<string, string> { ["PUEBLO"] = "1" })
				: null);
		await ConnectionService.Bind(handle, parsed.Value);
		return dbref;
	}

	[Test]
	public async Task CommonFunctions_IsInstalled_WithObjectAndAttributes()
	{
		var package = await Registry.GetInstalledPackageAsync("common-functions");
		await Assert.That(package.IsT0).IsTrue();
		await Assert.That(package.AsT0.Version).IsEqualTo("1.4.0");

		var objects = await Registry.GetPackageObjectsAsync("common-functions");
		await Assert.That(objects.Count).IsEqualTo(1);
		await Assert.That(objects.Single().Ref).IsEqualTo("functions");

		var attrs = (await Registry.GetManagedAttributesAsync("common-functions"))
			.Select(m => m.Attribute).ToList();
		await Assert.That(attrs).Contains("FUN`HEADER");
		await Assert.That(attrs).Contains("FUN`FOOTER");
		await Assert.That(attrs).Contains("FUN`LINE");
		await Assert.That(attrs).Contains("FUN`CMDTAG");
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

	/// <summary>type=left brackets the title and pushes it to the left edge after a short border.</summary>
	[Test]
	public async Task Header_LeftType_BracketsAndLeftJustifies()
	{
		var result = (await factory.FunctionParser.FunctionParse(MModule.single("header(Test,40,left)")))?.Message!.ToString();

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Length).IsEqualTo(40);
		await Assert.That(result).Contains("[ Test ]");
		await Assert.That(result).StartsWith("==");
	}

	/// <summary>type=right brackets the title and pushes it to the right edge before a short border.</summary>
	[Test]
	public async Task Footer_RightType_BracketsAndRightJustifies()
	{
		var result = (await factory.FunctionParser.FunctionParse(MModule.single("footer(Test,40,right)")))?.Message!.ToString();

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
		var result = (await factory.FunctionParser.FunctionParse(MModule.single($"header({huge},40,left)")))?.Message!.ToString();

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
		var dbref = PackageInstallService.ParseObjid(objid)!.Value;

		var heads = (await Eval($"lattr({dbref}/FUN`*)")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(heads.Length).IsGreaterThan(0);

		foreach (var name in heads.Select(head => head["FUN`".Length..].ToLowerInvariant()))
		{
			await Assert.That(await Eval($"{name}()")).IsNotEqualTo($"{name}()")
				.Because($"FUN`{name.ToUpperInvariant()} is in the tree, so {name}() must be registered");
		}
	}

	/// <summary>
	/// cmdtag() writes a clickable command link for a client that can render one and plain text for
	/// one that cannot. The person is an ARGUMENT rather than %#, because the object rendering a
	/// line is often not the enactor — these run as God, which has no Pueblo, so a %#-based
	/// implementation would answer "plain" for everyone and fail the case below.
	/// </summary>
	[Test]
	public async Task CmdTag_AsksTheNamedPerson_NotTheEnactor()
	{
		const long puebloHandle = 9820;
		const long plainHandle = 9821;
		var reader = await CreatePlayerAsync($"CmdTagP{Tag}", puebloHandle, pueblo: true);
		var plain = await CreatePlayerAsync($"CmdTagN{Tag}", plainHandle, pueblo: false);

		var forPueblo = await Eval($"cmdtag({reader},Read it,+help scene)");
		await Assert.That(forPueblo).Contains("xch_cmd")
			.Because("the named person's client can take a command, even though the enactor's cannot");
		await Assert.That(forPueblo).Contains("+help scene");

		var forPlain = await Eval($"cmdtag({plain},Read it,+help scene)");
		await Assert.That(forPlain).IsEqualTo("Read it")
			.Because("a client that renders neither Pueblo nor MXP must get the text, not the markup");
	}

	/// <summary>
	/// The command and the hint are written into QUOTED markup attributes, so a quote inside either
	/// would close the attribute early and hand the client a broken tag with the rest of the command
	/// loose inside it. Both are entity-encoded on the way in.
	/// </summary>
	[Test]
	public async Task CmdTag_EncodesQuotesAndAmpersandsInTheCommandAndHint()
	{
		const long handle = 9823;
		var reader = await CreatePlayerAsync($"CmdTagQ{Tag}", handle, pueblo: true);

		var result = await Eval($"cmdtag({reader},Say it,say \"hi\" & bye,A \"quoted\" hint)");

		await Assert.That(result).Contains("xch_cmd=\"say &quot;hi&quot; &amp; bye\"")
			.Because("a raw quote would end the attribute and leave the rest of the command in the tag");
		await Assert.That(result).Contains("xch_hint=\"A &quot;quoted&quot; hint\"");
	}

	/// <summary>The hint is optional and falls back to the command it runs.</summary>
	[Test]
	public async Task CmdTag_DefaultsTheHintToTheCommand()
	{
		const long handle = 9822;
		var reader = await CreatePlayerAsync($"CmdTagH{Tag}", handle, pueblo: true);

		await Assert.That(await Eval($"cmdtag({reader},Read it,+help scene)")).Contains("xch_hint=\"+help scene\"");
		await Assert.That(await Eval($"cmdtag({reader},Read it,+help scene,Open the topic)"))
			.Contains("xch_hint=\"Open the topic\"");
	}
}
