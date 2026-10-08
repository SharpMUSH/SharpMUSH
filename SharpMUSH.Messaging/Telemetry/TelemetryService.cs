// NOTE: relocated from SharpMUSH.Library so the ConnectionServer does not depend on the full
// Library. The original SharpMUSH.Library.* namespace is preserved so consumers are unchanged.
using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics.Metrics;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Implementation of telemetry service using OpenTelemetry metrics.
/// </summary>
public class TelemetryService : ITelemetryService, IDisposable
{
	private readonly Meter _meter;
	private readonly ITelemetryInvocationObserver[] _observers;
	private bool _disposed;
	private readonly Histogram<double> _functionInvocationDuration;
	private readonly Histogram<double> _commandInvocationDuration;
	private readonly Histogram<double> _notificationSpeed;
	private readonly Counter<long> _connectionEvents;
	private readonly ObservableGauge<int> _activeConnectionCount;
	private readonly ObservableGauge<int> _loggedInPlayerCount;
	private readonly ObservableGauge<int> _serverHealthState;
	private readonly ObservableGauge<int> _connectionServerHealthState;

	/// <summary>
	/// Bucket bounds, in milliseconds, of the function and command invocation histograms. Most built-ins
	/// finish in microseconds, so the SDK's default bounds (0, 5, 10, 25 ms, ...) would put nearly every
	/// call in the first bucket; these run in 1-2.5-5 steps from 10 microseconds to 5 seconds.
	/// </summary>
	public static readonly IReadOnlyList<double> DurationBucketsMs =
		[0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 25, 50, 100, 250, 500, 1000, 5000];

	/// <summary>The <c>function.name</c> recorded for a call to a function that does not exist.</summary>
	public const string UnknownFunction = "(unknown)";

	private static readonly InstrumentAdvice<double> DurationAdvice = new() { HistogramBucketBoundaries = DurationBucketsMs };

	private int _currentActiveConnectionCount;
	private int _currentLoggedInPlayerCount;
	private bool _currentServerHealthState = true;
	private bool _currentConnectionServerHealthState = true;

	public TelemetryService() : this([]) { }

	public TelemetryService(IEnumerable<ITelemetryInvocationObserver> observers)
	{
		_observers = observers.ToArray();
		_meter = new Meter("SharpMUSH", "1.0.0");

		_functionInvocationDuration = _meter.CreateHistogram<double>(
			"sharpmush.function.invocation.duration",
			unit: "ms",
			description: "Time taken to invoke a function",
			advice: DurationAdvice);

		_commandInvocationDuration = _meter.CreateHistogram<double>(
			"sharpmush.command.invocation.duration",
			unit: "ms",
			description: "Time taken to invoke a command",
			advice: DurationAdvice);

		_notificationSpeed = _meter.CreateHistogram<double>(
			"sharpmush.notification.speed",
			unit: "ms",
			description: "Time taken to send a notification");

		_connectionEvents = _meter.CreateCounter<long>(
			"sharpmush.connection.events",
			description: "Count of connection events");

		_activeConnectionCount = _meter.CreateObservableGauge<int>(
			"sharpmush.connections.active",
			() => _currentActiveConnectionCount,
			description: "Number of active connections");

		_loggedInPlayerCount = _meter.CreateObservableGauge<int>(
			"sharpmush.players.logged_in",
			() => _currentLoggedInPlayerCount,
			description: "Number of logged-in players");

		_serverHealthState = _meter.CreateObservableGauge<int>(
			"sharpmush.server.health",
			() => _currentServerHealthState ? 1 : 0,
			description: "Health state of the Server (1 = healthy, 0 = unhealthy)");

		_connectionServerHealthState = _meter.CreateObservableGauge<int>(
			"sharpmush.connectionserver.health",
			() => _currentConnectionServerHealthState ? 1 : 0,
			description: "Health state of the ConnectionServer (1 = healthy, 0 = unhealthy)");
	}

	public void RecordFunctionInvocation(string functionName, double durationMs, bool success)
	{
		_functionInvocationDuration.Record(durationMs,
			new KeyValuePair<string, object?>("function.name", functionName),
			new KeyValuePair<string, object?>("success", success));
		Observe(new(TelemetryInvocationKind.Function, functionName, durationMs, success));
	}

	public void RecordCommandInvocation(string commandName, double durationMs, bool success)
	{
		_commandInvocationDuration.Record(durationMs,
			new KeyValuePair<string, object?>("command.name", commandName),
			new KeyValuePair<string, object?>("success", success));
		Observe(new(TelemetryInvocationKind.Command, commandName, durationMs, success));
	}

	private void Observe(TelemetryInvocation invocation)
	{
		foreach (var observer in _observers)
		{
			try { observer.RecordInvocation(invocation); }
			catch { /* Optional diagnostics must not alter command results or execution order. */ }
		}
	}

	public void RecordNotificationSpeed(string notificationType, double durationMs, int recipientCount)
	{
		_notificationSpeed.Record(durationMs,
			new KeyValuePair<string, object?>("notification.type", notificationType),
			new KeyValuePair<string, object?>("recipient.count", recipientCount));
	}

	public void RecordConnectionEvent(string eventType)
	{
		_connectionEvents.Add(1,
			new KeyValuePair<string, object?>("event.type", eventType));
	}

	public void SetActiveConnectionCount(int count)
	{
		_currentActiveConnectionCount = count;
	}

	public void SetLoggedInPlayerCount(int count)
	{
		_currentLoggedInPlayerCount = count;
	}

	public void SetServerHealthState(bool isHealthy)
	{
		_currentServerHealthState = isHealthy;
	}

	public void SetConnectionServerHealthState(bool isHealthy)
	{
		_currentConnectionServerHealthState = isHealthy;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_meter?.Dispose();
			_disposed = true;
			GC.SuppressFinalize(this);
		}
	}
}
