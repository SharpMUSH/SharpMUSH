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

	/// <summary>
	/// The game's web address (<c>mud_url</c>, reported as <c>WEBSITE</c>), which the game's own pictures are
	/// relative to; null while it is unset.
	/// </summary>
	public string? Website => _website;

	private volatile string? _website;

	/// <summary>Completes when the first report from the main process arrives.</summary>
	public Task Received => _received.Task;

	public void Replace(IEnumerable<MSSPVariable> variables)
	{
		var config = new MSSPConfig();
		string? website = null;
		foreach (var variable in variables)
		{
			if (variable.Name == "WEBSITE") website ??= variable.Values.FirstOrDefault();
			foreach (var value in variable.Values)
			{
				config.Variables.Add(variable.Name, value);
			}
		}

		_website = website;
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
