using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CompactValuesCommandHandler(ICompressionStore store) : ICommandHandler<CompactValuesCommand, DictionaryCompaction>
{
	public ValueTask<DictionaryCompaction> Handle(CompactValuesCommand command, CancellationToken cancellationToken)
		=> store.CompactAsync(command.Values, command.At, cancellationToken);
}

public class UseDictionaryCommandHandler(ICompressionStore store) : ICommandHandler<UseDictionaryCommand, Found<long>>
{
	public ValueTask<Found<long>> Handle(UseDictionaryCommand command, CancellationToken cancellationToken)
		=> store.UseDictionaryAsync(command.Values, command.Id, cancellationToken);
}

public class GetDictionariesQueryHandler(ICompressionStore store) : IQueryHandler<GetDictionariesQuery, StoredDictionaries>
{
	public ValueTask<StoredDictionaries> Handle(GetDictionariesQuery query, CancellationToken cancellationToken)
		=> store.GetDictionariesAsync(query.Values, cancellationToken);
}
