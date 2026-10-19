using Mediator;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Library.Commands.Database;

public record SaveOnboardingProgressCommand(string AccountId, GuideJourney Journey,
	OnboardingProgress Progress, long ExpectedRevision) : ICommand<OnboardingSaveResult>;
