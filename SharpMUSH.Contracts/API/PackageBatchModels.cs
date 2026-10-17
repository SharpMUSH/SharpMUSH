using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.API;

/// <summary>The existing lifecycle action requested for a package or plugin.</summary>
public enum PackageBatchAction { Install, Uninstall, PluginOn, PluginOff }

/// <summary>A configured, bundled or staged-upload source, resolved by the server.</summary>
public sealed record PackageBatchSource(string Remote, string Path, string? Version = null);

/// <summary>An explicit request. Package and plugin target namespaces are distinguished by Action.</summary>
public sealed record PackageBatchIntent(string TargetId, PackageBatchAction Action,
	PackageBatchSource? Source = null, string? ClientIntentId = null);

/// <summary>Existing review choices, bound to a server step identity rather than a mutable UI row.</summary>
public sealed record PackageBatchStepReview(string StepId,
	Dictionary<string, string>? ConfigureAnswers = null,
	List<PackageConflictDecision>? Decisions = null,
	int KeepRevisions = 10, bool AllowPluginCode = false, bool AllowPastMaximum = false,
	bool ConfirmUninstall = false);

/// <summary>Read-only planning; proposed prerequisites become executable only after explicit resubmission.</summary>
public sealed record PackageBatchPlanRequest(IReadOnlyList<PackageBatchIntent> Intents,
	IReadOnlyList<PackageBatchStepReview>? Reviews = null);

/// <summary>One ordered effect; Review contains the ordinary package changeset and review controls.</summary>
public sealed record PackageBatchStep(string StepId, PackageBatchIntent Intent, string Reason,
	string? InstalledVersion = null, int? InstalledRevision = null,
	string? ResolvedVersion = null, string? Commit = null,
	PluginStatusDto? Plugin = null, PlanResponse? Review = null, bool RequiresRestart = false);

/// <summary>A change offered for explicit inclusion; it is never part of the executable Steps.</summary>
public sealed record PackageBatchProposal(PackageBatchIntent Intent, string Reason);

/// <summary>An actionable reason the requested batch cannot proceed.</summary>
public sealed record PackageBatchBlocker(string TargetId, string Code, string Message);

/// <summary>The reviewed order and a short-lived, actor-bound handle to its exact sources, state and choices.</summary>
public sealed record PackageBatchPlanResponse(IReadOnlyList<PackageBatchStep> Steps,
	IReadOnlyList<PackageBatchProposal> Proposals, IReadOnlyList<PackageBatchBlocker> Blockers,
	string? ReviewToken, DateTimeOffset? ExpiresAt);

/// <summary>Accepts the exact reviewed order. Repeating the token returns the same admitted run.</summary>
public sealed record PackageBatchApplyRequest(string ReviewToken);

/// <summary>Overall execution state; completed steps remain committed after partial failure.</summary>
public enum PackageBatchRunState { Pending, Running, Completed, Partial, Failed, AwaitingRestart, Cancelled, Unknown }

/// <summary>Truthful state of one effect, including later steps that have not run.</summary>
public enum PackageBatchStepState { Pending, Running, Completed, Failed, Unrun, AwaitingRestart, NeedsReview, Cancelled, Unknown }

/// <summary>Public progress deliberately excludes configure answers and other review secrets.</summary>
public sealed record PackageBatchStepProgress(string StepId, PackageBatchIntent Intent,
	PackageBatchStepState State, string? Message = null, string? ReviewUrl = null,
	PluginStatusDto? Plugin = null);

/// <summary>A process-owned run. Unknown or expired ids require refreshing actual state and replanning.</summary>
public sealed record PackageBatchRunResponse(string Id, PackageBatchRunState State,
	IReadOnlyList<PackageBatchStepProgress> Steps, string? Message = null);
