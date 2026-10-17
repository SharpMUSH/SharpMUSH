using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>A session-memory intent queue. No configuration answers are written to browser storage.</summary>
public sealed class PackageBatchDraft : IDisposable
{
	private readonly List<PackageBatchIntent> intents = [];
	private readonly Dictionary<string, string> prerequisiteReasons = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PackageBatchStepReview> reviews = new(StringComparer.Ordinal);
	private readonly HashSet<string> reviewedSteps = new(StringComparer.Ordinal);
	private readonly Dictionary<string, (string Version, int Revision)> uninstallConsents = new(StringComparer.OrdinalIgnoreCase);

	private readonly AccountAuthService? auth;

	public PackageBatchDraft(AccountAuthService? accountAuth = null)
	{
		auth = accountAuth;
		if (accountAuth is not null) accountAuth.AuthStateChanged += Clear;
	}

	public void Clear()
	{
		Revision++;
		AuthorityRevision++;
		AdmissionPending = false;
		AdmissionUnknown = false;
		intents.Clear();
		reviews.Clear();
		reviewedSteps.Clear();
		uninstallConsents.Clear();
		prerequisiteReasons.Clear();
		Plan = null;
		Run = null;
		Changed?.Invoke();
	}

	public void Dispose()
	{
		if (auth is not null) auth.AuthStateChanged -= Clear;
		Clear();
	}

	public IReadOnlyList<PackageBatchIntent> Intents => intents.AsReadOnly();
	public PackageBatchPlanResponse? Plan { get; private set; }
	private PackageBatchRunResponse? run;
	public PackageBatchRunResponse? Run
	{
		get => run;
		set
		{
			if (run == value) return;
			run = value;
			Changed?.Invoke();
		}
	}
	public event Action? Changed;
	public long Revision { get; private set; }
	public long AuthorityRevision { get; private set; }
	public bool AdmissionPending { get; private set; }
	public bool AdmissionUnknown { get; private set; }
	public bool AdmissionBlocked => AdmissionPending || AdmissionUnknown;

	internal void SetAdmissionState(bool pending, bool unknown)
	{
		AdmissionPending = pending;
		AdmissionUnknown = unknown;
		if (Plan is { } plan) Plan = plan with { ReviewToken = null };
		Changed?.Invoke();
	}


	public void Queue(PackageBatchIntent intent, string? prerequisiteReason = null)
	{
		var index = intents.FindIndex(i => SameTarget(i, intent));
		var previous = index >= 0 ? intents[index] : null;
		var next = intent with { ClientIntentId = previous?.ClientIntentId ?? intent.ClientIntentId ?? Guid.NewGuid().ToString("N") };
		if (previous == next) return;
		Revision++;
		reviewedSteps.Clear();
		if (Run?.State is not (PackageBatchRunState.Pending or PackageBatchRunState.Running)) Run = null;
		if (previous is not null) uninstallConsents.Remove(previous.TargetId);
		if (index >= 0) intents[index] = next;
		else intents.Add(next);
		if (prerequisiteReason is not null) prerequisiteReasons[next.ClientIntentId!] = prerequisiteReason;
		else prerequisiteReasons.Remove(next.ClientIntentId!);
		Plan = null;
		Changed?.Invoke();
	}

	public void Remove(string clientIntentId)
	{
		Revision++;
		reviewedSteps.Clear();
		if (Run?.State is not (PackageBatchRunState.Pending or PackageBatchRunState.Running)) Run = null;
		foreach (var removed in intents.Where(i => i.ClientIntentId == clientIntentId))
			uninstallConsents.Remove(removed.TargetId);
		intents.RemoveAll(i => i.ClientIntentId == clientIntentId);
		reviews.Clear();
		reviewedSteps.Clear();
		prerequisiteReasons.Remove(clientIntentId);
		Plan = null;
		Changed?.Invoke();
	}

	public void ConfirmUninstall(string target, string version, int revision)
	{
		Revision++;
		uninstallConsents[target] = (version, revision);
	}

	public bool SetPlan(PackageBatchPlanResponse plan)
	{
		Plan = plan;
		var current = plan.Steps.Select(s => s.StepId).ToHashSet(StringComparer.Ordinal);
		foreach (var stale in reviews.Keys.Where(k => !current.Contains(k)).ToList()) reviews.Remove(stale);
		reviewedSteps.RemoveWhere(id => !current.Contains(id));
		var added = false;
		foreach (var step in plan.Steps.Where(step => step.Intent.Action == PackageBatchAction.Uninstall))
		{
			if (uninstallConsents.Remove(step.Intent.TargetId, out var consent)
				&& consent.Version == step.InstalledVersion && consent.Revision == step.InstalledRevision
				&& !reviews.ContainsKey(step.StepId))
			{
				reviews[step.StepId] = new(step.StepId, ConfirmUninstall: true);
				Revision++;
				added = true;
			}
		}
		Changed?.Invoke();
		return added;
	}

	public string? PrerequisiteReason(string clientIntentId) => prerequisiteReasons.GetValueOrDefault(clientIntentId);

	public PackageBatchStepReview? ReviewFor(string stepId) => reviews.GetValueOrDefault(stepId);

	public bool IsReviewed(string stepId) => reviewedSteps.Contains(stepId);

	public bool SaveReview(PackageBatchStepReview review, bool reviewed = false)
	{
		if (Plan is not { } plan || !plan.Steps.Any(s => s.StepId == review.StepId)) return false;
		reviews[review.StepId] = review;
		if (reviewed) reviewedSteps.Add(review.StepId);
		else reviewedSteps.Remove(review.StepId);
		Revision++;
		Plan = plan with { ReviewToken = null };
		Changed?.Invoke();
		return true;
	}

	public PackageBatchPlanRequest Request() => new(intents.ToList(), reviews.Values.ToList());

	public void RemoveCompleted(PackageBatchRunResponse run)
	{
		Revision++;
		foreach (var step in run.Steps.Where(s => s.State == PackageBatchStepState.Completed))
			intents.RemoveAll(i => SameTarget(i, step.Intent) && i.Action == step.Intent.Action
				&& i.Source == step.Intent.Source && i.ClientIntentId == step.Intent.ClientIntentId);
		reviews.Clear();
		reviewedSteps.Clear();
		uninstallConsents.Clear();
		Plan = null;
		Changed?.Invoke();
	}

	private static bool SameTarget(PackageBatchIntent left, PackageBatchIntent right) =>
		IsPlugin(left.Action) == IsPlugin(right.Action)
		&& string.Equals(left.TargetId, right.TargetId, StringComparison.OrdinalIgnoreCase);

	private static bool IsPlugin(PackageBatchAction action) => action is PackageBatchAction.PluginOn or PackageBatchAction.PluginOff;
}
