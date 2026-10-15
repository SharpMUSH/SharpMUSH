using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RenameAttributeEntryCommandHandler(IAttributeStore database, IAttributeIndexStore indexes) : ICommandHandler<RenameAttributeEntryCommand, SharpAttributeEntry?>
{
	public async ValueTask<SharpAttributeEntry?> Handle(RenameAttributeEntryCommand request, CancellationToken cancellationToken)
	{
		// Get the old entry to preserve its settings
		var oldEntry = await database.GetSharpAttributeEntry(request.OldName, cancellationToken);
		if (oldEntry == null)
		{
			return null;
		}

		await database.DeleteAttributeEntryAsync(request.OldName, cancellationToken);

		var renamed = await database.CreateOrUpdateAttributeEntryAsync(
			request.NewName,
			oldEntry.DefaultFlags,
			oldEntry.Limit,
			oldEntry.Enum,
			oldEntry.EnumDelimiter,
			cancellationToken);

		// A watch moves with the entry: copies of the new name are watched, under the same pattern.
		if (renamed is not null && oldEntry.Watch is { } watch
			&& await database.SetAttributeWatchAsync(request.NewName, watch, cancellationToken) is SharpAttributeEntry watched)
		{
			renamed = watched;
		}

		// The index moves with the entry: its old entries went with the old name, so it builds again over the copies
		// of the new one. The caller starts the build.
		if (renamed is not null && oldEntry.Index is { } index)
		{
			return await indexes.SetAttributeIndexAsync(request.NewName, index.Options, cancellationToken) switch
			{
				SharpAttributeEntry indexed => indexed,
				_ => renamed
			};
		}

		return renamed;
	}
}
