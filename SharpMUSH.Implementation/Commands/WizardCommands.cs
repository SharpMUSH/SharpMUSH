using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using System.Text.RegularExpressions;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@POOR", Switches = [], Behavior = CB.Default, MinArgs = 1, MaxArgs = 1, ParameterNames = ["player"])]
	public async ValueTask<Option<CallState>> Poor(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PoorUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		var playerArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer player => await PoorAsync(executor, player),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> PoorAsync(AnySharpObject executor, SharpPlayer player)
	{
		await Mediator.Send(new SetPlayerQuotaCommand(player, 0));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PlayerSetToPoorFormat), executor, player.Object.Name);
		await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.YourQuotaSetToZeroByFormat), executor, executor.Object().Name);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@SQUOTA", Switches = [], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 1, ParameterNames = ["type", "value"])]
	public async ValueTask<Option<CallState>> ShortQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabledMessage), executor);
			return CallState.Empty;
		}

		AnySharpObject targetPlayer;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg))
			{
				case AnySharpObject found:
					targetPlayer = found;
					break;
				case Error<CallState> error:
					return error.Value;
				default:
					throw new InvalidOperationException("A player lookup returned neither a player nor an error.");
			}
		}
		else
		{
			// PennMUSH src/wiz.c:165 resolves the no-argument form as Owner(player), not the executor
			// itself, so a thing or a puppet reports the quota of whoever owns it. Defaulting to the
			// executor instead made every non-player caller - `@force <thing>=@quota`, a $-command on a
			// thing - throw out of the command and take its queue entry with it.
			targetPlayer = new AnySharpObject(
				await executor.Object().Owner.WithCancellation(CancellationToken.None));
		}

		if (targetPlayer is not SharpPlayer targetPlayerObj)
		{
			throw new InvalidOperationException("A quota belongs to a player.");
		}

		if (!await PermissionService.CanSeeQuota(executor, targetPlayer))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.CantLookAtOthersQuota,
				shouldNotify: true);
		}

		var quota = targetPlayerObj.Quota;

		var objectsOwned = await Mediator.Send(new GetOwnedObjectCountQuery(targetPlayerObj));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaStatusFormat), executor, objectsOwned, quota);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@ALLQUOTA", Switches = ["QUIET"], Behavior = CB.Default,
		CommandLock = "FLAG^WIZARD|POWER^QUOTA", MinArgs = 1, MaxArgs = 1, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> AllQuota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var isQuiet = switches.Contains("QUIET");

		if (parser.CurrentState.Arguments.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AllQuotaUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var amountArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		if (!int.TryParse(amountArg, out var amount))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaAmountMustBeNumber), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		var players = Mediator.CreateStream(new GetAllPlayersQuery());
		var count = 0;

		await foreach (var player in players)
		{
			await Mediator.Send(new SetPlayerQuotaCommand(player, amount));
			count++;

			if (!isQuiet)
			{
				await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.AllQuotaSetForPlayerFormat), executor, amount, executor.Object().Name);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SetQuotaForPlayersFormat), executor, amount, count);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@QUOTA", Switches = ["ALL", "SET"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0,
		MaxArgs = 2, ParameterNames = ["player", "quota"])]
	public async ValueTask<Option<CallState>> Quota(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;

		if (!Configuration.CurrentValue.Limit.UseQuota)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSystemDisabled), executor);
			return CallState.Empty;
		}

		if (switches.Contains("SET"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaSetUsage), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			var playerArg = args["0"].Message!.ToPlainText();
			var amountArg = args["1"].Message!.ToPlainText();

			if (!int.TryParse(amountArg, out var amount))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaAmountMustBeNumber), executor);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			}

			return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
				playerArg, async player =>
				{
					await Mediator.Send(new SetPlayerQuotaCommand(player, amount));

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaForPlayerSetFormat), executor, player.Object.Name, amount);
					await NotifyService.NotifyLocalized(player.Object.DBRef, nameof(ErrorMessages.Notifications.YourQuotaSetToByFormat), executor, amount, executor.Object().Name);

					return CallState.Empty;
				});
		}

		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingColumnHeader), executor);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaListingSeparator), executor);

			var players = Mediator.CreateStream(new GetAllPlayersQuery());
			await foreach (var player in players)
			{
				var objectCount = await Mediator.Send(new GetOwnedObjectCountQuery(player));
				var playerName = player.Object.Name.PadToColumns(27);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaPlayerRowFormat), executor, playerName, objectCount, player.Quota);
			}

			return CallState.Empty;
		}

		AnySharpObject targetPlayer;
		if (args.Count > 0)
		{
			var playerArg = args["0"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg))
			{
				case AnySharpObject found:
					targetPlayer = found;
					break;
				case Error<CallState> error:
					return error.Value;
				default:
					throw new InvalidOperationException("A player lookup returned neither a player nor an error.");
			}
		}
		else
		{
			// PennMUSH src/wiz.c:165 resolves the no-argument form as Owner(player), not the executor
			// itself, so a thing or a puppet reports the quota of whoever owns it. Defaulting to the
			// executor instead made every non-player caller - `@force <thing>=@quota`, a $-command on a
			// thing - throw out of the command and take its queue entry with it.
			targetPlayer = new AnySharpObject(
				await executor.Object().Owner.WithCancellation(CancellationToken.None));
		}

		if (targetPlayer is not SharpPlayer targetPlayerObj)
		{
			throw new InvalidOperationException("A quota belongs to a player.");
		}

		if (!await PermissionService.CanSeeQuota(executor, targetPlayer))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.CantLookAtOthersQuota,
				shouldNotify: true);
		}

		var quota = targetPlayerObj.Quota;

		var objectsOwned = await Mediator.Send(new GetOwnedObjectCountQuery(targetPlayerObj));

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.QuotaPlayerObjectsFormat), executor, targetPlayerObj.Object.Name, objectsOwned, quota);

		return CallState.Empty;
	}

}
