using System.Collections.Concurrent;
using System.Globalization;
using Discord.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Outbox;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>A linked channel and how its posting is going.</summary>
/// <param name="Depth">How many lines wait to be posted.</param>
public sealed record DiscordLinkStatus(DiscordChannelLink Link, DiscordLinkState State, int Depth);

/// <summary>
/// Carries the lines of every <c>discord/&lt;channel id&gt;</c> feed to its linked channel, each link through its own
/// sender so a slow or broken channel holds up no other. Links are made and dropped here, and kept in
/// <see cref="IDiscordLinkStore"/>.
/// </summary>
public sealed class DiscordRelay(
	IDiscordLinkStore store,
	ISecretStore secrets,
	IDiscordWebhooks webhooks,
	Func<DiscordChannelLink, IDiscordLinkPoster> posters,
	DiscordOutboxOptions options,
	TimeProvider time,
	ILogger<DiscordRelay> logger) : IHostedService, IFeedLineListener
{
	/// <summary>Who the secret store records as having stored a webhook token the relay made by itself.</summary>
	public static readonly SecretAuthor Author = new("discord", "Discord link");

	/// <summary>
	/// A link whose webhook was deleted gets a new one, but not again within this long: a channel whose webhooks keep
	/// disappearing needs an admin.
	/// </summary>
	public static readonly TimeSpan RecreateInterval = TimeSpan.FromMinutes(10);

	private readonly ConcurrentDictionary<ulong, LinkRunner> _links = new();

	/// <summary>
	/// One link, unlink or resume at a time, so two admins linking the same channel cannot both make it a webhook.
	/// </summary>
	private readonly SemaphoreSlim _changing = new(1, 1);
	private readonly CancellationTokenSource _stopping = new();

	public IReadOnlyList<DiscordLinkStatus> Links()
		=> store.List()
			.OrderBy(link => link.GuildName, StringComparer.OrdinalIgnoreCase)
			.ThenBy(link => link.ChannelName, StringComparer.OrdinalIgnoreCase)
			.Select(link => _links.TryGetValue(link.ChannelId, out var runner)
				? new DiscordLinkStatus(runner.Link, runner.Sender.State, runner.Sender.Depth)
				: new DiscordLinkStatus(link, link.Fault is null ? DiscordLinkState.Ok : DiscordLinkState.Faulted, 0))
			.ToList();

	/// <summary>
	/// Links a Discord channel to the feed <c>discord/&lt;channel id&gt;</c>: makes the channel's webhook, stores its
	/// token as a secret, and starts posting the feed's lines there.
	/// </summary>
	/// <param name="linkedBy">Who asked, by objid.</param>
	public async Task<Result<DiscordChannelLink>> LinkAsync(ulong channelId, string linkedBy, SecretAuthor author,
		CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			return await LinkNewAsync(channelId, linkedBy, author, cancellationToken);
		}
		finally
		{
			_changing.Release();
		}
	}

	private async Task<Result<DiscordChannelLink>> LinkNewAsync(ulong channelId, string linkedBy, SecretAuthor author,
		CancellationToken cancellationToken)
	{
		if (store.Get(channelId) is { } existing)
		{
			return new Error<string>($"#{existing.ChannelName} is already linked.");
		}

		return await webhooks.CreateWebhookAsync(channelId, cancellationToken) switch
		{
			CreatedWebhook created => await LinkCreatedAsync(channelId, created, linkedBy, author),
			Error<string> error => error,
		};
	}

	/// <summary>Stops posting to the channel, deletes its webhook as far as Discord lets it, and forgets the link.</summary>
	/// <returns>The link that was dropped, or <see cref="NotFound"/>.</returns>
	public async Task<Found<DiscordChannelLink>> UnlinkAsync(ulong channelId, CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			return await DropAsync(channelId, cancellationToken);
		}
		finally
		{
			_changing.Release();
		}
	}

	private async Task<Found<DiscordChannelLink>> DropAsync(ulong channelId, CancellationToken cancellationToken)
	{
		if (store.Get(channelId) is not { } link)
		{
			return new NotFound();
		}

		if (_links.TryRemove(channelId, out var runner))
		{
			await runner.StopAsync();
			await DeleteWebhookAsync(runner, cancellationToken);
			runner.Poster.Dispose();
		}

		await secrets.ClearAsync(link.TokenSecret, cancellationToken);
		await store.RemoveAsync(channelId, cancellationToken);
		return link;
	}

	/// <summary>An admin fixed what stopped the link; it posts again, starting with what is still young enough.</summary>
	/// <returns>The link, or <see cref="NotFound"/>.</returns>
	public async Task<Found<DiscordChannelLink>> ResumeAsync(ulong channelId, CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			return await RestartAsync(channelId, cancellationToken);
		}
		finally
		{
			_changing.Release();
		}
	}

	private async Task<Found<DiscordChannelLink>> RestartAsync(ulong channelId, CancellationToken cancellationToken)
	{
		if (store.Get(channelId) is not { } link)
		{
			return new NotFound();
		}

		var resumed = link with { Fault = null };
		await store.SaveAsync(resumed, cancellationToken);
		if (_links.TryGetValue(channelId, out var runner))
		{
			runner.Link = resumed;
			runner.Recreated = null;
			runner.Sender.Resume();
		}
		else
		{
			Start(resumed);
		}

		return resumed;
	}

	/// <summary>
	/// Hands a line on a linked channel's feed to that channel's sender, unless it was meant for only some members:
	/// named with <c>@feed/send/to</c>, or marked by its system (a channel's <c>seeall</c> line).
	/// </summary>
	public ValueTask LineSentAsync(FeedLine line, CancellationToken cancellationToken)
	{
		var message = line.Message;
		if (string.Equals(message.Kind, DiscordChannelLink.FeedKind, StringComparison.OrdinalIgnoreCase)
			&& line.To.Count == 0
			&& message.Audience.Length == 0
			&& ulong.TryParse(message.Key, NumberStyles.None, CultureInfo.InvariantCulture, out var channelId)
			&& _links.TryGetValue(channelId, out var runner))
		{
			runner.Sender.Send(DiscordLineText.From(message));
		}

		return ValueTask.CompletedTask;
	}

	/// <summary>Starts every stored link; a link that was faulted stays stopped until an admin resumes it.</summary>
	public Task StartAsync(CancellationToken cancellationToken)
	{
		foreach (var link in store.List())
		{
			Start(link);
		}

		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _stopping.CancelAsync();
		foreach (var channelId in _links.Keys)
		{
			if (_links.TryRemove(channelId, out var runner))
			{
				await runner.StopAsync();
				runner.Poster.Dispose();
			}
		}
	}

	private async Task<Result<DiscordChannelLink>> LinkCreatedAsync(ulong channelId, CreatedWebhook created,
		string linkedBy, SecretAuthor author)
	{
		// The webhook exists now; a timeout past this point must not leave it with no link to delete it by.
		if (await secrets.SetAsync(DiscordChannelLink.TokenSecretFor(channelId), created.Token, author, CancellationToken.None)
			is Error<string> refused)
		{
			return refused;
		}

		var link = new DiscordChannelLink(channelId, created.GuildId, created.WebhookId, created.ChannelName,
			created.GuildName, time.GetUtcNow(), linkedBy);
		await store.SaveAsync(link, CancellationToken.None);
		Start(link);
		logger.LogInformation("[Discord] Linked #{Channel} in {Guild}.", link.ChannelName, link.GuildName);
		return link;
	}

	private void Start(DiscordChannelLink link)
	{
		var poster = posters(link);
		var sender = new DiscordLinkSender(poster, new DiscordOutbox(options, Random.Shared), options, time, logger,
			(broken, cancellationToken) => FaultedAsync(link.ChannelId, broken, cancellationToken));
		if (link.Fault is { } fault)
		{
			sender.Halt(new LinkBroken(fault.Code, fault.Reason));
		}

		var stop = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
		var runner = new LinkRunner(link, poster, sender, stop, RunAsync(link, sender, stop.Token));
		if (!_links.TryAdd(link.ChannelId, runner))
		{
			// Linked twice at once: the first stays.
			_ = runner.StopAsync();
			poster.Dispose();
		}
	}

	private async Task RunAsync(DiscordChannelLink link, DiscordLinkSender sender, CancellationToken cancellationToken)
	{
		// Off the caller's thread: the sender loops until the link is dropped or the server stops.
		await Task.Yield();
		try
		{
			await sender.RunAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			logger.LogDebug("[Discord] Stopped posting to #{Channel}.", link.ChannelName);
		}
		catch (Exception exception)
		{
			// The sender sorts every failure of a post; this is a bug in the plugin, and must not end silently.
			logger.LogError(exception, "[Discord] Posting to #{Channel} stopped.", link.ChannelName);
		}
	}

	/// <summary>
	/// A link stopped. A deleted webhook is replaced once, if the bot may make one, and the link goes on; anything
	/// else is kept as the link's fault, for the admin to see and resume.
	/// </summary>
	private async Task FaultedAsync(ulong channelId, LinkBroken broken, CancellationToken cancellationToken)
	{
		if (!_links.TryGetValue(channelId, out var runner))
		{
			return;
		}

		var now = time.GetUtcNow();
		if (DiscordFailures.WebhookGone(broken) && (runner.Recreated is not { } last || now - last >= RecreateInterval))
		{
			runner.Recreated = now;
			var replaced = await ReplaceWebhookAsync(runner, cancellationToken);
			if (replaced is null)
			{
				runner.Sender.Resume();
				return;
			}

			broken = broken with { Reason = $"{broken.Reason} A new webhook could not be made: {replaced}" };
		}

		logger.LogWarning("[Discord] #{Channel} stopped posting ({Code}): {Reason}", runner.Link.ChannelName, broken.Code,
			broken.Reason);
		runner.Link = runner.Link with { Fault = new DiscordLinkFault(broken.Code, broken.Reason, now) };
		await store.SaveAsync(runner.Link, cancellationToken);
	}

	/// <returns>Null when the link has a new webhook, else why it has none.</returns>
	private async Task<string?> ReplaceWebhookAsync(LinkRunner runner, CancellationToken cancellationToken)
		=> await webhooks.CreateWebhookAsync(runner.Link.ChannelId, cancellationToken) switch
		{
			CreatedWebhook created => await UseWebhookAsync(runner, created, cancellationToken),
			Error<string> error => error.Value,
		};

	private async Task<string?> UseWebhookAsync(LinkRunner runner, CreatedWebhook created, CancellationToken cancellationToken)
	{
		// As when linking: the new webhook exists, so its token and id are kept whatever happens to the caller.
		if (await secrets.SetAsync(runner.Link.TokenSecret, created.Token, Author, CancellationToken.None) is Error<string> refused)
		{
			return refused.Value;
		}

		runner.Link = runner.Link with { WebhookId = created.WebhookId };
		await store.SaveAsync(runner.Link, CancellationToken.None);
		runner.Poster.WebhookId = created.WebhookId;
		logger.LogInformation("[Discord] #{Channel}'s webhook was deleted; the bot made a new one.", runner.Link.ChannelName);
		return null;
	}

	/// <summary>Best effort: a webhook already gone, or a token never stored, leaves nothing to delete.</summary>
	private async Task DeleteWebhookAsync(LinkRunner runner, CancellationToken cancellationToken)
	{
		try
		{
			await runner.Poster.DeleteWebhookAsync(cancellationToken);
		}
		catch (Exception exception) when (exception is HttpException or DiscordLinkException or HttpRequestException
			or TimeoutException)
		{
			logger.LogInformation("[Discord] #{Channel}'s webhook was not deleted: {Reason}", runner.Link.ChannelName,
				exception.Message);
		}
	}

	private sealed class LinkRunner(
		DiscordChannelLink link,
		IDiscordLinkPoster poster,
		DiscordLinkSender sender,
		CancellationTokenSource stop,
		Task running)
	{
		public DiscordChannelLink Link { get; set; } = link;
		public IDiscordLinkPoster Poster { get; } = poster;
		public DiscordLinkSender Sender { get; } = sender;

		/// <summary>When the relay last made this link a new webhook.</summary>
		public DateTimeOffset? Recreated { get; set; }

		public async Task StopAsync()
		{
			await stop.CancelAsync();
			await running;
			stop.Dispose();
		}
	}
}
