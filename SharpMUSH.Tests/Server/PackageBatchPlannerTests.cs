using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

public class PackageBatchPlannerTests
{
	[Test]
	public async Task RequiredPackage_IsProposedButNeverExecutedWithoutAcceptance()
	{
		var plan = Plan([Install("a")], [], [], [Candidate("a", "b"), Candidate("b")]);
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["a"]);
		await Assert.That(plan.Proposals.Select(s => s.Intent.TargetId)).IsEquivalentTo(["b"]);
		await Assert.That(plan.Blockers.Select(s => s.Code)).Contains("prerequisite-not-accepted");
	}

	[Test]
	public async Task AcceptedPrerequisites_AreOrderedBeforeDependents_OnceForDiamond()
	{
		var plan = Plan([Install("a"), Install("c"), Install("b"), Install("d")], [], [],
			[Candidate("a", "b", "c"), Candidate("b", "d"), Candidate("c", "d"), Candidate("d")]);
		await Assert.That(plan.Blockers).IsEmpty();
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["d", "b", "c", "a"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task InstallingDependentWhileRemovingPrerequisite_IsRejected()
	{
		var plan = Plan([Install("a"), Remove("b")], [Installed("b")], [], [Candidate("a", "b")]);
		await Assert.That(plan.Blockers.Select(s => s.Code)).Contains("contradictory-dependency");
	}

	[Test]
	public async Task RemovingBothPackages_OrdersDependentFirst()
	{
		var plan = Plan([Remove("b"), Remove("a")], [Installed("a"), Installed("b")],
			[new("a", "b", "")], []);
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["a", "b"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(plan.Blockers).IsEmpty();
	}

	[Test]
	public async Task RemovalWithDependent_OffersExplicitRemovalRatherThanCascade()
	{
		var plan = Plan([Remove("b")], [Installed("a"), Installed("b")], [new("a", "b", "")], []);
		await Assert.That(plan.Proposals.Select(s => s.Intent)).IsEquivalentTo([Remove("a")]);
		await Assert.That(plan.Steps).Count().IsEqualTo(1);
		await Assert.That(plan.Blockers).IsNotEmpty();
	}

	[Test]
	public async Task MissingSource_AndIncompatibleCandidate_AreActionableBlockers()
	{
		var missing = Plan([Install("a")], [], [], [Candidate("a", "b")]);
		await Assert.That(missing.Blockers.Select(s => s.Code)).Contains("missing-source");
		var incompatible = Candidate("a") with
		{
			Manifest = Manifest("a") with
			{
				Dependencies = [new("b", Constraint(">=2.0"), null)]
			}
		};
		var plan = Plan([Install("a"), Install("b")], [], [], [incompatible, Candidate("b")]);
		await Assert.That(plan.Blockers.Select(s => s.Code)).Contains("incompatible-version");
	}

	[Test]
	public async Task Cycle_DiagnosesEntirePath()
	{
		var plan = Plan([Install("a"), Install("b"), Install("c")], [], [],
			[Candidate("a", "b"), Candidate("b", "c"), Candidate("c", "a")]);
		await Assert.That(plan.Blockers.Single(s => s.Code == "dependency-cycle").Message).Contains("a -> b -> c -> a");
	}

	[Test]
	public async Task UnrelatedInvalidComponent_DoesNotBlockIndependentRemoval()
	{
		var plan = Plan([Remove("ok")], [Installed("ok"), Installed("bad")], [new("bad", "missing", "")], []);
		await Assert.That(plan.Blockers).IsEmpty();
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["ok"]);
	}

	[Test]
	public async Task PackageAndPluginNamespaces_AreDistinct_ButContradictoryDuplicatesAreRefused()
	{
		var plugin = Plugin("same");
		var plan = Plan([Remove("same"), new("same", PackageBatchAction.PluginOn)], [Installed("same")], [], [], [plugin]);
		await Assert.That(plan.Steps).Count().IsEqualTo(2);
		await Assert.That(plan.Blockers).IsEmpty();
		var duplicate = Plan([Install("a"), Remove("a")], [Installed("a")], [], [Candidate("a")]);
		await Assert.That(duplicate.Blockers.Select(b => b.Code)).Contains("contradictory-intents");
	}

	[Test]
	public async Task StepConsentIdentity_ChangesOnSourceOrStateDrift_ButNotAnswers()
	{
		var candidate = Candidate("a");
		var first = Plan([Install("a")], [], [], [candidate]);
		var changed = Plan([Install("a")], [], [], [candidate with { Commit = "moved" }]);
		await Assert.That(first.Steps[0].StepId).IsNotEqualTo(changed.Steps[0].StepId);
		var reviewed = PackageBatchPlanner.Plan(new([Install("a")], [new(first.Steps[0].StepId,
			new() { ["secret"] = "answer" })]), new([], [], [candidate], [], new Dictionary<string, IReadOnlyList<string>>()));
		await Assert.That(first.Steps[0].StepId).IsEqualTo(reviewed.Steps[0].StepId);
	}

	private static PackageBatchPlanResponse Plan(PackageBatchIntent[] intents, InstalledPackageRecord[] installed,
		PackageDependencyRecord[] dependencies, PackageBatchCandidate[] candidates, PluginStatusDto[]? plugins = null)
		=> PackageBatchPlanner.Plan(new(intents), new(installed, dependencies, candidates, plugins ?? [],
			new Dictionary<string, IReadOnlyList<string>>()));

	private static PackageBatchIntent Install(string id) => new(id, PackageBatchAction.Install, new("test", id));
	private static PackageBatchIntent Remove(string id) => new(id, PackageBatchAction.Uninstall);
	private static InstalledPackageRecord Installed(string id) => new(id, "1.0.0", "test", id, "old", null, DateTimeOffset.UnixEpoch, 1);
	private static PluginStatusDto Plugin(string id) => new(id, id, "1", null, PluginOrigin.BuiltIn,
		PluginBootStatus.Disabled, null, false, PluginPendingChange.None, false, null, 0, 0, []);
	private static VersionConstraint Constraint(string value) => VersionConstraint.TryParse(value, out var parsed)
		? parsed : throw new InvalidOperationException(value);
	private static PackageBatchCandidate Candidate(string id, params string[] dependencies)
		=> new(new("test", id), Manifest(id) with
		{
			Dependencies = dependencies.Select(d => new PackageDependencySpec(d, Constraint(">=1.0"), null)).ToList()
		}, "commit");
	private static PackageManifest Manifest(string id) => new(new(1, 0), id, new(1, 0, 0), [], id,
		null, null, [], null, null, null, [], [], new Dictionary<string, PackageConfigureSpec>(), []);
}
