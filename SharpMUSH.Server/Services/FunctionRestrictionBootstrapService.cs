using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Applies the configured <c>function_restrictions</c> to the live function table at boot, and again
/// whenever they change. PennMUSH applies <c>mush.cnf</c>'s <c>restrict_function</c> lines through
/// <c>restrict_function()</c> (the <c>restrict_function</c> branch of <c>config_set</c>,
/// <c>src/conf.c</c>); reading the option into <see cref="Library.Definitions.Configurable"/> is not
/// applying it.
/// </summary>
/// <remarks>
/// Registered in the same window as <see cref="CommandRestrictionBootstrapService"/>, for the same
/// reasons: after <see cref="PluginBootstrapService"/> so a plugin's functions are in the table, and
/// before <see cref="StartupAttributeBootstrapService"/> so boot <c>@STARTUP</c> is already held to
/// them. A change to any other option leaves the function table alone, so it does not discard a
/// live <c>@function/restrict</c>.
/// </remarks>
public sealed class FunctionRestrictionBootstrapService(
	ConfiguredFunctionRestrictions restrictions,
	IOptionsMonitor<SharpMUSHOptions> options,
	ILogger<FunctionRestrictionBootstrapService> logger) : IHostedService, IDisposable
{
	private readonly Lock _gate = new();
	private IReadOnlyDictionary<string, string[]> _applied = new Dictionary<string, string[]>();
	private IDisposable? _subscription;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		ApplyCurrent();
		_subscription = options.OnChange((_, _) => Reapply());
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_subscription?.Dispose();
		_subscription = null;
		return Task.CompletedTask;
	}

	public void Dispose() => _subscription?.Dispose();

	private void Reapply()
	{
		try
		{
			ApplyCurrent();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to reapply the configured function restrictions.");
		}
	}

	/// <summary>Applies the restrictions the configuration holds now, if they differ from the last ones applied.</summary>
	private void ApplyCurrent()
	{
		lock (_gate)
		{
			var configured = options.CurrentValue.Restriction.FunctionRestrictions;
			if (SameRestrictions(configured, _applied))
			{
				return;
			}

			logger.LogInformation("Applying {Count} configured function restriction(s).", configured.Count);
			restrictions.Apply(configured);
			_applied = configured;
		}
	}

	private static bool SameRestrictions(IReadOnlyDictionary<string, string[]> left, IReadOnlyDictionary<string, string[]> right)
		=> left.Count == right.Count
			&& left.All(entry => right.TryGetValue(entry.Key, out var words) && entry.Value.SequenceEqual(words));
}
