using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The commands a package declares (<c>commands:</c>): checked before an apply writes anything, recorded with the
/// package, and added, hooked or removed once the apply, rollback or uninstall commits.
/// </summary>
public partial class PackageInstallService
{
	/// <summary>
	/// The engine's command registrar. It is the runtime-registration service, which holds the command table; a host
	/// whose service is not one has no package commands.
	/// </summary>
	private IPackageCommandRegistrar? CommandRegistrar => registrations.Value as IPackageCommandRegistrar;

	/// <summary>
	/// What an apply of <paramref name="manifest"/> does to the command table: each declared command is added, kept or
	/// hooked elsewhere, or blocked when another installed package declares it or a built-in or plugin command already
	/// answers to it; each command <paramref name="installed"/> declared that the manifest does not is removed.
	/// </summary>
	private async Task<IReadOnlyList<PackageCommandChange>> PlanCommandsAsync(PackageManifest manifest, InstalledPackageRecord? installed)
	{
		var previous = (installed?.Commands ?? []).ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
		if (manifest.AddedCommands.Count == 0 && previous.Count == 0)
		{
			return [];
		}

		var claimed = await CommandsClaimedByOthersAsync(manifest.Name);
		var changes = new List<PackageCommandChange>();
		foreach (var command in manifest.AddedCommands)
		{
			var refused = claimed.TryGetValue(command.Name, out var owner)
				? $"The {owner} package adds it."
				: CommandRegistrar is { } registrar && await registrar.CheckAsync(command.Name) is Error<string> error
					? error.Value
					: null;
			var action = refused is not null ? PackageCommandAction.Blocked
				: !previous.TryGetValue(command.Name, out var was) ? PackageCommandAction.Add
				: was.Ref == command.Ref && was.Attribute.Equals(command.Attribute, StringComparison.OrdinalIgnoreCase) && was.NoBreak == command.NoBreak
					? PackageCommandAction.Keep
					: PackageCommandAction.Change;
			changes.Add(new PackageCommandChange(command.Name, action, command.Ref, command.Attribute, refused));
		}

		changes.AddRange(previous.Values
			.Where(was => !manifest.AddedCommands.Any(c => c.Name.Equals(was.Name, StringComparison.OrdinalIgnoreCase)))
			.Select(was => new PackageCommandChange(was.Name, PackageCommandAction.Remove, was.Ref, was.Attribute)));
		return changes;
	}

	/// <summary>Each command another installed package adds, with that package's id.</summary>
	private async Task<Dictionary<string, string>> CommandsClaimedByOthersAsync(string packageId)
	{
		var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var package in (await registry.GetInstalledPackagesAsync()).Where(p => p.Id != packageId))
		{
			foreach (var command in package.Commands ?? [])
			{
				claimed.TryAdd(command.Name, package.Id);
			}
		}

		return claimed;
	}

	/// <summary>
	/// The commands a rollback to <paramref name="snapshot"/> leaves the package with: the snapshot's, less any another
	/// installed package has added since. A snapshot recorded before packages listed their commands has none recorded,
	/// so the package keeps the ones it has.
	/// </summary>
	private async Task<IReadOnlyList<PackageCommandRecord>?> RollbackCommandsAsync(InstalledPackageRecord installed,
		PackageRevisionSnapshot snapshot, List<string> notes)
	{
		if (snapshot.Commands is not { } wanted)
		{
			if (installed.Commands is { Count: > 0 })
			{
				notes.Add("This revision was recorded before packages listed their commands; the current commands stay.");
			}

			return installed.Commands;
		}

		var claimed = await CommandsClaimedByOthersAsync(installed.Id);
		var kept = new List<PackageCommandRecord>();
		foreach (var command in wanted)
		{
			if (claimed.TryGetValue(command.Name, out var owner))
			{
				notes.Add($"The command {command.Name} was not added: the {owner} package adds it.");
				continue;
			}

			kept.Add(command);
		}

		return kept.Count == 0 ? null : kept;
	}

	/// <summary>The manifest's commands with the objects their hooks run on, or null for none.</summary>
	private static IReadOnlyList<PackageCommandRecord>? ResolveCommands(PackageManifest manifest, IReadOnlyDictionary<string, string> objidByRef)
		=> manifest.AddedCommands.Count == 0
			? null
			: manifest.AddedCommands
				.Select(spec => new PackageCommandRecord(spec.Name, spec.Ref, objidByRef[spec.Ref], spec.Attribute, spec.NoBreak))
				.ToList();

	/// <summary>
	/// Moves the command table from <paramref name="previous"/> to <paramref name="wanted"/>: removes each command no
	/// longer declared, then adds and hooks each declared one. Runs after the write commits, since the table is not
	/// part of the world and has nothing to undo.
	/// </summary>
	private async Task SyncCommandsAsync(IReadOnlyList<PackageCommandRecord>? previous, IReadOnlyList<PackageCommandRecord>? wanted,
		List<string> notes)
	{
		previous ??= [];
		wanted ??= [];
		if (previous.Count == 0 && wanted.Count == 0)
		{
			return;
		}

		if (CommandRegistrar is not { } registrar)
		{
			if (wanted.Count > 0)
			{
				notes.Add("This server cannot add package commands; they were not added.");
			}

			return;
		}

		var removed = new List<string>();
		foreach (var command in previous.Where(p => !wanted.Any(w => w.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase))))
		{
			await registrar.RemoveAsync(command);
			removed.Add(command.Name);
		}

		if (removed.Count > 0)
		{
			notes.Add($"Removed the command(s) {string.Join(", ", removed)}.");
		}

		var added = new List<string>();
		var changed = new List<string>();
		foreach (var command in wanted)
		{
			var was = previous.FirstOrDefault(p => p.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase));
			switch (await registrar.RegisterAsync(command))
			{
				case Success when was is null:
					added.Add(command.Name);
					break;
				case Success when was != command:
					changed.Add(command.Name);
					break;
				case Success:
					break;
				case Error<string> error:
					notes.Add($"The command {command.Name} was not added: {error.Value}");
					break;
			}
		}

		if (added.Count > 0)
		{
			notes.Add($"Added the command(s) {string.Join(", ", added)}.");
		}

		if (changed.Count > 0)
		{
			notes.Add($"Updated the command(s) {string.Join(", ", changed)}.");
		}
	}
}
