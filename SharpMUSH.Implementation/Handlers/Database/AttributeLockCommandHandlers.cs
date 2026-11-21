using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeEntryLockCommandHandler(IAttributeLockStore store)
	: ICommandHandler<SetAttributeEntryLockCommand, Found<SharpAttributeEntry>>
{
	public ValueTask<Found<SharpAttributeEntry>> Handle(SetAttributeEntryLockCommand command, CancellationToken cancellationToken)
		=> store.SetAttributeEntryLockAsync(command.Name, command.Kind, command.Key, cancellationToken);
}
