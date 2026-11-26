using SharpMUSH.Library.Extensions;
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
	IPermissionService permissions,
	IMediator mediator) : IPreferenceService
{
	/// <summary>Every value is written NO_COMMAND, so text a player chose can never be read as a $-command.</summary>
	private const string NoCommand = "NO_COMMAND";

	public async ValueTask<IReadOnlyList<PreferenceGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
		=> (await registry.GetInstalledPackagesAsync())
			.Where(package => package.Id != BuiltInPreferences.PackageId)
			.SelectMany(package => (package.Preferences ?? []).Select(spec => new PreferenceGroup(package.Id, spec)))
			.Concat(BuiltInPreferences.Groups)
			.OrderBy(group => group.Id, StringComparer.Ordinal)
			.ToList();

	public async ValueTask<Found<PreferenceGroup>> FindGroupAsync(string groupId, CancellationToken cancellationToken = default)
	{
		if (Preferences.ParseGroupId(groupId) is not PreferenceGroupRef reference)
		{
			return new NotFound();
		}

		if (reference.Package == BuiltInPreferences.PackageId)
		{
			return BuiltInPreferences.Find(reference) is { } builtIn ? builtIn : new NotFound();
		}

		if (await registry.GetInstalledPackageAsync(reference.Package) is not InstalledPackageRecord package)
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
			readings.Add(Reading(key, await LeafAsync(holder, group.AttributePath(key), cancellationToken)));
		}

		return readings;
	}

	public ValueTask<Result<IReadOnlyList<PreferenceReading>>> WriteAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
		=> WriteAsync(group, holder, values, new Dictionary<string, int>(), cancellationToken);

	public async ValueTask<Result<IReadOnlyList<PreferenceReading>>> WriteAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, int> versions,
		CancellationToken cancellationToken = default)
	{
		var writtenAt = new Dictionary<string, int>(versions, StringComparer.OrdinalIgnoreCase);
		// Every value is checked before anything is written, so a refusal leaves the group as it was.
		var accepted = new List<(PackagePreferenceSpec Key, string Value)>(values.Count);
		foreach (var (name, value) in values)
		{
			if (group.Spec.Find(name) is not PackagePreferenceSpec key)
			{
				return new Error<string>($"{group.Id} has no preference '{name}'.");
			}

			var version = writtenAt.GetValueOrDefault(name, key.Version);
			if (version < 1 || version > key.Version)
			{
				return new Error<string>($"{group.Id}/{key.Key} is at version {key.Version}; a value cannot be written at version {version}.");
			}

			switch (Preferences.Check(key, value))
			{
				case string checkedValue:
					accepted.Add((key, Preferences.Store(key with { Version = version }, checkedValue)));
					break;
				case Error<string> refused:
					return refused;
			}
		}

		if (await ProtectedAsync(group, holder, accepted.Select(pair => group.AttributePath(pair.Key)), false, cancellationToken)
			is Error<string> locked)
		{
			return locked;
		}

		var writer = await WriterAsync(holder, cancellationToken);
		var noCommand = await mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.FirstOrDefaultAsync(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase), cancellationToken);
		// Not cancellable from here: a request that stopped between two keys would leave the group half written.
		foreach (var (key, value) in accepted)
		{
			var path = group.AttributePath(key);
			await mediator.Send(new SetAttributeCommand(holder, path, MarkupText.Plain(value), writer), CancellationToken.None);
			if (noCommand is not null && await LeafAsync(holder, path, CancellationToken.None) is SharpAttribute leaf
				&& !leaf.Flags.Any(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase)))
			{
				// Not cancellable: once the value is stored it must not be left without NO_COMMAND.
				await mediator.Send(new SetAttributeFlagCommand(holder, leaf, noCommand), CancellationToken.None);
			}
		}

		return Result(await ReadAsync(group, holder, CancellationToken.None));
	}

	public async ValueTask<Result<IReadOnlyList<PreferenceReading>>> ResetAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
	{
		var reset = new List<PackagePreferenceSpec>();
		foreach (var name in keys)
		{
			if (group.Spec.Find(name) is not PackagePreferenceSpec key)
			{
				return new Error<string>($"{group.Id} has no preference '{name}'.");
			}

			reset.Add(key);
		}

		// The whole group is its branch, with any value a package that no longer declares it left behind.
		var wiped = reset.Count == 0
			? [Preferences.GroupAttributePath(group.PackageId, group.Spec.Name)]
			: reset.Select(group.AttributePath).ToList();
		if (await ProtectedAsync(group, holder, wiped, true, cancellationToken) is Error<string> locked)
		{
			return locked;
		}

		// Not cancellable from here, as a write is not.
		foreach (var path in wiped)
		{
			await mediator.Send(new WipeAttributeCommand(holder, path), CancellationToken.None);
		}

		return Result(await ReadAsync(group, holder, CancellationToken.None));
	}

	private static Result<IReadOnlyList<PreferenceReading>> Result(IReadOnlyList<PreferenceReading> readings) => new(readings);

	private static PreferenceReading Reading(PackagePreferenceSpec key, Found<SharpAttribute> stored)
		=> stored is SharpAttribute leaf && Preferences.Read(key, leaf.Value.ToPlainText()) is StoredPreference value
			? new PreferenceReading(key, value.Value, true) { Version = value.Version }
			: new PreferenceReading(key, key.Default ?? string.Empty, false) { Version = key.Version };

	/// <summary>The attribute at <paramref name="path"/> itself; not found when it, or a branch above it, is not there.</summary>
	private async ValueTask<Found<SharpAttribute>> LeafAsync(DBRef holder, string[] path, CancellationToken cancellationToken)
		=> await mediator.CreateStream(new GetAttributeQuery(holder, path), cancellationToken)
			.LastOrDefaultAsync(cancellationToken) is { } leaf
			&& leaf.LongName.Equals(string.Join('`', path), StringComparison.OrdinalIgnoreCase)
				? leaf
				: new NotFound();

	/// <summary>
	/// Refuses a change the player could not make with <c>&amp;</c>: a value staff or a package protected (a wizard or
	/// locked attribute, or a branch above it so flagged, or one the attribute table locks). With
	/// <paramref name="branches"/>, every attribute below each path is checked too, since a reset removes them all.
	/// </summary>
	private async ValueTask<Result<Success>> ProtectedAsync(PreferenceGroup group, DBRef holder,
		IEnumerable<string[]> paths, bool branches, CancellationToken cancellationToken)
	{
		var who = await WhoWritesAsync(holder, cancellationToken);
		var groupName = string.Join('`', Preferences.GroupAttributePath(group.PackageId, group.Spec.Name));
		foreach (var path in paths)
		{
			var below = branches
				? await mediator.CreateStream(new GetAttributesQuery(holder, $"{string.Join('`', path)}`**", false,
						IAttributeService.AttributePatternMode.Wildcard), cancellationToken)
					.Select(found => found.Attribute.LongName.Split('`'))
					.ToListAsync(cancellationToken)
				: [];
			foreach (var checkedPath in below.Prepend(path))
			{
				var chain = await ChainAsync(holder, checkedPath, cancellationToken);
				var allowed = who is var (character, target)
					? await permissions.CanSet(character, target, chain)
					: await UnlockedAsync(chain, cancellationToken);
				if (!allowed)
				{
					var name = string.Join('`', checkedPath);
					var key = name.Length > groupName.Length ? name[(groupName.Length + 1)..].ToLowerInvariant() : string.Empty;
					return new Error<string>(key.Length > 0
						? $"{group.Id}/{key} is protected, so it cannot be changed here."
						: $"{group.Id} is protected, so it cannot be changed here.");
				}
			}
		}

		return new Success();
	}

	/// <summary>
	/// Every level of <paramref name="path"/> as the write would find it: the stored level where it exists, else the
	/// level the attribute table would create (its default flags), as <c>&amp;</c> judges a new attribute.
	/// </summary>
	private async ValueTask<SharpAttribute[]> ChainAsync(DBRef holder, string[] path, CancellationToken cancellationToken)
	{
		// A path resolves only whole, so the stored levels are those of its longest stored prefix.
		var chain = new List<SharpAttribute>();
		for (var length = path.Length; length > 0 && chain.Count == 0; length--)
		{
			chain = await mediator.CreateStream(new GetAttributeQuery(holder, path[..length]), cancellationToken)
				.ToListAsync(cancellationToken);
		}

		for (var level = chain.Count; level < path.Length; level++)
		{
			var levelName = string.Join('`', path[..(level + 1)]).ToUpperInvariant();
			if (await mediator.Send(new GetAttributeEntryQuery(levelName), cancellationToken) is { } entry)
			{
				chain.Add(AttributeWriter.CreatedAttributeFor(entry, path[level].ToUpperInvariant(), levelName,
					await WriterAsync(holder, cancellationToken)));
			}
		}

		return [.. chain];
	}

	/// <summary>
	/// For an account with no character to check as: whether no level of <paramref name="chain"/> is flagged against a
	/// player's write (wizard, locked, safe, internal, nodump) or has a write key in the attribute table.
	/// </summary>
	private async ValueTask<bool> UnlockedAsync(SharpAttribute[] chain, CancellationToken cancellationToken)
	{
		foreach (var level in chain)
		{
			if (level.IsWizard() || level.IsLocked() || level.IsSafe() || level.IsInternal() || level.IsNoDump()
				|| await mediator.Send(new GetAttributeEntryQuery(level.LongName.ToUpperInvariant()), cancellationToken) is { WriteLock: not null })
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Whom a change to <paramref name="holder"/> is checked as, and what it is checked against: the character itself
	/// for a character's preference; for an account's, the account's holder as one of its characters, since each of
	/// them may write the account's attributes. Nothing when there is no such character; the attributes' own locks are checked then.
	/// </summary>
	private async ValueTask<(AnySharpObject Writer, AnySharpObject Target)?> WhoWritesAsync(DBRef holder,
		CancellationToken cancellationToken)
	{
		if (!holder.IsAccountHolder())
		{
			return await mediator.Send(new GetObjectNodeQuery(holder), cancellationToken) is AnySharpObject character
				? (character, character)
				: null;
		}

		if (await mediator.Send(new GetAccountHolderQuery(holder), cancellationToken) is not SharpAccountHolder account)
		{
			return null;
		}

		foreach (var number in (await account.Characters.WithCancellation(cancellationToken)).Order())
		{
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), cancellationToken) is AnySharpObject character)
			{
				return (character, new AnySharpObject(account));
			}
		}

		return null;
	}

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
