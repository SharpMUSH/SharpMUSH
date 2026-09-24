using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Applies the configured <c>command_restrictions</c> to the live command table at boot. PennMUSH
/// applies <c>mush.cnf</c>'s <c>restrict_command</c> lines through the same <c>restrict_command()</c>
/// that <c>@command/restrict</c> uses (the <c>restrict_command</c> branch of <c>config_set</c>,
/// <c>src/conf.c</c>); reading the option into <see cref="Configurable"/> is not applying it (#1224).
/// </summary>
/// <remarks>
/// Its own service because it is pinned between two others, and a hosted service's position in the
/// <c>StartAsync</c> pass is its registration order:
/// <list type="bullet">
/// <item>after <see cref="PluginBootstrapService"/>, which registers a plugin's
/// <c>[SharpCommand]</c> contributions into the live library — a restriction naming one of those
/// would otherwise find no such command and be skipped, silently, on every boot;</item>
/// <item>before <see cref="StartupAttributeBootstrapService"/>, which runs every stored
/// <c>@STARTUP</c> as God — a command a restriction disables outright would otherwise be usable by
/// boot softcode once per start.</item>
/// </list>
/// That window is what <c>CommandRestrictionBootstrapTests</c> pins.
/// </remarks>
public sealed class CommandRestrictionBootstrapService(
	ILibraryProvider<CommandDefinition> commandLibrary,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<CommandRestrictionBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (commandLibrary is not ICommandRestrictionApplier restrictions)
		{
			return;
		}

		var configured = options.CurrentValue.Restriction.CommandRestrictions;
		if (configured.Count == 0)
		{
			return;
		}

		logger.LogInformation("Applying {Count} configured command restriction(s).", configured.Count);
		await restrictions.ApplyConfiguredRestrictionsAsync(configured);
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
