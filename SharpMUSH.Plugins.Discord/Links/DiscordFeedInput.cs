using System.Globalization;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>
/// Speaks Discord messages into the game. Each line is sent on the game's queue, as the <c>discord</c> kind's owner
/// (the Discord Bridge) with the speaking identity as the line's speaker, as <c>@feed/send</c> run by the bridge for
/// that character would send it.
/// </summary>
/// <remarks>The host's services are resolved when first used: some are built after the plugin's.</remarks>
public sealed class DiscordFeedInput(IServiceProvider services, ILogger<DiscordFeedInput> logger) : IDiscordFeedInput
{
	/// <summary>How long a line waits for the game's queue before the author is told it did not get through.</summary>
	public static readonly TimeSpan QueueTimeout = TimeSpan.FromSeconds(30);

	private IMediator Mediator => services.GetRequiredService<IMediator>();

	public async Task<SpeechOutcome> SpeakAsync(DiscordSpeech speech, CancellationToken cancellationToken)
	{
		if (!DBRef.TryParse(speech.Character, out var parsed) || parsed is not { } dbref
			|| await Mediator.Send(new GetObjectNodeQuery(dbref), cancellationToken) is not AnySharpObject { IsPlayer: true } character)
		{
			return new Refused("The character you speak as there is gone. Choose another with @discord/speak.");
		}

		var feeds = services.GetRequiredService<IFeedService>();
		if (await feeds.GetKindAsync(DiscordChannelLink.FeedKind) is not SharpFeedKind kind
			|| await Mediator.Send(new GetObjectNodeQuery(kind.Owner), cancellationToken) is not AnySharpObject bridge)
		{
			return new Refused("The game has no Discord Bridge. Ask its staff to install the discord package.");
		}

		var spoken = new TaskCompletionSource<SpeechOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
		var admission = await services.GetRequiredService<ITaskScheduler>().AdmitWork(async () =>
		{
			try
			{
				spoken.TrySetResult(await SendAsync(speech, kind, bridge, character));
			}
			catch (Exception exception)
			{
				spoken.TrySetException(exception);
				throw;
			}

			return null;
		}, "discord:speak", "discord", bridge.Object().DBRef, dbref, notifyOnRejection: false);
		if (!admission.Accepted)
		{
			logger.LogWarning("[Discord] A line from #{Channel} was not queued: {Reason}", speech.Link.ChannelName, admission.Reason);
			return new Refused("The game is too busy to take it. Try again shortly.");
		}

		try
		{
			return await spoken.Task.WaitAsync(QueueTimeout, cancellationToken);
		}
		catch (TimeoutException)
		{
			return new Refused("The game did not take it in time.");
		}
	}

	/// <summary>
	/// On the game's queue: that the character is still on the speaker's active account, the checks <c>@feed/send</c>
	/// makes, then the line. The account is checked here rather than before queueing, since a line can wait behind others
	/// while the character moves to another account or the account is disabled.
	/// </summary>
	private async Task<SpeechOutcome> SendAsync(DiscordSpeech speech, SharpFeedKind kind, AnySharpObject bridge,
		AnySharpObject character)
	{
		var name = character.Object().Name;
		if (await services.GetRequiredService<IAccountService>().GetAccountForCharacterAsync(character.Object().DBRef,
			ExecutionBudget.CurrentToken) is not { } account || account.Id != speech.AccountId)
		{
			return new Refused($"{name} is no longer on your game account. Choose another character with @discord/speak.");
		}

		if (!account.IsActive)
		{
			return new Refused("Your game account is not active.");
		}

		if (await character.HasFlag("GAGGED"))
		{
			return new Refused($"{name} is gagged.");
		}

		var feed = await Mediator.Send(new GetFeedQuery(kind.Name, speech.Link.FeedKey), ExecutionBudget.CurrentToken) switch
		{
			SharpFeed found => found,
			_ => SharpFeed.New(kind.Name, speech.Link.FeedKey),
		};
		var feeds = services.GetRequiredService<IFeedService>();
		if (!await feeds.CanRunAsync(character, kind) && !await feeds.PassesAsync(kind, feed, FeedLocks.Send, character))
		{
			return new Refused($"{name} may not send to {feed.Name}.");
		}

		var parser = services.GetRequiredService<IMUSHCodeParser>().FromState(
			ParserState.RootFor(bridge.Object().DBRef) with
			{
				Enactor = character.Object().DBRef,
				ExecutionBudget = ExecutionBudget.Current,
			});
		var send = new FeedSend(kind, feed, character, bridge, speech.Style, speech.Text, [], Origin: speech.Origin);
		return await feeds.SendAsync(parser, send) switch
		{
			FeedDelivery delivery => new Spoken(delivery.Id),
			Error<string> error => new Refused(error.Value),
		};
	}

	public async Task<string?> NameAsync(string character, CancellationToken cancellationToken)
		=> DBRef.TryParse(character, out var parsed) && parsed is { } dbref
			&& await Mediator.Send(new GetObjectNodeQuery(dbref), cancellationToken) is AnySharpObject found
				? found.Object().Name
				: null;

	public async Task<bool> RemoveLineAsync(long lineId, DiscordChannelLink link, ulong messageId,
		CancellationToken cancellationToken)
	{
		if (!await Mediator.Send(new DeleteFeedMessageCommand(lineId), cancellationToken))
		{
			return false;
		}

		await services.GetRequiredService<IAuditLog>().RecordSystemAsync(AuditActions.FeedLineDelete,
			AuditTargets.Of(AuditTargetKinds.Feed, $"{DiscordChannelLink.FeedKind}/{link.FeedKey}"),
			$"Line {lineId.ToString(CultureInfo.InvariantCulture)}: its Discord message {messageId.ToString(CultureInfo.InvariantCulture)} in #{link.ChannelName} was deleted.",
			cancellationToken);
		return true;
	}
}
