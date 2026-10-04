using Microsoft.Extensions.DependencyInjection;
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
	/// <c>roles(&lt;player&gt;)</c>: the slugs of the roles the player holds while playing that character,
	/// highest priority first, <c>everyone</c> last. Empty for a player without an account.
	/// </summary>
	[SharpFunction(Name = "roles", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player"])]
	public async ValueTask<CallState> Roles(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WithPermissionContext(parser, parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			context => new CallState(string.Join(' ', RoleHierarchy.Ranked(context.Roles).Select(r => r.Slug)
				.Concat(context.Roles.Where(BuiltInRoles.IsEveryone).Select(r => r.Slug)))));

	/// <summary><c>hasrole(&lt;player&gt;, &lt;role&gt;)</c>: 1 when the player holds the role, else 0.</summary>
	[SharpFunction(Name = "hasrole", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "role"])]
	public async ValueTask<CallState> HasRole(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var slug = parser.CurrentState.Arguments["1"].Message!.ToPlainText().Trim();
		return await WithPermissionContext(parser, parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			context => new CallState(context.Roles.Any(r => string.Equals(r.Slug, slug, StringComparison.OrdinalIgnoreCase))));
	}

	/// <summary>
	/// <c>permission(&lt;player&gt;, &lt;permission&gt;)</c>: 1 when the player holds the permission while
	/// playing that character, else 0. <c>#-1 NO SUCH PERMISSION</c> for an unknown permission.
	/// </summary>
	[SharpFunction(Name = "permission", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "permission"])]
	public async ValueTask<CallState> Permission(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var scope = parser.CurrentState.Arguments["1"].Message!.ToPlainText().Trim();
		if (PortalPermission.Canonical(scope) is not { } canonical)
			return new CallState("#-1 NO SUCH PERMISSION");
		var resolver = parser.ServiceProvider.GetRequiredService<IPermissionResolver>();
		return await WithPermissionContext(parser, parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			context => new CallState(resolver.Explain(context, canonical).Allowed));
	}

	/// <summary>
	/// Locates a player and resolves what it holds while playing as itself. A player without an
	/// active account holds nothing, which is what every capability gate decides for it too.
	/// </summary>
	private async ValueTask<CallState> WithPermissionContext(IMUSHCodeParser parser, string name, Func<PermissionContext, CallState> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var capabilities = parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name.TrimStart('*'),
			async player =>
			{
				var actor = await capabilities.GetGameActorAsync(player.Object.DBRef, ExecutionBudget.CurrentToken);
				return answer(actor is null
					? PermissionContext.None
					: await capabilities.GetContextAsync(actor, ExecutionBudget.CurrentToken));
			});
	}
}
