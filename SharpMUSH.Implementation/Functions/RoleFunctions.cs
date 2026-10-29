using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// <c>roles(&lt;object&gt;)</c>: the slugs of the roles the object holds, its own and its account's,
	/// highest priority first, <c>everyone</c> last.
	/// </summary>
	[SharpFunction(Name = "roles", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Roles(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WithGrants(parser, parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			grants =>
			{
				var held = grants.Roles.Select(r => r.Role).DistinctBy(r => r.Slug).ToArray();
				return new CallState(string.Join(' ', RoleHierarchy.Ranked(held).Select(r => r.Slug)
					.Concat(held.Where(BuiltInRoles.IsEveryone).Select(r => r.Slug))));
			});

	/// <summary>
	/// <c>hasrole(&lt;object&gt;, &lt;role&gt;[, &lt;at least&gt;])</c>: 1 when the object holds the role, itself or through its
	/// account, else 0. With <c>&lt;at least&gt;</c> true, 1 when it holds a role of that priority or higher, and
	/// <c>#-1 NO SUCH ROLE</c> for a role neither the world nor the built-in roles define.
	/// </summary>
	[SharpFunction(Name = "hasrole", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "role", "at-least"])]
	public async ValueTask<CallState> HasRole(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var slug = args["1"].Message.ToPlainText().Trim();
		if (!args.TryGetValue("2", out var atLeast) || !atLeast.Message.Truthy(parser))
		{
			return await WithGrants(parser, args["0"].Message.ToPlainText(), grants => new CallState(grants.HoldsRole(slug)));
		}

		if (await RolePriorityOrBuiltInAsync(parser, slug) is not { } priority) return new CallState(ErrorMessages.Returns.NoSuchRole);
		return await WithGrants(parser, args["0"].Message.ToPlainText(),
			grants => new CallState(grants.IsOwner || grants.Roles.Any(held => held.Role.Priority >= priority)));
	}

	/// <summary>
	/// The priority of the role <paramref name="slug"/>: the world's role of that name, or the built-in one, a staff role
	/// of either set included, when the world has none.
	/// </summary>
	private static async ValueTask<int?> RolePriorityOrBuiltInAsync(IMUSHCodeParser parser, string slug)
		=> await parser.ServiceProvider.GetRequiredService<IRoleRegistryService>().GetRoleAsync(slug, ExecutionBudget.CurrentToken) is SharpRole role
			? role.Priority
			: BuiltInRoles.All.Concat(BuiltInRoles.Starters).Concat(BuiltInRoles.RhostLevels)
				.FirstOrDefault(builtIn => string.Equals(builtIn.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Priority;

	/// <summary>
	/// <c>permission(&lt;object&gt;, &lt;permission&gt;)</c>: 1 when the object holds the permission, else 0.
	/// <c>#-1 NO SUCH PERMISSION</c> for a permission that is neither built in nor defined with <c>@permission/define</c>.
	/// </summary>
	[SharpFunction(Name = "permission", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "permission"])]
	public async ValueTask<CallState> Permission(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var scope = parser.CurrentState.Arguments["1"].Message.ToPlainText().Trim();
		return await WithGrants(parser, parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			grants => grants.Canonical(scope) is { } canonical
				? new CallState(grants.Has(canonical))
				: new CallState("#-1 NO SUCH PERMISSION"));
	}

	/// <summary>Locates an object and answers from what it is granted (<see cref="ObjectGrants"/>).</summary>
	private async ValueTask<CallState> WithGrants(IMUSHCodeParser parser, string name, Func<ObjectGrants, CallState> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, LocateFlags.All,
			async found => answer(await found.GrantsAsync(ExecutionBudget.CurrentToken)));
	}
}
