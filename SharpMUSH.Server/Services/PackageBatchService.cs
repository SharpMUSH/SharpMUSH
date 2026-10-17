using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Reviews explicit lifecycle changes and executes the exact accepted snapshot through the existing runner.</summary>
public sealed class PackageBatchService(PackageManifestResolver resolver, IPackageRegistryService registry,
	IPackageInstallService installer, IPluginAdministration plugins, IPluginManager manager,
	IAdministrativeCapabilityService capabilities, IPackageOperationRunner operations,
	PackageBatchRunRegistry runs, IServiceScopeFactory scopes, IHostApplicationLifetime lifetime,
	EngineVersion engine, PluginInstallOptions installOptions, GameFeatureService features,
	PluginUploadStore uploads, IAuditLog audit, ILogger<PackageBatchService> logger)
{
	private const long MaxBatchBinaryBytes = 64 * 1024 * 1024;
	private sealed record Prepared(PackageBatchPlanResponse Plan, string Fingerprint, IReadOnlyList<PackageBatchCandidate> Candidates);
	private sealed record Finished(PackageBatchRunState State, string? Message = null);

	public async Task<Result<PackageBatchPlanResponse>> PlanAsync(PackageBatchPlanRequest request, CapabilityActor actor,
		CancellationToken cancellationToken = default)
	{
		if (!await Authorized(actor, cancellationToken, request.Intents)) return new Error<string>("The requested administrative permissions are required.");
		var prepared = await PrepareAsync(request, cancellationToken);
		if (prepared.Plan.Blockers.Count > 0) return prepared.Plan;
		return runs.CreateReview(actor, request, prepared.Plan, prepared.Fingerprint) switch
		{
			PackageBatchPlanResponse reviewed => reviewed,
			Error<string> error => prepared.Plan with { Blockers = [new("", "review-capacity", error.Value)] }
		};
	}

	public async Task<Result<PackageBatchRunResponse>> ApplyAsync(PackageBatchApplyRequest request, CapabilityActor actor,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!await Authorized(actor, cancellationToken)) return new Error<string>("Package administration permission is required.");
		if (string.IsNullOrWhiteSpace(request.ReviewToken)) return new Error<string>("Request a fresh review first.");
		return runs.Admit(request.ReviewToken, actor) switch
		{
			PackageBatchAdmission admitted => Start(admitted),
			Error<string> error => error
		};
	}

	public async Task<FoundResult<PackageBatchRunResponse>> GetRunAsync(string id, CapabilityActor actor,
		CancellationToken cancellationToken = default)
	{
		if (!await Authorized(actor, cancellationToken)) return new Error<string>("Package administration permission is required.");
		return runs.Get(id, actor) switch
		{
			PackageBatchRunResponse run => run,
			NotFound => new NotFound()
		};
	}

	private PackageBatchRunResponse Start(PackageBatchAdmission admission)
	{
		if (admission.IsNew) _ = Task.Run(() => ExecuteOwnedAsync(admission), CancellationToken.None);
		return admission.Run;
	}

	private async Task ExecuteOwnedAsync(PackageBatchAdmission admission)
	{
		try
		{
			await using var scope = scopes.CreateAsyncScope();
			await scope.ServiceProvider.GetRequiredService<PackageBatchService>()
				.ExecuteAsync(admission, lifetime.ApplicationStopping);
		}
		catch (Exception)
		{
			logger.LogWarning("Package batch {RunId} could not finish its owned execution scope.", admission.Run.Id);
			if (runs.Get(admission.Run.Id, admission.Snapshot.Actor) is PackageBatchRunResponse current
				&& current.State is PackageBatchRunState.Pending or PackageBatchRunState.Running)
				runs.Update(current.Id, PackageBatchRunState.Unknown, current.Steps.Select(s => s with
				{ State = s.State == PackageBatchStepState.Completed ? s.State : PackageBatchStepState.Unknown }).ToArray(),
					"The outcome is unknown. Refresh actual state and request a fresh review.");
		}
	}

	private async Task ExecuteAsync(PackageBatchAdmission admission, CancellationToken hostToken)
	{
		var snapshot = admission.Snapshot;
		var steps = snapshot.Plan.Steps.Select(s => new PackageBatchStepProgress(s.StepId, s.Intent, PackageBatchStepState.Unrun)).ToArray();
		runs.Update(admission.Run.Id, PackageBatchRunState.Running, steps);
		try
		{
			var outcome = await operations.RunAsync("batch", async _ =>
			{
				var token = hostToken;
				// All exits after a committed step return normally so the runner drains collected lifecycle hooks.
				try
				{
					var current = await RevalidateAsync(snapshot, token);
					var restartPending = false;
					var reviews = (snapshot.Request.Reviews ?? []).ToDictionary(r => r.StepId, StringComparer.Ordinal);
					foreach (var (step, index) in snapshot.Plan.Steps.Select((step, index) => (step, index)))
					{
						if (!await Authorized(snapshot.Actor, token, snapshot.Request.Intents)) return Stop(index, PackageBatchStepState.Failed, "Permission changed; review the remaining changes.");
						token.ThrowIfCancellationRequested();
						steps[index] = steps[index] with { State = PackageBatchStepState.Running };
						Publish();
						var committed = false;
						try
						{
							switch (step.Intent.Action)
							{
								case PackageBatchAction.Install:
									if (step.Intent.Source is not { } source) return Stop(index, PackageBatchStepState.Failed, "The package source is missing.");
									if (await resolver.ResolveAsync(source, true, token) is not ResolvedPackageManifest fetched)
										return Stop(index, PackageBatchStepState.NeedsReview, "The package source is unavailable; review again.");
									var candidate = current.Candidates.Single(c => Same(c.Manifest.Name, step.Intent.TargetId));
									if (candidate.Fingerprint != fetched.Fingerprint) return Stop(index, PackageBatchStepState.NeedsReview, "The source changed; review again.");
									if (fetched.Manifest.RequiredPlugins.Any(id => manager.Registration(id) is null))
										return Stop(index, PackageBatchStepState.AwaitingRestart, "A required plugin is not running. Restart separately, then request a fresh review.");
									var review = reviews.GetValueOrDefault(step.StepId) ?? new PackageBatchStepReview(step.StepId);
									var actual = await installer.PlanAsync(fetched.Manifest, review.ConfigureAnswers, token);
									if (actual.IsBlocked || step.Review is null || NewConflicts(step.Review.Changeset, actual)
										|| PackageConflictReview.Validate(actual, review.Decisions ?? []).Count > 0)
										return Stop(index, PackageBatchStepState.NeedsReview, "Current content needs a fresh review.");
									var apply = new PackageApplyRequest(new(fetched.Remote.Url, fetched.ApplyPath, fetched.Source.Commit, fetched.Remote.Branch),
										review.ConfigureAnswers ?? new(), review.Decisions ?? [], review.KeepRevisions,
										review.AllowPluginCode, review.AllowPastMaximum, fetched.Readme);
									if (await installer.ApplyAsync(fetched.Manifest, apply, token, fetched.Files) is not PackageApplyResult)
										return Stop(index, PackageBatchStepState.NeedsReview, "The installer refused the current content. Review again.");
									committed = true;
									if (fetched.Manifest.Kind == PackageKind.Plugin)
									{
										restartPending = true;
										if (await PluginProgressAsync(step.Intent.TargetId, token) is PluginStatusDto installedStatus)
											steps[index] = steps[index] with { Plugin = installedStatus };
										steps[index] = steps[index] with { Message = "Plugin files installed. Restart separately to load the reviewed version." };
									}
									await features.SetDeclinedAsync(step.Intent.TargetId, declined: false);
									if (PluginUploadStore.IsUploadRemote(source.Remote)) uploads.Discard(source.Path);
									if (fetched.Manifest.Kind is PackageKind.Plugin or PackageKind.Ui)
										await Audit(AuditActions.PluginInstall, step, snapshot.Actor, token);
									if (PackageServerCompatibility.Check(fetched.Manifest, engine.Current).Fit == PackageServerFit.AboveMaximum)
										await Audit(AuditActions.PackagePastMaximum, step, snapshot.Actor, token);
									break;
								case PackageBatchAction.Uninstall:
									var removesPlugin = await registry.GetInstalledPackageAsync(step.Intent.TargetId) is InstalledPackageRecord { Kind: PackageKind.Plugin };
									if (removesPlugin)
									{
										var blocked = await plugins.GetBlockersAsync(step.Intent.TargetId, false, token);
										if (blocked.Plugins.Count > 0) return Stop(index, PackageBatchStepState.AwaitingRestart, "A dependent plugin is still configured on or running. Refresh after it stops.");
										if (blocked.Packages.Count > 0) return Stop(index, PackageBatchStepState.NeedsReview, "Installed content still requires this plugin; review again.");
									}
									if (await installer.UninstallAsync(step.Intent.TargetId, cancellationToken: token) is not Success)
										return Stop(index, PackageBatchStepState.NeedsReview, "The uninstall was refused. Review current dependents and content.");
									committed = true;
									if (removesPlugin)
									{
										if (await PluginProgressAsync(step.Intent.TargetId, token) is PluginStatusDto removedStatus)
											steps[index] = steps[index] with { Plugin = removedStatus };
										if (manager.Registration(step.Intent.TargetId) is not null)
										{
											restartPending = true;
											steps[index] = steps[index] with { Message = "Files removed; the plugin is still running until the server restarts." };
										}
									}
									await features.SetDeclinedAsync(step.Intent.TargetId, declined: true);
									break;
								case PackageBatchAction.PluginOn:
								case PackageBatchAction.PluginOff:
									if (await plugins.SetEnabledAsync(step.Intent.TargetId, step.Intent.Action == PackageBatchAction.PluginOn, token)
										is not PluginChangeResponse changed)
										return Stop(index, PackageBatchStepState.NeedsReview, "The plugin state change was refused. Refresh its actual state.");
									committed = true;
									steps[index] = steps[index] with { Plugin = changed.Plugin with { Reason = null } };
									if (changed.Plugin.Pending != PluginPendingChange.None)
									{
										restartPending = true;
										steps[index] = steps[index] with { Message = "Configured state saved; a separate restart is still required." };
									}
									await Audit(step.Intent.Action == PackageBatchAction.PluginOn ? AuditActions.PluginEnable : AuditActions.PluginDisable,
										step, snapshot.Actor, token);
									break;
							}
							steps[index] = steps[index] with { State = PackageBatchStepState.Completed, Message = steps[index].Message ?? "Completed." };
							Publish();
						}
						catch (OperationCanceledException)
						{
							if (committed) return CommittedFollowupFailed(index);
							return Stop(index, PackageBatchStepState.Unknown, "Execution stopped. Refresh actual state before retrying.");
						}
						catch (Exception)
						{
							logger.LogWarning("Package batch {RunId} step {StepId} failed.", admission.Run.Id, step.StepId);
							if (committed) return CommittedFollowupFailed(index);
							return Stop(index, PackageBatchStepState.Unknown, "The step failed. Refresh actual state and request a fresh review.");
						}
					}
					return new Finished(restartPending ? PackageBatchRunState.AwaitingRestart : PackageBatchRunState.Completed,
						restartPending ? "A restart is still required. Restart separately, then refresh actual state." : null);
				}
				catch (OperationCanceledException) { return new Finished(HasCompleted() ? PackageBatchRunState.Partial : PackageBatchRunState.Cancelled, "Execution stopped; refresh actual state."); }
				catch (Exception) { return new Finished(HasCompleted() ? PackageBatchRunState.Partial : PackageBatchRunState.Failed, "The batch could not continue. Refresh actual state."); }
			}, CancellationToken.None, async _ =>
			{
				await RevalidateAsync(snapshot, hostToken);
				return new BackupProvenance("pre-package", snapshot.Actor.AccountId, "batch",
					string.Join(", ", snapshot.Plan.Steps.Select(s => s.Intent.TargetId)));
			});
			// RunAsync returns after the lifecycle hooks. Only now may polling observe a terminal run.
			var finished = outcome switch
			{
				PackageOperationRan<Finished> ran => ran.Result,
				PackageOperationRefused => new Finished(PackageBatchRunState.Failed, "The queue or automatic backup refused the batch; no steps ran.")
			};
			runs.Update(admission.Run.Id, finished.State, steps, finished.Message);
		}
		catch (OperationCanceledException) { runs.Update(admission.Run.Id, HasCompleted() ? PackageBatchRunState.Partial : PackageBatchRunState.Cancelled, steps, "Execution stopped. Refresh actual state."); }
		catch (Exception) { runs.Update(admission.Run.Id, PackageBatchRunState.Unknown, steps, "The outcome is unknown. Refresh actual state."); }

		bool HasCompleted() => steps.Any(s => s.State == PackageBatchStepState.Completed);
		void Publish() => runs.Update(admission.Run.Id, PackageBatchRunState.Running, steps);
		Finished CommittedFollowupFailed(int index)
		{
			steps[index] = steps[index] with
			{
				State = PackageBatchStepState.Completed,
				Message = "The change committed, but its follow-up bookkeeping did not finish. Refresh before retrying."
			};
			Publish();
			return new(PackageBatchRunState.Partial, steps[index].Message);
		}
		Finished Stop(int index, PackageBatchStepState state, string message)
		{
			steps[index] = steps[index] with
			{
				State = state,
				Message = message,
				ReviewUrl = ReviewUrl(steps[index].Intent)
			};
			if (state == PackageBatchStepState.AwaitingRestart)
				for (var i = index + 1; i < steps.Length; i++) steps[i] = steps[i] with { State = PackageBatchStepState.AwaitingRestart };
			Publish();
			return new(state == PackageBatchStepState.AwaitingRestart ? PackageBatchRunState.AwaitingRestart
				: HasCompleted() ? PackageBatchRunState.Partial : state == PackageBatchStepState.Unknown ? PackageBatchRunState.Unknown : PackageBatchRunState.Failed, message);
		}
	}

	private async Task<Prepared> PrepareAsync(PackageBatchPlanRequest request, CancellationToken token)
	{
		var blockers = new List<PackageBatchBlocker>();
		void Block(string id, string code, string text) => blockers.Add(new(id, code, text));
		if (request.Intents.Count is 0 or > 32 || request.Reviews?.Count > 32
			|| JsonSerializer.SerializeToUtf8Bytes(request).Length > 4 * 1024 * 1024)
			return new(new([], [], [new("", "batch-limit", "Use between 1 and 32 changes and reviews within the review size limit.")], null, null), "", []);
		var installed = await registry.GetInstalledPackagesAsync();
		var dependencies = await registry.GetAllPackageDependenciesAsync();
		var pluginList = await plugins.ListAsync(token);
		var pluginMetadata = await plugins.ReadDependenciesAsync(token);
		var remotes = await registry.GetPackageRemotesAsync();
		var candidates = new List<PackageBatchCandidate>();
		var canonicalSources = new Dictionary<string, PackageBatchSource>(StringComparer.OrdinalIgnoreCase);
		var queued = new HashSet<string>(StringComparer.Ordinal);
		var pending = new Queue<PackageBatchIntent>(request.Intents.Where(i => i.Action == PackageBatchAction.Install));
		long binaryBytes = 0;
		while (pending.TryDequeue(out var intent))
		{
			if (intent.Source is not { } source) { Block(intent.TargetId, "missing-source", "Choose a package source."); continue; }
			if (!queued.Add(JsonSerializer.Serialize(source))) continue;
			if (queued.Count > 32) { Block(intent.TargetId, "batch-limit", "Resolve at most 32 package sources per batch."); break; }
			if (await resolver.ResolveAsync(source, true, token) is not ResolvedPackageManifest resolved)
			{ Block(intent.TargetId, "source-unavailable", "The requested source could not be resolved. Refresh and choose an available source."); continue; }
			if (!Same(intent.TargetId, resolved.Manifest.Name)) { Block(intent.TargetId, "source-mismatch", "The source names a different package."); continue; }
			binaryBytes += resolved.BinaryBytes;
			if (binaryBytes > MaxBatchBinaryBytes) { Block(intent.TargetId, "batch-binary-limit", "A batch may contain at most 64 MiB of binary package files."); break; }
			var canonical = new PackageBatchSource(resolved.Remote.Name, resolved.ApplyPath, source.Version);
			canonicalSources[intent.TargetId] = canonical;
			candidates.Add(new(canonical, resolved.Manifest, resolved.Source.Commit, resolved.Fingerprint, resolved.Warnings));
			foreach (var dependency in resolved.Manifest.Dependencies)
			{
				if (request.Intents.Any(i => i.Action == PackageBatchAction.Install && Same(i.TargetId, dependency.PackageId))) continue;
				if (installed.FirstOrDefault(p => Same(p.Id, dependency.PackageId)) is { } current
					&& PackageVersion.TryParse(current.Version, out var version)
					&& dependency.Constraint.IsSatisfiedBy(version, includePrereleases: true)) continue;
				PackageBatchSource? next = null;
				if (dependency.Source is { } hint)
				{
					var matching = remotes.Where(r => Same(r.Url, hint.Repo) && (hint.Branch is null || Same(r.Branch ?? "", hint.Branch))).ToArray();
					if (matching.Length == 1) next = new(matching[0].Name, hint.Path ?? "");
				}
				else if (BundledPackages.Contains(dependency.PackageId)) next = new(BundledPackages.RemoteName, dependency.PackageId);
				if (next is not null) pending.Enqueue(new(dependency.PackageId, PackageBatchAction.Install, next));
			}
		}
		var normalized = request.Intents.Select(i => i with
		{
			TargetId = i.TargetId.Trim(),
			Source = i.Action == PackageBatchAction.Install && canonicalSources.TryGetValue(i.TargetId, out var source) ? source : i.Source
		}).ToArray();
		var projection = new PackageBatchProjection(installed, dependencies, candidates, pluginList.Plugins,
			pluginMetadata.Dependencies, pluginMetadata.Fingerprint);
		var plan = PackageBatchPlanner.Plan(new(normalized, request.Reviews), projection);
		blockers.AddRange(plan.Blockers);
		var desired = installed.Where(p => !normalized.Any(i => i.Action is PackageBatchAction.Uninstall or PackageBatchAction.Install && Same(i.TargetId, p.Id))).ToList();
		foreach (var candidate in candidates.Where(c => normalized.Any(i => i.Action == PackageBatchAction.Install && Same(i.TargetId, c.Manifest.Name))))
			desired.Add(new(candidate.Manifest.Name, candidate.Manifest.Version.ToString(), "projection", null, candidate.Commit, null, DateTimeOffset.UnixEpoch, 0));
		var desiredPlugins = pluginList.Plugins.Where(PackageBatchPlanner.IsRunning).Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var candidate in candidates.Where(c => c.Manifest.Kind == PackageKind.Plugin && normalized.Any(i =>
			i.Action == PackageBatchAction.Install && Same(i.TargetId, c.Manifest.Name) && i.Source == c.Source)))
			desiredPlugins.Add(candidate.Manifest.Name);
		foreach (var intent in normalized.Where(i => i.Action == PackageBatchAction.PluginOn)) desiredPlugins.Add(intent.TargetId);
		foreach (var intent in normalized.Where(i => i.Action == PackageBatchAction.PluginOff)) desiredPlugins.Remove(intent.TargetId);
		var reviews = (request.Reviews ?? []).GroupBy(r => r.StepId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
		if (reviews.Count != request.Reviews?.Count && request.Reviews is not null) Block("", "duplicate-reviews", "Submit only one review per step.");
		var steps = new List<PackageBatchStep>();
		foreach (var step in plan.Steps)
		{
			if (step.Intent.Action != PackageBatchAction.Install)
			{
				if (step.Intent.Action == PackageBatchAction.Uninstall && (!reviews.TryGetValue(step.StepId, out var consent) || !consent.ConfirmUninstall))
					Block(step.Intent.TargetId, "confirmation-required", "Confirm this uninstall and its existing removal effects.");
				steps.Add(step); continue;
			}
			var matching = candidates.Where(c => Same(c.Manifest.Name, step.Intent.TargetId) && c.Source == step.Intent.Source).ToArray();
			if (matching.Length != 1) { steps.Add(step); continue; }
			var candidate = matching[0];
			var identityPlan = await installer.PlanAsync(candidate.Manifest, cancellationToken: token);
			var stepId = Hash(step.StepId + JsonSerializer.Serialize(identityPlan));
			reviews.TryGetValue(stepId, out var review);
			var answers = review?.ConfigureAnswers ?? new Dictionary<string, string>();
			var changeset = answers.Count > 0 ? await installer.PlanAsync(candidate.Manifest, answers, token) : identityPlan;
			changeset = changeset with { DependencyIssues = PackagePlanService.CheckDependenciesAndConflicts(candidate.Manifest, desired, desiredPlugins) };
			var payload = PackageReviewPresenter.For(candidate.Manifest, candidate.Commit, changeset, answers, candidate.Warnings ?? []);
			steps.Add(step with { StepId = stepId, Review = payload });
			if (payload.Configure.Any(c => !c.Answered)) Block(step.Intent.TargetId, "configuration-required", "Answer every configure prompt before applying.");
			if (changeset.IsBlocked) Block(step.Intent.TargetId, "content-blocked", "Current content or server compatibility blocks this install.");
			if (PackageConflictReview.Validate(changeset, review?.Decisions ?? [], rejectUnused: true).Count > 0) Block(step.Intent.TargetId, "decisions-required", "Review and resolve the current conflicts.");
			if (candidate.Manifest.Kind is PackageKind.Plugin or PackageKind.Ui && (!installOptions.Allowed || review?.AllowPluginCode != true))
				Block(step.Intent.TargetId, "plugin-consent-required", "Managed package installation must be allowed and explicitly trusted for this apply.");
			if (candidate.Manifest.Binary is { } binary && !PluginContractVersion.Satisfies(binary.MinServerVersion))
				Block(step.Intent.TargetId, "plugin-contract-incompatible", "This server does not provide the package's required plugin contract.");
			if (changeset.Compatibility?.Fit == PackageServerFit.AboveMaximum && review?.AllowPastMaximum != true)
				Block(step.Intent.TargetId, "maximum-consent-required", "Confirm applying past the package's maximum server version.");
		}
		plan = plan with { Steps = steps, Blockers = blockers.Distinct().ToArray() };
		return new(plan, Hash(JsonSerializer.Serialize(new { Projection = projection, Steps = steps, engine.Current, installOptions.Allowed })), candidates);
	}

	private async Task<bool> Authorized(CapabilityActor actor, CancellationToken token, IReadOnlyList<PackageBatchIntent>? intents = null)
		=> await capabilities.AuthorizeAsync(actor, PortalPermission.PackagesAdmin, token)
			&& (intents?.Any(i => i.Action is PackageBatchAction.PluginOn or PackageBatchAction.PluginOff) != true
				|| await capabilities.AuthorizeAsync(actor, PortalPermission.ServerOperate, token));

	private sealed class ReviewChangedException() : Exception("The reviewed state or authority changed.");
	private async Task<Prepared> RevalidateAsync(PackageBatchReviewSnapshot snapshot, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		if (!await Authorized(snapshot.Actor, token, snapshot.Request.Intents)) throw new ReviewChangedException();
		var current = await PrepareAsync(snapshot.Request, token);
		if (current.Plan.Blockers.Count > 0 || current.Fingerprint != snapshot.Fingerprint) throw new ReviewChangedException();
		return current;
	}

	private async Task<Found<PluginStatusDto>> PluginProgressAsync(string id, CancellationToken token)
		=> (await plugins.ListAsync(token)).Plugins.FirstOrDefault(p => Same(p.Id, id)) is { } plugin
			? plugin with { Reason = null } : new NotFound();
	private async ValueTask Audit(string action, PackageBatchStep step, CapabilityActor actor, CancellationToken token)
		=> await audit.RecordPortalAsync(actor.AccountId, action, new AuditTarget(action == AuditActions.PackagePastMaximum ? AuditTargetKinds.Package : AuditTargetKinds.Plugin, step.Intent.TargetId, step.Intent.TargetId),
			$"Reviewed batch {step.Intent.Action}", token);
	private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
	private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
	private static string? ReviewUrl(PackageBatchIntent intent) => intent.Source is { } source
		? $"/admin/packages/review?remote={Uri.EscapeDataString(source.Remote)}&path={Uri.EscapeDataString(source.Path)}"
			+ (source.Version is null ? "" : $"&version={Uri.EscapeDataString(source.Version)}") : null;
	private static bool NewConflicts(PackageChangeset reviewed, PackageChangeset actual)
	{
		var before = Conflicts(reviewed).ToHashSet(StringComparer.Ordinal);
		return Conflicts(actual).Any(value => !before.Contains(value));
		static IEnumerable<string> Conflicts(PackageChangeset set) => set.Attributes.Where(a => a.Action == PackageAttributeAction.Conflict).Select(a => JsonSerializer.Serialize(a))
			.Concat(set.Structure.Where(s => s.Action == PackageStructureAction.Conflict).Select(s => JsonSerializer.Serialize(s)))
			.Concat((set.Wiki ?? []).Where(w => w.Action == PackageWikiAction.Conflict).Select(w => JsonSerializer.Serialize(w)));
	}
}
