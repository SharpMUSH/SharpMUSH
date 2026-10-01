using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IPageLogService"/> over <see cref="RecordPageCommand"/> and <see cref="PurgePageLogCommand"/>.
/// Both options are read on every call, so <c>@config/set</c> takes effect on the next page.
/// </summary>
public sealed class PageLogService(
	IMediator mediator,
	IChannelMessageIdSource ids,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	TimeProvider time) : IPageLogService
{
	public PageLogService(IMediator mediator, IChannelMessageIdSource ids, IOptionsWrapper<SharpMUSHOptions> configuration)
		: this(mediator, ids, configuration, TimeProvider.System)
	{
	}

	/// <summary><c>page_log_retention_days</c>'s value for "never delete".</summary>
	public const int KeepForever = -1;

	public bool Enabled => configuration.CurrentValue.Chat.PageLog;

	public async ValueTask<SharpPage> DeliveredAsync(AnySharpObject sender, string senderName,
		IReadOnlyList<AnySharpObject> recipients, string style, string message, CancellationToken cancellationToken = default)
	{
		var page = new SharpPage(
			await ids.NextAsync(cancellationToken),
			sender.Object().DBRef,
			senderName,
			recipients.Select(recipient => recipient.Object().DBRef).ToArray(),
			recipients.Select(recipient => recipient.Object().Name).ToArray(),
			style,
			message,
			time.GetUtcNow());

		// A page to more people than a conversation holds is not one: the portal could never open it.
		if (!Enabled || page.Participants.Count - 1 > PageConversation.MaxOthers) return page;

		// A copy is for a character who can read it, through the portal: a player. An object may page.
		var owners = recipients.Prepend(sender)
			.Where(participant => participant.IsPlayer)
			.Select(participant => participant.Object().DBRef)
			.Distinct()
			.ToArray();
		if (owners.Length > 0)
		{
			await mediator.Send(new RecordPageCommand(page, owners), cancellationToken);
		}

		return page;
	}

	public async ValueTask<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
	{
		var days = configuration.CurrentValue.Chat.PageLogRetentionDays;
		if (days < 0) return 0;

		return await mediator.Send(new PurgePageLogCommand(time.GetUtcNow().AddDays(-days)), cancellationToken);
	}
}
