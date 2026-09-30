using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Shared;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that counts requests per path and holds every answer until
/// <see cref="Release"/>, so a test can put several calls in flight at once and then count how many
/// reached the network. <paramref name="hold"/> picks which requests wait (all, by default), so
/// setup traffic such as a sign-in can answer straight away.
/// </summary>
public sealed class GatedHttpHandler(
	Func<HttpRequestMessage, HttpResponseMessage> respond,
	Func<HttpRequestMessage, bool>? hold = null) : HttpMessageHandler
{
	private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.Ordinal);

	/// <summary>Requests to <paramref name="pathAndQuery"/> (e.g. <c>/http/online</c>) so far.</summary>
	public int CallsTo(string pathAndQuery) => _calls.GetValueOrDefault(pathAndQuery);

	/// <summary>Requests to any path so far.</summary>
	public int Calls => _calls.Values.Sum();

	/// <summary>Lets every held request, and every later one, answer.</summary>
	public void Release() => _gate.TrySetResult();

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		_calls.AddOrUpdate(request.RequestUri!.PathAndQuery, 1, (_, n) => n + 1);
		if (hold?.Invoke(request) ?? true)
		{
			await _gate.Task.WaitAsync(cancellationToken);
		}

		return respond(request);
	}
}
