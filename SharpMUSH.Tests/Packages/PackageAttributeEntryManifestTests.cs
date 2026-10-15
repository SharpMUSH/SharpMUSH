using SharpMUSH.Database.Seed;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>attributes:</c> block of a manifest (format 1.5).</summary>
public class PackageAttributeEntryManifestTests
{
	private readonly PackageManifestService _service = new();

	private static string Header(string format = "1.5", string kind = "") => $"""
		format: {format}
		package: jobs
		version: "1.0"
		{kind}
		objects:
		  - ref: desk
		    type: thing
		    name: Job Desk
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string body, string format = "1.5")
		=> _service.ParseManifest(Header(format) + "\n" + body);

	private static IReadOnlyList<PackageManifestIssue> Errors(PackageManifestResult<ParsedPackageManifest> result)
		=> result.Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).ToList();

	private IReadOnlyList<PackageAttributeEntrySpec> Entries(string body, string format = "1.5")
		=> Parse(body, format).Expect<ParsedPackageManifest>().Manifest.Declared.AttributeEntries;

	[Test]
	public async Task EveryFieldParses()
	{
		var entries = Entries("""
			attributes:
			  bbpost_count:
			    flags: [no_command, WIZARD]
			  JOB`STATUS:
			    enum: [new, staff, player, held, closed]
			  JOB`TITLE:
			    limit: '^.{1,60}$'
			  JOB`DUE:
			    enum: [this week, someday]
			    delimiter: '|'
			    retroactive: true
			  JOB`NOTE:
			""");

		await Assert.That(entries.Count).IsEqualTo(5);
		var count = entries[0];
		await Assert.That(count.Name).IsEqualTo("BBPOST_COUNT");
		await Assert.That(count.Flags).IsEquivalentTo(["no_command", "wizard"]);
		await Assert.That(count.Limit).IsNull();
		await Assert.That(count.Enum).IsNull();
		await Assert.That(count.Retroactive).IsFalse();
		await Assert.That(entries[1].Enum!).IsEquivalentTo(["new", "staff", "player", "held", "closed"]);
		await Assert.That(entries[1].EnumDelimiter).IsEqualTo(' ');
		await Assert.That(entries[2].Limit).IsEqualTo("^.{1,60}$");
		await Assert.That(entries[3].Enum!).IsEquivalentTo(["this week", "someday"]);
		await Assert.That(entries[3].EnumDelimiter).IsEqualTo('|');
		await Assert.That(entries[3].Retroactive).IsTrue();
		await Assert.That(entries[4].Name).IsEqualTo("JOB`NOTE");
		await Assert.That(entries[4].Flags).IsEmpty();
	}

	[Test]
	public async Task NoBlockMeansNoEntries()
	{
		var manifest = Parse("").Expect<ParsedPackageManifest>().Manifest;
		await Assert.That(manifest.Declared.AttributeEntries).IsEmpty();
		await Assert.That(manifest.Declarations).IsNull();
	}

	[Test]
	public async Task AnOlderFormatIsRefused()
	{
		var errors = Errors(Parse("""
			attributes:
			  JOB`STATUS:
			    flags: [no_command]
			""", format: "1.4"));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes");
		await Assert.That(errors.Single().Message).Contains("format 1.5");
	}

	[Test]
	public async Task APluginPackageCannotDeclareThem()
	{
		var result = _service.ParseManifest("""
			format: 1.5
			package: plug
			version: "1.0"
			kind: plugin
			binaries:
			  min_server_version: ">=0.1"
			  files:
			    - file: Plug.dll
			      sha256: 0000000000000000000000000000000000000000000000000000000000000000
			attributes:
			  PLUG_DATA:
			    flags: [wizard]
			""");
		await Assert.That(Errors(result).Any(e => e.Path == "attributes" && e.Message.Contains("plugin"))).IsTrue();
	}

	[Test]
	[Arguments("BAD NAME", "attributes.BAD NAME")]
	[Arguments("`LEADING", "attributes.`LEADING")]
	[Arguments("TRAILING`", "attributes.TRAILING`")]
	[Arguments("DOUBLE``TICK", "attributes.DOUBLE``TICK")]
	[Arguments("BRACKET[1]", "attributes.BRACKET[1]")]
	[Arguments("PM`REFS", "attributes.PM`REFS")]
	public async Task ANameAtAttributeRefusesIsRefused(string name, string path)
	{
		var errors = Errors(Parse($"""
			attributes:
			  "{name}":
			    flags: [wizard]
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo(path);
	}

	[Test]
	public async Task ANameDeclaredTwiceIsRefused()
	{
		var errors = Errors(Parse("""
			attributes:
			  job_status:
			    flags: [wizard]
			  JOB_STATUS:
			    flags: [wizard]
			"""));
		await Assert.That(errors.Single().Message).Contains("more than once");
	}

	[Test]
	[Arguments("not_a_flag", "is not an attribute flag")]
	[Arguments("branch", "not a default flag")]
	public async Task AFlagMustBeADefaultAttributeFlag(string flag, string message)
	{
		var errors = Errors(Parse($"""
			attributes:
			  JOB_STATUS:
			    flags: [wizard, {flag}]
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.JOB_STATUS.flags[1]");
		await Assert.That(errors.Single().Message).Contains(message);
	}

	[Test]
	public async Task ALimitMustCompile()
	{
		var errors = Errors(Parse("""
			attributes:
			  SCORE:
			    limit: '^(\d+$'
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.SCORE.limit");
	}

	[Test]
	public async Task AnEntryTakesALimitOrAnEnumNotBoth()
	{
		var errors = Errors(Parse("""
			attributes:
			  SCORE:
			    limit: '^\d+$'
			    enum: [1, 2, 3]
			"""));
		await Assert.That(errors.Single().Message).Contains("not both");
	}

	[Test]
	public async Task AChoiceCannotHoldItsDelimiter()
	{
		var errors = Errors(Parse("""
			attributes:
			  DUE:
			    enum: [this week, someday]
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.DUE.enum[0]");
	}

	[Test]
	public async Task ADelimiterIsOneCharacter()
	{
		var errors = Errors(Parse("""
			attributes:
			  DUE:
			    enum: [a, b]
			    delimiter: '||'
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.DUE.delimiter");
	}

	[Test]
	public async Task AnEmptyEnumIsRefused()
	{
		var errors = Errors(Parse("""
			attributes:
			  DUE:
			    enum: []
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.DUE.enum");
	}

	[Test]
	public async Task RetroactiveIsTrueOrFalse()
	{
		var errors = Errors(Parse("""
			attributes:
			  DUE:
			    flags: [wizard]
			    retroactive: sometimes
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.DUE.retroactive");
	}

	[Test]
	public async Task AnEntryMustBeAMapping()
	{
		var errors = Errors(Parse("""
			attributes:
			  DUE: wizard
			"""));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.DUE");
	}

	[Test]
	public async Task AnIndexTakesTrueOrTheOptionWords()
	{
		var entries = Entries("""
			attributes:
			  JOB`STATUS:
			    enum: [new, closed]
			    index: true
			  JOB`LEVEL:
			    index: numeric type:thing
			  JOB`TAGS:
			    index: [exact, "split:|"]
			  JOB`NOTE:
			    index: false
			""", "1.8");

		await Assert.That(entries.Single(e => e.Name == "JOB`STATUS").Index).IsEqualTo(AttributeIndexOptions.Default);
		await Assert.That(entries.Single(e => e.Name == "JOB`LEVEL").Index)
			.IsEqualTo(new AttributeIndexOptions(Numeric: true, Type: "THING"));
		await Assert.That(entries.Single(e => e.Name == "JOB`TAGS").Index)
			.IsEqualTo(new AttributeIndexOptions(Exact: true, Split: "|"));
		await Assert.That(entries.Single(e => e.Name == "JOB`NOTE").Index).IsNull();
	}

	[Test]
	public async Task AnIndexWithAnUnknownOptionIsRefused()
	{
		var errors = Errors(Parse("""
			attributes:
			  JOB`STATUS:
			    index: { fold: case }
			  JOB`LEVEL:
			    index: sorted
			""", "1.8"));
		await Assert.That(errors.Select(e => e.Path)).IsEquivalentTo(["attributes.JOB`STATUS.index", "attributes.JOB`LEVEL.index"]);
	}

	[Test]
	public async Task AnInternalEntryCannotBeIndexed()
	{
		var errors = Errors(Parse("""
			attributes:
			  JOB`SECRET:
			    flags: [internal]
			    index: true
			""", "1.8"));
		await Assert.That(errors.Single().Path).IsEqualTo("attributes.JOB`SECRET.index");
	}

	[Test]
	public async Task ANameTooLongForAnIndexIsRefused()
	{
		var name = new string('N', AttributeIndexOptions.MaxNameBytes + 1);
		var errors = Errors(Parse($"""
			attributes:
			  ? {name}
			  : index: true
			""", "1.8"));
		await Assert.That(errors.Single().Path).IsEqualTo($"attributes.{name}.index");
	}

	/// <summary>Before format 1.8 <c>index:</c> was reserved, so an older manifest's is ignored with a warning.</summary>
	[Test]
	public async Task AnIndexBeforeFormat18IsIgnored()
	{
		var parsed = Parse("""
			attributes:
			  JOB`LEVEL:
			    index: numeric
			""").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.Declared.AttributeEntries.Single().Index).IsNull();
		await Assert.That(parsed.Warnings.Select(w => w.Path)).Contains("attributes.JOB`LEVEL.index");
	}

	/// <summary>The reader knows the attribute flags a new world is seeded with, less <c>branch</c>.</summary>
	[Test]
	public async Task TheKnownFlagsAreTheSeededOnes()
	{
		var seeded = AttributeFlagSeed.Flags.Select(f => f.Name).Where(n => n != "branch");
		await Assert.That(PackageManifestService.AttributeEntryFlags).IsEquivalentTo(seeded);
	}
}
