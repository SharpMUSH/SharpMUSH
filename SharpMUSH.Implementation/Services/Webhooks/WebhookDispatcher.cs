using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Implementation.Services.Webhooks;

/// <summary>
/// Sends webhook deliveries and keeps the ones waiting to be tried again. A delivery is tried when it is
/// due; one that fails is due again after <see cref="WebhookOptions.RetryDelay"/>, until
/// <see cref="WebhookOptions.MaxAttempts"/> tries have failed and it is dropped. Waiting deliveries live in
/// memory: a restart drops them.
///
/// <para>Each try reads the subscription afresh, so a changed address or a rotated secret applies to
/// deliveries already waiting, and a deleted or disabled subscription's deliveries are dropped.</para>
/// </summary>
public sealed class WebhookDispatcher : IDisposable
{
	/// <summary>The named <see cref="HttpClient"/> deliveries go through.</summary>
	public const string ClientName = "webhooks";

	private readonly IHttpClientFactory _clients;
	private readonly TimeProvider _time;
	private readonly WebhookOptions _options;
	private readonly ILogger<WebhookDispatcher> _logger;
	private readonly Lock _gate = new();
	private readonly PriorityQueue<Pending, DateTimeOffset> _queue = new();
	private readonly Dictionary<string, Ledger> _ledgers = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _wake = new(0);
	private readonly Meter _meter;
	private readonly Counter<long> _attempts;
	private readonly Histogram<double> _durations;

	/// <summary>
	/// Bucket bounds, in seconds. The SDK's defaults (0, 5, 10, 25, ...) are meant for milliseconds and would put
	/// every try under the 10-second timeout in one or two buckets.
	/// </summary>
	private static readonly InstrumentAdvice<double> DurationAdvice = new()
	{
		HistogramBucketBoundaries = [0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30]
	};

	private sealed record Pending(string Subscription, WebhookMessage Message, int Attempt, bool Test);

	private sealed class Ledger
	{
		public long Delivered;
		public long FailedAttempts;
		public long Dropped;
		public int Pending;
		public DateTimeOffset? LastDelivered;
		public DateTimeOffset? LastFailed;
		public readonly Queue<WebhookAttempt> Recent = new();
	}

	public WebhookDispatcher(IHttpClientFactory clients, TimeProvider time, IOptions<WebhookOptions> options,
		ILogger<WebhookDispatcher> logger)
	{
		_clients = clients;
		_time = time;
		_options = options.Value;
		_logger = logger;
		// The same meter name the host's OpenTelemetry pipeline already exports.
		_meter = new Meter("SharpMUSH", "1.0.0");
		_attempts = _meter.CreateCounter<long>("sharpmush.webhook.attempts",
			description: "Webhook delivery tries, by subscription and outcome");
		_durations = _meter.CreateHistogram<double>("sharpmush.webhook.duration", unit: "s",
			description: "How long each webhook delivery try took", advice: DurationAdvice);
	}

	/// <summary>
	/// Queues <paramref name="message"/> for <paramref name="subscription"/>, due now. False, and the message
	/// dropped, when the subscription already has <see cref="WebhookOptions.MaxPendingPerSubscription"/> waiting.
	/// A <paramref name="test"/> delivery is sent even while the subscription is off.
	/// </summary>
	public bool Enqueue(string subscription, WebhookMessage message, bool test = false)
	{
		lock (_gate)
		{
			var ledger = LedgerOf(subscription);
			if (ledger.Pending >= _options.MaxPendingPerSubscription)
			{
				ledger.Dropped++;
				Record(subscription, ledger, new WebhookAttempt(message.Id, message.EventName, 0, _time.GetUtcNow(), null,
					$"{ledger.Pending} deliveries already waiting", TimeSpan.Zero, WebhookAttemptOutcome.Dropped));
				return false;
			}

			ledger.Pending++;
			_queue.Enqueue(new Pending(subscription, message, 1, test), _time.GetUtcNow());
		}

		_wake.Release();
		return true;
	}

	/// <summary>Forgets <paramref name="subscription"/>'s history; its waiting deliveries are dropped when they come due.</summary>
	public void Forget(string subscription)
	{
		lock (_gate)
		{
			_ledgers.Remove(subscription);
		}
	}

	/// <summary>What <paramref name="subscription"/>'s deliveries have come to, with <paramref name="streams"/> open streams.</summary>
	public WebhookStatus Status(string subscription, int streams)
	{
		lock (_gate)
		{
			return _ledgers.TryGetValue(subscription, out var ledger)
				? new WebhookStatus(ledger.Delivered, ledger.FailedAttempts, ledger.Dropped, ledger.Pending, streams,
					ledger.LastDelivered, ledger.LastFailed, [.. ledger.Recent.Reverse()])
				: WebhookStatus.None with { Streams = streams };
		}
	}

	/// <summary>Waits until a delivery is due or a new one is queued.</summary>
	public async Task WaitAsync(CancellationToken cancellationToken)
	{
		TimeSpan? delay;
		lock (_gate)
		{
			delay = _queue.TryPeek(out _, out var due) ? due - _time.GetUtcNow() : null;
		}

		if (delay <= TimeSpan.Zero) return;

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var wake = _wake.WaitAsync(linked.Token);
		var tasks = delay is { } wait ? [wake, Task.Delay(wait, _time, linked.Token)] : new[] { wake };
		await Task.WhenAny(tasks);
		await linked.CancelAsync();
		cancellationToken.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// Tries every delivery that is due now. <paramref name="current"/> gives a subscription as it stands, or
	/// nothing once it is deleted; <paramref name="gone"/> is told when an endpoint answers 410 Gone, which
	/// asks never to be sent to again.
	/// </summary>
	/// <returns>How many deliveries were tried.</returns>
	public async Task<int> RunDueAsync(
		Func<string, WebhookSubscription?> current,
		Func<string, ValueTask> gone,
		CancellationToken cancellationToken)
	{
		List<Pending> due = [];
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			while (_queue.TryPeek(out _, out var at) && at <= now)
			{
				due.Add(_queue.Dequeue());
			}
		}

		await Parallel.ForEachAsync(due,
			new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrency), CancellationToken = cancellationToken },
			async (pending, token) => await AttemptAsync(pending, current(pending.Subscription), gone, token));

		return due.Count;
	}

	private async ValueTask AttemptAsync(Pending pending, WebhookSubscription? subscription, Func<string, ValueTask> gone,
		CancellationToken cancellationToken)
	{
		if (subscription is null)
		{
			// Deleted: its history went with it.
			Settle(pending, null);
			return;
		}

		if (!subscription.Enabled && !pending.Test || string.IsNullOrEmpty(subscription.Url))
		{
			Settle(pending, Attempt(pending, null, subscription.Enabled ? "no address to send to" : "subscription is off",
				TimeSpan.Zero, WebhookAttemptOutcome.Dropped), tried: false);
			return;
		}

		var started = _time.GetTimestamp();
		int? status = null;
		string? error = null;
		try
		{
			using var timeout = new CancellationTokenSource(_options.Timeout, _time);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			using var request = Request(subscription, pending.Message);
			using var response = await _clients.CreateClient(ClientName)
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
			status = (int)response.StatusCode;
			if (!response.IsSuccessStatusCode) error = $"answered {status} {response.ReasonPhrase}".TrimEnd();
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			error = $"no answer within {_options.Timeout.TotalSeconds:0.#}s";
		}
		catch (HttpRequestException ex)
		{
			error = ex.InnerException?.Message ?? ex.Message;
		}

		var duration = _time.GetElapsedTime(started);
		_durations.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("subscription", subscription.Name));

		if (error is null)
		{
			Settle(pending, Attempt(pending, status, null, duration, WebhookAttemptOutcome.Delivered));
			return;
		}

		if (status == (int)HttpStatusCode.Gone)
		{
			Settle(pending, Attempt(pending, status, "answered 410 Gone: the endpoint asks not to be sent to again", duration,
				WebhookAttemptOutcome.Dropped));
			await gone(subscription.Name);
			return;
		}

		if (pending.Attempt >= _options.MaxAttempts || pending.Test)
		{
			Settle(pending, Attempt(pending, status, error, duration, WebhookAttemptOutcome.Dropped));
			return;
		}

		lock (_gate)
		{
			var ledger = LedgerOf(pending.Subscription);
			ledger.FailedAttempts++;
			ledger.LastFailed = _time.GetUtcNow();
			Record(pending.Subscription, ledger, Attempt(pending, status, error, duration, WebhookAttemptOutcome.Retrying));
			var next = pending with { Attempt = pending.Attempt + 1 };
			_queue.Enqueue(next, _time.GetUtcNow() + _options.RetryDelay(next.Attempt));
		}

		_logger.LogDebug("Webhook {Subscription} delivery {Id} failed ({Error}); try {Next} of {Max} queued",
			pending.Subscription, pending.Message.Id, error, pending.Attempt + 1, _options.MaxAttempts);
	}

	private WebhookAttempt Attempt(Pending pending, int? status, string? error, TimeSpan duration, WebhookAttemptOutcome outcome)
		=> new(pending.Message.Id, pending.Message.EventName, pending.Attempt, _time.GetUtcNow(), status, error, duration, outcome);

	/// <summary>
	/// Ends a delivery: it leaves the waiting count, and its last try, if any, is recorded. A delivery dropped
	/// without being sent (<paramref name="tried"/> false) is not counted as a failed try.
	/// </summary>
	private void Settle(Pending pending, WebhookAttempt? attempt, bool tried = true)
	{
		lock (_gate)
		{
			if (!_ledgers.TryGetValue(pending.Subscription, out var ledger))
			{
				return;
			}

			ledger.Pending = Math.Max(0, ledger.Pending - 1);
			if (attempt is null) return;

			switch (attempt.Outcome)
			{
				case WebhookAttemptOutcome.Delivered:
					ledger.Delivered++;
					ledger.LastDelivered = attempt.At;
					break;
				default:
					if (tried) ledger.FailedAttempts++;
					ledger.Dropped++;
					ledger.LastFailed = attempt.At;
					_logger.LogWarning("Webhook {Subscription} dropped delivery {Id} ({Event}) after {Attempts} tries: {Error}",
						pending.Subscription, pending.Message.Id, pending.Message.EventName, pending.Attempt, attempt.Error);
					break;
			}

			Record(pending.Subscription, ledger, attempt);
		}
	}

	private void Record(string subscription, Ledger ledger, WebhookAttempt attempt)
	{
		_attempts.Add(1, new KeyValuePair<string, object?>("subscription", subscription),
			new KeyValuePair<string, object?>("outcome", attempt.Outcome.ToString().ToLowerInvariant()));
		ledger.Recent.Enqueue(attempt);
		while (ledger.Recent.Count > Math.Max(1, _options.RecentAttempts)) ledger.Recent.Dequeue();
	}

	private Ledger LedgerOf(string subscription)
	{
		if (!_ledgers.TryGetValue(subscription, out var ledger))
		{
			_ledgers[subscription] = ledger = new Ledger();
		}

		return ledger;
	}

	private HttpRequestMessage Request(WebhookSubscription subscription, WebhookMessage message)
	{
		var timestamp = _time.GetUtcNow().ToUnixTimeSeconds();
		var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
		{
			Content = new StringContent(message.Body, Encoding.UTF8, "application/json"),
		};
		request.Headers.Add("User-Agent", "SharpMUSH-Webhooks");
		request.Headers.Add(WebhookSignature.IdHeader, message.Id);
		request.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
		request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Sign(subscription.Secret, message.Id, timestamp, message.Body));
		return request;
	}

	public void Dispose() => _meter.Dispose();
}
