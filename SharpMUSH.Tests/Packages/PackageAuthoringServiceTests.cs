using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Phase 7 authoring round-trip: live objects → scan → classify → export →
/// re-parse as a valid format-v2 manifest with zero dbrefs.
/// </summary>
public class PackageAuthoringServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IPackageAuthoringService Authoring => WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>();

	[Test, NotInParallel]
	public async Task ScanAndExport_RoundTripsToValidManifest()
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Known;
		var pm = pmNode.AsPlayer;
		var location = pmNode.AsContainer;

		var coreDbref = await Database.CreateThingAsync("Author Core", location, pm, location);
		var globalDbref = await Database.CreateThingAsync("Author Global", location, pm, location);
		var core = (await Database.GetObjectNodeAsync(coreDbref)).Known.Object();
		var global = (await Database.GetObjectNodeAsync(globalDbref)).Known.Object();

		await Database.SetAttributeAsync(coreDbref, ["FN_FMT"], MarkupText.Plain("formatted output"), pm);
		await Database.SetAttributeAsync(globalDbref, ["CMD_+AUTH"],
			MarkupText.Plain($"$+auth:@pemit %#=[u(#{coreDbref.Number}/FN_FMT)] near #0"), pm);

		var coreObjid = core.DBRef.ToString();
		var globalObjid = global.DBRef.ToString();

		var scan = (await Authoring.ScanAsync([coreObjid, globalObjid])).Expect<PackageAuthoringScan>();
		await Assert.That(scan.Objects.Count).IsEqualTo(2);
		var external = scan.ExternalDbrefs.Single();
		await Assert.That(external.Dbref).IsEqualTo("#0");

		var result = await Authoring.ExportAsync(new PackageAuthoringRequest(
			"authored-pkg", "1.0.0", "Exported from live objects", "MIT", ["Tester"],
			[
				new AuthoringObjectSelection(coreObjid, "auth_core", []),
				new AuthoringObjectSelection(globalObjid, "auth_global", [])
			],
			new Dictionary<string, string> { ["#0"] = "room_zero" },
			new Dictionary<string, AuthoringConfigureClassification>()));

		var yaml = result.Expect<string>();

		await Assert.That(yaml).Contains("{{auth_core}}");
		await Assert.That(yaml).Contains("{{$room_zero}}");
		await Assert.That(yaml).DoesNotContain($"#{coreDbref.Number}/FN_FMT");

		var parsed = new PackageManifestService().ParseManifest(yaml).Expect<ParsedPackageManifest>();
		await Assert.That(parsed.Manifest.Name).IsEqualTo("authored-pkg");
		await Assert.That(parsed.Manifest.Objects.Count).IsEqualTo(2);
		var cmd = parsed.Manifest.Objects.Single(o => o.Ref == "auth_global").Attributes["CMD_+AUTH"];
		await Assert.That(cmd.Value).Contains("u({{auth_core}}/FN_FMT)");
	}

	[Test, NotInParallel]
	public async Task FullRoundTrip_AuthorExportInstall_VerifyState()
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Known;
		var pm = pmNode.AsPlayer;
		var location = pmNode.AsContainer;

		var sourceDbref = await Database.CreateThingAsync("Roundtrip Source", location, pm, location);
		await Database.SetAttributeAsync(sourceDbref, ["FN_GREET"],
			MarkupText.Plain($"Hello from #{sourceDbref.Number} near #0"), pm);
		var sourceObjid = (await Database.GetObjectNodeAsync(sourceDbref)).Known.Object().DBRef.ToString();

		var exported = await Authoring.ExportAsync(new PackageAuthoringRequest(
			"roundtrip-pkg", "1.0.0", "Round-trip test package", "MIT", ["Tester"],
			[new AuthoringObjectSelection(sourceObjid, "rt_core", [])],
			new Dictionary<string, string> { ["#0"] = "room_zero" },
			new Dictionary<string, AuthoringConfigureClassification>()));
		var exportedText = exported.Expect<string>();

		var parsed = new PackageManifestService().ParseManifest(exportedText).Expect<ParsedPackageManifest>();
		var manifest = parsed.Manifest;
		var installer = WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
		var applied = await installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://example.com/roundtrip", "roundtrip-pkg/", "rt-commit", "main"),
			new Dictionary<string, string>(), []));
		var clone = applied.Expect<PackageApplyResult>();

		var cloneObjid = clone.CreatedObjects["rt_core"];
		await Assert.That(cloneObjid).IsNotEqualTo(sourceObjid);

		// The clone's code recalls refs via v(PM`REFS`...) (decision 20.21),
		// and its ref attrs point at the CLONE's dbref and Room Zero.
		var cloneDbref = PackageInstallService.ParseObjid(cloneObjid)!.Value;
		var attribute = await Database.GetAttributeAsync(cloneDbref, ["FN_GREET"]).LastOrDefaultAsync();
		await Assert.That(attribute!.Value.ToPlainText())
			.IsEqualTo("Hello from [v(PM`REFS`RT_CORE)] near [v(PM`REFS`ROOM_ZERO)]");

		var roomZero = (await Database.GetObjectNodeAsync(new DBRef(0))).Known.Object().DBRef.ToString();
		var refCore = await Database.GetAttributeAsync(cloneDbref, ["PM", "REFS", "RT_CORE"]).LastOrDefaultAsync();
		var refRoom = await Database.GetAttributeAsync(cloneDbref, ["PM", "REFS", "ROOM_ZERO"]).LastOrDefaultAsync();
		await Assert.That(refCore!.Value.ToPlainText()).IsEqualTo(cloneObjid);
		await Assert.That(refRoom!.Value.ToPlainText()).IsEqualTo(roomZero);

		await installer.UninstallAsync("roundtrip-pkg");
	}

	[Test, NotInParallel]
	public async Task Export_FailsOnUnclassifiedDbrefs()
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Known;
		var pm = pmNode.AsPlayer;
		var location = pmNode.AsContainer;

		var dbref = await Database.CreateThingAsync("Author Loner", location, pm, location);
		await Database.SetAttributeAsync(dbref, ["FN_X"], MarkupText.Plain("points at #4242 mysteriously"), pm);
		var objid = (await Database.GetObjectNodeAsync(dbref)).Known.Object().DBRef.ToString();

		var result = await Authoring.ExportAsync(new PackageAuthoringRequest(
			"loner-pkg", "1.0.0", "x", null, [],
			[new AuthoringObjectSelection(objid, "loner", [])],
			new Dictionary<string, string>(),
			new Dictionary<string, AuthoringConfigureClassification>()));

		var error = result.Expect<Error<string>>();
		await Assert.That(error.Value).Contains("#4242");
	}

	[Test, NotInParallel]
	public async Task Export_BlankAndWhitespaceAttributeValues_ProduceValidManifest()
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(7))).Known;
		var pm = pmNode.AsPlayer;
		var location = pmNode.AsContainer;

		var dbref = await Database.CreateThingAsync("Whitespace Edge", location, pm, location);
		// A whitespace-only value is the regression: emitted as a block scalar whose
		// only line is blank, YamlDotNet rejected it with "extra spaces in first line"
		// before the explicit indentation indicator was added.
		await Database.SetAttributeAsync(dbref, ["WS_ONLY"], MarkupText.Plain("   "), pm);
		await Database.SetAttributeAsync(dbref, ["LEADING"], MarkupText.Plain("  indented body"), pm);
		await Database.SetAttributeAsync(dbref, ["NORMAL"], MarkupText.Plain("plain value"), pm);
		var objid = (await Database.GetObjectNodeAsync(dbref)).Known.Object().DBRef.ToString();

		var result = await Authoring.ExportAsync(new PackageAuthoringRequest(
			"ws-edge", "1.0.0", "whitespace edge cases", null, ["Tester"],
			[new AuthoringObjectSelection(objid, "ws_obj", [])],
			new Dictionary<string, string>(),
			new Dictionary<string, AuthoringConfigureClassification>()));

		var text = result.Expect<string>("export must produce a valid manifest for blank/whitespace values");

		var parsed = new PackageManifestService().ParseManifest(text).Expect<ParsedPackageManifest>();
		var attrs = parsed.Manifest.Objects.Single(o => o.Ref == "ws_obj").Attributes;
		await Assert.That(attrs.ContainsKey("WS_ONLY")).IsTrue();
		await Assert.That(attrs.ContainsKey("LEADING")).IsTrue();
		await Assert.That(attrs["NORMAL"].Value).IsEqualTo("plain value");
		await Assert.That(attrs["LEADING"].Value).Contains("indented body");
	}
}
