using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PreferenceService(
	IPackageRegistryService registry,
	IAccountService accounts,
	IMediator mediator) : IPreferenceService
{
	/// <summary>Every value is written NO_COMMAND, so text a player chose can never be read as a $-command.</summary>
	private const string NoCommand = "NO_COMMAND";

	public async ValueTask<IReadOnlyList<PreferenceGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
		=> (await registry.GetInstalledPackagesAsync())
			.SelectMany(package => (package.Preferences ?? []).Select(spec => new PreferenceGroup(package.Id, spec)))
			.OrderBy(group => group.Id, StringComparer.Ordinal)
			.ToList();

	public async ValueTask<Found<PreferenceGroup>> FindGroupAsync(string groupId, CancellationToken cancellationToken = default)
	{
		if (Preferences.ParseGroupId(groupId) is not PreferenceGroupRef reference
			|| await registry.GetInstalledPackageAsync(reference.Package) is not InstalledPackageRecord package)
		{
			return new NotFound();
		}

		return package.Preferences?.FirstOrDefault(spec => spec.Name == reference.Group) is { } found
			? new PreferenceGroup(package.Id, found)
			: new NotFound();
	}

	public async ValueTask<Result<DBRef>> HolderAsync(PreferenceGroup group, string? accountId, SharpPlayer? character,
		CancellationToken cancellationToken = default)
	{
		if (group.Spec.Scope == PreferenceScope.Character)
		{
			return character is null
				? new Error<string>("These preferences are kept on a character; choose one first.")
				: character.Object.DBRef;
		}

		accountId ??= character is null
			? null
			: (await accounts.GetAccountForCharacterAsync(character.Object.DBRef, cancellationToken))?.Id;
		return AccountHolders.ForAccount(accountId) is DBRef holder
			? holder
			: new Error<string>("These preferences are kept on an account, and this character is on none.");
	}

	public async ValueTask<IReadOnlyList<PreferenceReading>> ReadAsync(PreferenceGroup group, DBRef holder,
		CancellationToken cancellationToken = default)
	{
		var readings = new List<PreferenceReading>(group.Spec.Keys.Count);
		foreach (var key in group.Spec.Keys)
		{
			readings.Add(Reading(key, await StoredAsync(holder, group.AttributePath(key), cancellationToken)));
		}

		return readings;
	}

	public async ValueTask<Result<IReadOnlyList<PreferenceReading>>> WriteAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
	{
		// Every value is checked before anything is written, so a refusal leaves the group as it was.
		var accepted = new List<(PackagePreferenceSpec Key, string Value)>(values.Count);
		foreach (var (name, value) in values)
		{
			if (group.Spec.Find(name) is not { } key)
			{
				return new Error<string>($"{group.Id} has no preference '{name}'.");
			}

			switch (Preferences.Check(key, value))
			{
				case string stored:
					accepted.Add((key, stored));
					break;
				case Error<string> refused:
					return refused;
			}
		}

		var writer = await WriterAsync(holder, cancellationToken);
		var noCommand = await mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.FirstOrDefaultAsync(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase), cancellationToken);
		foreach (var (key, value) in accepted)
		{
			var path = group.AttributePath(key);
			await mediator.Send(new SetAttributeCommand(holder, path, MarkupText.Plain(value), writer), cancellationToken);
			if (noCommand is not null && await LeafAsync(holder, path, cancellationToken) is { } leaf
				&& !leaf.Flags.Any(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase)))
			{
				await mediator.Send(new SetAttributeFlagCommand(holder, leaf, noCommand), cancellationToken);
			}
		}

		return Result(await ReadAsync(group, holder, cancellationToken));
	}

	public async ValueTask<Result<IReadOnlyList<PreferenceReading>>> ResetAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
	{
		var reset = new List<PackagePreferenceSpec>();
		foreach (var name in keys)
		{
			if (group.Spec.Find(name) is not { } key)
			{
				return new Error<string>($"{group.Id} has no preference '{name}'.");
			}

			reset.Add(key);
		}

		if (reset.Count == 0)
		{
			// The whole group: its branch, with any value a package that no longer declares it left behind.
			await mediator.Send(new WipeAttributeCommand(holder, Preferences.GroupAttributePath(group.PackageId, group.Spec.Name)),
				cancellationToken);
		}
		else
		{
			foreach (var key in reset)
			{
				await mediator.Send(new WipeAttributeCommand(holder, group.AttributePath(key)), cancellationToken);
			}
		}

		return Result(await ReadAsync(group, holder, cancellationToken));
	}

	private static Result<IReadOnlyList<PreferenceReading>> Result(IReadOnlyList<PreferenceReading> readings) => new(readings);

	private static PreferenceReading Reading(PackagePreferenceSpec key, string? stored)
		=> stored is not null && Preferences.Check(key, stored) is string value
			? new PreferenceReading(key, value, true)
			: new PreferenceReading(key, key.Default ?? string.Empty, false);

	private async ValueTask<string?> StoredAsync(DBRef holder, string[] path, CancellationToken cancellationToken)
		=> (await LeafAsync(holder, path, cancellationToken))?.Value.ToPlainText();

	/// <summary>The attribute at <paramref name="path"/> itself; null when it, or a branch above it, is not there.</summary>
	private async ValueTask<SharpAttribute?> LeafAsync(DBRef holder, string[] path, CancellationToken cancellationToken)
		=> await mediator.CreateStream(new GetAttributeQuery(holder, path), cancellationToken)
			.LastOrDefaultAsync(cancellationToken) is { } leaf
			&& leaf.LongName.Equals(string.Join('`', path), StringComparison.OrdinalIgnoreCase)
				? leaf
				: null;

	/// <summary>
	/// Who a written value is recorded as owned by: the character, for a character's own preference; God for an
	/// account's, as for every attribute on an account's holder.
	/// </summary>
	private async ValueTask<SharpPlayer> WriterAsync(DBRef holder, CancellationToken cancellationToken)
		=> !holder.IsAccountHolder() && await mediator.Send(new GetObjectNodeQuery(holder), cancellationToken) is AnySharpObject and SharpPlayer player
			? player
			: await HelperFunctions.GetGod(mediator) is SharpPlayer god
				? god
				: throw new InvalidOperationException("God (#1) is not a player, so an account preference has no writer.");
}
