using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>preferences:</c> block (format 1.21): the settings a package lets each player choose.</summary>
public class PackagePreferenceManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string Header = """
		package: client-prefs
		title: Client preferences
		version: 1.0.0
		wiki:
		  - page: main:client
		    title: Client
		    markdown: About.
		""";

	private const string Keyboard = """
		preferences:
		  keyboard:
		    scope: account
		    label: Keyboard shortcuts
		    description: Keys the portal's play screen answers to.
		    keys:
		      zoom_in:
		        type: text
		        default: Ctrl+=
		        max_length: 20
		        label: Zoom in
		      volume:
		        type: integer
		        default: 70
		        min: 0
		        max: 100
		      sounds:
		        type: boolean
		        default: yes
		      theme:
		        type: choice
		        values: [dark, light]
		        default: Dark
		      triggers:
		        type: json
		        default: '[ {"match": "page"} ]'
		  notices:
		    scope: character
		    keys:
		      quiet:
		        type: boolean
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string block, string format = "1.21")
		=> _service.ParseManifest($"format: \"{format}\"\n{Header}\n{block}\n");

	private PackageManifestFailure Refused(string block) => Parse(block).Expect<PackageManifestFailure>();

	[Test]
	public async Task GroupsAndKeysAreReadWithTheirDefaultsInStoredForm()
	{
		var manifest = Parse(Keyboard).Expect<ParsedPackageManifest>().Manifest;

		var keyboard = manifest.PreferenceGroups[0];
		await Assert.That(manifest.PreferenceGroups.Select(g => g.Name)).IsEquivalentTo(["keyboard", "notices"]);
		await Assert.That(keyboard.Scope).IsEqualTo(PreferenceScope.Account);
		await Assert.That(keyboard.Label).IsEqualTo("Keyboard shortcuts");
		await Assert.That(keyboard.Keys.Select(k => k.Key)).IsEquivalentTo(["zoom_in", "volume", "sounds", "theme", "triggers"]);
		await Assert.That(keyboard.Find("ZOOM_IN").Expect<PackagePreferenceSpec>().MaxLength).IsEqualTo(20);
		await Assert.That(keyboard.Find("volume").Expect<PackagePreferenceSpec>().Default).IsEqualTo("70");
		await Assert.That(keyboard.Find("volume").Expect<PackagePreferenceSpec>().Max).IsEqualTo(100m);
		await Assert.That(keyboard.Find("sounds").Expect<PackagePreferenceSpec>().Default).IsEqualTo("1").Because("a truth value is stored as 1 or 0");
		await Assert.That(keyboard.Find("theme").Expect<PackagePreferenceSpec>().Default).IsEqualTo("dark").Because("a choice is stored as the value it names");
		await Assert.That(keyboard.Find("triggers").Expect<PackagePreferenceSpec>().Default).IsEqualTo("""[{"match":"page"}]""");
		await Assert.That(manifest.PreferenceGroups[1].Scope).IsEqualTo(PreferenceScope.Character);
		await Assert.That(manifest.PreferenceGroups[1].Keys[0].Default).IsNull();
	}

	[Test]
	public async Task BeforeFormat121TheBlockIsIgnoredWithAWarning()
	{
		var parsed = Parse(Keyboard, "1.20").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.PreferenceGroups).IsEmpty();
		await Assert.That(parsed.Warnings.Any(w => w.Path == "preferences")).IsTrue();
	}

	[Test]
	[Arguments("    scope: everyone\n    keys:\n      a:\n        type: text", "preferences.g.scope")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: colour", "preferences.g.keys.a.type")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: choice", "preferences.g.keys.a.values")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: text\n        values: [x]", "preferences.g.keys.a.values")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: integer\n        min: 5\n        max: 1", "preferences.g.keys.a.max")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: integer\n        default: 1.5", "preferences.g.keys.a.default")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: text\n        min: 1", "preferences.g.keys.a.min")]
	[Arguments("    scope: account\n    keys:\n      a:\n        type: boolean\n        max_length: 3", "preferences.g.keys.a.max_length")]
	[Arguments("    scope: account\n    keys:\n      Bad-Key:\n        type: text", "preferences.g.keys.Bad-Key")]
	[Arguments("    scope: account\n    keys: {}", "preferences.g.keys")]
	public async Task AMistakeIsReportedWhereItIs(string group, string path)
		=> await Assert.That(Refused($"preferences:\n  g:\n{group}").Errors.Select(e => e.Path)).Contains(path);

	[Test]
	public async Task ABadGroupNameIsRefused()
		=> await Assert.That(Refused("preferences:\n  Bad_Group:\n    scope: account\n    keys:\n      a:\n        type: text").Errors
			.Select(e => e.Path)).Contains("preferences.Bad_Group");

	[Test]
	public async Task TheWriterWritesWhatTheReaderReads()
	{
		var manifest = Parse(Keyboard).Expect<ParsedPackageManifest>().Manifest;

		var again = _service.ParseManifest(PackageManifestWriter.Write(manifest)).Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(again.PreferenceGroups.Count).IsEqualTo(manifest.PreferenceGroups.Count);
		foreach (var (written, read) in manifest.PreferenceGroups.Zip(again.PreferenceGroups))
		{
			await Assert.That(read.SameAs(written)).IsTrue().Because($"group {written.Name} reads back as it was written");
		}
	}

	[Test]
	public async Task StoredFormsSurviveTheRegistryRow()
	{
		var groups = Parse(Keyboard).Expect<ParsedPackageManifest>().Manifest.PreferenceGroups;

		var read = PreferenceGroupsJson.FromJson(PreferenceGroupsJson.ToJson(groups));

		await Assert.That(read.Zip(groups).All(pair => pair.First.SameAs(pair.Second))).IsTrue();
		await Assert.That(PreferenceGroupsJson.FromJson("not json")).IsEmpty();
	}

	[Test]
	public async Task ThePortalsPackageIdIsReserved()
	{
		var refused = _service.ParseManifest($"format: \"1.21\"\n{Header.Replace("package: client-prefs", "package: portal")}\n")
			.Expect<PackageManifestFailure>();

		await Assert.That(refused.Errors.Select(e => e.Path)).Contains("package");
	}
}

/// <summary>How a value written to a preference is checked and stored.</summary>
public class PreferenceValueTests
{
	private static PackagePreferenceSpec Spec(PreferenceType type, decimal? min = null, decimal? max = null,
		IReadOnlyList<string>? values = null, int maxLength = Preferences.DefaultMaxLength)
		=> new("k", type, null, values ?? [], min, max, maxLength, "", "");

	[Test]
	[Arguments(PreferenceType.Number, " 1.50 ", "1.5")]
	[Arguments(PreferenceType.Number, "-3", "-3")]
	[Arguments(PreferenceType.Integer, "12.0", "12")]
	[Arguments(PreferenceType.Boolean, "On", "1")]
	[Arguments(PreferenceType.Boolean, "false", "0")]
	[Arguments(PreferenceType.Text, " keep my spaces ", " keep my spaces ")]
	[Arguments(PreferenceType.Json, "{ \"a\": [1, 2], \"é\": \"ü\" }", "{\"a\":[1,2],\"é\":\"ü\"}")]
	public async Task AnAllowedValueIsStoredInItsPlainForm(PreferenceType type, string value, string stored)
		=> await Assert.That(Preferences.Check(Spec(type), value).Expect<string>()).IsEqualTo(stored);

	[Test]
	[Arguments(PreferenceType.Number, "1e3", "k is a number.")]
	[Arguments(PreferenceType.Integer, "1.5", "k is a whole number.")]
	[Arguments(PreferenceType.Boolean, "maybe", "k is on or off: 1 or 0.")]
	[Arguments(PreferenceType.Json, "{", "k is JSON, such as {\"a\":1} or [1,2].")]
	public async Task ARefusedValueSaysWhy(PreferenceType type, string value, string reason)
		=> await Assert.That(Preferences.Check(Spec(type), value).Expect<Error<string>>().Value).IsEqualTo(reason);

	[Test]
	public async Task BoundsLengthsAndChoicesAreHeld()
	{
		await Assert.That(Preferences.Check(Spec(PreferenceType.Integer, 0, 10), "11").Expect<Error<string>>().Value).IsEqualTo("k is at most 10.");
		await Assert.That(Preferences.Check(Spec(PreferenceType.Integer, 0, 10), "-1").Expect<Error<string>>().Value).IsEqualTo("k is at least 0.");
		await Assert.That(Preferences.Check(Spec(PreferenceType.Text, maxLength: 3), "abcd").Expect<Error<string>>().Value)
			.IsEqualTo("k is at most 3 characters long.");
		await Assert.That(Preferences.Check(Spec(PreferenceType.Choice, values: ["Dark", "Light"]), "LIGHT").Expect<string>()).IsEqualTo("Light");
		await Assert.That(Preferences.Check(Spec(PreferenceType.Choice, values: ["Dark", "Light"]), "blue").Expect<Error<string>>().Value)
			.IsEqualTo("k is one of: Dark, Light.");
	}

	[Test]
	public async Task AStoredValueTheDeclarationNoLongerAllowsReadsAsTheDefault()
	{
		var spec = Spec(PreferenceType.Integer, 0, 10) with { Default = "5" };

		await Assert.That(Preferences.Effective(spec, "50")).IsEqualTo("5");
		await Assert.That(Preferences.Effective(spec, "7")).IsEqualTo("7");
		await Assert.That(Preferences.Effective(spec, null)).IsEqualTo("5");
	}

	[Test]
	[Arguments("client-prefs.keyboard", "client-prefs", "keyboard")]
	[Arguments("Client-Prefs.Keyboard", "client-prefs", "keyboard")]
	public async Task AGroupIdNamesItsPackageAndGroup(string id, string package, string group)
		=> await Assert.That(Preferences.ParseGroupId(id).Expect<PreferenceGroupRef>()).IsEqualTo(new PreferenceGroupRef(package, group));

	[Test]
	[Arguments("keyboard")]
	[Arguments(".keyboard")]
	[Arguments("client.")]
	[Arguments("client.key_board")]
	public async Task AnythingElseIsNoGroupId(string id)
		=> await Assert.That(Preferences.ParseGroupId(id) is NotFound).IsTrue();
}
