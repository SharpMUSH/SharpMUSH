using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests;

/// <summary>Service overrides every Server test host applies, whichever factory builds it.</summary>
public static class TestHostServices
{
	/// <summary>
	/// Recurring-job tests drive the job document with a clock of their own, and test hosts can share one
	/// world. A background runner fires those jobs on the real clock and rewrites the document, so no test
	/// host runs one. TUnit wraps every hosted service, so a started runner cannot be picked out and
	/// stopped afterwards; it has to be removed before the host is built.
	/// </summary>
	public static void RemoveRecurringJobRunner(IServiceCollection services)
	{
		foreach (var runner in services.Where(descriptor => descriptor.ImplementationType == typeof(RecurringJobRunner)).ToArray())
			services.Remove(runner);
	}

	/// <summary>
	/// <c>@shutdown/reboot</c> and the portal's restart stop the host, and test hosts are shared. A test host
	/// records the request instead.
	/// </summary>
	public static void RecordServerRestarts(IServiceCollection services)
	{
		services.RemoveAll<IServerRestart>();
		services.AddSingleton<IServerRestart, RecordingServerRestart>();
	}

	/// <summary>Every test host hears feed lines through a <see cref="RecordingFeedLineListener"/>.</summary>
	public static void RecordFeedLines(IServiceCollection services)
	{
		services.AddSingleton<RecordingFeedLineListener>();
		services.AddSingleton<IFeedLineListener>(provider => provider.GetRequiredService<RecordingFeedLineListener>());
	}

	/// <summary>
	/// A test host has no connection server to count bytes, so <c>SESSION</c> reads the counts tests give a
	/// <see cref="FixedConnectionTraffic"/>.
	/// </summary>
	public static void FixConnectionTraffic(IServiceCollection services)
	{
		services.RemoveAll<IConnectionTrafficService>();
		services.AddSingleton<FixedConnectionTraffic>();
		services.AddSingleton<IConnectionTrafficService>(provider => provider.GetRequiredService<FixedConnectionTraffic>());
	}
}

/// <summary>
/// An <see cref="IConnectionTrafficService"/> answering with the counts tests set, one handle each, since the host
/// is shared.
/// </summary>
public sealed class FixedConnectionTraffic : IConnectionTrafficService
{
	private readonly ConcurrentDictionary<long, ConnectionTraffic> _connections = new();

	/// <summary>Reports <paramref name="traffic"/> for its handle from now on.</summary>
	public void Set(ConnectionTraffic traffic) => _connections[traffic.Handle] = traffic;

	/// <inheritdoc />
	public ValueTask<IReadOnlyDictionary<long, ConnectionTraffic>> GetAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult<IReadOnlyDictionary<long, ConnectionTraffic>>(new Dictionary<long, ConnectionTraffic>(_connections));

	/// <inheritdoc />
	public void Complete(Guid requestId, IReadOnlyList<ConnectionTraffic> connections)
	{
	}
}

/// <summary>
/// An <see cref="IFeedLineListener"/> that keeps every line it hears, as a plugin's would. Tests find their own
/// line by a unique text, since the host is shared.
/// </summary>
public sealed class RecordingFeedLineListener : IFeedLineListener
{
	private readonly ConcurrentQueue<FeedLine> _lines = new();

	/// <summary>Every line heard, oldest first.</summary>
	public IReadOnlyList<FeedLine> Lines => _lines.ToArray();

	/// <inheritdoc />
	public ValueTask LineSentAsync(FeedLine line, CancellationToken cancellationToken)
	{
		_lines.Enqueue(line);
		return ValueTask.CompletedTask;
	}
}

/// <summary>An <see cref="IServerRestart"/> that counts requests and stops nothing.</summary>
public sealed class RecordingServerRestart : IServerRestart
{
	private int _requests;

	/// <summary>How many restarts were asked for.</summary>
	public int Requests => Volatile.Read(ref _requests);

	/// <inheritdoc />
	public bool Pending => false;

	/// <inheritdoc />
	public ValueTask<bool> RestartAsync(string requestedBy)
	{
		Interlocked.Increment(ref _requests);
		return ValueTask.FromResult(true);
	}
}
