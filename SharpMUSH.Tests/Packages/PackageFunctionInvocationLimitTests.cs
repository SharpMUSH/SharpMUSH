using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// An object's <c>function_invocation_limit:</c> (format 1.19): read as the engine's <see cref="LiveWire"/>
/// attribute with the override on, written back as the key, and installed, upgraded and read by the limit.
/// </summary>
public class PackageFunctionInvocationLimitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();

	private static string FreshId() => "fil-" + Guid.NewGuid().ToString("N")[..8];

	private static string Yaml(string id, string version, string objectLines, string format = "\"1.19\"")
		=> $$"""
			format: {{format}}
			package: {{id}}
			title: Heavy Package
			version: "{{version}}"
			objects:
			  - ref: core
			    type: thing
			    name: Heavy Core
			{{objectLines}}
			""";

	private static PackageManifestResult<ParsedPackageManifest> Parse(string yaml)
		=> new PackageManifestService().ParseManifest(yaml);

	private Task<Result<PackageApplyResult>> ApplyAsync(PackageManifest manifest)
		=> Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{manifest.Name}/", "commit-1", "main"),
			new Dictionary<string, string>(), []));

	private async Task<string> Read(string expression)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	[Test]
	public async Task TheKeyBecomesTheEnginesLimitWithTheOverrideOn()
	{
		var manifest = Parse(Yaml(FreshId(), "1.0", "    function_invocation_limit: 100000"))
			.Expect<ParsedPackageManifest>().Manifest;

		var attribute = manifest.Objects.Single().Attributes[LiveWire.AttributeName];
		await Assert.That(attribute.Value).IsEqualTo("100000 1");
		await Assert.That(attribute.Flags).IsEquivalentTo(LiveWire.AttributeFlags);
	}

	[Test]
	public async Task AnEarlierFormatIgnoresTheKeyWithAWarning()
	{
		var parsed = Parse(Yaml(FreshId(), "1.0", "    function_invocation_limit: 100000", "\"1.18\""))
			.Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.Objects.Single().Attributes.ContainsKey(LiveWire.AttributeName)).IsFalse();
		await Assert.That(parsed.Warnings.Single().Message)
			.IsEqualTo("'function_invocation_limit' needs format 1.19 or later and is ignored; set format: \"1.19\".");
	}

	[Test]
	[Arguments("    function_invocation_limit: 0")]
	[Arguments("    function_invocation_limit: lots")]
	[Arguments("    function_invocation_limit: 2000000000")]
	[Arguments("    attributes:\n      WIREFUNCEVAL: \"100000 1\"")]
	public async Task ABadLimitOrAHandWrittenAttributeIsRefused(string lines)
	{
		await Assert.That(Parse(Yaml(FreshId(), "1.0", lines)).Expect<PackageManifestFailure>().Errors).IsNotEmpty();
	}

	[Test]
	public async Task AnAttachObjectCannotRaiseAnotherObjectsLimit()
	{
		var failure = Parse($$$"""
			format: "1.19"
			package: {{{FreshId()}}}
			title: Heavy Package
			version: "1.0"
			objects:
			  - ref: handler
			    target: "{{$http_handler}}"
			    function_invocation_limit: 100000
			""").Expect<PackageManifestFailure>();

		await Assert.That(failure.Errors.Select(e => e.Path)).Contains(path => path.EndsWith(".function_invocation_limit"));
	}

	[Test]
	public async Task TheWriterWritesTheKeyBack()
	{
		var manifest = Parse(Yaml(FreshId(), "1.0", "    function_invocation_limit: 100000"))
			.Expect<ParsedPackageManifest>().Manifest;

		var written = PackageManifestWriter.Write(manifest);
		var reread = Parse(written).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(written).Contains("    function_invocation_limit: 100000");
		await Assert.That(written).DoesNotContain(LiveWire.AttributeName);
		await Assert.That(reread.Objects.Single().Attributes[LiveWire.AttributeName].Value).IsEqualTo("100000 1");
	}

	[Test]
	public async Task AnInstallRaisesTheObjectAndAnUpgradeChangesIt()
	{
		var id = FreshId();
		var installed = (await ApplyAsync(Parse(Yaml(id, "1.0", "    function_invocation_limit: 100000"))
			.Expect<ParsedPackageManifest>().Manifest)).Expect<PackageApplyResult>();
		var core = installed.CreatedObjects["core"];

		await Assert.That(await Read($"livewire(funceval,{core})")).IsEqualTo("25000 100000 1");

		(await ApplyAsync(Parse(Yaml(id, "1.1", "    function_invocation_limit: 200000"))
			.Expect<ParsedPackageManifest>().Manifest)).Expect<PackageApplyResult>();
		await Assert.That(await Read($"livewire(funceval,{core})")).IsEqualTo("25000 200000 1");

		(await ApplyAsync(Parse(Yaml(id, "1.2", "")).Expect<ParsedPackageManifest>().Manifest)).Expect<PackageApplyResult>();
		await Assert.That(await Read($"livewire(funceval,{core})")).IsEqualTo("25000 0 0");
	}
}
