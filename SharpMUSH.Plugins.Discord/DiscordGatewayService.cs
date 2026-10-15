using System.Net;
using System.Threading.Channels;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.Discord;

/// <summary>
/// The bot's one connection to Discord. It reads <c>discord.token</c> when the server starts and again on
/// <see cref="Reconnect"/>, and keeps <see cref="State"/> current for the admin page's checklist.
/// </summary>
public sealed class DiscordGatewayService(ISecretStore secrets, ILogger<DiscordGatewayService> logger) : BackgroundService
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

	private readonly Channel<bool> _reconnect = Channel.CreateBounded<bool>(
		new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

	private volatile DiscordGatewayState _state = new(DiscordGatewayStatus.Connecting);
	private DiscordSocketClient? _client;

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
		});
		client.Log += LogAsync;
		client.Disconnected += exception => OnDisconnectedAsync(client, exception);
		client.Ready += () => OnReadyAsync(client);
		_client = client;

		try
		{
			await client.LoginAsync(TokenType.Bot, token);
			var application = await client.GetApplicationInfoAsync();
			var missing = DiscordSetup.MissingIntents(application.Flags);
			// With an intent off, Discord would close the gateway with 4014; there is no point starting it.
			_state = new DiscordGatewayState(missing.Count > 0 ? DiscordGatewayStatus.IntentsOff : DiscordGatewayStatus.Connecting,
				application.Id, application.Name, missing);
			if (missing.Count == 0)
			{
				await client.StartAsync();
			}
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

	private Task OnReadyAsync(DiscordSocketClient client)
	{
		if (ReferenceEquals(client, _client))
		{
			_state = _state with { Status = DiscordGatewayStatus.Connected, BotName = client.CurrentUser.Username };
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
