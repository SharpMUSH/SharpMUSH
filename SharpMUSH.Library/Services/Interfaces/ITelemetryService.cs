namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for reporting OpenTelemetry metrics around SharpMUSH operations.
/// </summary>
public interface ITelemetryService
{
	/// <summary>
	/// Records the time taken to invoke a function.
	/// </summary>
	/// <param name="functionName">Name of the function invoked.</param>
	/// <param name="durationMs">Duration in milliseconds.</param>
	/// <param name="success">Whether the function invocation was successful.</param>
	void RecordFunctionInvocation(string functionName, double durationMs, bool success);

	/// <summary>
	/// Records the time taken to invoke a command.
	/// </summary>
	/// <param name="commandName">Name of the command invoked.</param>
	/// <param name="durationMs">Duration in milliseconds.</param>
	/// <param name="success">Whether the command invocation was successful.</param>
	void RecordCommandInvocation(string commandName, double durationMs, bool success);

	/// <summary>
	/// Records the time taken to send a notification.
	/// </summary>
	/// <param name="notificationType">Type of notification (e.g., Emit, Say, Pose).</param>
	/// <param name="durationMs">Duration in milliseconds.</param>
	/// <param name="recipientCount">Number of recipients.</param>
	void RecordNotificationSpeed(string notificationType, double durationMs, int recipientCount);

	/// <summary>
	/// Records a connection event.
	/// </summary>
	/// <param name="eventType">Type of connection event (e.g., connected, disconnected).</param>
	void RecordConnectionEvent(string eventType);

	/// <summary>
	/// Sets the current connection count.
	/// </summary>
	/// <param name="count">Number of active connections.</param>
	void SetActiveConnectionCount(int count);

	/// <summary>
	/// Sets the current logged-in player count.
	/// </summary>
	/// <param name="count">Number of logged-in players.</param>
	void SetLoggedInPlayerCount(int count);

	/// <summary>
	/// Sets the health state of the Server.
	/// </summary>
	/// <param name="isHealthy">Whether the server is healthy.</param>
	void SetServerHealthState(bool isHealthy);

	/// <summary>
	/// Sets the health state of the ConnectionServer.
	/// </summary>
	/// <param name="isHealthy">Whether the connection server is healthy.</param>
	void SetConnectionServerHealthState(bool isHealthy);
}
