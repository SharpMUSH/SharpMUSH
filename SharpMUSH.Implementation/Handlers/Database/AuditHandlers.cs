using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RecordAuditCommandHandler(IAuditStore store) : ICommandHandler<RecordAuditCommand, AuditEntry>
{
	public ValueTask<AuditEntry> Handle(RecordAuditCommand command, CancellationToken cancellationToken)
		=> store.AppendAuditAsync(command.Draft, cancellationToken);
}

public class GetAuditEntriesQueryHandler(IAuditStore store) : IQueryHandler<GetAuditEntriesQuery, AuditPage>
{
	public ValueTask<AuditPage> Handle(GetAuditEntriesQuery query, CancellationToken cancellationToken)
		=> store.GetAuditEntriesAsync(query.Filter, cancellationToken);
}
