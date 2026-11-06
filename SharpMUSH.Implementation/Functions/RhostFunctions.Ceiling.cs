using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's ueval() and streval(): code evaluated under a privilege ceiling. The ceiling is a role, and the code
/// keeps only what the roles at or below that role's priority grant (<see cref="PrivilegeCeiling"/>). RhostMUSH's
/// level names stand for roles in the same order.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// RhostMUSH's evaluation levels, lowest first: the shortest abbreviation it takes, the role a game's import
	/// makes for the level, and the built-in role that stands in for it when there is none. <c>god</c> is no ceiling.
	/// </summary>
	private static readonly (string Name, int Shortest, string Role, string? Standin)[] RhostLevels =
	[
		("citizen", 2, "citizen", BuiltInRoles.PlayerSlug),
		("guildmaster", 1, "guildmaster", BuiltInRoles.PlayerSlug),
		("architect", 1, "architect", BuiltInRoles.BuilderSlug),
		("councilor", 2, "councilor", BuiltInRoles.RoyaltySlug),
		// RhostMUSH's royalty is a full wizard, not PennMUSH's royalty.
		("royalty", 1, BuiltInRoles.WizardSlug, BuiltInRoles.WizardSlug),
		("wizard", 1, BuiltInRoles.WizardSlug, BuiltInRoles.WizardSlug),
		("immortal", 1, "immortal", null),
		("god", 3, BuiltInRoles.GodSlug, null)
	];

	/// <summary>
	/// ueval(): u() with a ceiling named after the attribute, <c>[&lt;object&gt;/]&lt;attribute&gt;/&lt;role&gt;</c>.
	/// The level <c>subeval</c> evaluates only the attribute's substitutions, with no ceiling.
	/// </summary>
	[SharpFunction(Name = "ueval", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["object/attribute/role", "arguments..."])]
	public async ValueTask<CallState> UEval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var selector = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var cut = selector.LastIndexOf('/');
		if (cut < 0) return new CallState(ErrorMessages.Returns.NoLevelSpecified);

		var level = selector[(cut + 1)..].Trim();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await AttributeService.FetchAttributeFunctionAsync(parser, executor, selector[..cut]) switch
		{
			AttributeFunction function => await CallUnderCeilingAsync(parser, executor, function, level),
			CallState refusal => refusal
		};
	}

	private async ValueTask<CallState> CallUnderCeilingAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AttributeFunction function, string level)
	{
		var arguments = parser.CurrentState.ArgumentsOrdered.Skip(1)
			.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value.Value))
			.ToDictionary();
		var calling = parser.Push(parser.CurrentState with
		{
			Arguments = arguments,
			EnvironmentRegisters = new Dictionary<string, CallState>(arguments)
		});

		if (level.Length > 0 && "subeval".StartsWith(level, StringComparison.OrdinalIgnoreCase))
		{
			return await AttributeService.CallAttributeFunctionAsync(calling,
				function with { Code = MarkupText.Plain(SubstitutionsOnly(function.Code.ToPlainText())) });
		}

		switch (await CeilingAsync(parser, level, executor, function.Owner))
		{
			case NotFound:
				return new CallState(ErrorMessages.Returns.NoSuchRole);
			case PrivilegeCeiling ceiling:
				using (ceiling.Enter()) return await AttributeService.CallAttributeFunctionAsync(calling, function);
			default:
				return await AttributeService.CallAttributeFunctionAsync(calling, function);
		}
	}

	/// <summary>
	/// streval(): evaluates its first argument under the ceiling its second names, with the rest, unevaluated, as
	/// <c>%0</c> onwards.
	/// </summary>
	[SharpFunction(Name = "streval", MinArgs = 2, MaxArgs = 34, Flags = FunctionFlags.NoParse,
		ParameterNames = ["string", "role", "arguments..."])]
	public async ValueTask<CallState> StrEval(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var level = (await parser.FunctionParse(args["1"].Message))?.Message?.ToPlainText().Trim() ?? string.Empty;
		var arguments = args.Skip(2)
			.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), new CallState(value.Value.Message)))
			.ToDictionary();

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		switch (await CeilingAsync(parser, level, executor))
		{
			case NotFound:
				return new CallState(ErrorMessages.Returns.NoSuchRole);
			case PrivilegeCeiling ceiling:
				using (ceiling.Enter()) return await Evaluate();
			default:
				return await Evaluate();
		}

		async ValueTask<CallState> Evaluate()
			=> await parser.With(state => state with
			{
				Arguments = arguments,
				EnvironmentRegisters = new Dictionary<string, CallState>(arguments)
			}, evaluating => evaluating.FunctionParse(args["0"].Message)) ?? CallState.Empty;
	}

	/// <summary>The ceiling a level names: a privilege ceiling, none at all (<c>god</c>), or no such role.</summary>
	public union Ceiling(PrivilegeCeiling, None, NotFound);

	/// <summary>A role priority to cap at, none at all (<c>god</c>), or no such role.</summary>
	public union CeilingLevel(int, None, NotFound);

	/// <summary>
	/// The ceiling <paramref name="level"/> names over <paramref name="objects"/> and their owners: none at all for
	/// <c>god</c>, and <see cref="NotFound"/> when it names no role. A ceiling above the caller's own roles takes nothing
	/// they hold, so naming a higher one evaluates at their own level, as in RhostMUSH. The caller enters it: entered
	/// inside an async method, it would end when the method returned.
	/// </summary>
	private async ValueTask<Ceiling> CeilingAsync(IMUSHCodeParser parser, string level, params AnySharpObject[] objects)
	{
		switch (await CeilingPriorityAsync(parser, level))
		{
			case int priority:
				var lowered = ImmutableHashSet.CreateBuilder<int>();
				foreach (var obj in objects)
				{
					lowered.Add(obj.Object().DBRef.Number);
					if (await obj.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken) is { } owner)
						lowered.Add(owner.Object.DBRef.Number);
				}

				return new PrivilegeCeiling(priority, lowered.ToImmutable());
			case None none:
				return none;
			default:
				return new NotFound();
		}
	}

	/// <summary>
	/// The priority of the role <paramref name="level"/> names: a RhostMUSH level, which may be shortened, through the
	/// role a game's import made for it or the built-in role standing in for it, or else any role by its slug.
	/// </summary>
	private static async ValueTask<CeilingLevel> CeilingPriorityAsync(IMUSHCodeParser parser, string level)
	{
		if (level.Length == 0) return new NotFound();

		var roles = parser.ServiceProvider.GetRequiredService<IRoleRegistryService>();
		var rhost = RhostLevels.FirstOrDefault(entry => level.Length >= entry.Shortest
			&& entry.Name.StartsWith(level, StringComparison.OrdinalIgnoreCase));
		if (rhost.Name is null)
		{
			return await RolePriorityAsync(roles, level.ToLowerInvariant()) is { } priority ? priority : new NotFound();
		}

		if (rhost.Role == BuiltInRoles.GodSlug) return new None();

		// immortal with no role of its own: everything a role grants, but not the owner's blanket grant.
		return await RolePriorityAsync(roles, rhost.Role)
			?? (rhost.Standin is { } standin ? await RolePriorityAsync(roles, standin) : null)
			?? int.MaxValue;
	}

	/// <summary>
	/// The priority of the role <paramref name="slug"/>, held in the world or, for the roles held by being something
	/// (<c>player</c>), built in.
	/// </summary>
	private static async ValueTask<int?> RolePriorityAsync(IRoleRegistryService roles, string slug)
		=> await roles.GetRoleAsync(slug, ExecutionBudget.CurrentToken) is SharpRole role
			? role.Priority
			: BuiltInRoles.All.FirstOrDefault(builtIn => builtIn.Slug == slug)?.Priority;

	/// <summary>
	/// <paramref name="code"/> with every bracket and parenthesis escaped, so evaluating it runs its substitutions
	/// and calls no function, as RhostMUSH's subeval() does. What the code escaped already stays as it was.
	/// </summary>
	private static string SubstitutionsOnly(string code)
	{
		var escaped = new StringBuilder(code.Length + 8);
		for (var i = 0; i < code.Length; i++)
		{
			var c = code[i];
			if (c is '\\' or '%' && i + 1 < code.Length)
			{
				escaped.Append(c).Append(code[++i]);
				continue;
			}

			if (c is '[' or ']' or '(' or ')') escaped.Append('\\');
			escaped.Append(c);
		}

		return escaped.ToString();
	}
}
