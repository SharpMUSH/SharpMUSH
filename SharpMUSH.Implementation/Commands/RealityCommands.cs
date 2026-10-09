using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	// Administrative reports retain their existing permission and formatting rules;
	// reality only determines which identities may enter the projection.
	private static async ValueTask<Func<DBRef, CancellationToken, ValueTask<bool>>> ObserveRealityAsync(
		IMUSHCodeParser parser, AnySharpObject viewer)
	{
		var policy = parser.ServiceProvider.GetRequiredService<IRealityPolicy>();
		Func<DBRef, CancellationToken, ValueTask<bool>> observe = policy is IRealityObservationProvider observations
			? await observations.ObserveAsync(viewer.Object().DBRef, ExecutionBudget.CurrentToken)
			: (target, ct) => policy.CanPerceiveAsync(viewer.Object().DBRef, target, ct);
		return (target, ct) => observe(target, ct.CanBeCanceled ? ct : ExecutionBudget.CurrentToken);
	}

	/// <summary>
	/// The contents-visibility scan for <paramref name="container"/> — reality plus the DARK/LIGHT rules,
	/// the same one look uses, so what an examine lists and what a look lists cannot drift apart.
	/// </summary>
	private static ValueTask<Func<AnySharpContent, CancellationToken, ValueTask<bool>>> ObserveContentsAsync(
		IMUSHCodeParser parser, AnySharpObject viewer, AnySharpObject container, IConnectionService connections)
		=> WorldVisibility.CreateScanAsync(viewer, container,
			parser.ServiceProvider.GetRequiredService<IRealityPolicy>(), connections, ExecutionBudget.CurrentToken);

	/// <summary>Whether <paramref name="mover"/> could arrive at <paramref name="destination"/> at all.</summary>
	private static ValueTask<bool> CanMoveInReality(IMUSHCodeParser parser, DBRef mover, DBRef destination)
		=> parser.ServiceProvider.GetRequiredService<IRealityPolicy>().CanPerceiveAsync(mover, destination);

	[SharpCommand(Name = "@REALITY", Switches = ["LIST", "ENABLE", "DISABLE", "ADD", "REMOVE", "RX", "TX", "DESCRIBE", "INSPECT"],
		Behavior = CB.Default | CB.EqSplit | CB.Switches | CB.NoGagged, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["layer or object", "layers or layer/attribute"])]
	public async ValueTask<Option<CallState>> Reality(IMUSHCodeParser parser, SharpCommandAttribute command)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		string output;
		try
		{
			output = await RunRealityOperationAsync(parser, executor) switch
			{
				string message => message,
				Error<string> error => "#-1 " + error.Value
			};
		}
		catch (InvalidDataException ex)
		{
			// The stored reality configuration or profile is damaged.
			output = "#-1 " + ex.Message;
		}

		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private static async Task<Result<string>> RunRealityOperationAsync(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
			.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken);
		if (actor is null)
		{
			return new Error<string>("A linked active player is required.");
		}

		var switches = parser.CurrentState.Switches.ToArray();
		if (switches.Length > 1)
		{
			return new Error<string>("Choose one reality operation.");
		}

		var operation = switches.FirstOrDefault() ?? "LIST";
		var target = PlainArgument(parser, "0");
		var value = PlainArgument(parser, "1");
		return await parser.ServiceProvider.GetRequiredService<RealityAdministration>()
			.ExecuteAsync(actor, operation, target, value, ExecutionBudget.CurrentToken);
	}

	private static string PlainArgument(IMUSHCodeParser parser, string key)
		=> parser.CurrentState.Arguments.TryGetValue(key, out var argument) ? argument.Message?.ToPlainText() ?? "" : "";
}
