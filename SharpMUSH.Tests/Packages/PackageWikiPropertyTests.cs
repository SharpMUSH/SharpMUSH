using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Wiki.Semantics;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>wiki_properties:</c> block (format 1.17): the properties a package's wiki facts state.</summary>
public class PackageWikiPropertyTests
{
	private static readonly PackageWikiPropertySpec Level = new("pf2e:level", "number", false, [], "Level");
	private static readonly PackageWikiPropertySpec Tradition = new("pf2e:tradition", "keyword", true, ["arcane", "divine"]);

	private static PackageDeclarations Declared(params PackageWikiPropertySpec[] properties)
		=> new([], [], [], [], WikiProperties: properties.Length == 0 ? null : properties);

	private static PackageDeclarationLiveState Live(params PackageWikiPropertySpec[] world)
		=> Live(new Dictionary<(PackageDeclarationKind, string), string>(), world);

	private static PackageDeclarationLiveState Live(
		Dictionary<(PackageDeclarationKind, string), string> otherOwners, params PackageWikiPropertySpec[] world)
		=> new([], [], [], [], new HashSet<string>(), otherOwners,
			WikiProperties: [.. world.Select(PackageDeclarationPlanner.WikiPropertyDefinition)]);

	private static PackageDeclarationChange Change(PackageDeclarationPlan plan, string id)
		=> plan.Changes.Single(c => c.Kind == PackageDeclarationKind.WikiProperty && c.Name == id);

	[Test]
	public async Task InstallDefinesWhatTheWorldLacks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Level, Tradition), null, Live(), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(Change(plan, "pf2e:tradition").Action).IsEqualTo(PackageDeclarationAction.Create);
		await Assert.That(Change(plan, "pf2e:tradition").Detail).IsEqualTo("keyword, any number of values: arcane, divine.");
		await Assert.That(plan.WikiPropertyWrites.Select(PackageDeclarationPlanner.WikiPropertySpec).Select(p => p.Id))
			.IsEquivalentTo(["pf2e:level", "pf2e:tradition"]);
		await Assert.That(plan.Owned.WikiFactProperties).IsEquivalentTo([Level, Tradition]);
	}

	[Test]
	public async Task TheWorldsOwnPropertyIsUsedWhenItTakesThePackagesValues()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Tradition), null,
			Live(Tradition with { Values = [], Label = "Magic" }), 5);

		await Assert.That(Change(plan, "pf2e:tradition").Action).IsEqualTo(PackageDeclarationAction.Existing);
		await Assert.That(plan.WikiPropertyWrites).IsEmpty();
		await Assert.That(plan.Owned.WikiFactProperties).IsEmpty();
	}

	[Test]
	[Arguments("keyword", true, new[] { "arcane" })]
	[Arguments("keyword", false, new[] { "arcane", "divine" })]
	[Arguments("page", true, new string[0])]
	public async Task TheWorldsOwnPropertyThatDoesNotTakeThemBlocks(string type, bool multiple, string[] values)
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Tradition), null, Live(new PackageWikiPropertySpec("pf2e:tradition", type, multiple, values)), 5);

		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(Change(plan, "pf2e:tradition").Detail!).Contains("Change it with @wiki/property");
	}

	[Test]
	public async Task AnotherPackagesPropertyBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Level), null,
			Live(new Dictionary<(PackageDeclarationKind, string), string> { [(PackageDeclarationKind.WikiProperty, "pf2e:level")] = "pf2e-legacy" }, Level), 5);

		await Assert.That(Change(plan, "pf2e:level").Detail).IsEqualTo("It belongs to the pf2e-legacy package.");
	}

	[Test]
	public async Task AnUpgradeKeepsWhatStaffChangedAndTakesWhatThePackageChanged()
	{
		var staffLabelled = Tradition with { Label = "Magic tradition" };
		var upgraded = Tradition with { Values = ["arcane", "divine", "occult"] };
		var plan = PackageDeclarationPlanner.Plan(Declared(upgraded), Declared(Tradition), Live(staffLabelled), 5);

		var written = PackageDeclarationPlanner.WikiPropertySpec(plan.WikiPropertyWrites.Single());
		await Assert.That(written.Values).IsEquivalentTo(["arcane", "divine", "occult"]);
		await Assert.That(written.Label).IsEqualTo("Magic tradition");
		await Assert.That(Change(plan, "pf2e:tradition").Action).IsEqualTo(PackageDeclarationAction.Update);
		await Assert.That(plan.Owned.WikiFactProperties).IsEquivalentTo([upgraded]);
	}

	[Test]
	public async Task AnUnchangedPropertyIsNotWritten()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Level), Declared(Level), Live(Level), 5);

		await Assert.That(Change(plan, "pf2e:level").Action).IsEqualTo(PackageDeclarationAction.NoChange);
		await Assert.That(plan.WikiPropertyWrites).IsEmpty();
	}

	[Test]
	public async Task UninstallRemovesWhatThePackageOwns()
	{
		var plan = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(Level), Live(Level, Tradition), 5);

		await Assert.That(plan.WikiPropertyRemovals).IsEquivalentTo(["pf2e:level"]);
		await Assert.That(Change(plan, "pf2e:level").Detail).IsEqualTo("Pages that state it keep their facts.");
	}

	private const string Manifest = """
		format: "1.17"
		package: pf2e-rules
		title: PF2e rules
		version: "1.0"
		objects:
		  - ref: desk
		    type: thing
		    name: Rules Desk
		""";

	private static PackageManifestResult<ParsedPackageManifest> Parse(string block, string format = "\"1.17\"")
		=> new PackageManifestService().ParseManifest(Manifest.Replace("\"1.17\"", format) + "\n" + block);

	private static IEnumerable<string> Errors(PackageManifestResult<ParsedPackageManifest> result)
		=> result.Expect<PackageManifestFailure>().Issues.Where(i => i.Severity == PackageManifestIssueSeverity.Error).Select(i => i.Message);

	private const string Block = """
		wiki_properties:
		  pf2e:level:
		    type: number
		    label: " Level "
		  pf2e:tradition:
		    type: keyword
		    multiple: true
		    values: [Arcane, divine]
		    description: The magic a spell draws on.
		""";

	[Test]
	public async Task TheBlockParsesAsTheStoreChecksIt()
	{
		var declared = Parse(Block).Expect<ParsedPackageManifest>().Manifest.Declared;

		await Assert.That(declared.WikiFactProperties).IsEquivalentTo(
			[Level, Tradition with { Description = "The magic a spell draws on." }], new SpecComparer());
	}

	[Test]
	[Arguments("level:\n    type: number", "'level' is not a property key like 'game:level'.")]
	[Arguments("pf2e:level:\n    type: integer", "A wiki property's type is number, boolean, page or keyword.")]
	[Arguments("pf2e:level:\n    type: number\n    values: [high]", "pf2e:level is number; only a keyword property lists its values.")]
	[Arguments("pf2e:level:\n    type: number\n    multiple: maybe", "multiple is true or false.")]
	public async Task ADefinitionTheWikiCannotTakeIsRefused(string entry, string error)
	{
		await Assert.That(Errors(Parse($"wiki_properties:\n  {entry}")).Single()).IsEqualTo(error);
	}

	[Test]
	public async Task AnOlderFormatIgnoresTheBlock()
	{
		var parsed = Parse(Block, "\"1.16\"").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.Declared.WikiFactProperties).IsEmpty();
		await Assert.That(parsed.Warnings.Any(i => i.Path == "wiki_properties")).IsTrue();
	}

	[Test]
	public async Task TheWriterWritesWhatTheReaderReads()
	{
		var manifest = Parse(Block).Expect<ParsedPackageManifest>().Manifest;
		var again = new PackageManifestService().ParseManifest(PackageManifestWriter.Write(manifest)).Expect<ParsedPackageManifest>();

		await Assert.That(again.Manifest.Declared.WikiFactProperties).IsEquivalentTo(manifest.Declared.WikiFactProperties, new SpecComparer());
	}

	[Test]
	public async Task ASpecRoundTripsThroughTheStoresDefinition()
	{
		var definition = PackageDeclarationPlanner.WikiPropertyDefinition(Tradition);

		await Assert.That(WikiFactProperties.Checked(definition).Expect<PropertyDefinition>().Cardinality).IsEqualTo(Cardinality.Multiple);
		await Assert.That(PackageDeclarationPlanner.WikiPropertySpec(definition).SameAs(Tradition)).IsTrue();
	}

	private sealed class SpecComparer : IEqualityComparer<PackageWikiPropertySpec>
	{
		public bool Equals(PackageWikiPropertySpec? x, PackageWikiPropertySpec? y) => x is not null && y is not null && x.SameAs(y);

		public int GetHashCode(PackageWikiPropertySpec obj) => obj.Id.GetHashCode();
	}
}
