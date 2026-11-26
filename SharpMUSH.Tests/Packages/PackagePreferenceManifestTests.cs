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

	private const string Versioned = """
		preferences:
		  keyboard:
		    scope: account
		    keys:
		      triggers:
		        type: json
		        version: 3
		        default: '[]'
		      volume:
		        type: integer
		""";

	[Test]
	public async Task AJsonPreferenceDeclaresTheVersionOfItsShape()
	{
		var keyboard = Parse(Versioned, "1.23").Expect<ParsedPackageManifest>().Manifest.PreferenceGroups[0];

		await Assert.That(keyboard.Find("triggers").Expect<PackagePreferenceSpec>().Version).IsEqualTo(3);
		await Assert.That(keyboard.Find("volume").Expect<PackagePreferenceSpec>().Version).IsEqualTo(1).Because("only json takes a version");
		await Assert.That(Parse(Keyboard).Expect<ParsedPackageManifest>().Manifest.PreferenceGroups[0]
			.Find("triggers").Expect<PackagePreferenceSpec>().Version).IsEqualTo(1).Because("a json preference naming none is version 1");
	}

	[Test]
	public async Task BeforeFormat123AVersionIsIgnoredWithAWarning()
	{
		var parsed = Parse(Versioned, "1.22").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.PreferenceGroups[0].Find("triggers").Expect<PackagePreferenceSpec>().Version).IsEqualTo(1);
		await Assert.That(parsed.Warnings.Select(w => w.Path)).Contains("preferences.keyboard.keys.triggers.version");
	}

	[Test]
	[Arguments("text", "2")]
	[Arguments("json", "0")]
	[Arguments("json", "two")]
	[Arguments("json", "1000001")]
	public async Task AVersionIsAWholeNumberOnAJsonPreference(string type, string version)
		=> await Assert.That(_service.ParseManifest(
				$"format: \"1.23\"\n{Header}\npreferences:\n  g:\n    scope: account\n    keys:\n      a:\n        type: {type}\n        version: {version}\n")
			.Expect<PackageManifestFailure>().Errors.Select(e => e.Path)).Contains("preferences.g.keys.a.version");

	[Test]
	public async Task AVersionSurvivesTheWriterAndTheRegistryRow()
	{
		var manifest = Parse(Versioned, "1.23").Expect<ParsedPackageManifest>().Manifest;

		var written = _service.ParseManifest(PackageManifestWriter.Write(manifest)).Expect<ParsedPackageManifest>().Manifest;
		var row = PreferenceGroupsJson.FromJson(PreferenceGroupsJson.ToJson(manifest.PreferenceGroups));

		await Assert.That(written.PreferenceGroups[0].Find("triggers").Expect<PackagePreferenceSpec>().Version).IsEqualTo(3);
		await Assert.That(row[0].Find("triggers").Expect<PackagePreferenceSpec>().Version).IsEqualTo(3);
		await Assert.That(PreferenceGroupsJson.FromJson("""[{"name":"g","scope":"account","label":"","description":"","keys":[{"key":"a","type":"json","default":null,"values":[],"min":null,"max":null,"maxLength":10,"label":"","description":""}]}]""")[0]
			.Keys[0].Version).IsEqualTo(1).Because("a row written before versions were kept is version 1");
	}

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

		await Assert.That(read.Count).IsEqualTo(groups.Count);
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

	[Test]
	[Arguments(PreferenceType.Integer, "100")]
	[Arguments(PreferenceType.Number, "100")]
	[Arguments(PreferenceType.Text, "1e2")]
	[Arguments(PreferenceType.Choice, "1e2")]
	public async Task AJsonNumberIsReadInDecimalFormOnlyForANumberPreference(PreferenceType type, string expected)
	{
		var spec = new PackagePreferenceSpec("key", type, null, [], null, null, Preferences.DefaultMaxLength, "", "");
		using var document = System.Text.Json.JsonDocument.Parse("1e2");

		await Assert.That(SharpMUSH.Library.API.PreferenceJson.Text(spec, document.RootElement)).IsEqualTo(expected);
	}

	[Test]
	public async Task AJsonValueIsStoredWithItsVersionAndReadBackWithoutIt()
	{
		var spec = Spec(PreferenceType.Json) with { Version = 2 };

		var stored = Preferences.Store(spec, Preferences.Check(spec, "[ 1, 2 ]").Expect<string>());

		await Assert.That(stored).IsEqualTo("""{"version":2,"value":[1,2]}""");
		await Assert.That(Preferences.Read(spec, stored).Expect<StoredPreference>()).IsEqualTo(new StoredPreference("[1,2]", 2));
		await Assert.That(Preferences.Read(spec, "[1,2]").Expect<StoredPreference>()).IsEqualTo(new StoredPreference("[1,2]", 1))
			.Because("a value stored without a version is version 1");
		await Assert.That(Preferences.Read(spec, """{"version":1,"value":{"a":1},"extra":0}""").Expect<StoredPreference>().Version).IsEqualTo(1)
			.Because("an object with more than version and value is a value of its own");
		await Assert.That(Preferences.Read(spec, """{"version":0,"value":1}""").Expect<StoredPreference>().Value)
			.IsEqualTo("""{"version":0,"value":1}""").Because("a version is a whole number from 1");
		await Assert.That(Preferences.Read(spec, "not json").Expect<Error<string>>().Value).Contains("is JSON");
		await Assert.That(Preferences.Effective(spec with { Default = "[]" }, stored)).IsEqualTo("[1,2]");
		await Assert.That(Preferences.Store(Spec(PreferenceType.Integer), "5")).IsEqualTo("5").Because("only json is stored with a version");
	}

	[Test]
	public async Task AJsonValueStoredWithItsVersionStillFitsAnAttribute()
	{
		var spec = Spec(PreferenceType.Json, maxLength: Preferences.LargestMaxLength);
		var limit = Preferences.LargestMaxLength - Preferences.Store(spec, string.Empty).Length;

		await Assert.That(Preferences.Check(spec, $"\"{new string('a', limit - 2)}\"").Expect<string>().Length).IsEqualTo(limit);
		await Assert.That(Preferences.Check(spec, $"\"{new string('a', limit - 1)}\"").Expect<Error<string>>().Value)
			.IsEqualTo($"k is at most {limit} characters long.");
	}
}
