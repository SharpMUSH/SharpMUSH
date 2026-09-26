using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// The installed packages of a seeded world: cleared with the seeds, so the objects they created free the
/// numbers past #9 the source's objects need, and put back once the import is written.
/// </summary>
public partial class PennMUSHDatabaseConverter
{
	/// <summary>
	/// Uninstalls every installed package and deletes the objects it created. A fresh server's bundled
	/// packages create theirs at #10 and up, where every PennMUSH database has objects of its own.
	/// </summary>
	/// <remarks>
	/// Runs while the seeded Package Manager (#7) is still there, since the uninstall writes as it. An
	/// uninstall only marks a package's objects GOING, which keeps their numbers, so they are deleted here
	/// outright, and every hook that pointed at one is cleared: its number is about to be a source
	/// object's. Anything the package objects still own or hold goes to God with the seeds' holdings.
	/// </remarks>
	/// <returns>The numbers of the objects the packages created, for the caller to delete.</returns>
	private async Task<HashSet<int>> UninstallPackagesAsync(PennMUSHConversionContext context,
		CancellationToken cancellationToken)
	{
		var packageObjects = new HashSet<int>();
		if (_packageRegistry is null || _packageInstaller is null)
		{
			return packageObjects;
		}

		foreach (var package in await _packageRegistry.GetInstalledPackagesAsync())
		{
			// Read before the uninstall drops the registry rows, but only deleted once it has worked: a
			// package still registered keeps its objects, or its next upgrade would write to source objects.
			var objects = (await _packageRegistry.GetPackageObjectsAsync(package.Id))
				.Select(record => HelperFunctions.ParseDbRef(record.Objid))
				.Where(parsed => parsed.IsSome())
				.Select(parsed => ((DBRef)parsed.Value!).Number)
				.ToList();

			// Forced: the packages all go, so neither a dependent nor an attachment is left behind.
			if (await _packageInstaller.UninstallAsync(package.Id, force: true, cancellationToken) is Error<string> error)
			{
				context.Warnings.Add($"Package '{package.Id}' could not be uninstalled before the import: {error.Value}");
				continue;
			}

			packageObjects.UnionWith(objects);
			context.UninstalledPackages.Add(package.Id);
		}

		if (context.UninstalledPackages.Count > 0)
		{
			context.Warnings.Add($"Uninstalled the package(s) {string.Join(", ", context.UninstalledPackages)} " +
				"so the imported object numbers are kept; the bundled ones are installed again after the import.");
		}

		return packageObjects;
	}

	/// <summary>
	/// Deletes the objects the uninstalled packages created and clears the hooks that pointed at them.
	/// </summary>
	private async Task DeletePackageObjectsAsync(PennMUSHConversionContext context, IReadOnlySet<int> packageObjects,
		CancellationToken cancellationToken)
	{
		var deleted = new List<int>();
		foreach (var number in packageObjects.Order())
		{
			if (await _mediator.Send(new DeleteObjectCommand(new DBRef(number)), cancellationToken))
			{
				deleted.Add(number);
			}
			else
			{
				context.Warnings.Add($"Package object #{number} could not be removed before the import.");
			}
		}

		if (_hooks is not null && await _hooks.ClearHooksOnAsync(packageObjects) is var cleared and > 0)
		{
			context.Warnings.Add($"Cleared {cleared} command hook(s) that pointed at the removed package objects.");
		}

		if (deleted.Count > 0)
		{
			context.Warnings.Add($"Removed {deleted.Count} package object(s) so the imported object numbers are kept: " +
				$"{string.Join(", ", deleted.Select(n => $"#{n}"))}.");
		}
	}

	/// <summary>
	/// Puts the uninstalled bundled packages back once the source's objects hold their numbers: a new
	/// Package Manager past the highest of them, named by <c>package_manager</c>, and the packages
	/// installed through it, so their objects land past the imported range too.
	/// </summary>
	/// <remarks>
	/// Like the seeded one, the Package Manager is a WIZARD player that owns itself and has no password,
	/// so nobody logs in as it. It takes a name no imported player has. A package that is not bundled
	/// cannot be fetched again here and is reported for the administrator to reinstall.
	///
	/// It runs once, whether the import finished or stopped part way: the packages are gone either way.
	/// It takes no cancellation token, since stopping it part way would leave them gone.
	/// </remarks>
	private async Task ReinstallPackagesAsync(PennMUSHConversionContext context)
	{
		if (context.UninstalledPackages.Count == 0 || context.PackagesReinstalled)
		{
			return;
		}

		context.PackagesReinstalled = true;
		var cancellationToken = CancellationToken.None;
		try
		{
			var name = await FreePlayerNameAsync("Package Manager", cancellationToken);
			var packageManager = await _mediator.Send(new CreatePlayerCommand(name, string.Empty,
				new DBRef(0), new DBRef(0), 999999, StoredVerbatim, ApplyDefaultFlags: false), cancellationToken);
			if (await _mediator.Send(new GetObjectFlagQuery("WIZARD"), cancellationToken) is { } wizard &&
				await _mediator.Send(new GetObjectNodeQuery(packageManager), cancellationToken) is AnySharpObject node)
			{
				await _mediator.Send(new SetObjectFlagCommand(node, wizard), cancellationToken);
			}

			var options = _options.CurrentValue;
			var database = (context.WrittenDatabaseOptions ?? options.Database) with
			{
				PackageManager = (uint)packageManager.Number
			};
			await _mediator.Send(new SetExpandedServerDataCommand(nameof(SharpMUSHOptions),
				options with { Database = database }), cancellationToken);
			context.WrittenDatabaseOptions = database;
			_configurationReload?.SignalChange();
			context.Warnings.Add($"Created a new Package Manager '{name}' at #{packageManager.Number}, past the imported " +
				"objects, and set package_manager to it.");

			var reinstalled = _bundledPackages is null
				? []
				: await _bundledPackages.InstallBundledAsync(context.UninstalledPackages, cancellationToken);
			var missing = context.UninstalledPackages.Except(reinstalled, StringComparer.Ordinal).ToList();
			if (reinstalled.Count > 0)
			{
				context.Warnings.Add($"Installed the package(s) {string.Join(", ", reinstalled)} again.");
			}

			if (missing.Count > 0)
			{
				context.Warnings.Add($"The package(s) {string.Join(", ", missing)} were not installed again: " +
					"install them from the package manager.");
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not reinstall the packages uninstalled before the import");
			context.Warnings.Add($"The package(s) {string.Join(", ", context.UninstalledPackages)} could not be " +
				$"installed again ({ex.Message}): install them from the package manager.");
		}
	}

	/// <summary>
	/// <paramref name="name"/>, or it followed by the first number from 2 up that makes it a name (or
	/// alias) no player has, since player names are unique and the source may already have this one.
	/// </summary>
	private async Task<string> FreePlayerNameAsync(string name, CancellationToken cancellationToken)
	{
		var candidate = name;
		for (var suffix = 2;
			await _mediator.CreateStream(new GetPlayerQuery(candidate), cancellationToken).AnyAsync(cancellationToken);
			suffix++)
		{
			candidate = $"{name} {suffix}";
		}

		return candidate;
	}
}
