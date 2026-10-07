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
	private static readonly string[] PermissionOperations =
		["LIST", "INFO", "DEFINE", "UNDEFINE", "CATEGORY", "CATEGORIES", "CREATE", "DESCRIBE", "RENAME", "DELETE", "ALLOW", "DENY", "CLEAR"];

	/// <summary>
	/// <c>@permission</c>: list the permissions, define the game's own, keep their categories, and set
	/// overrides on an object or an account. What a role allows is <c>@role/allow</c>. Every change goes
	/// through <see cref="IRoleManagementService"/>, with the executor as the actor.
	/// </summary>
	[SharpCommand(Name = "@PERMISSION", Switches = ["LIST", "INFO", "DEFINE", "UNDEFINE", "CATEGORY", "CATEGORIES", "CREATE", "DESCRIBE", "RENAME", "DELETE", "ALLOW", "DENY", "CLEAR", "ACCOUNT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["permission or object", "value"])]
	public async ValueTask<Option<CallState>> Permission(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var allSwitches = parser.CurrentState.Switches.ToArray();
		var switches = CategorySwitches(allSwitches.Where(PermissionOperations.Contains).ToArray());
		var args = parser.CurrentState.Arguments;
		var left = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();
		var right = (args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "").Trim();
		var hasRight = args.ContainsKey("1");

		MString output;
		if (switches.Length > 1 || switches.FirstOrDefault() is "CREATE" or "DESCRIBE" or "RENAME" or "DELETE")
			output = MarkupText.Plain("Choose one @permission operation. A category is changed with @permission/category/<create|describe|rename|delete>.");
		else
		{
			var operation = switches.FirstOrDefault() ?? (left.Length == 0 ? "LIST" : "INFO");
			output = operation switch
			{
				"LIST" => MarkupText.Plain(await PermissionListAsync(parser)),
				"INFO" => MarkupText.Plain(await PermissionInfoAsync(parser, left)),
				"CATEGORIES" => await CategoriesAsync(parser, CategoryKind.Permission),
				_ when operation.StartsWith("CATEGORY/") => MarkupText.Plain(await CategoryChangeAsync(parser, executor, CategoryKind.Permission, operation, left, right, hasRight)),
				_ => MarkupText.Plain(await PermissionChangeAsync(parser, executor, operation, allSwitches.Contains("ACCOUNT"), left, right, hasRight))
			};
		}

		if (output.Length > 0) await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary><c>@permission</c>: every permission, with the narrower ones each umbrella covers, then the game's own by category.</summary>
	private static async ValueTask<string> PermissionListAsync(IMUSHCodeParser parser)
	{
		var output = new StringBuilder("Permissions (an umbrella also covers the scopes listed after it):");
		foreach (var scope in PortalPermission.AllScopes)
		{
			var implied = PortalPermission.ImpliedScopes(scope);
			output.Append($"\n  {scope}{(implied.Count > 0 ? "  -> " + string.Join(", ", implied) : "")}");
		}

		var custom = await RoleRegistry(parser).GetCustomPermissionsAsync(ExecutionBudget.CurrentToken);
		output.Append(custom.Count == 0 ? "\nCustom permissions: none. Add one with @permission/define." : "\nCustom permissions, by category:");
		foreach (var category in custom.GroupBy(p => p.Category, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
		{
			output.Append($"\n {category.Key}");
			foreach (var permission in category)
				output.Append($"\n  {permission.Scope}{(permission.Description.Length > 0 ? "  " + permission.Description : "")}");
		}
		return output.ToString();
	}

	/// <summary><c>@permission &lt;permission&gt;</c>: what it is, and which roles allow or deny it.</summary>
	private static async ValueTask<string> PermissionInfoAsync(IMUSHCodeParser parser, string name)
	{
		var ct = ExecutionBudget.CurrentToken;
		var registry = RoleRegistry(parser);
		var scope = name.ToLowerInvariant();
		var custom = (await registry.GetCustomPermissionsAsync(ct)).FirstOrDefault(p => p.Scope == scope);
		if (custom is null && !PortalPermission.IsKnown(scope))
			return $"No permission named '{scope}'. See @permission.";

		var output = new StringBuilder(custom is null
			? $"Permission: {scope}  Built in"
			: $"Permission: {scope}  Category: {custom.Category}{(custom.Description.Length > 0 ? "  " + custom.Description : "")}");
		var implied = PortalPermission.ImpliedScopes(scope);
		if (implied.Count > 0) output.Append($"\nCovers: {string.Join(", ", implied)}");
		var roles = await registry.GetRolesAsync(ct);
		output.Append($"\nAllowed by: {Joined(roles.Where(r => r.Permissions.GetValueOrDefault(scope) == PermissionState.Allow).Select(r => r.Slug))}");
		output.Append($"\nDenied by: {Joined(roles.Where(r => r.Permissions.GetValueOrDefault(scope) == PermissionState.Deny).Select(r => r.Slug))}");
		return output.ToString();
	}

	private async ValueTask<string> PermissionChangeAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		bool onAccount, string left, string right, bool hasRight)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (left.Length == 0 || (operation != "UNDEFINE" && !hasRight))
			return $"Usage: @permission/{operation.ToLowerInvariant()} {PermissionUsage(operation)}. See help @permission.";
		var management = parser.ServiceProvider.GetRequiredService<IRoleManagementService>();
		RoleActor actor = executor;

		switch (operation)
		{
			case "DEFINE":
				var (group, description) = CategoryAndRest(right);
				return await management.DefinePermissionAsync(actor, left, group, description, ct) switch
				{
					CustomPermission permission => $"Permission {permission.Scope} defined in {permission.Category}. Allow it on a role with @role/allow <role>={permission.Scope}.",
					RoleRefusal refusal => refusal.Message
				};
			case "UNDEFINE":
				return Done(await management.RemovePermissionAsync(actor, left, ct),
					_ => $"Permission {left.ToLowerInvariant()} removed, with every role and override that set it.");
			case "CATEGORY":
				var scope = left.ToLowerInvariant();
				if ((await RoleRegistry(parser).GetCustomPermissionsAsync(ct)).FirstOrDefault(p => p.Scope == scope) is not { } defined)
					return PortalPermission.IsKnown(scope)
						? $"{scope} is a built-in permission; its group is fixed."
						: $"No custom permission named '{scope}'. See @permission.";
				return await management.DefinePermissionAsync(actor, scope, right, defined.Description, ct) switch
				{
					CustomPermission permission => $"Permission {permission.Scope} is now in {permission.Category}.",
					RoleRefusal refusal => refusal.Message
				};
			default:
				if (await ScopesAsync(parser, right) is not string[] scopes) return await UnknownScopeAsync(parser, right);
				var state = StateOf(operation);
				var changed = $"{StateWord(state)} {string.Join(", ", scopes)}";
				if (onAccount)
					return await RoleAccountAsync(parser, executor, left) switch
					{
						(SharpPlayer player, SharpAccount account) => Done(await management.SetOverridesAsync(actor, account.Id!, scopes, state, ct),
							_ => $"{player.Object.Name} (account {account.Username}): {changed}."),
						_ => $"No player named '{left}' with an account."
					};
				return await RoleTargetAsync(parser, executor, left) switch
				{
					AnySharpObject target => Done(await management.SetObjectOverridesAsync(actor, target, scopes, state, ct),
						_ => $"{target.Object().Name}: {changed}."),
					Error<string> error => error.Value
				};
		}
	}

	/// <summary>
	/// <c>/category/create</c>, <c>/describe</c>, <c>/rename</c> and <c>/delete</c> on the category list
	/// <paramref name="kind"/>: <c>@role</c>'s for roles, <c>@permission</c>'s for custom permissions.
	/// </summary>
	private static async ValueTask<string> CategoryChangeAsync(IMUSHCodeParser parser, AnySharpObject executor, CategoryKind kind,
		string operation, string left, string right, bool hasRight)
	{
		var command = CategoryCommand(kind);
		if (left.Length == 0 || (operation != "CATEGORY/DELETE" && !hasRight))
			return $"Usage: {command}/{operation.ToLowerInvariant()} {CategoryUsage(operation)}. See help {command}.";
		var ct = ExecutionBudget.CurrentToken;
		var management = parser.ServiceProvider.GetRequiredService<IRoleManagementService>();
		RoleActor actor = executor;
		var noun = Capitalized(Categories.Noun(kind));
		return operation switch
		{
			"CATEGORY/CREATE" => await management.CreateCategoryAsync(actor, kind, left, right, ct) switch
			{
				RoleCategory created => kind == CategoryKind.Role
					? $"Role category {created.Name} created. Put a role in it with @role/category <role>={created.Name}."
					: $"Permission category {created.Name} created. Put a permission in it with @permission/define <permission>={created.Name}/<description>.",
				RoleRefusal refusal => refusal.Message
			},
			"CATEGORY/DESCRIBE" => await management.DescribeCategoryAsync(actor, kind, left, right, ct) switch
			{
				RoleCategory described => $"{noun} {described.Name}: {described.Description}",
				RoleRefusal refusal => refusal.Message
			},
			"CATEGORY/RENAME" => await management.RenameCategoryAsync(actor, kind, left, right, ct) switch
			{
				RoleCategory renamed => $"{noun} {left} is now {renamed.Name}, with everything in it.",
				RoleRefusal refusal => refusal.Message
			},
			_ => Done(await management.DeleteCategoryAsync(actor, kind, left, ct), _ => $"{noun} {left} deleted.")
		};
	}

	/// <summary>The space-separated permissions in <paramref name="value"/>, or null when one is not a permission the game has.</summary>
	private static async ValueTask<string[]?> ScopesAsync(IMUSHCodeParser parser, string value)
	{
		var scopes = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (scopes.Length == 0) return null;
		var custom = (await RoleRegistry(parser).GetCustomPermissionsAsync(ExecutionBudget.CurrentToken)).Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return scopes.All(s => PortalPermission.IsKnown(s) || custom.Contains(s)) ? scopes : null;
	}

	/// <summary>Why <see cref="ScopesAsync"/> refused <paramref name="value"/>.</summary>
	private static async ValueTask<string> UnknownScopeAsync(IMUSHCodeParser parser, string value)
	{
		var custom = (await RoleRegistry(parser).GetCustomPermissionsAsync(ExecutionBudget.CurrentToken)).Select(p => p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
		return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => !PortalPermission.IsKnown(s) && !custom.Contains(s)) is { } unknown
			? $"Unknown permission '{unknown}'. See @permission."
			: "Name at least one permission. See @permission.";
	}

	private static PermissionState StateOf(string operation) => operation switch
	{
		"ALLOW" => PermissionState.Allow,
		"DENY" => PermissionState.Deny,
		_ => PermissionState.Inherit
	};

	private static string PermissionUsage(string operation) => operation switch
	{
		"DEFINE" => "<permission>=<category>[/<description>]",
		"UNDEFINE" => "<permission>",
		"CATEGORY" => "<custom permission>=<category>",
		_ => "[/account] <object or player>=<permission> [<permission> ...]"
	};
}
