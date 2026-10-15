using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAttributeIndexCommandHandler(IAttributeIndexStore store)
	: ICommandHandler<SetAttributeIndexCommand, Result<SharpAttributeEntry>>
{
	public ValueTask<Result<SharpAttributeEntry>> Handle(SetAttributeIndexCommand command, CancellationToken cancellationToken)
		=> store.SetAttributeIndexAsync(command.Name, command.Options, cancellationToken);
}

public class DropAttributeIndexCommandHandler(IAttributeIndexStore store) : ICommandHandler<DropAttributeIndexCommand, bool>
{
	public ValueTask<bool> Handle(DropAttributeIndexCommand command, CancellationToken cancellationToken)
		=> store.DropAttributeIndexAsync(command.Name, cancellationToken);
}

public class BuildAttributeIndexBatchCommandHandler(IAttributeIndexStore store)
	: ICommandHandler<BuildAttributeIndexBatchCommand, Found<AttributeIndexBuildStep>>
{
	public ValueTask<Found<AttributeIndexBuildStep>> Handle(BuildAttributeIndexBatchCommand command,
		CancellationToken cancellationToken)
		=> store.BuildAttributeIndexBatchAsync(command.Name, command.BatchSize, cancellationToken);
}

public class GetIndexedAttributeQueryHandler(IAttributeIndexStore store)
	: IQueryHandler<GetIndexedAttributeQuery, IndexedAttributeResult>
{
	public ValueTask<IndexedAttributeResult> Handle(GetIndexedAttributeQuery query, CancellationToken cancellationToken)
		=> store.QueryAttributeIndexAsync(query.Spec, cancellationToken);
}

public class CountIndexedAttributeQueryHandler(IAttributeIndexStore store)
	: IQueryHandler<CountIndexedAttributeQuery, IndexedAttributeCount>
{
	public ValueTask<IndexedAttributeCount> Handle(CountIndexedAttributeQuery query, CancellationToken cancellationToken)
		=> store.CountAttributeIndexAsync(query.Name, query.Value, cancellationToken);
}

public class CountAttributeCopiesQueryHandler(IAttributeIndexStore store) : IQueryHandler<CountAttributeCopiesQuery, long>
{
	public ValueTask<long> Handle(CountAttributeCopiesQuery query, CancellationToken cancellationToken)
		=> store.CountAttributeCopiesAsync(query.Name, query.Type, cancellationToken);
}
