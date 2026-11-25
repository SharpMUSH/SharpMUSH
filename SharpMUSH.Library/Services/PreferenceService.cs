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
			readings.Add(Reading(key, await LeafAsync(holder, group.AttributePath(key), cancellationToken)));
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
			if (group.Spec.Find(name) is not PackagePreferenceSpec key)
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

		if (await ProtectedAsync(group, holder, accepted.Select(pair => group.AttributePath(pair.Key)), false, cancellationToken)
			is Error<string> locked)
		{
			return locked;
		}

		var writer = await WriterAsync(holder, cancellationToken);
		var noCommand = await mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.FirstOrDefaultAsync(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase), cancellationToken);
		foreach (var (key, value) in accepted)
		{
			var path = group.AttributePath(key);
			await mediator.Send(new SetAttributeCommand(holder, path, MarkupText.Plain(value), writer), cancellationToken);
			if (noCommand is not null && await LeafAsync(holder, path, CancellationToken.None) is SharpAttribute leaf
				&& !leaf.Flags.Any(flag => flag.Name.Equals(NoCommand, StringComparison.OrdinalIgnoreCase)))
			{
				// Not cancellable: once the value is stored it must not be left without NO_COMMAND.
				await mediator.Send(new SetAttributeFlagCommand(holder, leaf, noCommand), CancellationToken.None);
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

		foreach (var path in wiped)
		{
			await mediator.Send(new WipeAttributeCommand(holder, path), cancellationToken);
		}

		return Result(await ReadAsync(group, holder, cancellationToken));
	}

	private static Result<IReadOnlyList<PreferenceReading>> Result(IReadOnlyList<PreferenceReading> readings) => new(readings);

	private static PreferenceReading Reading(PackagePreferenceSpec key, Found<SharpAttribute> stored)
		=> stored is SharpAttribute leaf && Preferences.Check(key, leaf.Value.ToPlainText()) is string value
			? new PreferenceReading(key, value, true)
			: new PreferenceReading(key, key.Default ?? string.Empty, false);

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
		if (await WhoWritesAsync(holder, cancellationToken) is not { } who)
		{
			return new Success();
		}

		var (character, target) = who;

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
				var chain = await mediator.CreateStream(new GetAttributeQuery(holder, checkedPath), cancellationToken)
					.ToArrayAsync(cancellationToken);
				if (!await permissions.CanSet(character, target, chain))
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
	/// Whom a change to <paramref name="holder"/> is checked as, and what it is checked against: the character itself
	/// for a character's preference; for an account's, the account's holder as one of its characters, since each of
	/// them may write the account's attributes. Nothing when there is no such character.
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
