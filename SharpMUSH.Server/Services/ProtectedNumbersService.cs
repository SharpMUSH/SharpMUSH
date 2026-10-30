using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Keeps the store's protected numbers (<see cref="SetProtectedNumbersCommand"/>) equal to the numbers
/// the configuration names (<see cref="SpecialObjectNumbers"/>): written at start and again whenever the configuration
/// changes, so a free number the configuration names, such as an event handler not created yet, is never handed to an
/// unrelated creation.
/// </summary>
public sealed class ProtectedNumbersService(
	IMediator mediator,
	IOptionsMonitor<SharpMUSHOptions> configuration,
	ILogger<ProtectedNumbersService> logger) : IHostedService, IDisposable
{
	private readonly SemaphoreSlim _writing = new(1, 1);
	private IDisposable? _subscription;
	private IReadOnlySet<int> _written = new HashSet<int>();

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await WriteAsync(cancellationToken);
		_subscription = configuration.OnChange(_ => Task.Run(() => WriteAsync(CancellationToken.None)));
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public void Dispose()
	{
		_subscription?.Dispose();
		_writing.Dispose();
	}

	/// <summary>
	/// Writes the numbers the configuration names now. One write at a time, each reading the configuration once it
	/// holds the turn, so the last write to finish is always of the latest configuration.
	/// </summary>
	private async Task WriteAsync(CancellationToken cancellationToken)
	{
		await _writing.WaitAsync(cancellationToken);
		try
		{
			var numbers = SpecialObjectNumbers.From(configuration.CurrentValue);
			if (numbers.SetEquals(_written)) return;

			await mediator.Send(new SetProtectedNumbersCommand([.. numbers]), cancellationToken);
			_written = numbers;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Could not record the configuration's numbers as protected");
		}
		finally
		{
			_writing.Release();
		}
	}
}
