using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The host contract <c>StartupHandler</c> leans on: <c>IHostedLifecycleService.StartedAsync</c> runs
/// after <em>every</em> hosted service's <c>StartAsync</c>, not just its own, because the host makes
/// three separate passes over the registrations.
/// </summary>
/// <remarks>
/// <c>StartupHandler</c> applies the configured <c>command_restrictions</c> from <c>StartedAsync</c>
/// so that plugin commands — registered into the live library by <c>PluginBootstrapService.StartAsync</c>,
/// which is registered <em>after</em> it (HostedServiceRegistration.cs:66, :69) — are already in the
/// table when the restrictions are applied (#1224). Pinned here rather than assumed: if a future
/// runtime collapsed the passes, the restriction would silently go back to skipping plugin commands.
/// </remarks>
public class HostedLifecycleOrderingTests
{
	private sealed class Recorder
	{
		public List<string> Calls { get; } = [];
	}

	private sealed class Plain(Recorder recorder, string name) : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken)
		{
			recorder.Calls.Add($"{name}.StartAsync");
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class Lifecycle(Recorder recorder, string name) : IHostedLifecycleService
	{
		public Task StartingAsync(CancellationToken cancellationToken)
		{
			recorder.Calls.Add($"{name}.StartingAsync");
			return Task.CompletedTask;
		}

		public Task StartAsync(CancellationToken cancellationToken)
		{
			recorder.Calls.Add($"{name}.StartAsync");
			return Task.CompletedTask;
		}

		public Task StartedAsync(CancellationToken cancellationToken)
		{
			recorder.Calls.Add($"{name}.StartedAsync");
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
		public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	[Test]
	public async Task StartedAsync_RunsAfterEveryServicesStartAsync_IncludingOnesRegisteredLater()
	{
		var recorder = new Recorder();
		var builder = Host.CreateApplicationBuilder();
		builder.Services.AddSingleton(recorder);
		// The shape of HostedServiceRegistration: the lifecycle service first, a plain one after it.
		builder.Services.AddHostedService(sp => new Lifecycle(sp.GetRequiredService<Recorder>(), "first"));
		builder.Services.AddHostedService(sp => new Plain(sp.GetRequiredService<Recorder>(), "later"));

		using var host = builder.Build();
		await host.StartAsync();
		await host.StopAsync();

		await Assert.That(recorder.Calls).IsEquivalentTo(new[]
		{
			"first.StartingAsync",
			"first.StartAsync",
			"later.StartAsync",
			"first.StartedAsync"
		});
	}
}
