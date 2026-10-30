using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class RetireObjectCommandHandler(IRecoverListStore database, IUserDefinedFunctionService? functions = null)
	: ICommandHandler<RetireObjectCommand, bool>
{
	public async ValueTask<bool> Handle(RetireObjectCommand request, CancellationToken cancellationToken)
	{
		var retired = await database.RetireObjectAsync(request.Target, request.DestroyedBy, cancellationToken);
		if (retired) functions?.InvalidateLocalDefinitions(request.Target);
		return retired;
	}
}

public class RestoreRecoverEntryCommandHandler(IRecoverListStore database)
	: ICommandHandler<RestoreRecoverEntryCommand, Result<DBRef>>
{
	public async ValueTask<Result<DBRef>> Handle(RestoreRecoverEntryCommand request, CancellationToken cancellationToken)
		=> await database.RestoreRecoverEntryAsync(request.Restore, cancellationToken);
}

public class PurgeRecoverEntryCommandHandler(IRecoverListStore database) : ICommandHandler<PurgeRecoverEntryCommand, bool>
{
	public async ValueTask<bool> Handle(PurgeRecoverEntryCommand request, CancellationToken cancellationToken)
		=> await database.PurgeRecoverEntryAsync(request.Objid, request.EvenIfKept, cancellationToken);
}

public class SetRecoverKeepCommandHandler(IRecoverListStore database) : ICommandHandler<SetRecoverKeepCommand, bool>
{
	public async ValueTask<bool> Handle(SetRecoverKeepCommand request, CancellationToken cancellationToken)
		=> await database.SetRecoverKeepAsync(request.Objid, request.Keep, cancellationToken);
}

public class GetRecoverEntryQueryHandler(IRecoverListStore database)
	: IQueryHandler<GetRecoverEntryQuery, Found<RecoverEntry>>
{
	public async ValueTask<Found<RecoverEntry>> Handle(GetRecoverEntryQuery request, CancellationToken cancellationToken)
		=> await database.GetRecoverEntryAsync(request.Number, cancellationToken);
}

public class GetRecoverEntriesQueryHandler(IRecoverListStore database)
	: IStreamQueryHandler<GetRecoverEntriesQuery, RecoverEntry>
{
	public IAsyncEnumerable<RecoverEntry> Handle(GetRecoverEntriesQuery request, CancellationToken cancellationToken)
		=> database.GetRecoverEntriesAsync(cancellationToken);
}

public class MeasureRecoverListQueryHandler(IRecoverListStore database)
	: IQueryHandler<MeasureRecoverListQuery, RecoverListUsage>
{
	public async ValueTask<RecoverListUsage> Handle(MeasureRecoverListQuery request, CancellationToken cancellationToken)
		=> await database.MeasureRecoverListAsync(cancellationToken);
}

public class CountFreeNumbersQueryHandler(IFreeNumberStore database) : IQueryHandler<CountFreeNumbersQuery, int>
{
	public async ValueTask<int> Handle(CountFreeNumbersQuery request, CancellationToken cancellationToken)
		=> await database.CountFreeNumbersAsync(cancellationToken);
}

public class IsFreeNumberQueryHandler(IFreeNumberStore database) : IQueryHandler<IsFreeNumberQuery, bool>
{
	public async ValueTask<bool> Handle(IsFreeNumberQuery request, CancellationToken cancellationToken)
		=> await database.IsFreeNumberAsync(request.Number, cancellationToken);
}

public class GetFreeNumbersQueryHandler(IFreeNumberStore database) : IStreamQueryHandler<GetFreeNumbersQuery, FreeNumber>
{
	public IAsyncEnumerable<FreeNumber> Handle(GetFreeNumbersQuery request, CancellationToken cancellationToken)
		=> database.GetFreeNumbersAsync(cancellationToken);
}

public class RecordUntrackedHolesCommandHandler(IFreeNumberStore database) : ICommandHandler<RecordUntrackedHolesCommand, int>
{
	public async ValueTask<int> Handle(RecordUntrackedHolesCommand request, CancellationToken cancellationToken)
		=> await database.RecordUntrackedHolesAsync(request.ReusableAt, cancellationToken);
}

public class SetFreeNumberDueCommandHandler(IFreeNumberStore database) : ICommandHandler<SetFreeNumberDueCommand, bool>
{
	public async ValueTask<bool> Handle(SetFreeNumberDueCommand request, CancellationToken cancellationToken)
		=> await database.SetFreeNumberDueAsync(request.Number, request.ReusableAt, cancellationToken);
}

public class SetProtectedNumbersCommandHandler(IFreeNumberStore database) : ICommandHandler<SetProtectedNumbersCommand>
{
	public async ValueTask<Unit> Handle(SetProtectedNumbersCommand request, CancellationToken cancellationToken)
	{
		await database.SetProtectedNumbersAsync(request.Numbers, cancellationToken);
		return Unit.Value;
	}
}

public class FindNumberReferencesQueryHandler(IFreeNumberStore database) : IStreamQueryHandler<FindNumberReferencesQuery, NumberReference>
{
	public IAsyncEnumerable<NumberReference> Handle(FindNumberReferencesQuery request, CancellationToken cancellationToken)
		=> database.FindNumberReferencesAsync(request.Numbers, cancellationToken);
}
