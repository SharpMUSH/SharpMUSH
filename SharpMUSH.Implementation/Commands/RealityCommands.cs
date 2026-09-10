using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
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
		return policy is IRealityObservationProvider observations
			? await observations.ObserveAsync(viewer.Object().DBRef, ExecutionBudget.CurrentToken)
			: (target, ct) => policy.CanPerceiveAsync(viewer.Object().DBRef, target, ct);
	}

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
			var actor = await parser.ServiceProvider.GetRequiredService<IAdministrativeCapabilityService>()
				.GetGameActorAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken)
				?? throw new UnauthorizedAccessException("A linked active player is required.");
			var switches = parser.CurrentState.Switches;
			if (switches.Count() > 1) throw new ArgumentException("Choose one reality operation.");
			var operation = switches.FirstOrDefault() ?? "LIST";
			var target = parser.CurrentState.Arguments.TryGetValue("0", out var left) ? left.Message?.ToPlainText() ?? "" : "";
			var value = parser.CurrentState.Arguments.TryGetValue("1", out var right) ? right.Message?.ToPlainText() ?? "" : "";
			output = await parser.ServiceProvider.GetRequiredService<RealityAdministration>().ExecuteAsync(actor, operation, target, value, ExecutionBudget.CurrentToken);
		}
		catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidDataException)
		{
			output = "#-1 " + ex.Message;
		}
		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}
}
