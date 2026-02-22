using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RenameAttributeEntryCommandHandler(ISharpDatabase database) : ICommandHandler<RenameAttributeEntryCommand, SharpAttributeEntry?>
{
	public async ValueTask<SharpAttributeEntry?> Handle(RenameAttributeEntryCommand request, CancellationToken cancellationToken)
	{
		// Get the old entry to preserve its settings
		var oldEntry = await database.GetSharpAttributeEntry(request.OldName, cancellationToken);
		if (oldEntry == null)
		{
			return null;
		}

		// Delete the old entry
		await database.DeleteAttributeEntryAsync(request.OldName, cancellationToken);

		// Create new entry with the same settings but new name
		return await database.CreateOrUpdateAttributeEntryAsync(
			request.NewName,
			oldEntry.DefaultFlags,
			oldEntry.Limit,
			oldEntry.Enum,
			cancellationToken);
	}
}
