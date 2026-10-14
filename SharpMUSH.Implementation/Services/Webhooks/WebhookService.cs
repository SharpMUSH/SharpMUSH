using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Implementation.Services.Webhooks;

/// <inheritdoc cref="IWebhookService"/>
public sealed partial class WebhookService(
	IExpandedObjectDataService serverData,
	IMediator mediator,
	WebhookDispatcher dispatcher,
	TimeProvider time,
	IOptions<WebhookOptions> options,
	ILogger<WebhookService> logger) : IWebhookService
{
	/// <summary>The event <see cref="IWebhookService.TestAsync"/> sends.</summary>
	public const string TestEvent = "WEBHOOK`TEST";

	// One record holds every subscription: two changes at once must not each drop the other's.
	private readonly SemaphoreSlim _writeLock = new(1, 1);
	private readonly Dictionary<string, List<ChannelWriter<WebhookMessage>>> _streams = new(StringComparer.OrdinalIgnoreCase);
	private readonly Lock _streamGate = new();
	private Snapshot? _snapshot;

	/// <summary>
	/// The subscriptions as last saved, and which of them hear each event name asked about so far. Replaced
	/// whole on every save, so a publish never caches an answer read from an older list.
	/// </summary>
	private sealed record Snapshot(ImmutableArray<WebhookSubscription> All)
	{
		public ConcurrentDictionary<string, ImmutableArray<WebhookSubscription>> Hearing { get; } = new(StringComparer.OrdinalIgnoreCase);
	}

	[GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
	private static partial Regex NamePattern();

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<WebhookSubscription>> ListAsync()
		=> (await SnapshotAsync()).All;

	/// <inheritdoc />
	public async ValueTask<Found<WebhookSubscription>> GetAsync(string name)
		=> Find((await SnapshotAsync()).All, name) is { } found ? found : new NotFound();

	/// <summary>The subscription as it stands, or nothing once deleted; what the dispatcher reads before each try.</summary>
	public WebhookSubscription? Current(string name)
		=> _snapshot is { } loaded ? Find(loaded.All, name) : null;

	/// <summary>Turns a subscription off because its endpoint answered 410 Gone.</summary>
	public async ValueTask MarkGoneAsync(string name)
	{
		logger.LogWarning("Webhook {Subscription} answered 410 Gone; it has been turned off", name);
		await ChangeAsync(name, subscription => subscription with
		{
			Enabled = false,
			DisabledReason = "the endpoint answered 410 Gone"
		});
	}

	/// <inheritdoc />
	public async ValueTask<Result<WebhookSubscription>> CreateAsync(string name, string? url, IReadOnlyList<string> events, string? createdBy)
	{
		if (!NamePattern().IsMatch(name))
		{
			return new Error<string>("A webhook's name is letters, digits, - and _, up to 64 of them.");
		}

		if (CheckUrl(url) is { } urlError) return new Error<string>(urlError);
		if (CheckEvents(events) is { } eventsError) return new Error<string>(eventsError);

		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			if (Find(data.Subscriptions, name) is not null)
			{
				return new Error<string>($"There is already a webhook called {name}.");
			}

			var subscription = new WebhookSubscription
			{
				Name = name,
				Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim(),
				Events = Normalize(events),
				Secret = WebhookSignature.NewSecret(),
				Enabled = true,
				CreatedBy = createdBy,
				CreatedAt = time.GetUtcNow(),
			};
			data.Subscriptions.Add(subscription);
			await SaveAsync(data);
			return subscription;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<WebhookSubscription>> SetUrlAsync(string name, string? url)
		=> CheckUrl(url) is { } error
			? new Error<string>(error)
			: Lift(await ChangeAsync(name, subscription => subscription with { Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim() }));

	/// <inheritdoc />
	public async ValueTask<FoundResult<WebhookSubscription>> SetEventsAsync(string name, IReadOnlyList<string> events)
		=> CheckEvents(events) is { } error
			? new Error<string>(error)
			: Lift(await ChangeAsync(name, subscription => subscription with { Events = Normalize(events) }));

	/// <inheritdoc />
	public async ValueTask<FoundResult<WebhookSubscription>> SetEnabledAsync(string name, bool enabled)
		=> Lift(await ChangeAsync(name, subscription => subscription with { Enabled = enabled, DisabledReason = null }));

	/// <inheritdoc />
	public async ValueTask<FoundResult<WebhookSubscription>> RotateSecretAsync(string name)
	{
		var changed = await ChangeAsync(name, subscription => subscription with { Secret = WebhookSignature.NewSecret() });
		// A stream was let in with the old secret; it must connect again with the new one.
		if (changed is WebhookSubscription rotated) CloseStreams(rotated.Name);
		return Lift(changed);
	}

	/// <inheritdoc />
	public async ValueTask<Found<WebhookSubscription>> DeleteAsync(string name)
	{
		WebhookSubscription? removed;
		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			removed = Find(data.Subscriptions, name);
			if (removed is null) return new NotFound();
			data.Subscriptions.Remove(removed);
			await SaveAsync(data);
		}
		finally
		{
			_writeLock.Release();
		}

		dispatcher.Forget(removed.Name);
		CloseStreams(removed.Name);
		return removed;
	}

	private void CloseStreams(string name)
	{
		List<ChannelWriter<WebhookMessage>> closing;
		lock (_streamGate)
		{
			closing = _streams.Remove(name, out var writers) ? writers : [];
		}

		foreach (var writer in closing) writer.TryComplete();
	}

	/// <inheritdoc />
	public async ValueTask<FoundResult<WebhookMessage>> TestAsync(string name, DBRef? enactor)
	{
		if (Find((await SnapshotAsync()).All, name) is not { } subscription) return new NotFound();
		if (string.IsNullOrEmpty(subscription.Url) && StreamCount(subscription.Name) == 0)
		{
			return new Error<string>($"Webhook {subscription.Name} has no address and no open stream to test.");
		}

		var message = await MessageAsync(TestEvent, enactor, []);
		Deliver(subscription, message, test: true);
		return message;
	}

	/// <inheritdoc />
	public async ValueTask PublishAsync(string eventName, DBRef? enactor, IReadOnlyList<string> args)
	{
		var snapshot = await SnapshotAsync();
		if (snapshot.All.IsEmpty) return;

		var hearing = snapshot.Hearing.GetOrAdd(eventName, static (name, subscriptions) =>
			[.. subscriptions.Where(subscription => subscription.Enabled && subscription.Hears(name))], snapshot.All);
		if (hearing.IsEmpty) return;

		var message = await MessageAsync(eventName, enactor, args);
		foreach (var subscription in hearing)
		{
			Deliver(subscription, message, test: false);
		}
	}

	/// <inheritdoc />
	public WebhookStatus Status(string name) => dispatcher.Status(name, StreamCount(name));

	/// <inheritdoc />
	public IDisposable OpenStream(string name, ChannelWriter<WebhookMessage> writer)
	{
		lock (_streamGate)
		{
			if (!_streams.TryGetValue(name, out var writers)) _streams[name] = writers = [];
			writers.Add(writer);
		}

		return new StreamLease(this, name, writer);
	}

	private sealed class StreamLease(WebhookService owner, string name, ChannelWriter<WebhookMessage> writer) : IDisposable
	{
		public void Dispose()
		{
			lock (owner._streamGate)
			{
				if (owner._streams.TryGetValue(name, out var writers) && writers.Remove(writer) && writers.Count == 0)
				{
					owner._streams.Remove(name);
				}
			}
		}
	}

	private int StreamCount(string name)
	{
		lock (_streamGate)
		{
			return _streams.TryGetValue(name, out var writers) ? writers.Count : 0;
		}
	}

	private void Deliver(WebhookSubscription subscription, WebhookMessage message, bool test)
	{
		if (!string.IsNullOrEmpty(subscription.Url))
		{
			dispatcher.Enqueue(subscription.Name, message, test);
		}

		lock (_streamGate)
		{
			if (!_streams.TryGetValue(subscription.Name, out var writers)) return;
			foreach (var writer in writers) writer.TryWrite(message);
		}
	}

	/// <summary>
	/// The body every receiver gets, in the Standard Webhooks shape: the event's name as <c>type</c>, when it
	/// was raised, and in <c>data</c> who caused it and its arguments, by name and by position.
	/// </summary>
	private async ValueTask<WebhookMessage> MessageAsync(string eventName, DBRef? enactor, IReadOnlyList<string> args)
	{
		var now = time.GetUtcNow();
		var named = new JsonObject();
		foreach (var (name, value) in EventArguments.Of(eventName, args))
		{
			if (!int.TryParse(name, out _)) named[name] = value.Message?.ToPlainText();
		}

		var body = new JsonObject
		{
			["type"] = eventName,
			["timestamp"] = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
			["data"] = new JsonObject
			{
				["enactor"] = await EnactorAsync(enactor),
				["arguments"] = named,
				["positional"] = new JsonArray([.. args.Select(arg => (JsonNode?)JsonValue.Create(arg))]),
			},
		};

		return new WebhookMessage("msg_" + Guid.CreateVersion7(now).ToString("N"), eventName,
			body.ToJsonString(JsonOptions), now);
	}

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	private async ValueTask<JsonNode?> EnactorAsync(DBRef? enactor)
	{
		if (enactor is not { Number: >= 0 } dbref
				|| await mediator.Send(new GetObjectNodeQuery(dbref)) is not AnySharpObject found)
		{
			return null;
		}

		var obj = found.Object();
		return new JsonObject
		{
			["dbref"] = $"#{obj.DBRef.Number}",
			["objid"] = obj.DBRef.ToString(),
			["name"] = obj.Name,
		};
	}

	private string? CheckUrl(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return null;
		if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
				|| uri.Scheme is not ("https" or "http"))
		{
			return "A webhook's address is a full https:// URL.";
		}

		var settings = options.Value;
		if (uri.Scheme == "http" && !settings.AllowHttp)
		{
			return "A webhook's address must use https. The server can be set to allow http (Webhooks:AllowHttp).";
		}

		var local = uri.IsLoopback || IPAddress.TryParse(uri.DnsSafeHost, out var address) && !WebhookAddress.IsPublic(address);
		return local && !settings.AllowPrivateNetworks
			? "That address is on a private network. The server can be set to allow it (Webhooks:AllowPrivateNetworks)."
			: null;
	}

	private static string? CheckEvents(IReadOnlyList<string> events)
		=> events.FirstOrDefault(pattern => !WebhookEventPattern.IsValid(pattern)) is { } bad
			? $"'{bad}' is not an event name or pattern."
			: null;

	private static IReadOnlyList<string> Normalize(IReadOnlyList<string> events)
		=> [.. events.Select(pattern => pattern.ToUpperInvariant()).Distinct()];

	private static WebhookSubscription? Find(IEnumerable<WebhookSubscription> subscriptions, string name)
		=> subscriptions.FirstOrDefault(subscription => string.Equals(subscription.Name, name, StringComparison.OrdinalIgnoreCase));

	private static FoundResult<WebhookSubscription> Lift(Found<WebhookSubscription> found) => found switch
	{
		WebhookSubscription subscription => subscription,
		NotFound notFound => notFound,
	};

	private async ValueTask<Found<WebhookSubscription>> ChangeAsync(string name, Func<WebhookSubscription, WebhookSubscription> change)
	{
		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			if (Find(data.Subscriptions, name) is not { } existing) return new NotFound();
			var changed = change(existing);
			data.Subscriptions[data.Subscriptions.IndexOf(existing)] = changed;
			await SaveAsync(data);
			return changed;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async ValueTask<Snapshot> SnapshotAsync()
	{
		if (_snapshot is { } loaded) return loaded;

		var data = await LoadAsync();
		return _snapshot ??= new Snapshot([.. data.Subscriptions]);
	}

	private async ValueTask<WebhooksData> LoadAsync()
		=> await serverData.GetExpandedServerDataAsync<WebhooksData>() ?? new WebhooksData();

	private async ValueTask SaveAsync(WebhooksData data)
	{
		await serverData.SetExpandedServerDataAsync(data);
		_snapshot = new Snapshot([.. data.Subscriptions]);
	}
}
