using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Portal.Onboarding;

public enum GuideJourney { Player, Administrator }
public enum GuidePath { Discord, Mush, OtherRoleplay, Explore }
public enum GuideImagery { Full, Reduced, None }

/// <summary>Learning preferences only; never credentials, character data or command drafts.</summary>
public sealed record OnboardingProgress
{
	public int SchemaVersion { get; init; } = 1;
	public int GuideVersion { get; init; } = 1;
	public long Revision { get; init; }
	public GuidePath Path { get; init; } = GuidePath.Explore;
	public string[] CompletedStepIds { get; init; } = [];
	public string[] SkippedStepIds { get; init; } = [];
	public bool Dismissed { get; init; }
	public bool Active { get; init; }
	public bool AnonymousContinuationHandled { get; init; }
	public GuideImagery Imagery { get; init; } = GuideImagery.Full;
	public string? CurrentStepId { get; init; }
}

public readonly record struct OnboardingConflict(long CurrentRevision);
public union OnboardingSaveResult(OnboardingProgress, OnboardingConflict, Error<string>);
public sealed record SaveOnboardingRequest(OnboardingProgress Progress, long ExpectedRevision);

/// <summary>Stable journey IDs shared by storage validation and the lesson catalog.</summary>
public static class GuideStepIds
{
	public static IReadOnlyList<string> Player { get; } = Array.AsReadOnly(new[]
		{ "place", "sign-in", "character", "world", "useful-action", "speak-act", "channels", "continue" });
	public static IReadOnlyList<string> Administrator { get; } = Array.AsReadOnly(new[]
		{ "identity", "joining", "rules", "capabilities", "walkthrough", "invite" });

	public static bool IsValid(GuideJourney journey, OnboardingProgress? progress, long expectedRevision)
	{
		if (!Enum.IsDefined(journey) || progress is null || progress.SchemaVersion != 1
			|| progress.GuideVersion != 1 || expectedRevision < 0 || expectedRevision == long.MaxValue
			|| progress.Revision != expectedRevision || !Enum.IsDefined(progress.Path)
			|| !Enum.IsDefined(progress.Imagery) || progress.Active && progress.Dismissed
			|| progress.CompletedStepIds is null || progress.SkippedStepIds is null)
			return false;
		var allowed = journey == GuideJourney.Player ? Player : Administrator;
		return progress.CompletedStepIds.Length <= allowed.Count && progress.SkippedStepIds.Length <= allowed.Count
			&& progress.CompletedStepIds.All(id => allowed.Contains(id))
			&& progress.SkippedStepIds.All(id => allowed.Contains(id))
			&& progress.CompletedStepIds.Distinct().Count() == progress.CompletedStepIds.Length
			&& progress.SkippedStepIds.Distinct().Count() == progress.SkippedStepIds.Length
			&& !progress.CompletedStepIds.Intersect(progress.SkippedStepIds).Any()
			&& (progress.CurrentStepId is null || allowed.Contains(progress.CurrentStepId));
	}
}
