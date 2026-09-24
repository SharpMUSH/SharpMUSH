using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetMailAliasesQueryHandler(IMailAliasStore database)
	: IStreamQueryHandler<GetMailAliasesQuery, SharpMailAlias>
{
	public IAsyncEnumerable<SharpMailAlias> Handle(GetMailAliasesQuery request, CancellationToken cancellationToken)
		=> database.GetMailAliasesAsync(cancellationToken);
}

public class CreateMailAliasCommandHandler(IMailAliasStore database)
	: ICommandHandler<CreateMailAliasCommand, Result<SharpMailAlias>>
{
	public ValueTask<Result<SharpMailAlias>> Handle(CreateMailAliasCommand request, CancellationToken cancellationToken)
		=> database.CreateMailAliasAsync(request.Alias, cancellationToken);
}

public class UpdateMailAliasCommandHandler(IMailAliasStore database)
	: ICommandHandler<UpdateMailAliasCommand, Result<SharpMailAlias>>
{
	public ValueTask<Result<SharpMailAlias>> Handle(UpdateMailAliasCommand request, CancellationToken cancellationToken)
		=> database.UpdateMailAliasAsync(request.Name, request.Alias, cancellationToken);
}

public class DeleteMailAliasCommandHandler(IMailAliasStore database)
	: ICommandHandler<DeleteMailAliasCommand, Found<None>>
{
	public ValueTask<Found<None>> Handle(DeleteMailAliasCommand request, CancellationToken cancellationToken)
		=> database.DeleteMailAliasAsync(request.Name, cancellationToken);
}

public class DeleteAllMailAliasesCommandHandler(IMailAliasStore database) : ICommandHandler<DeleteAllMailAliasesCommand>
{
	public async ValueTask<Unit> Handle(DeleteAllMailAliasesCommand request, CancellationToken cancellationToken)
	{
		await database.DeleteAllMailAliasesAsync(cancellationToken);
		return Unit.Value;
	}
}

public class ReleaseMailAliasesCommandHandler(IMailAliasStore database) : ICommandHandler<ReleaseMailAliasesCommand>
{
	public async ValueTask<Unit> Handle(ReleaseMailAliasesCommand request, CancellationToken cancellationToken)
	{
		await database.ReleaseMailAliasesAsync(request.Player, request.NewOwner, cancellationToken);
		return Unit.Value;
	}
}
