using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// RhostMUSH's and TinyMUX's reality-level commands, over SharpMUSH's reality layers: each changes what <c>@reality</c>
/// changes, under the same permission.
/// </summary>
public partial class Commands
{
	/// <summary><c>@rxlevel &lt;object&gt;=&lt;levels&gt;</c>: adds layers the object receives, and takes away <c>!layer</c>.</summary>
	[SharpCommand(Name = "@RXLEVEL", Switches = ["RESET"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 1, MaxArgs = 2,
		ParameterNames = ["object", "levels"])]
	public ValueTask<Option<CallState>> RxLevelCommand(IMUSHCodeParser parser, SharpCommandAttribute _2) => LevelCommandAsync(parser, true);

	/// <summary><c>@txlevel &lt;object&gt;=&lt;levels&gt;</c>: adds layers the object transmits, and takes away <c>!layer</c>.</summary>
	[SharpCommand(Name = "@TXLEVEL", Switches = ["RESET"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 1, MaxArgs = 2,
		ParameterNames = ["object", "levels"])]
	public ValueTask<Option<CallState>> TxLevelCommand(IMUSHCodeParser parser, SharpCommandAttribute _2) => LevelCommandAsync(parser, false);

	/// <summary><c>@leveldefault &lt;object&gt;</c>: clears the object's reality settings, so it takes the defaults again.</summary>
	[SharpCommand(Name = "@LEVELDEFAULT", Switches = ["LIST"], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1,
		ParameterNames = ["objects"])]
	public async ValueTask<Option<CallState>> LevelDefault(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var given = PlainArgument(parser, "0");
		var names = parser.CurrentState.Switches.Contains("LIST")
			? given.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			: [given.Trim()];

		var output = "";
		foreach (var name in names)
		{
			if (await RealityLevels.FindAsync(parser, LocateService, Mediator, executor, name) is not AnySharpObject target)
			{
				continue;
			}

			output = await RealityLevels.ResetAsync(parser, executor, target.Object().DBRef) switch
			{
				string message => message,
				Error<string> error => "#-1 " + error.Value
			};
			await NotifyService.Notify(executor, output);
		}

		return new CallState(output);
	}

	private async ValueTask<Option<CallState>> LevelCommandAsync(IMUSHCodeParser parser, bool receive)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var output = await RealityLevels.FindAsync(parser, LocateService, Mediator, executor, PlainArgument(parser, "0")) switch
		{
			AnySharpObject target => await RealityLevels.SetAsync(parser, executor, target.Object().DBRef, receive, PlainArgument(parser, "1"),
				parser.CurrentState.Switches.Contains("RESET")) switch
			{
				string message => message,
				Error<string> error => "#-1 " + error.Value
			},
			CallState => null
		};

		if (output is null) return CallState.Empty;
		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}
}

/// <summary>The reality-level changes the commands and functions share, made through <see cref="RealityAdministration"/>.</summary>
internal static class RealityLevels
{
	public union Target(AnySharpObject, CallState);

	/// <summary>
	/// The object <paramref name="name"/> names. A dbref is taken as it stands, as <c>@reality</c> takes one, so a
	/// controller can repair an object it cannot perceive; a name is matched as usual, with the refusal notified.
	/// </summary>
	public static async ValueTask<Target> FindAsync(IMUSHCodeParser parser, ILocateService locate, IMediator mediator,
		AnySharpObject executor, string name)
	{
		// AnySharpObject converts to CallState, so each case is made a Target explicitly.
		if (DBRef.TryParse(name.Trim(), out var parsed) && parsed is { } reference)
		{
			return await mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject found
				? new Target(found)
				: new Target(new CallState(ErrorMessages.Returns.NotFound));
		}

		return await locate.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, LocateFlags.All) switch
		{
			AnySharpObject found => new Target(found),
			Error<CallState> refusal => new Target(refusal.Value)
		};
	}

	public static async Task<Result<string>> SetAsync(IMUSHCodeParser parser, AnySharpObject executor, DBRef target, bool receive,
		string changes, bool reset)
		=> await ActorAsync(parser, executor) switch
		{
			CapabilityActor actor => await parser.ServiceProvider.GetRequiredService<RealityAdministration>()
				.AdjustAsync(actor, target, receive, changes, reset, ExecutionBudget.CurrentToken),
			_ => new Error<string>("A linked active player is required.")
		};

	public static async Task<Result<string>> ResetAsync(IMUSHCodeParser parser, AnySharpObject executor, DBRef target)
		=> await ActorAsync(parser, executor) switch
		{
			CapabilityActor actor => await parser.ServiceProvider.GetRequiredService<RealityAdministration>()
				.ExecuteAsync(actor, "reset", target.ToString(), "", ExecutionBudget.CurrentToken),
			_ => new Error<string>("A linked active player is required.")
		};

	private static async Task<CapabilityActor?> ActorAsync(IMUSHCodeParser parser, AnySharpObject executor)
		=> await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken);
}
