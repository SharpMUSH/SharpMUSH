using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using MarkupString.Layout;
using SharpMUSH.Library.Markup;
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
		["LIST", "INFO", "PLAYER", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN", "CATEGORY", "CATEGORIES", "DESCRIBE"];

	/// <summary>The operations <c>@role/category/&lt;operation&gt;</c> and <c>@permission/category/&lt;operation&gt;</c> run on a category itself.</summary>
	private static readonly string[] CategoryOperations = ["CREATE", "DESCRIBE", "RENAME", "DELETE"];

	/// <summary>
	/// <c>@role</c>: list, inspect and manage roles, what they allow, who holds them, and the role
	/// categories. Permissions themselves (custom ones, their categories and overrides) are
	/// <c>@permission</c>. Every change goes through <see cref="IRoleManagementService"/>, the same rules the
	/// portal applies, with the executor as the actor: an object needs no account to manage roles.
	/// </summary>
	[SharpCommand(Name = "@ROLE", Switches = ["LIST", "INFO", "PLAYER", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN", "CATEGORY", "CATEGORIES", "DESCRIBE", "ACCOUNT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["role or object", "value"])]
	public async ValueTask<Option<CallState>> Role(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var allSwitches = parser.CurrentState.Switches.ToArray();
		var switches = CategorySwitches(allSwitches.Where(RoleOperations.Contains).ToArray());
		var args = parser.CurrentState.Arguments;
		var left = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();
		var right = (args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "").Trim();
		var hasRight = args.ContainsKey("1");

		MString output;
		if (switches.Length > 1)
			output = MarkupText.Plain("Choose one @role operation.");
		else if (switches.FirstOrDefault() == "DESCRIBE")
			output = MarkupText.Plain($"Usage: @role/category/describe {CategoryUsage("CATEGORY/DESCRIBE")}. See help @role.");
		else
		{
			var operation = switches.FirstOrDefault() ?? (left.Length == 0 ? "LIST" : "INFO");
			output = operation switch
			{
				"LIST" => await RoleListAsync(parser),
				"INFO" => await RoleInfoAsync(parser, left),
				"CATEGORIES" => await CategoriesAsync(parser, CategoryKind.Role),
				"PLAYER" => await RoleExplainAsync(parser, executor, left.Length == 0 ? "me" : left),
				_ when operation.StartsWith("CATEGORY/") => MarkupText.Plain(await CategoryChangeAsync(parser, executor, CategoryKind.Role, operation, left, right, hasRight)),
				_ => MarkupText.Plain(await RoleChangeAsync(parser, executor, operation, allSwitches.Contains("ACCOUNT"), left, right, hasRight))
			};
		}

		if (output.Length > 0) await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	/// <summary><c>/category/create</c> and friends act on a category itself: the pair becomes one operation, <c>CATEGORY/CREATE</c>.</summary>
	private static string[] CategorySwitches(string[] switches)
		=> switches.Length == 2 && switches.Contains("CATEGORY") && switches.FirstOrDefault(CategoryOperations.Contains) is { } onCategory
			? [$"CATEGORY/{onCategory}"]
			: switches;

	private static async ValueTask<MString> RoleListAsync(IMUSHCodeParser parser)
	{
		var roles = await RoleRegistry(parser).GetRolesAsync(ExecutionBudget.CurrentToken);
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Priority")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Role")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Category")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Name")) { Min = 10 },
			],
			roles.Select(role => new[] { role.Priority.ToString(CultureInfo.InvariantCulture), role.Slug, role.Category, role.IsSystem ? $"{role.Name} (system)" : role.Name }));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Roles, highest first"), table), 78);
	}

	private static async ValueTask<MString> RoleInfoAsync(IMUSHCodeParser parser, string slug)
	{
		if (await RoleRegistry(parser).GetRoleAsync(slug, ExecutionBudget.CurrentToken) is not SharpRole role)
			return MarkupText.Plain($"No role named '{slug}'. See @role/list.");
		var system = !role.IsSystem ? null : role.Slug switch
		{
			BuiltInRoles.EveryoneSlug => "held by everything",
			BuiltInRoles.PlayerSlug => "held by every player that is not a guest",
			BuiltInRoles.GodSlug => "held by #1",
			_ when RoleFlags.ForRole(role.Slug) is { } flag => $"the {flag.Name} flag",
			_ when GamePowers.ForRole(role.Slug) is { } power => $"the {power.Name} power",
			_ => "yes"
		};
		(string, MString)[] fields =
		[
			("Role", MarkupText.Plain(role.Slug)),
			("Category", MarkupText.Plain(role.Category)),
			("Priority", MarkupText.Plain(role.Priority.ToString(CultureInfo.InvariantCulture))),
			.. role.Color is null ? [] : new[] { ("Colour", MarkupText.Plain(role.Color)) },
			.. system is null ? [] : new[] { ("System", MarkupText.Plain(system)) },
			("Allows", MarkupText.Plain(ScopeList(role.Permissions, PermissionState.Allow))),
			("Denies", MarkupText.Plain(ScopeList(role.Permissions, PermissionState.Deny))),
		];
		return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain(role.Name), ServerLayout.KeyValues(fields)), 78);
	}

	/// <summary><c>@role/categories</c> and <c>@permission/categories</c>: one category list, what each category holds and its description.</summary>
	private static async ValueTask<MString> CategoriesAsync(IMUSHCodeParser parser, CategoryKind kind)
	{
		var ct = ExecutionBudget.CurrentToken;
		var registry = RoleRegistry(parser);
		var members = kind == CategoryKind.Role
			? (await registry.GetRolesAsync(ct)).Select(r => r.Category).ToArray()
			: (await registry.GetCustomPermissionsAsync(ct)).Select(p => p.Category).ToArray();
		var heading = kind == CategoryKind.Role ? "Role categories" : "Permission categories";
		var categories = await registry.GetCategoriesAsync(kind, ct);
		if (categories.Count == 0)
			return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain(heading),
				new TextBlock(MarkupText.Plain($"None. Create one with {CategoryCommand(kind)}/category/create <name>=<description>."))), 78);

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Category")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Holds")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Description")) { Min = 10 },
			],
			categories.Select(category => new[]
			{
				category.Name,
				Counted(members.Count(m => string.Equals(m, category.Name, StringComparison.OrdinalIgnoreCase)), kind == CategoryKind.Role ? "role" : "permission"),
				category.Description
			}));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain(heading), table), 78);
	}

	/// <summary>The command that manages the category list <paramref name="kind"/>.</summary>
	private static string CategoryCommand(CategoryKind kind) => kind == CategoryKind.Role ? "@role" : "@permission";

	private static string Counted(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

	/// <summary><c>@role/player &lt;object&gt;</c>: every role the object holds and where from, its overrides, and what they resolve to.</summary>
	private async ValueTask<MString> RoleExplainAsync(IMUSHCodeParser parser, AnySharpObject executor, string name)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All)
				is not AnySharpObject target)
			return MarkupText.Empty;

		var capabilities = Capabilities(parser);
		var mine = await capabilities.GetObjectGrantsAsync(executor, ct);
		if (!target.Object().DBRef.Equals(executor.Object().DBRef)
				&& !mine.Has(PortalPermission.PlayersView)
				&& !mine.Has(PortalPermission.RolesAdmin)
				&& !await PermissionService.CanExamine(executor, target))
			return MarkupText.Plain($"Seeing another object's permissions needs the {PortalPermission.PlayersView} permission.");

		var grants = await capabilities.GetObjectGrantsAsync(target, ct);
		var account = target.IsPlayer ? await AccountService.GetAccountForCharacterAsync(target.Object().DBRef, ct) : null;
		var roles = RoleHierarchy.Ranked(grants.Roles.Select(held => held.Role).DistinctBy(role => role.Slug).ToArray())
			.Select(role => $"{role.Name} ({role.Priority}{SourceNote(grants, role.Slug)})")
			.ToArray();
		var scopes = PortalPermission.AllScopes.Concat(grants.Context.CustomScopes).ToArray();
		(string, MString)[] fields =
		[
			.. account is null ? [] : new[] { ("Account", MarkupText.Plain(account.Username)) },
			.. !grants.IsOwner ? [] : new[] { ("Owner", MarkupText.Plain("yes, and holds every permission")) },
			("Roles", MarkupText.Plain($"{(roles.Length > 0 ? string.Join(", ", roles) : "none")}, plus everyone")),
			("Overrides", MarkupText.Plain($"allow {ScopeList(grants.Context.ObjectOverrides, PermissionState.Allow)}; deny {ScopeList(grants.Context.ObjectOverrides, PermissionState.Deny)}")),
			.. account is null ? [] : new[] { ("Account overrides", MarkupText.Plain($"allow {ScopeList(grants.Context.Overrides, PermissionState.Allow)}; deny {ScopeList(grants.Context.Overrides, PermissionState.Deny)}")) },
			("Holds", MarkupText.Plain(Joined(scopes.Where(grants.Has)))),
			("Lacks", MarkupText.Plain(Joined(scopes.Where(scope => !grants.Has(scope))))),
		];
		return ServerLayout.Build(ServerLayout.Section(MarkupText.Plain(target.Object().Name), ServerLayout.KeyValues(fields)), 78);
	}

	private static string SourceNote(ObjectGrants grants, string slug)
	{
		var sources = grants.Roles.Where(held => held.Role.Slug == slug).Select(held => held.Source).ToHashSet();
		return sources.Contains(RoleSource.Account) && !sources.Contains(RoleSource.Object) ? ", account" : "";
	}

	private async ValueTask<string> RoleChangeAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		bool onAccount, string left, string right, bool hasRight)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (left.Length == 0 || (operation != "DELETE" && !hasRight))
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
			case "CATEGORY":
				return Done(await management.EditRoleAsync(actor, left, role => Draft(role) with { Category = right }, ct),
					role => $"Role {role.Name} is now in {role.Category}.");
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
				if (await ScopesAsync(parser, right) is not string[] scopes) return await UnknownScopeAsync(parser, right);
				var state = StateOf(operation);
				return Done(await management.EditRoleAsync(actor, left, role =>
					{
						var permissions = new Dictionary<string, PermissionState>(role.Permissions, StringComparer.OrdinalIgnoreCase);
						foreach (var scope in scopes) permissions[scope] = state;
						return Draft(role) with { Permissions = permissions };
					}, ct),
					role => $"Role {role.Name}: {StateWord(state)} {string.Join(", ", scopes)}.");
			default:
				var assign = operation == "ASSIGN";
				if (onAccount)
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
		"ALLOW" or "DENY" or "CLEAR" => "<role>=<permission> [<permission> ...]",
		"ASSIGN" or "UNASSIGN" => "[/account] <object>=<role>",
		"CREATE" => "<role>=<category>[/<display name>]",
		"CATEGORY" => "<role>=<category>",
		_ => "<role>"
	};

	private static string CategoryUsage(string operation) => operation switch
	{
		"CATEGORY/CREATE" or "CATEGORY/DESCRIBE" => "<category>=<description>",
		"CATEGORY/RENAME" => "<category>=<new name>",
		_ => "<category>"
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
