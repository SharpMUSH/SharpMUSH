using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetOnboardingProgressQueryHandler(IOnboardingStore store)
	: IQueryHandler<GetOnboardingProgressQuery, OnboardingProgress>
{
	public ValueTask<OnboardingProgress> Handle(GetOnboardingProgressQuery query, CancellationToken cancellationToken)
		=> store.GetOnboardingProgressAsync(query.AccountId, query.Journey, cancellationToken);
}

public class SaveOnboardingProgressCommandHandler(IOnboardingStore store)
	: ICommandHandler<SaveOnboardingProgressCommand, OnboardingSaveResult>
{
	public ValueTask<OnboardingSaveResult> Handle(SaveOnboardingProgressCommand command, CancellationToken cancellationToken)
		=> store.SaveOnboardingProgressAsync(command.AccountId, command.Journey, command.Progress,
			command.ExpectedRevision, cancellationToken);
}

public class GetOnboardingGameIdQueryHandler(IOnboardingStore store) : IQueryHandler<GetOnboardingGameIdQuery, string>
{
	public ValueTask<string> Handle(GetOnboardingGameIdQuery query, CancellationToken cancellationToken)
		=> store.GetOnboardingGameIdAsync(cancellationToken);
}
