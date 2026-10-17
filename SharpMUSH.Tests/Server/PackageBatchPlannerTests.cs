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
		var revision = Plan([Install("a")], [Installed("a")], [], [candidate]);
		var newerRevision = Plan([Install("a")], [Installed("a") with { CurrentRevision = 2 }], [], [candidate]);
		await Assert.That(revision.Steps[0].StepId).IsNotEqualTo(newerRevision.Steps[0].StepId);
		var pluginBefore = Plan([Install("a")], [], [], [candidate], [Plugin("p")]);
		var pluginAfter = Plan([Install("a")], [], [], [candidate], [Plugin("p") with { Enabled = true, Pending = PluginPendingChange.Starts }]);
		await Assert.That(pluginBefore.Steps[0].StepId).IsNotEqualTo(pluginAfter.Steps[0].StepId);
	}

	[Test]
	public async Task RemovingPluginOwner_ProposesItsContentDependents_ThenOrdersApprovedRemovalFirst()
	{
		var owner = Installed("plugin") with { Kind = PackageKind.Plugin };
		var content = Installed("content") with { Plugins = ["plugin"] };
		var plugin = Plugin("plugin") with { Package = "plugin", Dependents = ["content"] };
		var plan = Plan([Remove("plugin")], [owner, content], [], [], [plugin]);
		await Assert.That(plan.Proposals.Select(p => p.Intent.TargetId)).Contains("content");
		await Assert.That(plan.Blockers).IsNotEmpty();
		var accepted = Plan([Remove("plugin"), Remove("content")], [owner, content], [], [], [plugin]);
		await Assert.That(accepted.Blockers).IsEmpty();
		await Assert.That(accepted.Steps.Select(p => p.Intent.TargetId)).IsEquivalentTo(["content", "plugin"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task DiscoveredPluginPrerequisites_AreProposed_ThenEnabledBeforeDependent()
	{
		PackageBatchIntent enable = new("leaf", PackageBatchAction.PluginOn);
		var projection = new PackageBatchProjection([], [], [], [Plugin("base"), Plugin("leaf")],
			new Dictionary<string, IReadOnlyList<string>> { ["leaf"] = ["base"] });
		var plan = PackageBatchPlanner.Plan(new([enable]), projection);
		await Assert.That(plan.Proposals.Select(p => p.Intent)).IsEquivalentTo([new PackageBatchIntent("base", PackageBatchAction.PluginOn)]);
		await Assert.That(plan.Steps.Select(p => p.Intent.TargetId)).IsEquivalentTo(["leaf"]);
		var accepted = PackageBatchPlanner.Plan(new([enable, new("base", PackageBatchAction.PluginOn)]), projection);
		await Assert.That(accepted.Blockers).IsEmpty();
		await Assert.That(accepted.Steps.Select(p => p.Intent.TargetId)).IsEquivalentTo(["base", "leaf"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(accepted.Steps.All(p => p.RequiresRestart)).IsTrue();
	}

	[Test]
	public async Task DiscoveredPluginDependents_MustBeExplicitlyTurnedOffFirst()
	{
		var projection = new PackageBatchProjection([], [], [],
			[Plugin("base") with { Enabled = true }, Plugin("leaf") with { Enabled = true }],
			new Dictionary<string, IReadOnlyList<string>> { ["leaf"] = ["base"] });
		var plan = PackageBatchPlanner.Plan(new([new("base", PackageBatchAction.PluginOff)]), projection);
		await Assert.That(plan.Proposals.Select(p => p.Intent.TargetId)).IsEquivalentTo(["leaf"]);
		var accepted = PackageBatchPlanner.Plan(new([new("base", PackageBatchAction.PluginOff), new("leaf", PackageBatchAction.PluginOff)]), projection);
		await Assert.That(accepted.Blockers).IsEmpty();
		await Assert.That(accepted.Steps.Select(p => p.Intent.TargetId)).IsEquivalentTo(["leaf", "base"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task PluginCycle_IsDiagnosed_AndMissingRequiredPluginBlocksInstall()
	{
		var projection = new PackageBatchProjection([], [], [], [Plugin("a"), Plugin("b")],
			new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["b"], ["b"] = ["a"] });
		var plan = PackageBatchPlanner.Plan(new([new("a", PackageBatchAction.PluginOn)]), projection);
		await Assert.That(plan.Blockers.Single(b => b.Code == "dependency-cycle").Message).Contains("a -> b -> a");
		var content = Candidate("content") with { Manifest = Manifest("content") with { Plugins = ["missing"] } };
		var missing = Plan([Install("content")], [], [], [content]);
		await Assert.That(missing.Blockers.Select(b => b.Code)).Contains("missing-plugin");
	}

	[Test]
	public async Task ExplicitPluginPackageInstall_ProvidesRequiredPlugin_AndRunsBeforeContent()
	{
		var content = Candidate("content") with { Manifest = Manifest("content") with { Plugins = ["managed"] } };
		var managed = Candidate("managed") with { Manifest = Manifest("managed") with { Kind = PackageKind.Plugin } };
		var plan = Plan([Install("content"), Install("managed")], [], [], [content, managed]);
		await Assert.That(plan.Blockers).IsEmpty();
		await Assert.That(plan.Proposals).IsEmpty();
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["managed", "content"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(plan.Steps[0].RequiresRestart).IsTrue();
	}

	[Test]
	public async Task RequiredPlugin_IsNotProvidedByUnselectedOrDifferentSourcePluginCandidate()
	{
		var content = Candidate("content") with { Manifest = Manifest("content") with { Plugins = ["managed"] } };
		var managed = Candidate("managed") with { Manifest = Manifest("managed") with { Kind = PackageKind.Plugin } };
		var unselected = Plan([Install("content")], [], [], [content, managed]);
		await Assert.That(unselected.Blockers.Select(b => b.Code)).Contains("missing-plugin");
		var other = Candidate("managed") with { Source = new("other", "managed") };
		var wrongSource = Plan([Install("content"), Install("managed") with { Source = other.Source }], [], [], [content, managed, other]);
		await Assert.That(wrongSource.Blockers.Select(b => b.Code)).Contains("missing-plugin");
		var off = Plan([Install("content"), Install("managed"), new("managed", PackageBatchAction.PluginOff)], [], [], [content, managed]);
		await Assert.That(off.Blockers.Select(b => b.Code)).Contains("contradictory-dependency");
	}

	[Test]
	public async Task RemovingPluginOwner_RequiresDiscoveredDependentPluginOffFirst()
	{
		var projection = new PackageBatchProjection([Installed("owner") with { Kind = PackageKind.Plugin }], [], [],
			[Plugin("owner") with { Package = "owner" }, Plugin("dependent") with { Enabled = true, Status = PluginBootStatus.Loaded }],
			new Dictionary<string, IReadOnlyList<string>> { ["dependent"] = ["owner"] });
		var proposed = PackageBatchPlanner.Plan(new([Remove("owner")]), projection);
		await Assert.That(proposed.Proposals.Select(p => p.Intent)).IsEquivalentTo([new PackageBatchIntent("dependent", PackageBatchAction.PluginOff)]);
		var accepted = PackageBatchPlanner.Plan(new([Remove("owner"), new("dependent", PackageBatchAction.PluginOff)]), projection);
		await Assert.That(accepted.Blockers).IsEmpty();
		await Assert.That(accepted.Steps.Select(p => p.Intent.TargetId)).IsEquivalentTo(["dependent", "owner"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(accepted.Steps[0].RequiresRestart).IsTrue();
	}

	[Test]
	public async Task SelectedUpgradeThatDropsOldDependency_RunsBeforeRemovingPrerequisite()
	{
		var plan = Plan([Remove("b"), Install("a")], [Installed("a"), Installed("b")],
			[new("a", "b", "")], [Candidate("a")]);
		await Assert.That(plan.Blockers).IsEmpty();
		await Assert.That(plan.Proposals).IsEmpty();
		await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["a", "b"],
			TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task SelectedUpgradeThatDropsPluginRequirement_RunsBeforeTurningPluginOffOrRemovingOwner()
	{
		var owner = Installed("plugin") with { Kind = PackageKind.Plugin };
		var content = Installed("a") with { Plugins = ["plugin"] };
		var plugin = Plugin("plugin") with { Package = "plugin", Dependents = ["a"] };
		foreach (var action in new[] { PackageBatchAction.PluginOff, PackageBatchAction.Uninstall })
		{
			var plan = Plan([new("plugin", action), Install("a")], [owner, content], [], [Candidate("a")], [plugin]);
			await Assert.That(plan.Blockers).IsEmpty();
			await Assert.That(plan.Proposals).IsEmpty();
			await Assert.That(plan.Steps.Select(s => s.Intent.TargetId)).IsEquivalentTo(["a", "plugin"],
				TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task UninstallingRunningLoadOncePlugin_ShowsRestartBoundary_OnlyWhenNeeded()
	{
		var owner = Installed("plugin") with { Kind = PackageKind.Plugin };
		var running = Plugin("plugin") with { Package = "plugin", Enabled = true, Status = PluginBootStatus.Loaded };
		var plan = Plan([Remove("plugin")], [owner], [], [], [running]);
		await Assert.That(plan.Steps[0].Plugin?.Id).IsEqualTo("plugin");
		await Assert.That(plan.Steps[0].RequiresRestart).IsTrue();
		var stopped = Plan([Remove("plugin")], [owner], [], [], [Plugin("plugin") with { Package = "plugin" }]);
		await Assert.That(stopped.Steps[0].RequiresRestart).IsFalse();
		var unloadable = Plan([Remove("plugin")], [owner], [], [], [running with { Unloadable = true }]);
		await Assert.That(unloadable.Steps[0].RequiresRestart).IsFalse();
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
