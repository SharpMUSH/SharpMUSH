using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageDeclarationService(
	IRoleRegistryService roles,
	IPackageRegistryService packages,
	IAccountClaimsInvalidator claims,
	IMediator mediator,
	IAttributeIndexBuilder indexBuilder,
	TimeProvider? clock = null) : IPackageDeclarationService
{
	private readonly TimeProvider _clock = clock ?? TimeProvider.System;

	public async Task<IReadOnlyList<PackageDeclarationChange>> PlanAsync(
		string packageId, PackageDeclarations declared, PackageDeclarations? owned, CancellationToken cancellationToken = default)
		=> (await PlanCoreAsync(packageId, declared, owned, cancellationToken)).Changes;

	public async Task<Result<PackageDeclarations>> ApplyAsync(
		PackageWriteTransaction writes,
		string packageId,
		PackageDeclarations declared,
		PackageDeclarations? owned,
		List<string> notes,
		CancellationToken cancellationToken = default)
	{
		var plan = await PlanCoreAsync(packageId, declared, owned, cancellationToken);
		if (plan.IsBlocked)
		{
			return new Error<string>(string.Join(" ", plan.Changes
				.Where(c => c.Action == PackageDeclarationAction.Blocked)
				.Select(c => $"{c.Kind.Noun()} {c.Name}: {c.Detail}")));
		}

		// Pushed first so that it runs last when the operation is undone: the cached grants must not keep
		// what the undo takes away.
		writes.Track($"{packageId} grants cache", () => InvalidateAsync([], CancellationToken.None));

		foreach (var (kind, category) in plan.CategoryWrites)
		{
			var previous = await FindCategoryAsync(kind, category.Name, cancellationToken);
			await roles.UpsertCategoryAsync(kind, category);
			writes.Track($"{kind} category {category.Name}", () => previous is null
				? roles.RemoveCategoryAsync(kind, category.Name)
				: roles.UpsertCategoryAsync(kind, previous));
		}

		foreach (var permission in plan.PermissionWrites)
		{
			var previous = (await roles.GetCustomPermissionsAsync(cancellationToken)).FirstOrDefault(p => p.Scope == permission.Scope);
			await roles.UpsertCustomPermissionAsync(permission);
			writes.Track($"permission {permission.Scope}", () => previous is null
				? roles.RemoveCustomPermissionAsync(permission.Scope)
				: roles.UpsertCustomPermissionAsync(previous));
		}

		var holders = new HashSet<string>(StringComparer.Ordinal);
		foreach (var role in plan.RoleWrites)
		{
			var previous = await roles.GetRoleAsync(role.Slug, cancellationToken) is SharpRole found ? found : null;
			await roles.UpsertRoleAsync(role);
			writes.Track($"role {role.Slug}", () => previous is null ? roles.RemoveRoleAsync(role.Slug) : roles.UpsertRoleAsync(previous));
			holders.UnionWith(await roles.GetAccountIdsForRoleAsync(role.Slug));
		}

		// Removals last: a removed permission takes every override of it along, which no undo brings back.
		foreach (var slug in plan.RoleRemovals)
		{
			if (await roles.GetRoleAsync(slug, cancellationToken) is not SharpRole previous) continue;
			await roles.RemoveRoleAsync(slug);
			writes.Track($"role {slug}", () => roles.UpsertRoleAsync(previous));
		}

		foreach (var scope in plan.PermissionRemovals)
		{
			if ((await roles.GetCustomPermissionsAsync(cancellationToken)).FirstOrDefault(p => p.Scope == scope) is not { } previous) continue;
			var overrides = await roles.GetOverridesOfAsync(scope, cancellationToken);
			await roles.RemoveCustomPermissionAsync(scope);
			writes.Track($"permission {scope}", () => RestorePermissionAsync(previous, overrides));
		}

		foreach (var (kind, name) in plan.CategoryRemovals)
		{
			if (await FindCategoryAsync(kind, name, cancellationToken) is not { } previous) continue;
			await roles.RemoveCategoryAsync(kind, name);
			writes.Track($"{kind} category {name}", () => roles.UpsertCategoryAsync(kind, previous));
		}

		await ApplyAttributeEntriesAsync(writes, plan, notes, cancellationToken);

		await InvalidateAsync(holders, cancellationToken);
		notes.AddRange(plan.Changes
			.Where(c => c.Action is not (PackageDeclarationAction.NoChange or PackageDeclarationAction.Existing))
			.Select(c => $"{Verb(c.Action)} {c.Kind.Noun()} {c.Name}{(c.Detail is null ? "." : $": {c.Detail}")}"));
		return plan.Owned;
	}

	private async Task<PackageDeclarationPlan> PlanCoreAsync(
		string packageId, PackageDeclarations declared, PackageDeclarations? owned, CancellationToken cancellationToken)
	{
		var otherOwners = new Dictionary<(PackageDeclarationKind, string), string>();
		foreach (var package in (await packages.GetInstalledPackagesAsync()).Where(p => p.Id != packageId))
		{
			foreach (var permission in package.Owned?.Permissions ?? []) otherOwners[(PackageDeclarationKind.Permission, permission.Name)] = package.Id;
			foreach (var role in package.Owned?.Roles ?? []) otherOwners[(PackageDeclarationKind.Role, role.Slug)] = package.Id;
			foreach (var entry in package.Owned?.AttributeEntries ?? []) otherOwners[(PackageDeclarationKind.Attribute, entry.Name)] = package.Id;
		}

		var held = new HashSet<string>(StringComparer.Ordinal);
		foreach (var role in owned?.Roles ?? [])
		{
			if ((await roles.GetAccountIdsForRoleAsync(role.Slug)).Count > 0
				|| (await roles.GetObjectsForRoleAsync(role.Slug, cancellationToken)).Count > 0)
			{
				held.Add(role.Slug);
			}
		}

		var live = new PackageDeclarationLiveState(
			await roles.GetCategoriesAsync(CategoryKind.Role, cancellationToken),
			await roles.GetCategoriesAsync(CategoryKind.Permission, cancellationToken),
			await roles.GetCustomPermissionsAsync(cancellationToken),
			await roles.GetRolesAsync(cancellationToken),
			held,
			otherOwners,
			declared.AttributeEntries.Count > 0 || owned?.AttributeEntries.Count > 0
				? await mediator.CreateStream(new GetAllAttributeEntriesQuery(), cancellationToken).ToListAsync(cancellationToken)
				: [],
			declared.AttributeEntries.Count > 0
				? await mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken).Select(f => f.Name.ToLowerInvariant()).ToHashSetAsync(cancellationToken: cancellationToken)
				: null,
			await AttributeCopiesAsync(declared.AttributeEntries.Where(e => e.Retroactive).Select(e => e.Name).ToArray(), cancellationToken),
			await IndexCopiesAsync(declared.AttributeEntries, cancellationToken));
		return PackageDeclarationPlanner.Plan(declared, owned, live, _clock.GetUtcNow().ToUnixTimeMilliseconds());
	}

	/// <summary>
	/// Writes and deletes the attribute entries the plan names, then gives each retroactive entry's default flags to
	/// every copy of it already on an object. A copy's <c>branch</c> flag stays, and so does its owner: unlike
	/// <c>@attribute/access/retroactive</c>, a package does not make itself the creator of the game's attributes.
	/// </summary>
	private async Task ApplyAttributeEntriesAsync(
		PackageWriteTransaction writes, PackageDeclarationPlan plan, List<string> notes, CancellationToken cancellationToken)
	{
		foreach (var entry in plan.AttributeEntryWrites.Select(write => write.Entry))
		{
			var previous = await mediator.Send(new GetAttributeEntryQuery(entry.Name), cancellationToken);
			// Upper case, as @attribute/access stores them, so @attribute shows one spelling.
			await mediator.Send(new CreateAttributeEntryCommand(entry.Name, [.. entry.Flags.Select(flag => flag.ToUpperInvariant())], entry.Limit,
				entry.Enum is null ? null : [.. entry.Enum], entry.EnumDelimiter), cancellationToken);
			writes.Track($"attribute entry {entry.Name}", () => RestoreAttributeEntryAsync(entry.Name, previous));
			await PutIndexAsync(entry.Name, previous?.Index?.Options, entry.Index, cancellationToken);
			await PutWatchAsync(entry.Name, previous?.Watch, entry.Watch, cancellationToken);
		}

		foreach (var name in plan.AttributeEntryRemovals)
		{
			if (await mediator.Send(new GetAttributeEntryQuery(name), cancellationToken) is not { } previous) continue;
			await mediator.Send(new DeleteAttributeEntryCommand(name), cancellationToken);
			writes.Track($"attribute entry {name}", () => RestoreAttributeEntryAsync(name, previous));
		}

		var retroactive = plan.AttributeEntryWrites.Where(w => w.Retroactive).Select(w => w.Entry).ToArray();
		if (retroactive.Length == 0)
		{
			return;
		}

		var known = await mediator.CreateStream(new GetAttributeFlagsQuery(), cancellationToken)
			.ToDictionaryAsync(f => f.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
		var changed = retroactive.ToDictionary(e => e.Name, _ => 0, StringComparer.Ordinal);
		await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery(), cancellationToken).WithCancellation(cancellationToken))
		{
			foreach (var entry in retroactive)
			{
				var path = entry.Name.Split('`');
				var copy = await mediator.CreateStream(new GetAttributeQuery(obj.DBRef, path), cancellationToken).LastOrDefaultAsync(cancellationToken);
				if (copy is null || !copy.LongName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var unset = copy.Flags.Where(had => !had.Name.Equals("branch", StringComparison.OrdinalIgnoreCase)
					&& !entry.Flags.Contains(had.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
				var set = entry.Flags.Where(wanted => !copy.Flags.Any(had => had.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
					.Select(wanted => known.GetValueOrDefault(wanted)).OfType<SharpAttributeFlag>().ToArray();
				if (unset.Length == 0 && set.Length == 0)
				{
					continue;
				}

				foreach (var flag in unset) await writes.UnsetAttributeFlagAsync(obj.DBRef, path, flag, cancellationToken);
				foreach (var flag in set) await writes.SetAttributeFlagAsync(obj.DBRef, path, flag, cancellationToken);
				changed[entry.Name]++;
			}
		}

		notes.AddRange(changed.Select(c => $"Gave {c.Key}'s default flags to {c.Value} existing {(c.Value == 1 ? "copy" : "copies")} of it."));
	}

	/// <summary>Puts an attribute entry back as it was before an apply: as it stood, or gone when there was none.</summary>
	private async Task RestoreAttributeEntryAsync(string name, SharpAttributeEntry? previous)
	{
		if (previous is null)
		{
			await mediator.Send(new DeleteAttributeEntryCommand(name));
			return;
		}

		var now = await mediator.Send(new GetAttributeEntryQuery(name));
		await mediator.Send(new CreateAttributeEntryCommand(previous.Name, previous.DefaultFlags, previous.Limit, previous.Enum, previous.EnumDelimiter));
		await PutIndexAsync(previous.Name, now?.Index?.Options, previous.Index?.Options, CancellationToken.None);
		await PutWatchAsync(previous.Name, now?.Watch, previous.Watch, CancellationToken.None);
	}

	/// <summary>Moves an entry's <c>@attribute/watch</c> pattern from <paramref name="had"/> to <paramref name="wanted"/>.</summary>
	private async Task PutWatchAsync(string name, string? had, string? wanted, CancellationToken cancellationToken)
	{
		if (had != wanted)
		{
			await mediator.Send(new SetAttributeWatchCommand(name, wanted), cancellationToken);
		}
	}

	/// <summary>
	/// Moves an entry's index from <paramref name="had"/> to <paramref name="wanted"/>: takes it off, or puts it on and
	/// starts its build in the background. Queries on it answer <c>#-1 INDEX BUILDING</c> until the build is done.
	/// </summary>
	private async Task PutIndexAsync(string name, AttributeIndexOptions? had, AttributeIndexOptions? wanted,
		CancellationToken cancellationToken)
	{
		if (had == wanted)
		{
			return;
		}

		if (wanted is null)
		{
			await mediator.Send(new DropAttributeIndexCommand(name), cancellationToken);
			return;
		}

		if (await mediator.Send(new SetAttributeIndexCommand(name, wanted), cancellationToken) is SharpAttributeEntry)
		{
			indexBuilder.Build(name);
		}
	}

	/// <summary>For each declared entry with an index, how many objects hold a copy of the attribute for it to list.</summary>
	private async Task<IReadOnlyDictionary<string, long>> IndexCopiesAsync(
		IReadOnlyList<PackageAttributeEntrySpec> entries, CancellationToken cancellationToken)
	{
		var counts = new Dictionary<string, long>(StringComparer.Ordinal);
		foreach (var entry in entries)
		{
			if (entry.Index is { } index)
			{
				counts[entry.Name] = await mediator.Send(new CountAttributeCopiesQuery(entry.Name, index.Type), cancellationToken);
			}
		}

		return counts;
	}

	/// <summary>The flags, lowercase, of every copy of each named attribute on an object: one pass over the world, or none for no names.</summary>
	private async Task<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<string>>>> AttributeCopiesAsync(
		IReadOnlyList<string> names, CancellationToken cancellationToken)
	{
		var copies = names.ToDictionary(n => n, _ => new List<IReadOnlyList<string>>(), StringComparer.Ordinal);
		if (names.Count == 0)
		{
			return new Dictionary<string, IReadOnlyList<IReadOnlyList<string>>>();
		}

		await foreach (var obj in mediator.CreateStream(new GetAllObjectsQuery(), cancellationToken).WithCancellation(cancellationToken))
		{
			foreach (var name in names)
			{
				var copy = await mediator.CreateStream(new GetAttributeQuery(obj.DBRef, name.Split('`')), cancellationToken).LastOrDefaultAsync(cancellationToken);
				if (copy is not null && copy.LongName.Equals(name, StringComparison.OrdinalIgnoreCase))
				{
					copies[name].Add([.. copy.Flags.Select(f => f.Name.ToLowerInvariant())]);
				}
			}
		}

		return copies.ToDictionary(c => c.Key, c => (IReadOnlyList<IReadOnlyList<string>>)c.Value, StringComparer.Ordinal);
	}

	/// <summary>Undoes a permission removal: the definition, then every override the removal cleared.</summary>
	private async Task RestorePermissionAsync(CustomPermission permission, PermissionOverrideHolders overrides)
	{
		await roles.UpsertCustomPermissionAsync(permission);
		foreach (var (account, state) in overrides.Accounts)
		{
			await roles.SetAccountOverrideAsync(account, permission.Scope, state);
		}

		foreach (var (number, state) in overrides.Objects)
		{
			await roles.SetObjectOverrideAsync(number, permission.Scope, state);
		}
	}

	private async Task<RoleCategory?> FindCategoryAsync(CategoryKind kind, string name, CancellationToken cancellationToken)
		=> (await roles.GetCategoriesAsync(kind, cancellationToken)).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

	private async Task InvalidateAsync(IEnumerable<string> accounts, CancellationToken cancellationToken)
	{
		foreach (var account in accounts)
		{
			await claims.InvalidateAsync(account, cancellationToken);
		}

		await mediator.Send(new InvalidateGrantsCommand(null), cancellationToken);
	}

	private static string Verb(PackageDeclarationAction action) => action switch
	{
		PackageDeclarationAction.Create => "Created",
		PackageDeclarationAction.Update => "Updated",
		PackageDeclarationAction.Remove => "Removed",
		PackageDeclarationAction.Release => "Kept",
		_ => action.ToString()
	};
}
