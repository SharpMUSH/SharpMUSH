using Mediator;
using Microsoft.Extensions.Logging;
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
	ILogger<PageLogService> logger,
	TimeProvider time) : IPageLogService
{
	public PageLogService(IMediator mediator, IChannelMessageIdSource ids, IOptionsWrapper<SharpMUSHOptions> configuration,
		ILogger<PageLogService> logger)
		: this(mediator, ids, configuration, logger, TimeProvider.System)
	{
	}

	/// <summary><c>page_log_retention_days</c>'s value for "never delete".</summary>
	public const int KeepForever = -1;

	public bool Enabled => configuration.CurrentValue.Chat.PageLog;

	public async ValueTask<SharpPage> PageAsync(AnySharpObject sender, string senderName,
		IReadOnlyList<AnySharpObject> recipients, string style, string message, CancellationToken cancellationToken = default)
		=> new(
			await ids.NextAsync(cancellationToken),
			sender.Object().DBRef,
			senderName,
			recipients.Select(recipient => recipient.Object().DBRef).ToArray(),
			recipients.Select(recipient => recipient.Object().Name).ToArray(),
			style,
			message,
			time.GetUtcNow(),
			sender.Object().Name);

	public async ValueTask RecordAsync(SharpPage page, IReadOnlyList<AnySharpObject> participants,
		CancellationToken cancellationToken = default)
	{
		// A page to more people than a conversation holds is not one: the portal could never open it.
		if (!Enabled || page.Participants.Count - 1 > PageConversation.MaxOthers) return;

		// A copy is for a character who can read it, through the portal: a player. An object may page.
		var owners = participants
			.Where(participant => participant.IsPlayer)
			.Select(participant => participant.Object().DBRef)
			.Distinct()
			.ToArray();
		if (owners.Length == 0) return;

		try
		{
			await mediator.Send(new RecordPageCommand(page, owners), cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
		{
			// The page was delivered and pushed already; a failed write loses only the history.
			logger.LogError(ex, "Could not keep page {Id} from {Sender} in the page log", page.Id, page.Sender);
		}
	}

	public async ValueTask<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
	{
		var days = configuration.CurrentValue.Chat.PageLogRetentionDays;
		if (days < 0) return 0;

		return await mediator.Send(new PurgePageLogCommand(time.GetUtcNow().AddDays(-days)), cancellationToken);
	}
}
