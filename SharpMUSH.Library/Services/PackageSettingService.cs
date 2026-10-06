using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageSettingService(
	IConfigOptionWriter config,
	IPackageRegistryService packages) : IPackageSettingService
{
	/// <summary>One option's plan, with what applying it writes and records.</summary>
	/// <param name="Change">What the review screen shows.</param>
	/// <param name="Property">The option's property name.</param>
	/// <param name="Write">Whether applying writes <paramref name="Target"/>.</param>
	/// <param name="Target">The parsed value to write.</param>
	/// <param name="Record">What the package owns of the option afterwards, or null when it lets go.</param>
	/// <param name="Pending">The value names an object the install has yet to create, so it is known only at apply.</param>
	private sealed record Planned(
		PackageSettingChange Change,
		string Property,
		bool Write,
		object? Target,
		PackageSettingRecord? Record,
		bool Pending = false);

	/// <inheritdoc />
	public async Task<IReadOnlyList<PackageSettingChange>> PlanAsync(
		string packageId,
		IReadOnlyList<PackageSettingSpec> declared,
		IReadOnlyList<PackageSettingRecord>? owned,
		Func<PackageRef, string?> resolve)
		=> [.. (await PlanCoreAsync(packageId, declared, owned, resolve)).Select(p => p.Change)];

	/// <inheritdoc />
	public async Task<Result<IReadOnlyList<PackageSettingRecord>>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		IReadOnlyList<PackageSettingSpec> declared,
		IReadOnlyList<PackageSettingRecord>? owned,
		Func<PackageRef, string?> resolve,
		List<string> notes)
	{
		var plan = await PlanCoreAsync(packageId, declared, owned, resolve);
		if (plan.Where(p => p.Change.Action == PackageSettingAction.Blocked).ToArray() is { Length: > 0 } blocked)
		{
			return new Error<string>($"Settings are blocked: {string.Join("; ", blocked.Select(p => $"{p.Change.Option}: {p.Change.Detail}"))}");
		}

		if (plan.FirstOrDefault(p => p.Pending) is { } unresolved)
		{
			return new Error<string>($"{unresolved.Change.Option} names {unresolved.Change.Value}, which does not resolve to an object.");
		}

		foreach (var step in plan.Where(p => p.Write))
		{
			var change = step.Change;
			var before = Parsed(step.Property, change.Current);
			if (await config.SetAsync(step.Property, step.Target) is Error<string> refused)
			{
				return new Error<string>($"Setting {change.Option} to {Shown(change.Value)} was refused: {refused.Value}");
			}

			var written = ConfigOptionWriter.FormatValue(step.Property, step.Target);
			writes.Track($"config {change.Option}", async () =>
			{
				// Something else set the option since: its value stands.
				if (await config.CurrentTextAsync(step.Property) != written)
				{
					return;
				}

				if (await config.SetAsync(step.Property, before) is Error<string> refused)
				{
					throw new InvalidOperationException($"{change.Option} could not be put back to {Shown(change.Current)}: {refused.Value}");
				}
			});
			notes.Add(change.Action == PackageSettingAction.Restore
				? $"Put {change.Option} back to {Shown(change.Value)}."
				: $"Set {change.Option} to {Shown(change.Value)} (was {Shown(change.Current)}).");
		}

		foreach (var step in plan.Where(p => p.Change.Action is PackageSettingAction.Keep or PackageSettingAction.Release))
		{
			notes.Add($"Left {step.Change.Option} at {Shown(step.Change.Current)}: {step.Change.Detail}");
		}

		return plan.Select(p => p.Record).OfType<PackageSettingRecord>().ToList();
	}

	private async Task<IReadOnlyList<Planned>> PlanCoreAsync(
		string packageId,
		IReadOnlyList<PackageSettingSpec> declared,
		IReadOnlyList<PackageSettingRecord>? owned,
		Func<PackageRef, string?> resolve)
	{
		var ownedByOption = (owned ?? []).ToDictionary(o => o.Option, StringComparer.OrdinalIgnoreCase);
		var setByOthers = new Dictionary<string, (string Package, string? Value)>(StringComparer.OrdinalIgnoreCase);
		if (declared.Count > 0)
		{
			foreach (var other in (await packages.GetInstalledPackagesAsync()).Where(p => p.Id != packageId))
			{
				foreach (var setting in other.Settings ?? [])
				{
					setByOthers.TryAdd(setting.Option, (other.Id, setting.Value));
				}
			}
		}

		var plan = new List<Planned>();
		foreach (var spec in declared)
		{
			plan.Add(await PlanSettingAsync(spec, ownedByOption.GetValueOrDefault(spec.Option), setByOthers, resolve));
		}

		foreach (var dropped in ownedByOption.Values.Where(o => declared.All(d => !d.Option.Equals(o.Option, StringComparison.OrdinalIgnoreCase))))
		{
			plan.Add(await PlanReleaseAsync(dropped));
		}

		return plan;
	}

	private async Task<Planned> PlanSettingAsync(
		PackageSettingSpec spec,
		PackageSettingRecord? mine,
		IReadOnlyDictionary<string, (string Package, string? Value)> setByOthers,
		Func<PackageRef, string?> resolve)
	{
		if (ConfigOptionWriter.PropertyOf(spec.Option) is not { } property)
		{
			return Blocked(spec.Option, "", null, spec.Value, "It is not a configuration option on this server.");
		}

		var current = await config.CurrentTextAsync(property);
		if (setByOthers.TryGetValue(spec.Option, out var other))
		{
			return Blocked(spec.Option, property, current, spec.Value,
				$"The {other.Package} package already sets it (to {Shown(other.Value)}); uninstall that package first.");
		}

		if (ResolveValue(spec.Value, resolve) is not { } text)
		{
			// An object this install has yet to create: there is nothing to check its dbref against until it exists.
			return new Planned(
				new PackageSettingChange(spec.Option, PackageSettingAction.Set, current, spec.Value,
					"The object this install creates."),
				property, Write: false, Target: null, Record: null, Pending: true);
		}

		if (!ConfigOptionWriter.TryParseValue(property, text, out var target))
		{
			return Blocked(spec.Option, property, current, text, $"'{text}' is not a value {spec.Option} takes.");
		}

		var value = ConfigOptionWriter.FormatValue(property, target);
		if (await config.PreviewAsync(property, target) is Error<string> refused)
		{
			return Blocked(spec.Option, property, current, value, refused.Value);
		}

		// The package set this before. A new value is the new version's to set; the same value leaves the game's
		// own, whatever an administrator has made it since.
		if (mine is not null && mine.Value == value)
		{
			return current == value
				? new Planned(new PackageSettingChange(spec.Option, PackageSettingAction.Unchanged, current, value),
					property, Write: false, target, mine)
				: new Planned(new PackageSettingChange(spec.Option, PackageSettingAction.Keep, current, current,
					"the game has changed it since the package set it."), property, Write: false, target, mine);
		}

		var previous = mine?.Previous ?? current;
		return new Planned(
			new PackageSettingChange(spec.Option, current == value ? PackageSettingAction.Unchanged : PackageSettingAction.Set, current, value),
			property, Write: current != value, target, new PackageSettingRecord(spec.Option, value, previous));
	}

	/// <summary>An option the package set and no longer declares: given back, unless the game has changed it since.</summary>
	private async Task<Planned> PlanReleaseAsync(PackageSettingRecord mine)
	{
		if (ConfigOptionWriter.PropertyOf(mine.Option) is not { } property)
		{
			return new Planned(new PackageSettingChange(mine.Option, PackageSettingAction.Release, null, null,
				"it is no longer a configuration option on this server."), "", Write: false, null, Record: null);
		}

		var current = await config.CurrentTextAsync(property);
		if (current != mine.Value)
		{
			return new Planned(new PackageSettingChange(mine.Option, PackageSettingAction.Release, current, current,
				"the game has changed it since the package set it."), property, Write: false, null, Record: null);
		}

		var previous = Parsed(property, mine.Previous);
		return new Planned(new PackageSettingChange(mine.Option, PackageSettingAction.Restore, current,
				ConfigOptionWriter.FormatValue(property, previous)),
			property, Write: true, previous, Record: null);
	}

	/// <summary>The value as <c>@config/set</c> takes it: an object ref becomes its dbref, or null while the object does not exist.</summary>
	private static string? ResolveValue(string value, Func<PackageRef, string?> resolve)
	{
		if (PackageRefScanner.ParseSingle(value) is not { } reference)
		{
			return value;
		}

		return resolve(reference) is { } objid && HelperFunctions.ParseDbRef(objid) is DBRef dbref
			? $"#{dbref.Number}"
			: null;
	}

	/// <summary>A value as <see cref="ConfigOptionWriter.FormatValue"/> wrote it, read back; null stays null.</summary>
	private static object? Parsed(string property, string? text)
		=> text is not null && ConfigOptionWriter.TryParseValue(property, text, out var value) ? value : null;

	private static Planned Blocked(string option, string property, string? current, string? value, string detail)
		=> new(new PackageSettingChange(option, PackageSettingAction.Blocked, current, value, detail),
			property, Write: false, null, Record: null);

	private static string Shown(string? value) => value ?? "nothing";
}
