using SharpMUSH.Library.Services.Interfaces;
using System.Diagnostics.Metrics;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Implementation of telemetry service using OpenTelemetry metrics.
/// </summary>
public class TelemetryService : ITelemetryService, IDisposable
{
	private readonly Meter _meter;
	private bool _disposed;
	private readonly Histogram<double> _functionInvocationDuration;
	private readonly Histogram<double> _commandInvocationDuration;
	private readonly Histogram<double> _notificationSpeed;
	private readonly Counter<long> _connectionEvents;
	private readonly ObservableGauge<int> _activeConnectionCount;
	private readonly ObservableGauge<int> _loggedInPlayerCount;
	private readonly ObservableGauge<int> _serverHealthState;
	private readonly ObservableGauge<int> _connectionServerHealthState;

	private int _currentActiveConnectionCount;
	private int _currentLoggedInPlayerCount;
	private bool _currentServerHealthState = true;
	private bool _currentConnectionServerHealthState = true;

	public TelemetryService()
	{
		_meter = new Meter("SharpMUSH", "1.0.0");

		// Function invocation metrics
		_functionInvocationDuration = _meter.CreateHistogram<double>(
			"sharpmush.function.invocation.duration",
			unit: "ms",
			description: "Time taken to invoke a function");

		// Command invocation metrics
		_commandInvocationDuration = _meter.CreateHistogram<double>(
			"sharpmush.command.invocation.duration",
			unit: "ms",
			description: "Time taken to invoke a command");

		// Notification speed metrics
		_notificationSpeed = _meter.CreateHistogram<double>(
			"sharpmush.notification.speed",
			unit: "ms",
			description: "Time taken to send a notification");

		// Connection event counter
		_connectionEvents = _meter.CreateCounter<long>(
			"sharpmush.connection.events",
			description: "Count of connection events");

		// Active connection count gauge
		_activeConnectionCount = _meter.CreateObservableGauge<int>(
			"sharpmush.connections.active",
			() => _currentActiveConnectionCount,
			description: "Number of active connections");

		// Logged-in player count gauge
		_loggedInPlayerCount = _meter.CreateObservableGauge<int>(
			"sharpmush.players.logged_in",
			() => _currentLoggedInPlayerCount,
			description: "Number of logged-in players");

		// Server health state gauge (1 = healthy, 0 = unhealthy)
		_serverHealthState = _meter.CreateObservableGauge<int>(
			"sharpmush.server.health",
			() => _currentServerHealthState ? 1 : 0,
			description: "Health state of the Server (1 = healthy, 0 = unhealthy)");

		// ConnectionServer health state gauge (1 = healthy, 0 = unhealthy)
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
	}

	public void RecordCommandInvocation(string commandName, double durationMs, bool success)
	{
		_commandInvocationDuration.Record(durationMs,
			new KeyValuePair<string, object?>("command.name", commandName),
			new KeyValuePair<string, object?>("success", success));
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
