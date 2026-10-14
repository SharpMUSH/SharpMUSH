using System.Net;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services.Webhooks;

/// <summary>A clock that moves only when a test moves it, and whose timers never fire on their own.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
	private DateTimeOffset _now = start;
	private long _ticks;

	public override DateTimeOffset GetUtcNow() => _now;

	public override long GetTimestamp() => _ticks;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Idle();

	public void Advance(TimeSpan by)
	{
		_now += by;
		_ticks += by.Ticks;
	}

	private sealed class Idle : ITimer
	{
		public bool Change(TimeSpan dueTime, TimeSpan period) => true;
		public void Dispose() { }
		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}

/// <summary>One request an endpoint received, read before the client disposed it.</summary>
internal sealed record ReceivedRequest(Uri? Uri, IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>An endpoint whose answers a test sets, keeping every request it was sent.</summary>
internal sealed class RecordingEndpoint : HttpMessageHandler, IHttpClientFactory
{
	private readonly Queue<HttpStatusCode> _answers = new();
	public List<ReceivedRequest> Received { get; } = [];

	/// <summary>What the endpoint answers once the queued answers run out.</summary>
	public HttpStatusCode Otherwise { get; set; } = HttpStatusCode.OK;

	public RecordingEndpoint Answer(params HttpStatusCode[] answers)
	{
		foreach (var answer in answers) _answers.Enqueue(answer);
		return this;
	}

	public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
			.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => string.Join(",", pair.Value));
		var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
		lock (Received) Received.Add(new ReceivedRequest(request.RequestUri, headers, body));
		var status = _answers.TryDequeue(out var queued) ? queued : Otherwise;
		return new HttpResponseMessage(status);
	}
}

/// <summary>The server document store, in memory.</summary>
internal sealed class MemoryServerData : IExpandedObjectDataService
{
	private readonly Dictionary<string, object> _server = [];

	public ValueTask<T?> GetExpandedDataAsync<T>(Library.Models.SharpObject obj) where T : class => throw new NotSupportedException();

	public ValueTask SetExpandedDataAsync<T>(T data, Library.Models.SharpObject obj, bool ignoreNull = false) where T : class
		=> throw new NotSupportedException();

	public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
		=> ValueTask.FromResult(_server.TryGetValue(typeof(T).Name, out var data)
			? System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(data))
			: null);

	public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
	{
		_server[typeof(T).Name] = data;
		return ValueTask.CompletedTask;
	}
}
