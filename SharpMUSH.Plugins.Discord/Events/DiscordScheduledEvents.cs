using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Plugins.Discord.Events;

/// <summary>
/// Publishes the game's events, a scene say, as Discord external Scheduled Events, one way: each is known by a key the
/// game chooses, and an edit made on Discord lasts until the game next publishes it. Which events to publish, and when,
/// is softcode's business (<c>@discord/event</c>); this only talks to Discord.
/// </summary>
public sealed class DiscordScheduledEvents(IDiscordEventStore store, TimeProvider time) : IDiscordClientHandler
{
	private DiscordSocketClient? _client;

	/// <summary>One change at a time, so two quick publishes of one key cannot make two Discord events.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	public void Attach(DiscordSocketClient client) => _client = client;

	/// <summary>
	/// Creates the event, or updates the one already published for <paramref name="key"/> while it is still to come. One
	/// that has started is ended and replaced by a new one, since the start given is still to come: a scene paused and
	/// moved to another day. One that ended or was cancelled is replaced too.
	/// </summary>
	public async Task<Result<string>> PublishAsync(string key, DiscordEventSpec spec, CancellationToken cancellationToken)
		=> Guild(spec.GuildId) switch
		{
			SocketGuild guild => await PublishInAsync(guild, key, spec, cancellationToken),
			Error<string> error => error,
		};

	private async Task<Result<string>> PublishInAsync(SocketGuild guild, string key, DiscordEventSpec spec, CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			if (!guild.CurrentUser.GuildPermissions.CreateEvents)
			{
				return new Error<string>($"The bot lacks Create Events in {guild.Name}.");
			}

			var options = new RequestOptions { CancelToken = cancellationToken };
			var existing = store.Get(key) is { } record && record.GuildId == guild.Id
				? await FindAsync(guild, record.EventId, options)
				: null;
			switch (existing)
			{
				case { Status: GuildScheduledEventStatus.Scheduled } current:
					await current.ModifyAsync(properties =>
					{
						properties.Name = spec.Name;
						properties.Description = spec.Description ?? string.Empty;
						properties.Location = spec.Location;
						properties.StartTime = spec.Start;
						properties.EndTime = spec.End;
					}, options);
					await SaveAsync(key, guild.Id, current.Id, spec, cancellationToken);
					return $"Updated the Discord event for {key} in {guild.Name}.";
				case { Status: GuildScheduledEventStatus.Active } current:
					// Discord never puts a started event back on the schedule, so the move is a new event.
					await current.EndAsync(options);
					await CreateAsync(guild, key, spec, options, cancellationToken);
					return $"The Discord event for {key} had started, so it was ended and {key} published again in {guild.Name}.";
				default:
					await CreateAsync(guild, key, spec, options, cancellationToken);
					return $"Published {key} as a Discord event in {guild.Name}.";
			}
		}
		finally
		{
			_changing.Release();
		}
	}

	private async Task CreateAsync(SocketGuild guild, string key, DiscordEventSpec spec, RequestOptions options,
		CancellationToken cancellationToken)
	{
		var created = await guild.CreateEventAsync(spec.Name, spec.Start, GuildScheduledEventType.External,
			GuildScheduledEventPrivacyLevel.Private, spec.Description, spec.End, location: spec.Location, options: options);
		await SaveAsync(key, guild.Id, created.Id, spec, cancellationToken);
	}

	/// <summary>Marks the event as started: Discord shows it as happening now.</summary>
	public Task<Result<string>> StartAsync(string key, CancellationToken cancellationToken)
		=> ChangeAsync(key, cancellationToken, async (current, options) =>
		{
			if (current.Status != GuildScheduledEventStatus.Scheduled)
			{
				return new Error<string>($"The Discord event for {key} is {Describe(current.Status)}, so it cannot start.");
			}

			await current.StartAsync(options);
			return $"The Discord event for {key} has started.";
		}, forget: false);

	/// <summary>Ends an event that started, or cancels one that has not; the game forgets it either way.</summary>
	public Task<Result<string>> EndAsync(string key, CancellationToken cancellationToken)
		=> ChangeAsync(key, cancellationToken, async (current, options) =>
		{
			switch (current.Status)
			{
				case GuildScheduledEventStatus.Active:
					await current.EndAsync(options);
					return $"The Discord event for {key} has ended.";
				case GuildScheduledEventStatus.Scheduled:
					await current.ModifyAsync(properties => properties.Status = GuildScheduledEventStatus.Cancelled, options);
					return $"The Discord event for {key} was cancelled.";
				default:
					return $"The Discord event for {key} was already {Describe(current.Status)}.";
			}
		}, forget: true);

	private async Task<Result<string>> ChangeAsync(string key, CancellationToken cancellationToken,
		Func<IGuildScheduledEvent, RequestOptions, Task<Result<string>>> change, bool forget)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return new Error<string>("The bot is not connected to Discord.");
		}

		await _changing.WaitAsync(cancellationToken);
		try
		{
			if (store.Get(key) is not { } record)
			{
				return new Error<string>($"Nothing is published for {key}.");
			}

			var options = new RequestOptions { CancelToken = cancellationToken };
			if (client.GetGuild(record.GuildId) is not { } guild || await FindAsync(guild, record.EventId, options) is not { } current)
			{
				await store.RemoveAsync(key, cancellationToken);
				return new Error<string>($"The Discord event for {key} is gone; it was deleted on Discord, or the bot left its server.");
			}

			var result = await change(current, options);
			if (forget && result is string)
			{
				await store.RemoveAsync(key, cancellationToken);
			}

			return result;
		}
		finally
		{
			_changing.Release();
		}
	}

	private Result<SocketGuild> Guild(ulong? guildId)
	{
		if (_client is not { ConnectionState: ConnectionState.Connected } client)
		{
			return new Error<string>("The bot is not connected to Discord.");
		}

		if (guildId is { } id)
		{
			return client.GetGuild(id) is { } guild ? guild : new Error<string>($"The bot is not in the server {id}.");
		}

		return client.Guilds.Count switch
		{
			1 => client.Guilds.First(),
			0 => new Error<string>("The bot is in no server yet."),
			_ => new Error<string>("The bot is in more than one server. Give the server's id as server."),
		};
	}

	private static async Task<IGuildScheduledEvent?> FindAsync(SocketGuild guild, ulong eventId, RequestOptions options)
	{
		try
		{
			return await guild.GetEventAsync(eventId, options);
		}
		catch (HttpException exception) when (exception.HttpCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	private ValueTask SaveAsync(string key, ulong guildId, ulong eventId, DiscordEventSpec spec, CancellationToken cancellationToken)
		=> store.SaveAsync(new DiscordEventRecord(key, guildId, eventId, spec.Name, spec.Start, time.GetUtcNow()), cancellationToken);

	private static string Describe(GuildScheduledEventStatus status) => status switch
	{
		GuildScheduledEventStatus.Active => "under way",
		GuildScheduledEventStatus.Completed => "over",
		GuildScheduledEventStatus.Cancelled => "cancelled",
		_ => "still to come",
	};
}
