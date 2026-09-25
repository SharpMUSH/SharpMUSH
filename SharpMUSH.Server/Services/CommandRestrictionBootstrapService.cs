using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Applies the configured <c>command_restrictions</c> to the live command table at boot, and again
/// whenever they change. PennMUSH applies <c>mush.cnf</c>'s <c>restrict_command</c> lines through the
/// same <c>restrict_command()</c> that <c>@command/restrict</c> uses (the <c>restrict_command</c>
/// branch of <c>config_set</c>, <c>src/conf.c</c>); reading the option into
/// <see cref="Configurable"/> is not applying it (#1224).
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
/// <para>
/// PennMUSH reads <c>restrict_command</c> only at boot and restart. Here a change made through the
/// portal or <c>@config/set</c> reapplies them, loosening as well as tightening (#1250); a change to
/// any other option leaves the command table alone, so it does not discard a live
/// <c>@command/restrict</c>.
/// </para>
/// </remarks>
public sealed class CommandRestrictionBootstrapService(
	ILibraryProvider<CommandDefinition> commandLibrary,
	IOptionsMonitor<SharpMUSHOptions> options,
	ILogger<CommandRestrictionBootstrapService> logger) : IHostedService, IDisposable
{
	private readonly SemaphoreSlim _gate = new(1, 1);
	private IReadOnlyDictionary<string, string[]> _applied = new Dictionary<string, string[]>();
	private IDisposable? _subscription;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (commandLibrary is not ICommandRestrictionApplier)
		{
			return;
		}

		await ApplyCurrentAsync();
		_subscription = options.OnChange((_, _) => _ = ReapplyAsync());
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_subscription?.Dispose();
		_subscription = null;
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		_subscription?.Dispose();
		_gate.Dispose();
	}

	private async Task ReapplyAsync()
	{
		try
		{
			await ApplyCurrentAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to reapply the configured command restrictions.");
		}
	}

	/// <summary>
	/// Applies the restrictions the configuration holds now, if they differ from the last ones
	/// applied. Read inside the gate, so whichever of two quick changes runs last applies the latest.
	/// </summary>
	private async Task ApplyCurrentAsync()
	{
		await _gate.WaitAsync();
		try
		{
			var configured = options.CurrentValue.Restriction.CommandRestrictions;
			if (SameRestrictions(configured, _applied))
			{
				return;
			}

			logger.LogInformation("Applying {Count} configured command restriction(s).", configured.Count);
			await ((ICommandRestrictionApplier)commandLibrary).ApplyConfiguredRestrictionsAsync(configured);
			_applied = configured;
		}
		finally
		{
			_gate.Release();
		}
	}

	private static bool SameRestrictions(IReadOnlyDictionary<string, string[]> left, IReadOnlyDictionary<string, string[]> right)
		=> left.Count == right.Count
			&& left.All(entry => right.TryGetValue(entry.Key, out var words) && entry.Value.SequenceEqual(words));
}
