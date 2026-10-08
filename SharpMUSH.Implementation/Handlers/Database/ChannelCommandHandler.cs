using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class CreateChannelCommandHandler(IChannelStore database) : ICommandHandler<CreateChannelCommand, ChannelCreationResult>
{
	public async ValueTask<ChannelCreationResult> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
		=> await database.CreateChannelAsync(request.Channel, request.Privs, request.Owner, cancellationToken);
}

/// <summary>
/// A rename changes every member's channel list too, but it is announced by <c>ChannelRename</c> once
/// this has returned, not from here. <c>CacheInvalidationBehavior</c> clears the cached channel list
/// before this handler and again after it; a read that lands between the first pass and the write (any
/// other command listing channels meanwhile) caches the pre-rename list again, and only the second pass
/// removes it. A listener run from inside this handler runs before that pass and can be handed the old
/// name — which is what the rename test in <c>CommFeedPackageTests</c> saw when the announcement was
/// published here.
///
/// <para>A channel's id is its name, so a rename moves the recall buffer to the new id: PennMUSH keeps
/// the buffer on the channel, and its history survives a rename.</para>
/// </summary>
public class UpdateChannelCommandHandler(IChannelStore database, IChannelBufferService buffers)
	: ICommandHandler<UpdateChannelCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelAsync(request.Channel,
			request.Name,
			request.Description,
			request.Privs,
			request.JoinLock,
			request.SpeakLock,
			request.SeeLock,
			request.HideLock,
			request.ModLock,
			request.Mogrifier,
			request.Buffer, cancellationToken);

		if (request.Name is { } name
			&& request.Channel.Id is { } oldId
			&& await database.GetChannelAsync(name.ToPlainText(), cancellationToken) is { Id: { } newId })
		{
			await buffers.MoveBufferAsync(oldId, newId);
		}

		return Unit.Value;
	}
}

/// <summary>
/// The members are read before the channel goes, since afterwards there is nothing to read them from.
/// Announcing from inside the handler is safe here, unlike a rename: a cached channel list that still
/// holds the deleted channel yields nothing for it, because membership is read from the store.
/// </summary>
public class DeleteChannelCommandHandler(IChannelStore database, IChannelBufferService buffers, IPublisher publisher)
	: ICommandHandler<DeleteChannelCommand>
{
	public async ValueTask<Unit> Handle(DeleteChannelCommand request, CancellationToken cancellationToken)
	{
		var members = await request.Channel.Members.Value
			.Select(member => member.Member)
			.ToListAsync(cancellationToken);

		await database.DeleteChannelAsync(request.Channel, cancellationToken);
		// Recall is kept on disk now, so a channel made later under the same name must not inherit it.
		if (request.Channel.Id is { } id) await buffers.ClearBufferAsync(id);

		foreach (var member in members)
		{
			await publisher.Publish(
				new ChannelMembershipChangedNotification(member, request.Channel.Name.ToPlainText(), "delete"),
				cancellationToken);
		}

		return Unit.Value;
	}
}

public class AddUserToChannelCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<AddUserToChannelCommand>
{
	public async ValueTask<Unit> Handle(AddUserToChannelCommand request, CancellationToken cancellationToken)
	{
		await database.AddUserToChannelAsync(request.Channel, request.Object, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "join"),
			cancellationToken);
		return Unit.Value;
	}
}

public class RemoveUserFromChannelCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<RemoveUserFromChannelCommand>
{
	public async ValueTask<Unit> Handle(RemoveUserFromChannelCommand request, CancellationToken cancellationToken)
	{
		var previous = await StatusOn(request.Channel, request.Object, cancellationToken);
		await database.RemoveUserFromChannelAsync(request.Channel, request.Object, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "leave", previous),
			cancellationToken);
		return Unit.Value;
	}

	/// <summary><paramref name="member"/>'s flags on <paramref name="channel"/> as stored now, or null when not on it.</summary>
	internal static async ValueTask<SharpChannelStatus?> StatusOn(SharpChannel channel, AnySharpObject member,
		CancellationToken cancellationToken)
	{
		var number = member.Object().DBRef.Number;
		var membership = await channel.Members.Value
			.FirstOrDefaultAsync(x => x.Member.Object().DBRef.Number == number, cancellationToken);
		return membership?.Status;
	}
}

public class UpdateChannelUserStatusCommandHandler(IChannelStore database, IPublisher publisher) : ICommandHandler<UpdateChannelUserStatusCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelUserStatusCommand request, CancellationToken cancellationToken)
	{
		var previous = await RemoveUserFromChannelCommandHandler.StatusOn(request.Channel, request.Object, cancellationToken);
		await database.UpdateChannelUserStatusAsync(request.Channel, request.Object, request.Status, cancellationToken);
		await publisher.Publish(
			new ChannelMembershipChangedNotification(request.Object, request.Channel.Name.ToPlainText(), "status", previous),
			cancellationToken);
		return Unit.Value;
	}
}

public class UpdateChannelOwnerCommandHandler(IChannelStore database) : ICommandHandler<UpdateChannelOwnerCommand>
{
	public async ValueTask<Unit> Handle(UpdateChannelOwnerCommand request, CancellationToken cancellationToken)
	{
		await database.UpdateChannelOwnerAsync(request.Channel, request.Player, cancellationToken);
		return Unit.Value;
	}
}
