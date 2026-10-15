using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Starts again, in the background, every attribute index a restart left building (<c>@attribute/index</c>): each
/// carries on from the object its entry says it reached. Startup does not wait for any of them; until one is ready,
/// queries on it answer <c>#-1 INDEX BUILDING</c>.
/// </summary>
public class AttributeIndexResumeService(IAttributeIndexBuilder builder, ILogger<AttributeIndexResumeService> logger)
	: IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			await builder.ResumeAllAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Could not resume the attribute indexes still building; they resume at the next start.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
