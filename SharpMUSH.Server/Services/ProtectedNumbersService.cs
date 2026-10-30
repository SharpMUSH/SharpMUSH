using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Keeps the store's protected numbers (<see cref="IFreeNumberStore.SetProtectedNumbersAsync"/>) equal to the numbers
/// the configuration names (<see cref="SpecialObjectNumbers"/>): written at start and again whenever the configuration
/// changes, so a free number the configuration names, such as an event handler not created yet, is never handed to an
/// unrelated creation.
/// </summary>
public sealed class ProtectedNumbersService(
	IFreeNumberStore store,
	IOptionsMonitor<SharpMUSHOptions> configuration,
	ILogger<ProtectedNumbersService> logger) : IHostedService, IDisposable
{
	private IDisposable? _subscription;
	private IReadOnlySet<int> _written = new HashSet<int>();

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await WriteAsync(configuration.CurrentValue, cancellationToken);
		_subscription = configuration.OnChange(options => _ = WriteAsync(options, CancellationToken.None));
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public void Dispose() => _subscription?.Dispose();

	private async Task WriteAsync(SharpMUSHOptions options, CancellationToken cancellationToken)
	{
		var numbers = SpecialObjectNumbers.From(options);
		if (numbers.SetEquals(_written)) return;

		try
		{
			await store.SetProtectedNumbersAsync([.. numbers], cancellationToken);
			_written = numbers;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			logger.LogError(exception, "Could not record the configuration's numbers as protected");
		}
	}
}
