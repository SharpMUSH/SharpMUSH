using Mediator;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>Uncached: each request observes the account's most recent guide revision.</summary>
public record GetOnboardingProgressQuery(string AccountId, GuideJourney Journey) : IQuery<OnboardingProgress>;
public record GetOnboardingGameIdQuery : IQuery<string>;
