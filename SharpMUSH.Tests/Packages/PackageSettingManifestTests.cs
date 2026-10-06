using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>settings:</c> block of a manifest (format 1.3): configuration options a package sets.</summary>
public class PackageSettingManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string Header = """
		format: 1.3
		package: greeter
		version: "1.0"
		objects:
		  - ref: desk
		    type: thing
		    name: Greeting Desk
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string body) => _service.ParseManifest(Header + "\n" + body);

	private IReadOnlyList<PackageManifestIssue> Errors(string body)
		=> Parse(body).Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).ToList();

	[Test]
	public async Task ValuesAndRefsParse()
	{
		var settings = Parse("""
			settings:
			  Messages_Object: "{{desk}}"
			  portal_port: "4300"
			  noisy_whisper: yes
			""").Expect<ParsedPackageManifest>().Manifest.Settings;

		await Assert.That(settings).IsEquivalentTo(new[]
		{
			new PackageSettingSpec("messages_object", "{{desk}}"),
			new PackageSettingSpec("portal_port", "4300"),
			new PackageSettingSpec("noisy_whisper", "yes")
		});
	}

	[Test]
	public async Task APackageThatSetsNothingHasNoSettings()
		=> await Assert.That(Parse("").Expect<ParsedPackageManifest>().Manifest.Settings).IsNull();

	[Test]
	[Arguments("no_such_option: 1", "is not a configuration option")]
	[Arguments("sql_password: hunter2", "cannot be set by a package")]
	[Arguments("portal_port: lots", "is not a value 'portal_port' takes")]
	[Arguments("noisy_whisper: perhaps", "is not a value 'noisy_whisper' takes")]
	[Arguments("portal_port: \"{{desk}}\"", "is not a dbref option")]
	[Arguments("messages_object: \"#{{desk}}\"", "must be exactly one ref")]
	[Arguments("messages_object: \"{{nowhere}}\"", "nowhere")]
	[Arguments("portal_port: [1, 2]", "must be a single value")]
	public async Task ABadSettingIsRefused(string line, string expected)
	{
		var errors = Errors($"settings:\n  {line}\n");
		await Assert.That(errors.Any(e => e.Path.StartsWith("settings") && e.Message.Contains(expected))).IsTrue()
			.Because(string.Join("\n", errors.Select(e => $"{e.Path}: {e.Message}")));
	}

	[Test]
	public async Task AnOptionSetTwiceIsRefused()
	{
		var errors = Errors("settings:\n  portal_port: 1\n  PORTAL_PORT: 2\n");
		await Assert.That(errors.Any(e => e.Message.Contains("more than once"))).IsTrue();
	}

	[Test]
	public async Task SettingsMustBeAMapping()
	{
		var errors = Errors("settings:\n  - portal_port\n");
		await Assert.That(errors.Any(e => e.Path == "settings" && e.Message.Contains("mapping"))).IsTrue();
	}
}
