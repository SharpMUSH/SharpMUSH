using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Models.RecurringJobs;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.RecurringJobs;

namespace SharpMUSH.Library.Services;

/// <inheritdoc />
public sealed class PackageDeclarationService(
	IRoleRegistryService roles,
	IRecurringJobService jobs,
	IPackageRegistryService packages,
	IAccountClaimsInvalidator claims,
	IMediator mediator,
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
		Func<PackageRef, string?> resolve,
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

		var definitions = new List<PackageJobDefinition>();
		foreach (var job in plan.Jobs)
		{
			if (resolve(job.Target) is not { } target)
			{
				return new Error<string>($"Job {job.Ref}: its target {job.Target} does not resolve to an object.");
			}

			definitions.Add(new PackageJobDefinition(job.Ref, target, job.Attribute, job.Schedule, job.TimeZone, job.Description));
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

		try
		{
			var previous = await jobs.SetPackageJobsAsync(packageId, definitions, cancellationToken);
			writes.Track($"{packageId} jobs", () => jobs.RestorePackageJobsAsync(packageId, previous, CancellationToken.None));
		}
		catch (RecurringJobException ex)
		{
			return new Error<string>($"Jobs: {ex.Message}");
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
			await roles.RemoveCustomPermissionAsync(scope);
			writes.Track($"permission {scope}", () => roles.UpsertCustomPermissionAsync(previous));
		}

		foreach (var (kind, name) in plan.CategoryRemovals)
		{
			if (await FindCategoryAsync(kind, name, cancellationToken) is not { } previous) continue;
			await roles.RemoveCategoryAsync(kind, name);
			writes.Track($"{kind} category {name}", () => roles.UpsertCategoryAsync(kind, previous));
		}

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
		foreach (var package in await packages.GetInstalledPackagesAsync())
		{
			if (package.Id == packageId || package.Owned is not { } theirs) continue;
			foreach (var permission in theirs.Permissions) otherOwners[(PackageDeclarationKind.Permission, permission.Name)] = package.Id;
			foreach (var role in theirs.Roles) otherOwners[(PackageDeclarationKind.Role, role.Slug)] = package.Id;
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
			await jobs.GetPackageJobsAsync(packageId, cancellationToken));
		return PackageDeclarationPlanner.Plan(declared, owned, live, _clock.GetUtcNow().ToUnixTimeMilliseconds());
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
