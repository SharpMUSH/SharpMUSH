using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The MSSP report the telnet option answers <c>IAC DO MSSP</c> with. The main process builds it (it
/// owns the game's name, player count and settings) and sends it here as an
/// <see cref="MSSPReportMessage"/>; the option has to be answered at once, so the latest one is held.
/// </summary>
public sealed class MsspReportHolder
{
	/// <summary>What is reported until the main process has sent a report.</summary>
	private static MSSPConfig Fallback() => new() { Name = "SharpMUSH", UTF_8 = true };

	private volatile MSSPConfig _current = Fallback();

	private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public MSSPConfig Current => _current;

	/// <summary>Completes when the first report from the main process arrives.</summary>
	public Task Received => _received.Task;

	public void Replace(IEnumerable<MSSPVariable> variables)
	{
		var config = new MSSPConfig();
		foreach (var variable in variables)
		{
			foreach (var value in variable.Values)
			{
				config.Variables.Add(variable.Name, value);
			}
		}

		_current = config.Variables.Count == 0 ? Fallback() : config;
		_received.TrySetResult();
	}
}

/// <summary>Takes each <see cref="MSSPReportMessage"/> the main process sends.</summary>
public sealed class MSSPReportConsumer(MsspReportHolder holder) : IMessageConsumer<MSSPReportMessage>
{
	public Task HandleAsync(MSSPReportMessage message, CancellationToken cancellationToken = default)
	{
		holder.Replace(message.Variables);
		return Task.CompletedTask;
	}
}
