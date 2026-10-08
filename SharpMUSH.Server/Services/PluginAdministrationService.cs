using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Default <see cref="IPluginAdministration"/>. What ran is the catalog's record of the last start; what will run
/// is the plugins on disk now less the ones turned off in <see cref="PluginState"/>. The difference is what the next
/// restart changes.
/// </summary>
public sealed class PluginAdministrationService(
	PluginCatalog catalog,
	PluginDirectories directories,
	IPluginManager manager,
	IPackageRegistryService registry,
	IPackageInstallService installer,
	IPackageOperationRunner operations,
	GameFeatureService features,
	PluginInstallOptions installOptions,
	ILogger<PluginAdministrationService> logger) : IPluginAdministration
{
	private readonly SemaphoreSlim _stateLock = new(1, 1);

	/// <summary>Plugins this process unloaded after the start, so a loaded-but-not-running one is not taken for a failure.</summary>
	private readonly HashSet<string> _unloaded = new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	public async Task<PluginsResponse> ListAsync(CancellationToken cancellationToken = default)
	{
		var plugins = await StatusesAsync();
		return new PluginsResponse(plugins, installOptions.Allowed, plugins.Any(p => p.Pending != PluginPendingChange.None));
	}

	/// <inheritdoc />
	public async Task<Result<PluginChangeResponse>> SetEnabledAsync(string pluginId, bool enabled,
		CancellationToken cancellationToken = default)
	{
		if ((await StatusesAsync()).FirstOrDefault(p => string.Equals(p.Id, pluginId, StringComparison.OrdinalIgnoreCase))
			is not { } plugin)
		{
			return new Error<string>($"There is no plugin '{pluginId}'.");
		}

		if (plugin.Enabled == enabled)
		{
			return new PluginChangeResponse(plugin, [], [enabled ? $"{plugin.Name} is already on." : $"{plugin.Name} is already off."]);
		}

		var removed = new List<string>();
		var notes = new List<string>();
		if (!enabled && plugin.Dependents.Count > 0)
		{
			switch (await RemoveDependentsAsync(plugin, cancellationToken))
			{
				case IReadOnlyList<string> uninstalled:
					removed.AddRange(uninstalled);
					break;
				case Error<string> error:
					return error;
			}
		}

		await _stateLock.WaitAsync(cancellationToken);
		try
		{
			PluginState.Read(directories.StateFile).With(plugin.Id, enabled).Write(directories.StateFile);
		}
		finally
		{
			_stateLock.Release();
		}

		if (enabled)
		{
			// A shipped package over this plugin comes back at the next start, once the plugin runs again.
			foreach (var application in GameFeatureService.All.Where(a => string.Equals(a.RequiredPlugin, plugin.Id, StringComparison.OrdinalIgnoreCase)))
			{
				await features.SetDeclinedAsync(application.PackageId, declined: false);
			}
		}
		else if (plugin.Unloadable && manager.Registration(plugin.Id) is not null)
		{
			switch (await manager.UnloadAsync(plugin.Id))
			{
				case Success:
					lock (_unloaded)
					{
						_unloaded.Add(plugin.Id);
					}

					notes.Add($"{plugin.Name} stopped.");
					break;
				case Error<string> error:
					logger.LogWarning("Plugin {Id} is turned off but could not be unloaded now: {Reason}", plugin.Id, error.Value);
					break;
			}
		}

		logger.LogInformation("Plugin {Id} turned {State}.", plugin.Id, enabled ? "on" : "off");
		var after = (await StatusesAsync()).First(p => string.Equals(p.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));
		if (after.Pending != PluginPendingChange.None)
		{
			notes.Add(enabled
				? $"{plugin.Name} starts when the server restarts."
				: $"{plugin.Name} stops when the server restarts.");
		}

		return new PluginChangeResponse(after, removed, notes);
	}

	/// <summary>
	/// Uninstalls the packages that need <paramref name="plugin"/>, dependents of dependents first, in one package
	/// operation (one backup first). A shipped package is recorded as declined so the next start leaves it out.
	/// </summary>
	private async Task<Result<IReadOnlyList<string>>> RemoveDependentsAsync(PluginStatusDto plugin, CancellationToken cancellationToken)
	{
		var order = plugin.Dependents.Reverse().ToList();
		var outcome = await operations.RunAsync("uninstall", async token =>
		{
			var done = new List<string>();
			foreach (var packageId in order)
			{
				if (await installer.UninstallAsync(packageId, cancellationToken: token) is Error<string> error)
				{
					return (Result<IReadOnlyList<string>>)new Error<string>(
						$"{packageId} needs {plugin.Name} and could not be uninstalled, so {plugin.Name} is still on: {error.Value}");
				}

				done.Add(packageId);
			}

			return new Result<IReadOnlyList<string>>(done);
		}, cancellationToken);

		switch (outcome)
		{
			case PackageOperationRan<Result<IReadOnlyList<string>>> { Result: IReadOnlyList<string> done }:
				foreach (var packageId in done)
				{
					await features.SetDeclinedAsync(packageId, declined: true);
				}

				return new Result<IReadOnlyList<string>>(done);
			case PackageOperationRan<Result<IReadOnlyList<string>>> { Result: Error<string> error }:
				return error;
			case PackageOperationRefused refused:
				return new Error<string>(refused.Reason);
			default:
				return new Error<string>("The package operation did not run.");
		}
	}

	/// <summary>Every plugin the last start found or that is on disk now, shipped ones first.</summary>
	private async Task<IReadOnlyList<PluginStatusDto>> StatusesAsync()
	{
		var state = ReadState();
		var onDisk = PluginLoaderService.Discover(directories.BuiltIn, logger, PluginOrigin.BuiltIn)
			.Concat(PluginLoaderService.Discover(directories.Installed, logger, PluginOrigin.Installed))
			.GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		var booted = catalog.Entries
			.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		var installed = await registry.GetInstalledPackagesAsync();
		var installedIds = installed.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var dependencies = await registry.GetAllPackageDependenciesAsync();

		var ids = booted.Keys.Concat(onDisk.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(id => (booted.GetValueOrDefault(id)?.Origin ?? onDisk[id].Origin) == PluginOrigin.BuiltIn ? 0 : 1)
			.ThenBy(id => id, StringComparer.OrdinalIgnoreCase);

		var result = new List<PluginStatusDto>();
		foreach (var id in ids)
		{
			var boot = booted.GetValueOrDefault(id);
			var disk = onDisk.GetValueOrDefault(id);
			var registration = manager.Registration(id);
			var enabled = !state.IsDisabled(id);
			var running = registration is not null;
			var willRun = disk is { Problem: null } && enabled;
			var origin = boot?.Origin ?? disk!.Origin;
			var package = origin == PluginOrigin.Installed && installed.FirstOrDefault(p =>
				p.Kind == PackageKind.Plugin && string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) is { } owner
				? owner.Id
				: null;

			// Loaded at the start but not running: unloaded since, or it threw while starting.
			var (status, reason) = boot switch
			{
				null => (disk!.Problem ?? (enabled ? PluginBootStatus.NotStarted : PluginBootStatus.Disabled), disk.Reason),
				{ Status: PluginBootStatus.Loaded } when !running => WasUnloaded(id)
					? (PluginBootStatus.Disabled, null)
					: (PluginBootStatus.Failed, "It threw while starting; the server log has the error."),
				_ => (boot.Status, boot.Reason)
			};

			result.Add(new PluginStatusDto(
				id,
				boot?.Name ?? disk?.Name ?? id,
				boot?.Version ?? disk?.Version,
				boot?.Description ?? disk?.Description,
				origin,
				status,
				reason,
				enabled,
				running == willRun ? PluginPendingChange.None : willRun ? PluginPendingChange.Starts : PluginPendingChange.Stops,
				registration?.Unloadable ?? false,
				package,
				registration?.Commands ?? 0,
				registration?.Functions ?? 0,
				Dependents(id, package, installedIds, dependencies)));
		}

		return result;
	}

	/// <summary>
	/// The installed packages that need plugin <paramref name="pluginId"/>: the shipped packages built over it, the
	/// packages that depend on the package that installed it, and so on down, nearest first.
	/// </summary>
	private static IReadOnlyList<string> Dependents(string pluginId, string? package, IReadOnlySet<string> installed,
		IReadOnlyList<PackageDependencyRecord> dependencies)
	{
		var found = new List<string>();
		var queue = new Queue<string>(GameFeatureService.All
			.Where(a => string.Equals(a.RequiredPlugin, pluginId, StringComparison.OrdinalIgnoreCase))
			.Select(a => a.PackageId)
			.Where(installed.Contains));
		if (package is not null)
		{
			foreach (var dependent in dependencies.Where(d => string.Equals(d.DependsOnId, package, StringComparison.OrdinalIgnoreCase)))
			{
				queue.Enqueue(dependent.PackageId);
			}
		}

		while (queue.TryDequeue(out var next))
		{
			if (found.Contains(next, StringComparer.OrdinalIgnoreCase) || !installed.Contains(next))
			{
				continue;
			}

			found.Add(next);
			foreach (var dependent in dependencies.Where(d => string.Equals(d.DependsOnId, next, StringComparison.OrdinalIgnoreCase)))
			{
				queue.Enqueue(dependent.PackageId);
			}
		}

		return found;
	}

	private bool WasUnloaded(string pluginId)
	{
		lock (_unloaded)
		{
			return _unloaded.Contains(pluginId);
		}
	}

	private PluginState ReadState()
	{
		try
		{
			return PluginState.Read(directories.StateFile);
		}
		catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
		{
			logger.LogError(ex, "The plugin state file {Path} cannot be read.", directories.StateFile);
			return PluginState.Empty;
		}
	}
}
