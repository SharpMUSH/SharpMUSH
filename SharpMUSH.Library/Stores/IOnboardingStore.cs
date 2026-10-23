using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Library;

/// <summary>Account-scoped guide progress with atomic optimistic concurrency.</summary>
public interface IOnboardingStore
{
	ValueTask<OnboardingProgress> GetOnboardingProgressAsync(string accountId, GuideJourney journey,
		CancellationToken cancellationToken = default);
	ValueTask<OnboardingSaveResult> SaveOnboardingProgressAsync(string accountId, GuideJourney journey,
		OnboardingProgress progress, long expectedRevision, CancellationToken cancellationToken = default);
	ValueTask<string> GetOnboardingGameIdAsync(CancellationToken cancellationToken = default);
}
