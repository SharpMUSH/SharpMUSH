using Microsoft.Extensions.Hosting;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Builds the wiki's fact index in the background at every start: a world that never had one gets it, a build a
/// restart interrupted carries on, and a schema change starts it over. Startup does not wait for it; until it is
/// ready, fact queries answer that the index is building. Stopping waits for the batch being written.
/// </summary>
public class WikiFactIndexStartupService(IWikiFactIndexBuilder builder) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		builder.Start();
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => builder.StopAsync().WaitAsync(cancellationToken);
}
