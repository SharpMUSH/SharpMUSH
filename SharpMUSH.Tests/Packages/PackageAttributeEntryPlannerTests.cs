using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>What a package's attribute entries (format 1.5) do on install, upgrade and removal.</summary>
public class PackageAttributeEntryPlannerTests
{
	private static readonly PackageAttributeEntrySpec Status =
		new("JOB`STATUS", ["no_command"], Enum: ["new", "held", "closed"]);

	private static PackageDeclarations Declared(params PackageAttributeEntrySpec[] entries) => new([], [], [], [], entries);

	private static PackageDeclarationLiveState Live(
		IReadOnlyList<SharpAttributeEntry>? table = null,
		IReadOnlyDictionary<(PackageDeclarationKind, string), string>? others = null,
		IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>? copies = null)
		=> new(
			[.. Categories.RoleSeeds],
			[.. Categories.PermissionSeeds],
			[],
			[],
			new HashSet<string>(),
			others ?? new Dictionary<(PackageDeclarationKind, string), string>(),
			table ?? [],
			PackageManifestService.AttributeEntryFlags,
			copies);

	private static SharpAttributeEntry Entry(PackageAttributeEntrySpec spec) => new()
	{
		Name = spec.Name, DefaultFlags = [.. spec.Flags], Limit = spec.Limit, Enum = spec.Enum?.ToArray(), EnumDelimiter = spec.EnumDelimiter
	};

	private static PackageDeclarationChange ChangeOf(PackageDeclarationPlan plan, string name)
		=> plan.Changes.Single(c => c.Kind == PackageDeclarationKind.Attribute && c.Name == name);

	[Test]
	public async Task AFreshInstallCreatesAndOwnsTheEntry()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), null, Live(), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		await Assert.That(ChangeOf(plan, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.Create);
		var write = plan.AttributeEntryWrites.Single();
		await Assert.That(write.Entry.SameAs(Status)).IsTrue();
		await Assert.That(write.Retroactive).IsFalse();
		await Assert.That(plan.Owned.AttributeEntries.Single().SameAs(Status)).IsTrue();
	}

	[Test]
	public async Task AnEntryTheGameHasThatAgreesIsLeftAlone()
	{
		var theirs = Entry(Status with { Flags = ["wizard"] });
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), null, Live([theirs]), 5);

		await Assert.That(plan.IsBlocked).IsFalse();
		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Existing);
		await Assert.That(change.Detail!).Contains("default flags stay WIZARD");
		await Assert.That(plan.AttributeEntryWrites).IsEmpty();
		await Assert.That(plan.Owned.AttributeEntries).IsEmpty();
	}

	[Test]
	public async Task AnEntryTheGameHasWithNoRestrictionDeclaredIsTheGames()
	{
		var theirs = Entry(Status);
		var plan = PackageDeclarationPlanner.Plan(Declared(new PackageAttributeEntrySpec("JOB`STATUS", ["no_command"])), null, Live([theirs]), 5);

		await Assert.That(ChangeOf(plan, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.Existing);
	}

	[Test]
	[Arguments("enum")]
	[Arguments("limit")]
	public async Task AnEntryTheGameHasThatContradictsBlocks(string kind)
	{
		var theirs = kind == "enum"
			? Entry(Status with { Enum = ["open", "closed"] })
			: Entry(Status with { Enum = null, Limit = "^[a-z]+$" });
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), null, Live([theirs]), 5);

		await Assert.That(plan.IsBlocked).IsTrue();
		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Blocked);
		await Assert.That(change.Detail!).Contains("enum new held closed");
		await Assert.That(plan.AttributeEntryWrites).IsEmpty();
	}

	[Test]
	public async Task AnEntryAnotherPackageOwnsBlocks()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), null,
			Live([Entry(Status)], new Dictionary<(PackageDeclarationKind, string), string> { [(PackageDeclarationKind.Attribute, "JOB`STATUS")] = "jobs-classic" }), 5);

		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(ChangeOf(plan, "JOB`STATUS").Detail!).Contains("jobs-classic");
	}

	[Test]
	public async Task AFlagTheGameLacksBlocks()
	{
		var live = Live() with { AttributeFlags = new HashSet<string> { "wizard" } };
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), null, live, 5);

		await Assert.That(ChangeOf(plan, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.Blocked);
	}

	[Test]
	public async Task AnUnchangedOwnedEntryStays()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), Declared(Status), Live([Entry(Status)]), 5);

		await Assert.That(ChangeOf(plan, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.NoChange);
		await Assert.That(plan.AttributeEntryWrites).IsEmpty();
		await Assert.That(plan.Owned.AttributeEntries.Single().SameAs(Status)).IsTrue();
	}

	/// <summary>
	/// The administrator changed the flags; the new version changes only the enum. The game's flags stay and the
	/// new enum goes in.
	/// </summary>
	[Test]
	public async Task AnUpgradeMergesFieldByField()
	{
		var mine = Entry(Status with { Flags = ["no_command", "wizard"] });
		var next = Status with { Enum = ["new", "held", "closed", "reopened"] };
		var plan = PackageDeclarationPlanner.Plan(Declared(next), Declared(Status), Live([mine]), 5);

		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Update);
		var written = plan.AttributeEntryWrites.Single().Entry;
		await Assert.That(written.Flags).IsEquivalentTo(["no_command", "wizard"]);
		await Assert.That(written.Enum!).IsEquivalentTo(["new", "held", "closed", "reopened"]);
		await Assert.That(plan.Owned.AttributeEntries.Single().SameAs(next)).IsTrue();
	}

	/// <summary>The administrator replaced the enum with a limit; a version that changes only the flags keeps the limit.</summary>
	[Test]
	public async Task AnUpgradeKeepsTheGamesRestrictionWhenTheVersionLeavesItAlone()
	{
		var mine = Entry(Status with { Enum = null, Limit = "^[a-z]+$" });
		var next = Status with { Flags = ["no_command", "safe"] };
		var plan = PackageDeclarationPlanner.Plan(Declared(next), Declared(Status), Live([mine]), 5);

		var written = plan.AttributeEntryWrites.Single().Entry;
		await Assert.That(written.Flags).IsEquivalentTo(["no_command", "safe"]);
		await Assert.That(written.Limit).IsEqualTo("^[a-z]+$");
		await Assert.That(written.Enum).IsNull();
	}

	[Test]
	public async Task AnOwnedEntryTheGameDeletedIsMadeAgain()
	{
		var plan = PackageDeclarationPlanner.Plan(Declared(Status), Declared(Status), Live(), 5);

		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Create);
		await Assert.That(change.Detail!).Contains("deleted");
	}

	[Test]
	public async Task ADroppedEntryAndUninstallRemoveIt()
	{
		var dropped = PackageDeclarationPlanner.Plan(Declared(), Declared(Status), Live([Entry(Status)]), 5);
		await Assert.That(ChangeOf(dropped, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.Remove);
		await Assert.That(dropped.AttributeEntryRemovals.Single()).IsEqualTo("JOB`STATUS");
		await Assert.That(dropped.Owned.AttributeEntries).IsEmpty();

		var uninstall = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(Status), Live([Entry(Status)]), 5);
		await Assert.That(uninstall.AttributeEntryRemovals.Single()).IsEqualTo("JOB`STATUS");

		// Already gone: nothing to remove.
		var gone = PackageDeclarationPlanner.Plan(PackageDeclarations.None, Declared(Status), Live(), 5);
		await Assert.That(gone.AttributeEntryRemovals).IsEmpty();
		await Assert.That(gone.Changes).IsEmpty();
	}

	/// <summary>
	/// A retroactive entry counts the copies already on objects whose flags differ from the new defaults, a copy's
	/// branch flag aside; the review screen shows the count and waits for a confirm.
	/// </summary>
	[Test]
	public async Task ARetroactiveEntryCountsTheCopiesItChanges()
	{
		var retro = Status with { Retroactive = true };
		IReadOnlyList<IReadOnlyList<string>> found = [["no_command"], ["no_command", "branch"], [], ["wizard"]];
		var copies = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> { ["JOB`STATUS"] = found };
		var plan = PackageDeclarationPlanner.Plan(Declared(retro), null, Live(copies: copies), 5);

		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Retroactive).IsEqualTo(2);
		await Assert.That(change.Detail!).Contains("2 existing copies");
		await Assert.That(plan.AttributeEntryWrites.Single().Retroactive).IsTrue();
	}

	[Test]
	public async Task ARetroactiveEntryWhoseFlagsDoNotChangeChangesNoCopies()
	{
		var retro = Status with { Retroactive = true };
		var next = retro with { Enum = ["new", "closed"] };
		var plan = PackageDeclarationPlanner.Plan(Declared(next), Declared(retro), Live([Entry(retro)]), 5);

		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Update);
		await Assert.That(change.Retroactive).IsNull();
		await Assert.That(plan.AttributeEntryWrites.Single().Retroactive).IsFalse();
	}

	[Test]
	public async Task AVersionThatTurnsRetroactiveOnChangesTheCopies()
	{
		var retro = Status with { Retroactive = true };
		IReadOnlyList<IReadOnlyList<string>> found = [["no_command"], []];
		var copies = new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>> { ["JOB`STATUS"] = found };
		var plan = PackageDeclarationPlanner.Plan(Declared(retro), Declared(Status), Live([Entry(Status)], copies: copies), 5);

		var change = ChangeOf(plan, "JOB`STATUS");
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Update);
		await Assert.That(change.Retroactive).IsEqualTo(1);
		await Assert.That(plan.AttributeEntryWrites.Single().Retroactive).IsTrue();
	}

	[Test]
	public async Task AnEntryAlreadyRetroactiveWithNothingChangedStays()
	{
		var retro = Status with { Retroactive = true };
		var plan = PackageDeclarationPlanner.Plan(Declared(retro), Declared(retro), Live([Entry(retro)]), 5);

		await Assert.That(ChangeOf(plan, "JOB`STATUS").Action).IsEqualTo(PackageDeclarationAction.NoChange);
		await Assert.That(plan.AttributeEntryWrites).IsEmpty();
	}
}
