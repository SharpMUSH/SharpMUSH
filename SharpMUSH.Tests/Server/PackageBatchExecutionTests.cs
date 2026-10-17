using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Plugins;

namespace SharpMUSH.Tests.Server;

public class PackageBatchExecutionTests
{
	private static readonly CapabilityActor Actor = new("initiator");
	private static PackageBatchIntent Install(string id) => new(id, PackageBatchAction.Install, new("test", id));

	[Test]
	public async Task PrerequisiteInstallsFirst_WithOneBackup_AndOwnWritesDoNotInvalidateNextConsent()
	{
		using var f = new Fixture();
		f.Add("a"); f.Add("b", "a");
		var run = await f.Apply(await f.Review([Install("b"), Install("a")]));
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Completed);
		await Assert.That(f.Applied).IsEquivalentTo(["a", "b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(f.Runner.Backups).IsEqualTo(1);
	}

	[Test]
	public async Task BackupRefusalCausesZeroWrites()
	{
		using var f = new Fixture(); f.Add("a"); f.Runner.Refuse = true;
		var run = await f.Apply(await f.Review([Install("a")]));
		await Assert.That(f.Applied).IsEmpty();
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Failed);
	}

	[Test]
	[Arguments("revision")]
	[Arguments("commit")]
	[Arguments("trust")]
	[Arguments("plugin")]
	public async Task DriftInsideTheGateRefusesBeforeAnyWrites(string kind)
	{
		using var f = new Fixture(); f.Add("a");
		f.Installed.Add(new("a", "1.0.0", f.Remote.Url, "a", "old", null, DateTimeOffset.UnixEpoch, 1));
		var review = await f.Review([Install("a")]);
		f.Runner.BeforeBody = () =>
		{
			switch (kind)
			{
				case "revision": f.Installed[0] = f.Installed[0] with { CurrentRevision = 2 }; break;
				case "commit": f.Commit = "moved"; break;
				case "trust": f.Remote = f.Remote with { Trust = PackageRemoteTrust.Official }; break;
				case "plugin": f.PluginStates.Add(Plugin("changed")); break;
			}
		};
		var run = await f.Apply(review);
		await Assert.That(f.Applied).IsEmpty();
		await Assert.That(f.Runner.Backups).IsEqualTo(0);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Failed);
		await Assert.That(f.Runner.Backups).IsEqualTo(0);
	}

	[Test]
	public async Task LostPermissionIsRecheckedInsideGate()
	{
		using var f = new Fixture(); f.Add("a");
		var review = await f.Review([Install("a")]);
		f.Runner.BeforeBody = () => f.Authorized = false;
		await f.Apply(review);
		await Assert.That(f.Applied).IsEmpty();
		await Assert.That(f.Runner.Backups).IsEqualTo(0);
	}

	[Test]
	public async Task LaterConflictPreservesCommittedPrerequisite_AndRetryGetsFreshReview()
	{
		using var f = new Fixture(); f.Add("a"); f.Add("b", "a");
		f.PlanFor = manifest => Changes(manifest) with
		{
			Attributes = manifest.Name == "b" && f.Applied.Contains("a")
				? [new("root", "#42:1", "CODE", PackageAttributeAction.Conflict, PackageConflictKind.ModifyModify,
					BaseValue: "old", LiveValue: "local", NewValue: "new")] : []
		};
		var review = await f.Review([Install("a"), Install("b")]);
		var run = await f.Apply(review);
		await Assert.That(f.Applied).IsEquivalentTo(["a"]);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Partial);
		await Assert.That(run.Steps.Single(s => s.Intent.TargetId == "b").State).IsEqualTo(PackageBatchStepState.NeedsReview);
		var fresh = (await f.Service.PlanAsync(new([Install("b")]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(fresh.Steps.Single().StepId).IsNotEqualTo(review.Steps.Single(s => s.Intent.TargetId == "b").StepId);
		await Assert.That(fresh.Steps.Single().Review!.Changeset.HasConflicts).IsTrue();
		var retried = (await f.Service.PlanAsync(new([Install("b")], [new(fresh.Steps[0].StepId,
			Decisions: [new("root", "CODE", PackageConflictResolution.KeepMine)])]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That((await f.Apply(retried)).State).IsEqualTo(PackageBatchRunState.Completed);
		await Assert.That(f.Applied).IsEquivalentTo(["a", "b"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task PluginOnPausesDependentContentUntilPluginActuallyRuns()
	{
		using var f = new Fixture(); f.Add("a", plugin: "required"); f.PluginStates.Add(Plugin("required"));
		f.Plugins.SetEnabledAsync("required", true, Arg.Any<CancellationToken>()).Returns(_ =>
		{
			f.PluginStates[0] = f.PluginStates[0] with { Enabled = true, Pending = PluginPendingChange.Starts };
			return Task.FromResult<Result<PluginChangeResponse>>(new PluginChangeResponse(f.PluginStates[0], [], []));
		});
		var run = await f.Apply(await f.Review([new("required", PackageBatchAction.PluginOn), Install("a")]));
		await Assert.That(f.Applied).IsEmpty();
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
		await Assert.That(run.Steps.Single(s => s.Intent.TargetId == "a").State).IsEqualTo(PackageBatchStepState.AwaitingRestart);
	}

	[Test]
	public async Task AcceptedRunSurvivesHttpCancellation_AndTerminalStatusWaitsForRunnerReturn()
	{
		using var f = new Fixture(); f.Add("a");
		var bodyDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var hooksDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		f.Runner.AfterBody = () => { bodyDone.TrySetResult(); return hooksDone.Task; };
		using var disconnect = new CancellationTokenSource();
		var review = await f.Review([Install("a")]);
		var admitted = (await f.Service.ApplyAsync(new(review.ReviewToken!), Actor, disconnect.Token)).Expect<PackageBatchRunResponse>();
		disconnect.Cancel();
		await bodyDone.Task.WaitAsync(TimeSpan.FromSeconds(20));
		await Assert.That(f.Runs.Get(admitted.Id, Actor).Expect<PackageBatchRunResponse>().State).IsEqualTo(PackageBatchRunState.Running);
		await Assert.That(f.Runner.Token.IsCancellationRequested).IsFalse();
		hooksDone.TrySetResult();
		await Assert.That((await f.Terminal(admitted.Id)).State).IsEqualTo(PackageBatchRunState.Completed);
		var replay = (await f.Service.ApplyAsync(new(review.ReviewToken!), Actor)).Expect<PackageBatchRunResponse>();
		await Assert.That(replay.Id).IsEqualTo(admitted.Id);
		await Assert.That(f.Applied).IsEquivalentTo(["a"]);
	}

	[Test]
	public async Task LostRunAndWrongActorAreUnknown_AndPermissionLossCannotReadProgress()
	{
		using var f = new Fixture(); f.Add("a");
		await Assert.That(await f.Service.GetRunAsync("missing", Actor) is NotFound).IsTrue();
		var run = await f.Apply(await f.Review([Install("a")]));
		await Assert.That(await f.Service.GetRunAsync(run.Id, new("other")) is NotFound).IsTrue();
		f.Authorized = false;
		await Assert.That(await f.Service.GetRunAsync(run.Id, Actor) is Error<string>).IsTrue();
	}

	[Test]
	public async Task PlainInstallNeedsNoEmptyReviewVisit_AndHostCancellationCannotAbandonRunnerWait()
	{
		using var f = new Fixture(); f.Add("a");
		var plan = (await f.Service.PlanAsync(new([Install("a")]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(plan.Blockers).IsEmpty();
		var bodyDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var hooksDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		f.Runner.AfterBody = () => { bodyDone.TrySetResult(); return hooksDone.Task; };
		var run = (await f.Service.ApplyAsync(new(plan.ReviewToken!), Actor)).Expect<PackageBatchRunResponse>();
		await bodyDone.Task.WaitAsync(TimeSpan.FromSeconds(20));
		f.HostStop.Cancel();
		await Assert.That(f.Runner.Token.CanBeCanceled).IsFalse();
		await Assert.That(f.Runs.Get(run.Id, Actor).Expect<PackageBatchRunResponse>().State).IsEqualTo(PackageBatchRunState.Running);
		hooksDone.TrySetResult();
		await Assert.That((await f.Terminal(run.Id)).State).IsEqualTo(PackageBatchRunState.Completed);
	}

	[Test]
	public async Task PackagesAdminAloneCannotApprovePluginStateChanges()
	{
		using var f = new Fixture(); f.PluginStates.Add(Plugin("required")); f.CanOperate = false;
		await Assert.That(await f.Service.PlanAsync(new([new("required", PackageBatchAction.PluginOn)]), Actor) is Error<string>).IsTrue();
		await Assert.That(f.Applied).IsEmpty();
	}

	[Test]
	[Arguments("missing")]
	[Arguments("duplicate")]
	[Arguments("wrong-target")]
	public async Task EveryKnownConflictNeedsOneValidDecisionBeforeAnyBatchCanStart(string kind)
	{
		using var f = new Fixture(); f.Add("a");
		f.PlanFor = manifest => Changes(manifest) with
		{
			Attributes = [new("root", "#42:1", "FIRST", PackageAttributeAction.Conflict, PackageConflictKind.ModifyModify),
				new("root", "#42:1", "SECOND", PackageAttributeAction.Conflict, PackageConflictKind.ModifyModify)]
		};
		var initial = (await f.Service.PlanAsync(new([Install("a")]), Actor)).Expect<PackageBatchPlanResponse>();
		List<PackageConflictDecision> decisions = [new("root", "FIRST", PackageConflictResolution.KeepMine)];
		if (kind == "duplicate") decisions.Add(new("root", "FIRST", PackageConflictResolution.TakeTheirs));
		if (kind == "wrong-target") decisions.Add(new("elsewhere", "SECOND", PackageConflictResolution.TakeTheirs));
		var reviewed = (await f.Service.PlanAsync(new([Install("a")], [new(initial.Steps[0].StepId, Decisions: decisions)]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(reviewed.ReviewToken).IsNull();
		await Assert.That(reviewed.Blockers.Select(b => b.Code)).Contains("decisions-required");
		await Assert.That(f.Applied).IsEmpty();
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task PluginOnlyStateChangeRetainsActualRestartPendingOutcome(bool enabled)
	{
		using var f = new Fixture();
		f.PluginStates.Add(Plugin("required") with { Enabled = !enabled, Status = enabled ? PluginBootStatus.Disabled : PluginBootStatus.Loaded });
		if (!enabled) f.Registrations["required"] = new(false, 1, 0);
		var run = await f.Apply(await f.Review([new("required", enabled ? PackageBatchAction.PluginOn : PackageBatchAction.PluginOff)]));
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
		await Assert.That(run.Steps[0].Plugin!.Pending).IsEqualTo(enabled ? PluginPendingChange.Starts : PluginPendingChange.Stops);
	}

	[Test]
	public async Task PluginOwnerUninstallRetainsRunningUntilRestartTruth()
	{
		using var f = new Fixture();
		f.Installed.Add(new("owned", "1.0.0", f.Remote.Url, "owned", "old", null, DateTimeOffset.UnixEpoch, 1, Kind: PackageKind.Plugin));
		f.PluginStates.Add(Plugin("owned") with { Package = "owned", Enabled = true, Status = PluginBootStatus.Loaded });
		f.Registrations["owned"] = new(false, 1, 0);
		var run = await f.Apply(await f.Review([new("owned", PackageBatchAction.Uninstall)]));
		await Assert.That(f.Removed).IsEquivalentTo(["owned"]);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
		await Assert.That(run.Steps[0].Plugin!.Pending).IsEqualTo(PluginPendingChange.Stops);
	}

	[Test]
	public async Task PluginCodeRequiresPerApplyConsent_AndInstallReportsPendingLoad()
	{
		using var f = new Fixture();
		f.AddPlugin("managed", new PluginPackageFixture.BinarySource("managed"));
		var initial = (await f.Service.PlanAsync(new([Install("managed")]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(initial.ReviewToken).IsNull();
		await Assert.That(initial.Blockers.Select(b => b.Code)).Contains("plugin-consent-required");
		var reviewed = (await f.Service.PlanAsync(new([Install("managed")],
			[new(initial.Steps[0].StepId, AllowPluginCode: true)]), Actor)).Expect<PackageBatchPlanResponse>();
		var run = await f.Apply(reviewed);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
		await Assert.That(run.Steps[0].Plugin!.Pending).IsEqualTo(PluginPendingChange.Starts);
	}

	[Test]
	public async Task ExplicitNewPluginAndContent_AreReviewableButContentWaitsUntilPluginActuallyRuns()
	{
		using var f = new Fixture();
		f.AddPlugin("managed", new PluginPackageFixture.BinarySource("managed"));
		f.Add("content", plugin: "managed");
		PackageBatchIntent[] intents = [Install("content"), Install("managed")];
		var initial = (await f.Service.PlanAsync(new(intents), Actor)).Expect<PackageBatchPlanResponse>();
		var pluginStep = initial.Steps.Single(s => s.Intent.TargetId == "managed");
		var reviewed = (await f.Service.PlanAsync(new(intents, [new(pluginStep.StepId, AllowPluginCode: true)]), Actor))
			.Expect<PackageBatchPlanResponse>();
		await Assert.That(reviewed.Blockers).IsEmpty();
		var run = await f.Apply(reviewed);
		await Assert.That(f.Applied).IsEquivalentTo(["managed"]);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.AwaitingRestart);
		await Assert.That(run.Steps.Single(s => s.Intent.TargetId == "content").State).IsEqualTo(PackageBatchStepState.AwaitingRestart);
		f.Registrations["managed"] = new(false, 1, 0);
		f.PluginStates[0] = f.PluginStates[0] with { Status = PluginBootStatus.Loaded, Pending = PluginPendingChange.None };
		var resumed = await f.Apply(await f.Review([Install("content")]));
		await Assert.That(resumed.State).IsEqualTo(PackageBatchRunState.Completed);
		await Assert.That(f.Applied).IsEquivalentTo(["managed", "content"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AggregateBinaryLimitRefusesBeforeTokenAndWrites()
	{
		using var f = new Fixture();
		var bytes = new byte[33 * 1024 * 1024];
		f.AddPlugin("one", new LargeBinary("one", bytes)); f.AddPlugin("two", new LargeBinary("two", bytes));
		var plan = (await f.Service.PlanAsync(new([Install("one"), Install("two")]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(plan.ReviewToken).IsNull();
		await Assert.That(plan.Blockers.Select(b => b.Code)).Contains("batch-binary-limit");
		await Assert.That(f.Applied).IsEmpty();
	}

	private sealed class LargeBinary(string id, byte[] bytes) : IPackageFileSource
	{
		public Task<byte[]?> ReadFileAsync(string path, CancellationToken cancellationToken = default)
			=> Task.FromResult<byte[]?>(path == PluginPackageFixture.DllName ? bytes
				: path == "plugin.json" ? PluginPackageFixture.PluginJson(id) : null);
	}

	[Test]
	public async Task DisabledBackupsStillRevalidateBeforeWrites()
	{
		using var f = new Fixture(); f.Add("a"); f.Runner.BackupEnabled = false;
		var review = await f.Review([Install("a")]);
		f.Runner.BeforeBody = () => f.Commit = "changed";
		var run = await f.Apply(review);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Failed);
		await Assert.That(f.Applied).IsEmpty();
		await Assert.That(f.Runner.Backups).IsEqualTo(0);
	}

	[Test]
	public async Task CancellationBeforeAdmissionDoesNothing_WhileCancellationAfterCommitPreservesIt()
	{
		using var f = new Fixture(); f.Add("a"); f.Add("b", "a");
		var review = await f.Review([Install("a"), Install("b")]);
		using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await f.Service.ApplyAsync(new(review.ReviewToken!), Actor, cancelled.Token));
		await Assert.That(f.Runner.Backups).IsEqualTo(0);
		f.AfterApply = id => { if (id == "a") f.HostStop.Cancel(); };
		var run = await f.Apply(review);
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Partial);
		await Assert.That(f.Applied).IsEquivalentTo(["a"]);
		await Assert.That(run.Steps[0].State).IsEqualTo(PackageBatchStepState.Completed);
		await Assert.That(run.Steps[1].State).IsEqualTo(PackageBatchStepState.Unrun);
	}

	[Test]
	public async Task ExceptionAfterPriorCommitPreservesUnknownOutcome_AndDoesNotPublishSecrets()
	{
		using var f = new Fixture(); f.Add("a"); f.Add("b", "a"); f.Add("c", "b");
		f.AfterApply = id => { if (id == "b") throw new IOException("private-config-value"); };
		var run = await f.Apply(await f.Review([Install("a"), Install("b"), Install("c")]));
		await Assert.That(run.State).IsEqualTo(PackageBatchRunState.Partial);
		await Assert.That(run.Steps[0].State).IsEqualTo(PackageBatchStepState.Completed);
		await Assert.That(run.Steps[1].State).IsEqualTo(PackageBatchStepState.Unknown);
		await Assert.That(run.Steps[2].State).IsEqualTo(PackageBatchStepState.Unrun);
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(run)).DoesNotContain("private-config-value");
	}

	[Test]
	public async Task IncompatibleManagedContractCannotReceiveAnApplyToken()
	{
		using var f = new Fixture(); f.AddPlugin("managed", new PluginPackageFixture.BinarySource("managed"));
		f.Manifests["managed"] = PluginPackageFixture.Yaml("managed", minServerVersion: ">=9999.0");
		var first = (await f.Service.PlanAsync(new([Install("managed")]), Actor)).Expect<PackageBatchPlanResponse>();
		var plan = (await f.Service.PlanAsync(new([Install("managed")], [new(first.Steps[0].StepId, AllowPluginCode: true)]), Actor)).Expect<PackageBatchPlanResponse>();
		await Assert.That(plan.ReviewToken).IsNull();
		await Assert.That(plan.Blockers.Select(b => b.Code)).Contains("plugin-contract-incompatible");
		await Assert.That(f.Applied).IsEmpty();
	}

	private static PackageChangeset Changes(PackageManifest manifest) => new(manifest.Name, null, manifest.Version.ToString(),
		PackageRevisionKind.Install, [], [], [], [], [], []);
	private static PluginStatusDto Plugin(string id) => new(id, id, "1", null, PluginOrigin.BuiltIn,
		PluginBootStatus.Disabled, null, false, PluginPendingChange.None, false, null, 0, 0, []);

	private sealed class Fixture : IDisposable
	{
		public PackageRemoteRecord Remote = new("test", "https://example.test/repo", PackageRemoteTrust.Unknown, "main");
		public string Commit = "reviewed";
		public bool Authorized = true;
		public bool CanOperate = true;
		public CancellationTokenSource HostStop { get; } = new();
		public List<InstalledPackageRecord> Installed { get; } = [];
		public List<PluginStatusDto> PluginStates { get; } = [];
		public List<string> Applied { get; } = [];
		public List<string> Removed { get; } = [];
		public Dictionary<string, PluginRegistration> Registrations { get; } = [];
		public Dictionary<string, IPackageFileSource> Files { get; } = [];
		public Dictionary<string, string> Manifests { get; } = [];
		public Func<PackageManifest, PackageChangeset> PlanFor = Changes;
		public Action<string>? AfterApply;
		public IPluginAdministration Plugins { get; } = Substitute.For<IPluginAdministration>();
		public PackageBatchRunRegistry Runs { get; } = new();
		public Runner Runner { get; } = new();
		public PackageBatchService Service { get; }
		private readonly ServiceProvider _provider;
		private readonly Library.Plugins.PluginDirectories _directories = PluginPackageFixture.ScratchDirectories();
		public Fixture()
		{
			var registry = Substitute.For<IPackageRegistryService>();
			var source = Substitute.For<IPackageSourceService>();
			var installer = Substitute.For<IPackageInstallService>();
			var manager = Substitute.For<IPluginManager>();
			manager.Registration(Arg.Any<string>()).Returns(info => Registrations.GetValueOrDefault(info.Arg<string>()));
			var capabilities = Substitute.For<IAdministrativeCapabilityService>();
			var lifetime = Substitute.For<IHostApplicationLifetime>();
			lifetime.ApplicationStopping.Returns(HostStop.Token);
			capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(info => Authorized && (info.Arg<string>() != PortalPermission.ServerOperate || CanOperate));
			registry.GetPackageRemoteAsync(Arg.Any<string>()).Returns(_ => Remote);
			registry.GetPackageRemotesAsync().Returns(_ => new[] { Remote });
			registry.GetInstalledPackagesAsync().Returns(_ => Installed.ToArray());
			registry.GetAllPackageDependenciesAsync().Returns(Array.Empty<PackageDependencyRecord>());
			registry.GetInstalledPackageAsync(Arg.Any<string>()).Returns(info => Installed.FirstOrDefault(p => p.Id == info.Arg<string>())
				is { } found ? new Found<InstalledPackageRecord>(found) : new NotFound());
			source.RefreshAsync(Arg.Any<PackageRemoteRecord>(), Arg.Any<CancellationToken>()).Returns(_ => new PackageRepoSnapshot("test", Remote.Url, Commit, []));
			source.GetManifestAsync(Arg.Any<PackageRemoteRecord>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
				.Returns(info => new PackageManifestSource(Manifests[info.ArgAt<string>(1)], Commit, null));
			source.GetFileSourceAsync(Arg.Any<PackageRemoteRecord>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
				.Returns(info => Task.FromResult<Result<IPackageFileSource>>(Files.TryGetValue(info.ArgAt<string>(1), out var files) ? files : new Error<string>("No files")));
			Plugins.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => new PluginsResponse(PluginStates.ToArray(), true, false));
			Plugins.ReadDependenciesAsync(Arg.Any<CancellationToken>()).Returns(new PluginDependencySnapshot(new Dictionary<string, IReadOnlyList<string>>(), "discovery"));
			Plugins.GetBlockersAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new PluginStateBlockers([], []));
			Plugins.SetEnabledAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(info =>
			{
				var id = info.Arg<string>(); var enabled = info.Arg<bool>();
				var index = PluginStates.FindIndex(p => p.Id == id);
				var before = PluginStates[index];
				if (!enabled && before.Unloadable) Registrations.Remove(id);
				PluginStates[index] = before with
				{
					Enabled = enabled,
					Pending = enabled ? PluginPendingChange.Starts : Registrations.ContainsKey(id) ? PluginPendingChange.Stops : PluginPendingChange.None
				};
				return Task.FromResult<Result<PluginChangeResponse>>(new PluginChangeResponse(PluginStates[index], [], []));
			});
			installer.UninstallAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(info =>
			{
				var id = info.Arg<string>(); Removed.Add(id); Installed.RemoveAll(p => p.Id == id);
				var index = PluginStates.FindIndex(p => p.Id == id);
				if (index >= 0) PluginStates[index] = PluginStates[index] with { Enabled = false, Pending = PluginPendingChange.Stops };
				return Task.FromResult<Result<Success>>(new Success());
			});
			installer.PlanAsync(Arg.Any<PackageManifest>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
				.Returns(info => PlanFor(info.Arg<PackageManifest>()));
			installer.ApplyAsync(Arg.Any<PackageManifest>(), Arg.Any<PackageApplyRequest>(), Arg.Any<CancellationToken>(), Arg.Any<IPackageFileSource?>())
				.Returns(info =>
				{
					var manifest = info.Arg<PackageManifest>(); Applied.Add(manifest.Name);
					Installed.RemoveAll(p => p.Id == manifest.Name);
					Installed.Add(new(manifest.Name, manifest.Version.ToString(), Remote.Url, manifest.Name, Commit, null, DateTimeOffset.UtcNow, 1, Kind: manifest.Kind));
					if (manifest.Kind == PackageKind.Plugin && PluginStates.All(p => p.Id != manifest.Name))
						PluginStates.Add(Plugin(manifest.Name) with { Enabled = true, Status = PluginBootStatus.NotStarted, Pending = PluginPendingChange.Starts });
					AfterApply?.Invoke(manifest.Name);
					return Task.FromResult<Result<PackageApplyResult>>(new PackageApplyResult(1, new Dictionary<string, string>(), []));
				});
			var manifests = new PackageManifestService();
			var uploads = new PluginUploadStore(_directories, manifests, NullLogger<PluginUploadStore>.Instance);
			var resolver = new PackageManifestResolver(registry, source, manifests, uploads);
			var features = new GameFeatureService(registry, installer, manifests, Substitute.For<IBundledPackageBootstrap>(),
				Substitute.For<IExpandedObjectDataService>(), Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), PluginCatalog.Empty(),
				NullLogger<GameFeatureService>.Instance);
			PackageBatchService? service = null;
			_provider = new ServiceCollection().AddSingleton(_ => service ?? throw new InvalidOperationException()).BuildServiceProvider();
			Service = service = new(resolver, registry, installer, Plugins, manager, capabilities, Runner, Runs,
				_provider.GetRequiredService<IServiceScopeFactory>(), lifetime, new(new(1, 0, 0)), new(true), features, uploads,
				Substitute.For<IAuditLog>(), NullLogger<PackageBatchService>.Instance);
		}
		public void Add(string id, string? dependency = null, string? plugin = null)
		{
			Manifests[id] = $"package: {id}\nversion: 1.0.0\nobjects:\n  - ref: root\n    name: Example\n    type: thing\n"
				+ (dependency is null ? "" : $"depends:\n  - package: {dependency}\n    version: '>=1.0'\n")
				+ (plugin is null ? "" : $"plugins:\n  - {plugin}\n");
		}
		public void AddPlugin(string id, IPackageFileSource files)
		{
			Manifests[id] = PluginPackageFixture.Yaml(id, minServerVersion: $">={PluginContractVersion.Current}"); Files[id] = files;
		}

		public async Task<PackageBatchPlanResponse> Review(PackageBatchIntent[] intents)
		{
			var first = (await Service.PlanAsync(new(intents), Actor)).Expect<PackageBatchPlanResponse>();
			var reviews = first.Steps.Select(s => new PackageBatchStepReview(s.StepId, ConfirmUninstall: true)).ToArray();
			return (await Service.PlanAsync(new(intents, reviews), Actor)).Expect<PackageBatchPlanResponse>();
		}
		public async Task<PackageBatchRunResponse> Apply(PackageBatchPlanResponse review)
		{
			var run = (await Service.ApplyAsync(new(review.ReviewToken!), Actor)).Expect<PackageBatchRunResponse>();
			return await Terminal(run.Id);
		}
		public async Task<PackageBatchRunResponse> Terminal(string id)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
			while (true)
			{
				var run = Runs.Get(id, Actor).Expect<PackageBatchRunResponse>();
				if (run.State is not (PackageBatchRunState.Pending or PackageBatchRunState.Running)) return run;
				await Task.Delay(10, timeout.Token);
			}
		}
		public void Dispose() { _provider.Dispose(); HostStop.Dispose(); PluginPackageFixture.Delete(_directories); }
	}

	private sealed class Runner : IPackageOperationRunner
	{
		public bool Refuse;
		public bool BackupEnabled = true;
		public int Backups;
		public Action? BeforeBody;
		public Func<Task>? AfterBody;
		public CancellationToken Token;
		private readonly PackageOperationGate _gate = new();
		public Task<PackageOperationOutcome<T>> RunAsync<T>(string operation, Func<CancellationToken, Task<T>> body,
			CancellationToken cancellationToken = default, Func<CancellationToken, Task<BackupProvenance>>? provenance = null)
			=> _gate.RunAsync<PackageOperationOutcome<T>>(async () =>
			{
				Token = cancellationToken;
				BeforeBody?.Invoke();
				if (BackupEnabled && provenance is not null)
				{
					try { await provenance(cancellationToken); }
					catch (Exception) { return new PackageOperationRefused("Provenance refused"); }
				}
				if (BackupEnabled) Backups++;
				if (Refuse) return new PackageOperationRefused("Backup refused");
				var result = await body(cancellationToken);
				if (AfterBody is not null) await AfterBody();
				return new PackageOperationRan<T>(result, null);
			}, cancellationToken);
	}
}
