using Microsoft.Extensions.Hosting;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Reads the layout themes staff added and disabled (<c>@theme/add</c>, <c>@theme/disable</c>) before any softcode
/// runs, so boot <c>@STARTUP</c> and the first logins see the game's themes.
/// </summary>
public sealed class LayoutThemeBootstrapService(ILayoutThemeService themes) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken) => await themes.LoadAsync();

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
