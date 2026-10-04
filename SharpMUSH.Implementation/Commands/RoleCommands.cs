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
		["LIST", "INFO", "PLAYER", "SCOPES", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN"];

	/// <summary>
	/// <c>@role</c>: list, inspect and manage roles, their assignments and per-player overrides. Every
	/// change goes through <see cref="IRoleManagementService"/>, the same rules the portal applies.
	/// </summary>
	[SharpCommand(Name = "@ROLE", Switches = ["LIST", "INFO", "PLAYER", "SCOPES", "CREATE", "DELETE", "RENAME", "COLOR", "PRIORITY", "ALLOW", "DENY", "CLEAR", "ASSIGN", "UNASSIGN"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["role or player", "value"])]
	public async ValueTask<Option<CallState>> Role(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Where(RoleOperations.Contains).ToArray();
		var args = parser.CurrentState.Arguments;
		var left = (args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "").Trim();
		var right = (args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "").Trim();
		var hasRight = args.ContainsKey("1");

		string output;
		if (switches.Length > 1)
			output = "Choose one @role operation.";
		else
		{
			var operation = switches.FirstOrDefault() ?? (left.Length == 0 ? "LIST" : "INFO");
			output = operation switch
			{
				"LIST" => await RoleListAsync(parser),
				"INFO" => await RoleInfoAsync(parser, left),
				"SCOPES" => RoleScopes(),
				"PLAYER" => await RolePlayerAsync(parser, executor, left.Length == 0 ? null : left),
				_ => await RoleChangeAsync(parser, executor, operation, left, right, hasRight)
			};
		}

		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private static async ValueTask<string> RoleListAsync(IMUSHCodeParser parser)
	{
		var roles = await RoleRegistry(parser).GetRolesAsync(ExecutionBudget.CurrentToken);
		var slugWidth = Math.Max(4, roles.Max(r => r.Slug.Length));
		var output = new StringBuilder("Roles, highest first:");
		foreach (var role in roles)
			output.Append($"\n{role.Priority,4}  {role.Slug.PadRight(slugWidth)}  {role.Name}{(role.IsSystem ? "  (system)" : "")}");
		return output.ToString();
	}

	private static async ValueTask<string> RoleInfoAsync(IMUSHCodeParser parser, string slug)
	{
		if (await RoleRegistry(parser).GetRoleAsync(slug, ExecutionBudget.CurrentToken) is not SharpRole role)
			return $"No role named '{slug}'. See @role/list.";
		var output = new StringBuilder($"Role: {role.Name} ({role.Slug})  Priority: {role.Priority}");
		if (role.Color is not null) output.Append($"  Colour: {role.Color}");
		if (role.IsSystem) output.Append(BuiltInRoles.IsEveryone(role) ? "  System: held by every account" : "  System: follows character flags");
		output.Append($"\nAllows: {ScopeList(role.Permissions, PermissionState.Allow)}");
		output.Append($"\nDenies: {ScopeList(role.Permissions, PermissionState.Deny)}");
		return output.ToString();
	}

	private static string RoleScopes()
	{
		var output = new StringBuilder("Permissions (an umbrella also covers the scopes listed after it):");
		foreach (var scope in PortalPermission.AllScopes)
		{
			var implied = PortalPermission.ImpliedScopes(scope);
			output.Append($"\n  {scope}{(implied.Count > 0 ? "  -> " + string.Join(", ", implied) : "")}");
		}

		return output.ToString();
	}

	private async ValueTask<string> RolePlayerAsync(IMUSHCodeParser parser, AnySharpObject executor, string? name)
	{
		var ct = ExecutionBudget.CurrentToken;
		SharpPlayer player;
		if (name is null)
		{
			if (executor is not SharpPlayer self) return "Only a player holds roles.";
			player = self;
		}
		else if (await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, name.TrimStart('*'))
						 is AnySharpObject and SharpPlayer found)
			player = found;
		else
			return $"No player named '{name}'.";

		var capabilities = Capabilities(parser);
		if (!player.Object.DBRef.Equals(executor.Object().DBRef)
				&& !await ActorHolds(parser, executor, PortalPermission.PlayersView)
				&& !await ActorHolds(parser, executor, PortalPermission.RolesAdmin))
			return $"Seeing another player's permissions needs the {PortalPermission.PlayersView} permission.";
		if (await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, ct) is not { Id: not null } account)
			return $"{player.Object.Name} has no account, and roles belong to accounts.";

		var actor = new CapabilityActor(account.Id, player.Object.DBRef, player.Object.DBRef);
		var context = await capabilities.GetContextAsync(actor, ct);
		var explained = await capabilities.ExplainAsync(actor, ct);
		var ranked = RoleHierarchy.Ranked(context.Roles).Select(r => $"{r.Name} ({r.Priority})").ToArray();
		var output = new StringBuilder($"{player.Object.Name} (account {account.Username})");
		if (context.IsOwner) output.Append(" is the owner and holds every permission.");
		output.Append($"\nRoles: {(ranked.Length > 0 ? string.Join(", ", ranked) : "none")}, plus everyone");
		output.Append($"\nOverrides: allow {ScopeList(context.Overrides, PermissionState.Allow)}; deny {ScopeList(context.Overrides, PermissionState.Deny)}");
		output.Append($"\nHolds: {Joined(explained.Where(e => e.Value.Allowed).Select(e => e.Key))}");
		output.Append($"\nLacks: {Joined(explained.Where(e => !e.Value.Allowed).Select(e => e.Key))}");
		return output.ToString();
	}

	private async ValueTask<string> RoleChangeAsync(IMUSHCodeParser parser, AnySharpObject executor, string operation,
		string left, string right, bool hasRight)
	{
		var ct = ExecutionBudget.CurrentToken;
		if (left.Length == 0 || (operation is not ("DELETE" or "CREATE") && !hasRight))
			return $"Usage: @role/{operation.ToLowerInvariant()} {RoleUsage(operation)}. See help @role.";
		if (await Capabilities(parser).GetGameActorAsync(executor.Object().DBRef, ct) is not { } actor)
			return "Managing roles needs a player linked to an active account.";
		var management = parser.ServiceProvider.GetRequiredService<IRoleManagementService>();

		switch (operation)
		{
			case "CREATE":
				return Done(await management.SaveRoleAsync(actor,
					new RoleDraft(left.ToLowerInvariant(), hasRight ? right : left, null, 1, new Dictionary<string, PermissionState>()), ct),
					role => $"Role {role.Name} ({role.Slug}) created at priority {role.Priority}. Set what it allows with @role/allow.");
			case "DELETE":
				return Done(await management.DeleteRoleAsync(actor, left, ct), _ => $"Role {left} deleted.");
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
				if (scopes.FirstOrDefault(s => !PortalPermission.IsKnown(s)) is { } unknown)
					return $"Unknown permission '{unknown}'. See @role/scopes.";
				return left.StartsWith('*')
					? await PlayerOverrideAsync(parser, executor, actor, management, left[1..], scopes, state)
					: Done(await management.EditRoleAsync(actor, left, role =>
						{
							var permissions = new Dictionary<string, PermissionState>(role.Permissions, StringComparer.OrdinalIgnoreCase);
							foreach (var scope in scopes) permissions[scope] = state;
							return Draft(role) with { Permissions = permissions };
						}, ct),
						role => $"Role {role.Name}: {StateWord(state)} {string.Join(", ", scopes)}.");
			default:
				var assign = operation == "ASSIGN";
				if (await AccountFor(parser, executor, left) is not { } target) return $"No player named '{left}' with an account.";
				return Done(assign
						? await management.AssignAsync(actor, target.Account.Id!, right, ct)
						: await management.UnassignAsync(actor, target.Account.Id!, right, ct),
					_ => assign
						? $"{target.Player.Object.Name} (account {target.Account.Username}) now holds {right}."
						: $"{target.Player.Object.Name} (account {target.Account.Username}) no longer holds {right}.");
		}
	}

	private async ValueTask<string> PlayerOverrideAsync(IMUSHCodeParser parser, AnySharpObject executor, CapabilityActor actor,
		IRoleManagementService management, string name, string[] scopes, PermissionState state)
	{
		if (await AccountFor(parser, executor, name) is not { } target) return $"No player named '{name}' with an account.";
		if (await management.SetOverridesAsync(actor, target.Account.Id!, scopes, state, ExecutionBudget.CurrentToken) is RoleRefusal refusal)
			return refusal.Message;
		return $"{target.Player.Object.Name} (account {target.Account.Username}): {StateWord(state)} {string.Join(", ", scopes)}.";
	}

	private async ValueTask<(SharpPlayer Player, SharpAccount Account)?> AccountFor(IMUSHCodeParser parser, AnySharpObject executor, string name)
	{
		if (await LocateService.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, name.TrimStart('*'))
				is not (AnySharpObject and SharpPlayer player))
			return null;
		return await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, ExecutionBudget.CurrentToken) is { Id: not null } account
			? (player, account)
			: null;
	}

	private static async ValueTask<bool> ActorHolds(IMUSHCodeParser parser, AnySharpObject executor, string scope)
		=> await Capabilities(parser).GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken) is { } actor
			 && await Capabilities(parser).AuthorizeAsync(actor, scope, ExecutionBudget.CurrentToken);

	private static IRoleRegistryService RoleRegistry(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IRoleRegistryService>();

	private static IAdministrativeCapabilityService Capabilities(IMUSHCodeParser parser)
		=> parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>();

	private static RoleDraft Draft(SharpRole role) => new(role.Slug, role.Name, role.Color, role.Priority, role.Permissions);

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
		"ALLOW" or "DENY" or "CLEAR" => "<role or *player>=<permission> [<permission> ...]",
		"ASSIGN" or "UNASSIGN" => "<player>=<role>",
		"CREATE" => "<role>[=<display name>]",
		_ => "<role>"
	};

	private static string ScopeList(IReadOnlyDictionary<string, PermissionState> permissions, PermissionState state)
		=> Joined(permissions.Where(p => p.Value == state).Select(p => p.Key));

	private static string Joined(IEnumerable<string> items)
	{
		var list = items.Order(StringComparer.Ordinal).ToArray();
		return list.Length == 0 ? "none" : string.Join(" ", list);
	}
}
