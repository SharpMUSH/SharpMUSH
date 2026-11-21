using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeLockCommandHandler(IAttributeLockStore store) : ICommandHandler<SetAttributeLockCommand>
{
	public async ValueTask<Unit> Handle(SetAttributeLockCommand command, CancellationToken cancellationToken)
	{
		await store.SetAttributeLockAsync(command.Target, command.Kind, command.Key, cancellationToken);
		return Unit.Value;
	}
}

public class SetAttributeEntryLockCommandHandler(IAttributeLockStore store)
	: ICommandHandler<SetAttributeEntryLockCommand, Found<SharpAttributeEntry>>
{
	public ValueTask<Found<SharpAttributeEntry>> Handle(SetAttributeEntryLockCommand command, CancellationToken cancellationToken)
		=> store.SetAttributeEntryLockAsync(command.Name, command.Kind, command.Key, cancellationToken);
}
