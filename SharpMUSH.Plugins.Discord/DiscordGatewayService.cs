using System.Net;
using System.Threading.Channels;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Plugins.Discord;

/// <summary>
/// The bot's one connection to Discord. It reads <c>discord.token</c> when the server starts and again on
/// <see cref="Reconnect"/>, and keeps <see cref="State"/> current for the admin page's checklist.
/// </summary>
public sealed class DiscordGatewayService(
	ISecretStore secrets,
	IEnumerable<IDiscordClientHandler> handlers,
	ILogger<DiscordGatewayService> logger)
	: BackgroundService, IDiscordWebhooks
{
	/// <summary>Discord's close code for a token it does not accept.</summary>
	private const int AuthenticationFailed = 4004;

	/// <summary>Discord's close code for a privileged intent the application has not turned on.</summary>
	private const int DisallowedIntents = 4014;

	/// <summary>
	/// Discord's close codes for a shard, API version or intent it refuses (4010-4013). Discord.Net retries these
	/// forever, and each retry spends one of the day's thousand logins.
	/// </summary>
	private const int FirstRefusedSetting = 4010;

	private const int LastRefusedSetting = 4013;

	/// <summary>Below this many logins left today, each new session is logged as a warning.</summary>
	private const int LowLoginsLeft = 100;

	private readonly Channel<bool> _reconnect = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

	private volatile DiscordGatewayState _state = new(DiscordGatewayStatus.Connecting);
	private DiscordSocketClient? _client;

	/// <summary>How many sessions the current client has started; a resume is not a new one.</summary>
	private int _sessions;

	public DiscordGatewayState State => _state with { Guilds = _client?.Guilds.Count ?? 0 };

	/// <summary>Drops the connection and logs in again with the token stored now.</summary>
	public void Reconnect() => _reconnect.Writer.TryWrite(true);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				await ConnectAsync(stoppingToken);
				await _reconnect.Reader.ReadAsync(stoppingToken);
				await DisconnectAsync();
			}
		}
		finally
		{
			await DisconnectAsync();
		}
	}

	private async Task ConnectAsync(CancellationToken cancellationToken)
	{
		_state = new DiscordGatewayState(DiscordGatewayStatus.Connecting);
		switch (await secrets.GetAsync(DiscordPlugin.Token.Name, cancellationToken))
		{
			case string token:
				await LogInAsync(token);
				break;
			case NotFound:
				_state = new DiscordGatewayState(DiscordGatewayStatus.NoToken);
				break;
			case Error<string> error:
				_state = new DiscordGatewayState(DiscordGatewayStatus.Unreachable, Detail: error.Value);
				break;
		}
	}

	/// <summary>Sets <see cref="State"/> before starting the gateway, so a quick Ready is not overwritten.</summary>
	private async Task LogInAsync(string token)
	{
		var client = new DiscordSocketClient(new DiscordSocketConfig
		{
			GatewayIntents = DiscordSetup.Intents,
			LogGatewayIntentWarnings = false,
			// The library waits out rate limits; anything else comes back to the caller, which owns its retries.
			DefaultRetryMode = RetryMode.RetryRatelimit,
			// Rate-limit resets by Discord's relative header, so a skewed host clock cannot cut a wait short.
			UseSystemClock = false,
			// The Linked role goes to every linked member, so the bot keeps every member of its servers in mind.
			AlwaysDownloadUsers = true,
		});
		client.Log += LogAsync;
		client.Disconnected += exception => OnDisconnectedAsync(client, exception);
		client.Connected += () => OnConnectedAsync(client);
		client.Ready += () => OnReadyAsync(client);
		foreach (var handler in handlers)
		{
			handler.Attach(client);
		}

		_sessions = 0;
		_client = client;

		try
		{
			await client.LoginAsync(TokenType.Bot, token);
			var application = await client.GetApplicationInfoAsync();
			var missing = DiscordSetup.MissingIntents(application.Flags);
			var state = new DiscordGatewayState(DiscordGatewayStatus.Connecting, application.Id, application.Name, missing,
				application.IsBotPublic);
			if (missing.Count > 0)
			{
				// Discord would close the gateway with 4014; there is no point starting it.
				_state = state with { Status = DiscordGatewayStatus.IntentsOff };
				return;
			}

			// A bot has a thousand logins a day; spending the last one resets its token.
			var limit = (await client.GetBotGatewayAsync()).SessionStartLimit;
			if (limit.Remaining == 0)
			{
				_state = state with
				{
					Status = DiscordGatewayStatus.Unreachable,
					Detail = $"the bot has used all of today's {limit.Total} logins; Discord allows more in {TimeSpan.FromMilliseconds(limit.ResetAfter):h\\:mm}.",
				};
				return;
			}

			if (limit.Remaining < LowLoginsLeft)
			{
				logger.LogWarning("[Discord] Only {Remaining} of today's {Total} logins are left.", limit.Remaining, limit.Total);
			}

			_state = state;
			await client.StartAsync();
		}
		catch (Exception exception) when (exception is ArgumentException
			or HttpException { HttpCode: HttpStatusCode.Unauthorized })
		{
			logger.LogWarning("[Discord] The bot token was refused: {Reason}", exception.Message);
			_state = new DiscordGatewayState(DiscordGatewayStatus.BadToken);
		}
		catch (Exception exception) when (exception is HttpException or HttpRequestException or TimeoutException)
		{
			logger.LogWarning(exception, "[Discord] Could not reach Discord.");
			_state = new DiscordGatewayState(DiscordGatewayStatus.Unreachable, Detail: exception.Message);
		}
	}

	/// <summary>Raised once the gateway is up, after a new session's Ready or a resume.</summary>
	private Task OnConnectedAsync(DiscordSocketClient client)
	{
		if (ReferenceEquals(client, _client))
		{
			_state = _state with { Status = DiscordGatewayStatus.Connected };
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Ready comes only with a new session: a resumed one replays what it missed and raises no Ready. A second
	/// Ready on the same client therefore means Discord events may have been lost while it reconnected.
	/// </summary>
	private Task OnReadyAsync(DiscordSocketClient client)
	{
		if (!ReferenceEquals(client, _client))
		{
			return Task.CompletedTask;
		}

		_state = _state with { BotName = client.CurrentUser.Username };
		if (Interlocked.Increment(ref _sessions) > 1)
		{
			logger.LogWarning("[Discord] The session could not be resumed; Discord events while it was down may be lost.");
		}
		else
		{
			logger.LogInformation("[Discord] Connected as {Bot} in {Guilds} server(s).", client.CurrentUser.Username, client.Guilds.Count);
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Discord.Net reconnects by itself after a dropped connection. A refused token or intent never succeeds, so
	/// those stop the client and wait for <see cref="Reconnect"/>.
	/// </summary>
	private Task OnDisconnectedAsync(DiscordSocketClient client, Exception exception)
	{
		if (!ReferenceEquals(client, _client))
		{
			return Task.CompletedTask;
		}

		var closeCode = (exception as WebSocketClosedException ?? exception.InnerException as WebSocketClosedException)?.CloseCode;
		DiscordGatewayStatus? status = closeCode switch
		{
			AuthenticationFailed => DiscordGatewayStatus.BadToken,
			DisallowedIntents => DiscordGatewayStatus.IntentsOff,
			>= FirstRefusedSetting and <= LastRefusedSetting => DiscordGatewayStatus.Unreachable,
			_ => null,
		};

		if (status is not { } refused)
		{
			_state = _state with { Status = DiscordGatewayStatus.Connecting };
			return Task.CompletedTask;
		}

		_state = _state with { Status = refused };
		logger.LogWarning("[Discord] Discord closed the connection: {Status}.", refused);
		// Stopping from inside the client's own event would wait on itself.
		_ = Task.Run(client.StopAsync);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Makes the webhook a linked channel posts through, once the bot can see the channel and holds every permission
	/// a link uses there. A thread is refused: its parent channel's webhook posts into it.
	/// </summary>
	public async Task<WebhookCreation> CreateWebhookAsync(ulong channelId, CancellationToken cancellationToken)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return new Error<string>("The bot is not connected to Discord. @discord shows why.");
		}

		var channel = client.GetChannel(channelId);
		if (channel is SocketThreadChannel)
		{
			return new Error<string>("That is a thread. Link the channel it is in.");
		}

		if (channel is not SocketTextChannel text)
		{
			return new Error<string>(channel is null
				? "The bot cannot see a channel with that id. Check that it is in that server and may view the channel."
				: "That channel does not take messages.");
		}

		var missing = DiscordSetup.MissingInChannel(text.Guild.CurrentUser.GetPermissions(text));
		if (missing.Count > 0)
		{
			return new Error<string>($"The bot lacks {string.Join(", ", missing)} in #{text.Name}.");
		}

		try
		{
			// Discord refuses a webhook named with "discord" in it; the bot's own name never has it.
			var webhook = await text.CreateWebhookAsync(client.CurrentUser.Username,
				options: new RequestOptions { CancelToken = cancellationToken });
			return new CreatedWebhook(webhook.Id, webhook.Token, text.Guild.Id, text.Guild.Name, text.Name);
		}
		catch (HttpException exception)
		{
			logger.LogWarning("[Discord] Could not make a webhook in #{Channel}: {Reason}", text.Name, exception.Message);
			return new Error<string>($"Discord refused to make a webhook in #{text.Name}: {exception.Reason ?? exception.Message}");
		}
	}

	private Task LogAsync(LogMessage message)
	{
		var level = message.Severity switch
		{
			LogSeverity.Critical => LogLevel.Critical,
			LogSeverity.Error => LogLevel.Error,
			LogSeverity.Warning => LogLevel.Warning,
			LogSeverity.Info => LogLevel.Information,
			LogSeverity.Verbose => LogLevel.Debug,
			_ => LogLevel.Trace,
		};
		logger.Log(level, message.Exception, "[Discord] {Source}: {Message}", message.Source, message.Message);
		return Task.CompletedTask;
	}

	private async Task DisconnectAsync()
	{
		if (Interlocked.Exchange(ref _client, null) is not { } client)
		{
			return;
		}

		try
		{
			await client.StopAsync();
			await client.LogoutAsync();
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "[Discord] Error while disconnecting.");
		}
		finally
		{
			await client.DisposeAsync();
		}
	}
}
