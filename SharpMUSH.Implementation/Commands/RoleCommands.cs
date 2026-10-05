using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly string[] RoleOperations =
		["LIST", "INFO", "PLAYER", "SCOPES", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN", "DEFINE", "UNDEFINE", "CATEGORY", "CATEGORIES", "DESCRIBE"];

	/// <summary>The operations <c>@role/category/&lt;operation&gt;</c> runs on a category itself.</summary>
	private static readonly string[] CategoryOperations = ["CREATE", "DESCRIBE", "RENAME", "DELETE"];

	/// <summary>
	/// <c>@role</c>: list, inspect and manage roles, who holds them, and per-object or per-account
	/// overrides. Every change goes through <see cref="IRoleManagementService"/>, the same rules the
	/// portal applies, with the executor as the actor: an object needs no account to manage roles.
	/// </summary>
	[SharpCommand(Name = "@ROLE", Switches = ["LIST", "INFO", "PLAYER", "SCOPES", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN", "DEFINE", "UNDEFINE", "CATEGORY", "CATEGORIES", "DESCRIBE", "OBJECT", "ACCOUNT", "PERMISSION"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["role or object", "value"])]
	public async ValueTask<Option<CallState>> Role(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var allSwitches = parser.CurrentState.Switches.ToArray();
		var switches = allSwitches.Where(RoleOperations.Contains).ToArray();
		var holder = allSwitches.Contains("ACCOUNT") ? RoleHolder.Account
			: allSwitches.Contains("OBJECT") ? RoleHolder.Object
			: RoleHolder.Default;
		var kind = allSwitches.Contains("PERMISSION") ? CategoryKind.Permission : CategoryKind.Role;
		var args = parser.CurrentState.Arguments;
		var left = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();
		var right = (args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "").Trim();
		var hasRight = args.ContainsKey("1");

		// @role/category/create and friends act on the category itself.
		if (switches.Length == 2 && switches.Contains("CATEGORY") && switches.FirstOrDefault(CategoryOperations.Contains) is { } onCategory)
			switches = [$"CATEGORY/{onCategory}"];

		string output;
		if (switches.Length > 1)
			output = "Choose one @role operation.";
		else if (switches.FirstOrDefault() == "DESCRIBE")
			output = $"Usage: @role/category/describe {RoleUsage("CATEGORY/DESCRIBE")}. See help @role.";
		else if (allSwitches.Contains("ACCOUNT") && allSwitches.Contains("OBJECT"))
			output = "Choose /object or /account, not both.";
		else
		{
			var operation = switches.FirstOrDefault() ?? (left.Length == 0 ? "LIST" : "INFO");
			output = operation switch
			{
				"LIST" => await RoleListAsync(parser),
				"INFO" => await RoleInfoAsync(parser, left),
				"SCOPES" => await RoleScopesAsync(parser),
				"CATEGORIES" => await RoleCategoriesAsync(parser),
				"PLAYER" => await RoleExplainAsync(parser, executor, left.Length == 0 ? "me" : left),
				_ => await RoleChangeAsync(parser, executor, operation, holder, kind, left, right, hasRight)
			};
		}

		if (output.Length > 0) await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary>Who an assignment or override is for: the named role, the object, or its account.</summary>
	private enum RoleHolder
	{
		Default,
		Object,
		Account
	}

	private static async ValueTask<string> RoleListAsync(IMUSHCodeParser parser)
	{
		var roles = await RoleRegistry(parser).GetRolesAsync(ExecutionBudget.CurrentToken);
		var slugWidth = Math.Max(4, roles.Max(r => r.Slug.Length));
		var categoryWidth = Math.Max(8, roles.Max(r => r.Category.Length));
		var output = new StringBuilder("Roles, highest first:");
		foreach (var role in roles)
			output.Append($"\n{role.Priority,4}  {role.Slug.PadRight(slugWidth)}  {role.Category.PadRight(categoryWidth)}  {role.Name}{(role.IsSystem ? "  (system)" : "")}");
		return output.ToString();
	}

	private static async ValueTask<string> RoleInfoAsync(IMUSHCodeParser parser, string slug)
	{
		if (await RoleRegistry(parser).GetRoleAsync(slug, ExecutionBudget.CurrentToken) is not SharpRole role)
			return $"No role named '{slug}'. See @role/list.";
		var output = new StringBuilder($"Role: {role.Name} ({role.Slug})  Category: {role.Category}  Priority: {role.Priority}");
		if (role.Color is not null) output.Append($"  Colour: {role.Color}");
		if (role.IsSystem) output.Append(role.Slug switch
		{
			BuiltInRoles.EveryoneSlug => "  System: held by everything",
			BuiltInRoles.PlayerSlug => "  System: held by every player that is not a guest",
			BuiltInRoles.GodSlug => "  System: held by #1",
			_ when RoleFlags.ForRole(role.Slug) is { } flag => $"  System: the {flag.Name} flag",
			_ when GamePowers.ForRole(role.Slug) is { } power => $"  System: the {power.Name} power",
			_ => "  System"
		});
		output.Append($"\nAllows: {ScopeList(role.Permissions, PermissionState.Allow)}");
		output.Append($"\nDenies: {ScopeList(role.Permissions, PermissionState.Deny)}");
		return output.ToString();
	}

	private static async ValueTask<string> RoleScopesAsync(IMUSHCodeParser parser)
	{
		var output = new StringBuilder("Permissions (an umbrella also covers the scopes listed after it):");
		foreach (var scope in PortalPermission.AllScopes)
		{
			var implied = PortalPermission.ImpliedScopes(scope);
			output.Append($"\n  {scope}{(implied.Count > 0 ? "  -> " + string.Join(", ", implied) : "")}");
		}

		var custom = await RoleRegistry(parser).GetCustomPermissionsAsync(ExecutionBudget.CurrentToken);
		output.Append(custom.Count == 0 ? "\nCustom permissions: none. Add one with @role/define." : "\nCustom permissions, by category:");
		foreach (var category in custom.GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
		{
			output.Append($"\n {category.Key}");
			foreach (var permission in category)
				output.Append($"\n  {permission.Scope}{(permission.Description.Length > 0 ? "  " + permission.Description : "")}");
		}
		return output.ToString();
	}

	/// <summary><c>@role/categories</c>: both category lists, what each category holds and its description.</summary>
	private static async ValueTask<string> RoleCategoriesAsync(IMUSHCodeParser parser)
	{
		var ct = ExecutionBudget.CurrentToken;
		var registry = RoleRegistry(parser);
		var roles = await registry.GetRolesAsync(ct);
		var permissions = await registry.GetCustomPermissionsAsync(ct);
		var output = new StringBuilder();
		AppendCategories(output, "Role categories:", await registry.GetCategoriesAsync(CategoryKind.Role, ct),
			category => Counted(roles.Count(r => string.Equals(r.Category, category, StringComparison.OrdinalIgnoreCase)), "role"),
			"@role/category/create <name>=<description>");
		output.Append('\n');
		AppendCategories(output, "Permission categories:", await registry.GetCategoriesAsync(CategoryKind.Permission, ct),
			category => Counted(permissions.Count(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)), "permission"),
			"@role/category/create/permission <name>=<description>");
		return output.ToString();
	}

	private static void AppendCategories(StringBuilder output, string heading, IReadOnlyList<RoleCategory> categories,
		Func<string, string> held, string create)
	{
		output.Append(heading);
		if (categories.Count == 0)
		{
			output.Append($"\n  none. Create one with {create}.");
			return;
		}

		var width = categories.Max(c => c.Name.Length);
		foreach (var category in categories)
			output.Append($"\n  {category.Name.PadRight(width)}  {held(category.Name),-14}  {category.Description}");
	}

	private static string Counted(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

	/// <summary><c>@role/player &lt;object&gt;</c>: every role the object holds and where from, its overrides, and what they resolve to.</summary>
	private async ValueTask<string> RoleExplainAsync(IMUSHCodeParser parser, AnySharpObject executor, string name)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All)
				is not AnySharpObject target)
			return "";

		var capabilities = Capabilities(parser);
		var mine = await capabilities.GetObjectGrantsAsync(executor, ct);
		if (!target.Object().DBRef.Equals(executor.Object().DBRef)
				&& !mine.Has(PortalPermission.PlayersView)
				&& !mine.Has(PortalPermission.RolesAdmin)
				&& !await PermissionService.CanExamine(executor, target))
			return $"Seeing another object's permissions needs the {PortalPermission.PlayersView} permission.";

		var grants = await capabilities.GetObjectGrantsAsync(target, ct);
		var account = target.IsPlayer ? await AccountService.GetAccountForCharacterAsync(target.Object().DBRef, ct) : null;
		var output = new StringBuilder(target.Object().Name);
		if (account is not null) output.Append($" (account {account.Username})");
		if (grants.IsOwner) output.Append(" is the owner and holds every permission.");
		var roles = RoleHierarchy.Ranked(grants.Roles.Select(held => held.Role).DistinctBy(role => role.Slug).ToArray())
			.Select(role => $"{role.Name} ({role.Priority}{SourceNote(grants, role.Slug)})")
			.ToArray();
		output.Append($"\nRoles: {(roles.Length > 0 ? string.Join(", ", roles) : "none")}, plus everyone");
		output.Append($"\nOverrides: allow {ScopeList(grants.Context.ObjectOverrides, PermissionState.Allow)}; deny {ScopeList(grants.Context.ObjectOverrides, PermissionState.Deny)}");
		if (account is not null)
			output.Append($"\nAccount overrides: allow {ScopeList(grants.Context.Overrides, PermissionState.Allow)}; deny {ScopeList(grants.Context.Overrides, PermissionState.Deny)}");
		var scopes = PortalPermission.AllScopes.Concat(grants.Context.CustomScopes).ToArray();
		output.Append($"\nHolds: {Joined(scopes.Where(grants.Has))}");
		output.Append($"\nLacks: {Joined(scopes.Where(scope => !grants.Has(scope)))}");
		return output.ToString();
	}

	private static string SourceNote(ObjectGrants grants, string slug)
	{
		var sources = grants.Roles.Where(held => held.Role.Slug == slug).Select(held => held.Source).ToHashSet();
		return sources.Contains(RoleSource.Account) && !sources.Contains(RoleSource.Object) ? ", account" : "";
	}

	private async ValueTask<string> RoleChangeAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		RoleHolder holder, CategoryKind kind, string left, string right, bool hasRight)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (left.Length == 0 || (operation is not ("DELETE" or "UNDEFINE" or "CATEGORY/DELETE") && !hasRight))
			return $"Usage: @role/{operation.ToLowerInvariant()} {RoleUsage(operation)}. See help @role.";
		var management = parser.ServiceProvider.GetRequiredService<IRoleManagementService>();
		RoleActor actor = executor;

		switch (operation)
		{
			case "CREATE":
				var (category, name) = CategoryAndRest(right);
				return Done(await management.SaveRoleAsync(actor,
					new RoleDraft(left.ToLowerInvariant(), name.Length > 0 ? name : left, category, null, 1, new Dictionary<string, PermissionState>()), ct),
					role => $"Role {role.Name} ({role.Slug}) created in {role.Category} at priority {role.Priority}. Set what it allows with @role/allow.");
			case "DELETE":
				return Done(await management.DeleteRoleAsync(actor, left, ct), _ => $"Role {left} deleted.");
			case "DEFINE":
				var (group, description) = CategoryAndRest(right);
				return await management.DefinePermissionAsync(actor, left, group, description, ct) switch
				{
					CustomPermission permission => $"Permission {permission.Scope} defined in {permission.Category}. Allow it on a role with @role/allow <role>={permission.Scope}.",
					RoleRefusal refusal => refusal.Message
				};
			case "CATEGORY/CREATE":
				return await management.CreateCategoryAsync(actor, kind, left, right, ct) switch
				{
					RoleCategory created => kind == CategoryKind.Role
						? $"Role category {created.Name} created. Put a role in it with @role/category <role>={created.Name}."
						: $"Permission category {created.Name} created. Put a permission in it with @role/define <permission>={created.Name}/<description>.",
					RoleRefusal refusal => refusal.Message
				};
			case "CATEGORY/DESCRIBE":
				return await management.DescribeCategoryAsync(actor, kind, left, right, ct) switch
				{
					RoleCategory described => $"{Capitalized(Categories.Noun(kind))} {described.Name}: {described.Description}",
					RoleRefusal refusal => refusal.Message
				};
			case "CATEGORY/RENAME":
				return await management.RenameCategoryAsync(actor, kind, left, right, ct) switch
				{
					RoleCategory renamed => $"{Capitalized(Categories.Noun(kind))} {left} is now {renamed.Name}, with everything in it.",
					RoleRefusal refusal => refusal.Message
				};
			case "CATEGORY/DELETE":
				return Done(await management.DeleteCategoryAsync(actor, kind, left, ct), _ => $"{Capitalized(Categories.Noun(kind))} {left} deleted.");
			case "CATEGORY" when left.Contains('.'):
				var scope = left.ToLowerInvariant();
				if ((await RoleRegistry(parser).GetCustomPermissionsAsync(ct)).FirstOrDefault(p => p.Scope == scope) is not { } defined)
					return PortalPermission.IsKnown(scope)
						? $"{scope} is a built-in permission; its group is fixed."
						: $"No custom permission named '{scope}'. See @role/scopes.";
				return await management.DefinePermissionAsync(actor, scope, right, defined.Description, ct) switch
				{
					CustomPermission permission => $"Permission {permission.Scope} is now in {permission.Category}.",
					RoleRefusal refusal => refusal.Message
				};
			case "CATEGORY":
				return Done(await management.EditRoleAsync(actor, left, role => Draft(role) with { Category = right }, ct),
					role => $"Role {role.Name} is now in {role.Category}.");
			case "UNDEFINE":
				return Done(await management.RemovePermissionAsync(actor, left, ct),
					_ => $"Permission {left.ToLowerInvariant()} removed, with every role and override that set it.");
			case "RENAME":
				return Done(await management.EditRoleAsync(actor, left, role => Draft(role) with { Name = right }, ct),
					role => $"Role {role.Slug} is now named {role.Name}.");
			case "COLOR":
				return Done(await management.EditRoleAsync(actor, left,
						role => Draft(role) with { Color = right.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : right }, ct),
					role => $"Role {role.Name} colour: {role.Color ?? "none"}.");
			case "PRIORITY":
				if (!int.TryParse(right, out var priority)) return "A priority is a whole number.";
				return Done(await management.EditRoleAsync(actor, left, role => Draft(role) with { Priority = priority }, ct),
					role => $"Role {role.Name} is now at priority {role.Priority}.");
			case "ALLOW" or "DENY" or "CLEAR":
				var state = operation switch { "ALLOW" => PermissionState.Allow, "DENY" => PermissionState.Deny, _ => PermissionState.Inherit };
				var scopes = right.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (scopes.Length == 0) return "Name at least one permission. See @role/scopes.";
				var custom = (await RoleRegistry(parser).GetCustomPermissionsAsync(ct)).Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
				if (scopes.FirstOrDefault(s => !PortalPermission.IsKnown(s) && !custom.Contains(s)) is { } unknown)
					return $"Unknown permission '{unknown}'. See @role/scopes.";
				var changed = $"{StateWord(state)} {string.Join(", ", scopes)}";
				return holder switch
				{
					RoleHolder.Object => await RoleTargetAsync(parser, executor, left) switch
					{
						AnySharpObject target => Done(await management.SetObjectOverridesAsync(actor, target, scopes, state, ct),
							_ => $"{target.Object().Name}: {changed}."),
						Error<string> error => error.Value
					},
					RoleHolder.Account => await RoleAccountAsync(parser, executor, left) switch
					{
						(SharpPlayer player, SharpAccount account) => Done(await management.SetOverridesAsync(actor, account.Id!, scopes, state, ct),
							_ => $"{player.Object.Name} (account {account.Username}): {changed}."),
						_ => $"No player named '{left}' with an account."
					},
					_ => Done(await management.EditRoleAsync(actor, left, role =>
						{
							var permissions = new Dictionary<string, PermissionState>(role.Permissions, StringComparer.OrdinalIgnoreCase);
							foreach (var scope in scopes) permissions[scope] = state;
							return Draft(role) with { Permissions = permissions };
						}, ct),
						role => $"Role {role.Name}: {changed}.")
				};
			default:
				var assign = operation == "ASSIGN";
				if (holder == RoleHolder.Account)
				{
					if (await RoleAccountAsync(parser, executor, left) is not var (player, account))
						return $"No player named '{left}' with an account.";
					return Done(assign
							? await management.AssignAsync(actor, account.Id!, right, ct)
							: await management.UnassignAsync(actor, account.Id!, right, ct),
						_ => $"{player.Object.Name} (account {account.Username}) {(assign ? "now holds" : "no longer holds")} {right}.");
				}

				return await RoleTargetAsync(parser, executor, left) switch
				{
					AnySharpObject target => Done(assign
							? await management.AssignToObjectAsync(actor, target, right, ct)
							: await management.UnassignFromObjectAsync(actor, target, right, ct),
						_ => $"{target.Object().Name} {(assign ? "now holds" : "no longer holds")} {right}."),
					Error<string> error => error.Value
				};
		}
	}

	private async ValueTask<Result<AnySharpObject>> RoleTargetAsync(IMUSHCodeParser parser, AnySharpObject executor, string name)
		=> await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All) switch
		{
			AnySharpObject found => found,
			// Locate has already told the executor why.
			_ => new Error<string>("")
		};

	private async ValueTask<(SharpPlayer Player, SharpAccount Account)?> RoleAccountAsync(IMUSHCodeParser parser, AnySharpObject executor, string name)
	{
		if (await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, name.TrimStart('*'))
				is not (AnySharpObject and SharpPlayer player))
			return null;
		return await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, ExecutionBudget.CurrentToken) is { Id: not null } account
			? (player, account)
			: null;
	}

	private static IRoleRegistryService RoleRegistry(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IRoleRegistryService>();

	private static IAdministrativeCapabilityService Capabilities(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>();

	private static RoleDraft Draft(SharpRole role) => new(role.Slug, role.Name, role.Category, role.Color, role.Priority, role.Permissions);

	/// <summary><c>&lt;category&gt;[/&lt;rest&gt;]</c>: the category, then everything after the first <c>/</c>.</summary>
	private static (string Category, string Remainder) CategoryAndRest(string value)
	{
		var slash = value.IndexOf('/');
		return slash < 0 ? (value.Trim(), "") : (value[..slash].Trim(), value[(slash + 1)..].Trim());
	}

	private static string Done(RoleOutcome<SharpRole> outcome, Func<SharpRole, string> success) => outcome switch
	{
		SharpRole role => success(role),
		RoleRefusal refusal => refusal.Message
	};

	private static string Done(RoleOutcome<Success> outcome, Func<Success, string> success) => outcome switch
	{
		Success done => success(done),
		RoleRefusal refusal => refusal.Message
	};

	private static string StateWord(PermissionState state) => state switch
	{
		PermissionState.Allow => "allows",
		PermissionState.Deny => "denies",
		_ => "inherits"
	};

	private static string RoleUsage(string operation) => operation switch
	{
		"RENAME" => "<role>=<name>",
		"COLOR" => "<role>=<#rrggbb|none>",
		"PRIORITY" => "<role>=<number>",
		"ALLOW" or "DENY" or "CLEAR" => "[/object|/account] <role, object or player>=<permission> [<permission> ...]",
		"ASSIGN" or "UNASSIGN" => "[/account] <object>=<role>",
		"CREATE" => "<role>=<category>[/<display name>]",
		"DEFINE" => "<permission>=<category>[/<description>]",
		"CATEGORY" => "<role or custom permission>=<category>",
		"CATEGORY/CREATE" or "CATEGORY/DESCRIBE" => "[/permission] <category>=<description>",
		"CATEGORY/RENAME" => "[/permission] <category>=<new name>",
		"CATEGORY/DELETE" => "[/permission] <category>",
		"UNDEFINE" => "<permission>",
		_ => "<role>"
	};

	private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

	private static string ScopeList(IReadOnlyDictionary<string, PermissionState> permissions, PermissionState state)
		=> Joined(permissions.Where(p => p.Value == state).Select(p => p.Key));

	private static string Joined(IEnumerable<string> items)
	{
		var list = items.Order(StringComparer.Ordinal).ToArray();
		return list.Length == 0 ? "none" : string.Join(" ", list);
	}
}
