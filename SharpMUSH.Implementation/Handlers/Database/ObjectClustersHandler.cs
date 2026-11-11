using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Clusters;

namespace SharpMUSH.Implementation.Handlers.Database;

public class ObjectClustersHandler(IExpandedDataStore database)
	: IQueryHandler<GetObjectClustersQuery, ObjectClusterTable>, ICommandHandler<SetObjectClustersCommand>
{
	public async ValueTask<ObjectClusterTable> Handle(GetObjectClustersQuery query, CancellationToken cancellationToken)
		=> await database.GetExpandedServerData<ObjectClusterTable>(ObjectClusters.Key, cancellationToken) ?? ObjectClusterTable.Empty;

	public async ValueTask<Unit> Handle(SetObjectClustersCommand command, CancellationToken cancellationToken)
	{
		await database.SetExpandedServerData(ObjectClusters.Key, command.Table, cancellationToken);
		return Unit.Value;
	}
}
