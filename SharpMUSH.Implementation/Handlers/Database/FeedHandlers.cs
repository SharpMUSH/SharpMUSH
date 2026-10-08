using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetFeedKindCommandHandler(IFeedStore store) : ICommandHandler<SetFeedKindCommand>
{
	public async ValueTask<Unit> Handle(SetFeedKindCommand command, CancellationToken cancellationToken)
	{
		await store.SetFeedKindAsync(command.Kind, cancellationToken);
		return Unit.Value;
	}
}

public class DeleteFeedKindCommandHandler(IFeedStore store) : ICommandHandler<DeleteFeedKindCommand, bool>
{
	public ValueTask<bool> Handle(DeleteFeedKindCommand command, CancellationToken cancellationToken)
		=> store.DeleteFeedKindAsync(command.Kind, cancellationToken);
}

public class SetFeedCommandHandler(IFeedStore store) : ICommandHandler<SetFeedCommand>
{
	public async ValueTask<Unit> Handle(SetFeedCommand command, CancellationToken cancellationToken)
	{
		await store.SetFeedAsync(command.Feed, cancellationToken);
		return Unit.Value;
	}
}

public class DeleteFeedCommandHandler(IFeedStore store) : ICommandHandler<DeleteFeedCommand, bool>
{
	public ValueTask<bool> Handle(DeleteFeedCommand command, CancellationToken cancellationToken)
		=> store.DeleteFeedAsync(command.Kind, command.Key, cancellationToken);
}

public class SetFeedMemberCommandHandler(IFeedStore store) : ICommandHandler<SetFeedMemberCommand>
{
	public async ValueTask<Unit> Handle(SetFeedMemberCommand command, CancellationToken cancellationToken)
	{
		await store.SetFeedMemberAsync(command.Kind, command.Key, command.Member, cancellationToken);
		return Unit.Value;
	}
}

public class RemoveFeedMemberCommandHandler(IFeedStore store) : ICommandHandler<RemoveFeedMemberCommand, bool>
{
	public ValueTask<bool> Handle(RemoveFeedMemberCommand command, CancellationToken cancellationToken)
		=> store.RemoveFeedMemberAsync(command.Kind, command.Key, command.Member, cancellationToken);
}

public class AppendFeedMessageCommandHandler(IFeedStore store) : ICommandHandler<AppendFeedMessageCommand>
{
	public async ValueTask<Unit> Handle(AppendFeedMessageCommand command, CancellationToken cancellationToken)
	{
		await store.AppendFeedMessageAsync(command.Message, command.Limits, cancellationToken);
		return Unit.Value;
	}
}

public class PurgeFeedCommandHandler(IFeedStore store) : ICommandHandler<PurgeFeedCommand, int>
{
	public ValueTask<int> Handle(PurgeFeedCommand command, CancellationToken cancellationToken)
		=> store.PurgeFeedAsync(command.Kind, command.Key, command.Before, cancellationToken);
}

public class AddFeedTapCommandHandler(IFeedStore store) : ICommandHandler<AddFeedTapCommand>
{
	public async ValueTask<Unit> Handle(AddFeedTapCommand command, CancellationToken cancellationToken)
	{
		await store.AddFeedTapAsync(command.Tap, cancellationToken);
		return Unit.Value;
	}
}

public class RemoveFeedTapCommandHandler(IFeedStore store) : ICommandHandler<RemoveFeedTapCommand, bool>
{
	public ValueTask<bool> Handle(RemoveFeedTapCommand command, CancellationToken cancellationToken)
		=> store.RemoveFeedTapAsync(command.Tap, cancellationToken);
}

public class GetFeedKindsQueryHandler(IFeedStore store) : IQueryHandler<GetFeedKindsQuery, IReadOnlyList<SharpFeedKind>>
{
	public ValueTask<IReadOnlyList<SharpFeedKind>> Handle(GetFeedKindsQuery query, CancellationToken cancellationToken)
		=> store.GetFeedKindsAsync(cancellationToken);
}

public class GetFeedKindQueryHandler(IFeedStore store) : IQueryHandler<GetFeedKindQuery, Found<SharpFeedKind>>
{
	public ValueTask<Found<SharpFeedKind>> Handle(GetFeedKindQuery query, CancellationToken cancellationToken)
		=> store.GetFeedKindAsync(query.Kind, cancellationToken);
}

public class GetFeedsQueryHandler(IFeedStore store) : IQueryHandler<GetFeedsQuery, IReadOnlyList<SharpFeed>>
{
	public ValueTask<IReadOnlyList<SharpFeed>> Handle(GetFeedsQuery query, CancellationToken cancellationToken)
		=> store.GetFeedsAsync(query.Kind, cancellationToken);
}

public class GetFeedQueryHandler(IFeedStore store) : IQueryHandler<GetFeedQuery, Found<SharpFeed>>
{
	public ValueTask<Found<SharpFeed>> Handle(GetFeedQuery query, CancellationToken cancellationToken)
		=> store.GetFeedAsync(query.Kind, query.Key, cancellationToken);
}

public class GetFeedMembersQueryHandler(IFeedStore store)
	: IQueryHandler<GetFeedMembersQuery, IReadOnlyList<SharpFeedMember>>
{
	public ValueTask<IReadOnlyList<SharpFeedMember>> Handle(GetFeedMembersQuery query, CancellationToken cancellationToken)
		=> store.GetFeedMembersAsync(query.Kind, query.Key, cancellationToken);
}

public class GetMemberFeedsQueryHandler(IFeedStore store)
	: IQueryHandler<GetMemberFeedsQuery, IReadOnlyList<(string Kind, string Key)>>
{
	public ValueTask<IReadOnlyList<(string Kind, string Key)>> Handle(GetMemberFeedsQuery query,
		CancellationToken cancellationToken)
		=> store.GetMemberFeedsAsync(query.Member, query.Kind, cancellationToken);
}

public class GetFeedMessagesQueryHandler(IFeedStore store)
	: IQueryHandler<GetFeedMessagesQuery, IReadOnlyList<SharpFeedMessage>>
{
	public ValueTask<IReadOnlyList<SharpFeedMessage>> Handle(GetFeedMessagesQuery query, CancellationToken cancellationToken)
		=> store.GetFeedMessagesAsync(query.Kind, query.Key, query.Count, query.AfterId, cancellationToken);
}

public class GetFeedMessageQueryHandler(IFeedStore store) : IQueryHandler<GetFeedMessageQuery, Found<SharpFeedMessage>>
{
	public ValueTask<Found<SharpFeedMessage>> Handle(GetFeedMessageQuery query, CancellationToken cancellationToken)
		=> store.GetFeedMessageAsync(query.Id, cancellationToken);
}

public class GetFeedTapsQueryHandler(IFeedStore store) : IQueryHandler<GetFeedTapsQuery, IReadOnlyList<SharpFeedTap>>
{
	public ValueTask<IReadOnlyList<SharpFeedTap>> Handle(GetFeedTapsQuery query, CancellationToken cancellationToken)
		=> store.GetFeedTapsAsync(query.Kind, cancellationToken);
}
