using Humanizer;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
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

	[SharpCommand(Name = "@MOTD", Switches = ["CONNECT", "LIST", "WIZARD", "DOWN", "FULL", "CLEAR"],
		Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["type", "message"])]
	public async ValueTask<Option<CallState>> MessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("LIST"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return CallState.Empty;
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine("Message of the Day settings:");
			output.AppendLine($"Connect MOTD: {(string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd)}");
			output.AppendLine($"Wizard MOTD:  {(string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd)}");
			output.AppendLine($"Down MOTD:    {(string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd)}");
			output.AppendLine($"Full MOTD:    {(string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		string motdType;
		bool isConnect = !switches.Any() || switches.Contains("CONNECT");
		bool isWizard = switches.Contains("WIZARD");
		bool isDown = switches.Contains("DOWN");
		bool isFull = switches.Contains("FULL");

		if (isWizard)
			motdType = "wizard";
		else if (isDown)
			motdType = "down";
		else if (isFull)
			motdType = "full";
		else
			motdType = "connect";

		if (motdType == "connect")
		{
			if (!await executor.IsWizard() && !await executor.HasPower("ANNOUNCE"))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NeedAnnouncePower), executor);
				return CallState.Empty;
			}
		}
		else
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return CallState.Empty;
			}
		}

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdType switch
			{
				"wizard" => motdData with { WizardMotd = null },
				"down" => motdData with { DownMotd = null },
				"full" => motdData with { FullMotd = null },
				_ => motdData with { ConnectMotd = null }
			};

			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdClearedFormat), executor, motdType.Humanize(LetterCasing.Title));
			return CallState.Empty;
		}

		if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdUsage), executor);
			return CallState.Empty;
		}

		var newMotdDataSet = motdType switch
		{
			"wizard" => motdData with { WizardMotd = argText },
			"down" => motdData with { DownMotd = argText },
			"full" => motdData with { FullMotd = argText },
			_ => motdData with { ConnectMotd = argText }
		};

		await ObjectDataService.SetExpandedServerDataAsync(newMotdDataSet, ignoreNull: true);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.MotdSetFormat), executor, motdType.Humanize(LetterCasing.Title));
		return CallState.Empty;
	}

	[SharpCommand(Name = "@REJECTMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> RejectMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/full
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { FullMotd = null };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FullMotdCleared), executor);
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.RejectMotdUsage), executor);
		}
		else
		{
			var newMotdData = motdData with { FullMotd = argText };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FullMotdSet), executor);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CHOWNALL", Switches = ["PRESERVE", "THINGS", "ROOMS", "EXITS"],
		Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 2, ParameterNames = ["old-owner", "new-owner"])]
	public async ValueTask<Option<CallState>> Chown(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.Arguments;
		var preserve = switches.Contains("PRESERVE");

		if (args.Count < 1)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var playerArg = args["0"].Message!.ToPlainText();
		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, playerArg) switch
		{
			AnySharpObject and SharpPlayer oldOwner => await ChownAllFromAsync(parser, executor, oldOwner, switches, args,
				preserve),
			AnySharpObject => throw new InvalidOperationException("A player lookup found something that is not a player."),
			Error<CallState> error => error.Value
		};
	}

	private async ValueTask<Option<CallState>> ChownAllFromAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer oldOwner, IEnumerable<string> switches, Dictionary<string, CallState> args, bool preserve)
	{
		var newOwner = executor;
		if (args.Count > 1)
		{
			var newOwnerArg = args["1"].Message!.ToPlainText();
			switch (await LocateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, newOwnerArg))
			{
				case AnySharpObject found:
					newOwner = found;
					break;
				case Error<CallState> error:
					return error.Value;
			}
		}

		var chownThings = switches.Contains("THINGS") || (!switches.Contains("ROOMS") && !switches.Contains("EXITS"));
		var chownRooms = switches.Contains("ROOMS") || (!switches.Contains("THINGS") && !switches.Contains("EXITS"));
		var chownExits = switches.Contains("EXITS") || (!switches.Contains("THINGS") && !switches.Contains("ROOMS"));

		var objects = Mediator.CreateStream(new GetAllTypedObjectsQuery());
		var count = 0;

		await foreach (var obj in objects)
		{
			var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);

			if (objOwner.Object.DBRef.Number != oldOwner.Object.DBRef.Number)
			{
				continue;
			}

			// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

			var shouldChown = (chownThings && obj.IsThing) ||
												(chownRooms && obj.IsRoom) ||
												(chownExits && obj.IsExit);

			if (!shouldChown)
			{
				continue;
			}

			if (newOwner is not SharpPlayer newOwnerPlayer)
			{
				throw new InvalidOperationException("Objects are owned by players.");
			}

			await Mediator.Send(new SetObjectOwnerCommand(obj, newOwnerPlayer));
			count++;

			if (!preserve && !obj.IsPlayer)
			{
				if (await obj.HasFlag("WIZARD"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
				}
				if (await obj.HasFlag("ROYALTY"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
				}
				if (await obj.HasFlag("TRUST"))
				{
					await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
				}
				await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);

				await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ChownAllCompleteFormat), executor, count, oldOwner.Object.Name, newOwner.Object().Name);

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

	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["old-zone", "new-zone"])]
	public async ValueTask<Option<CallState>> ChangeZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var playerName = args["0"].Message!.ToPlainText();
		var zoneName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		// A player by name, so MAT_PMATCH: MAT_EVERYTHING carries MAT_PLAYER, which only answers to a
		// leading '*'.
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, playerName,
			LocateFlags.All | LocateFlags.MatchOptionalWildCardForPlayerName,
			async player =>
			{
				if (!player.IsPlayer)
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.InvalidPlayer,
						notifyMessage: ErrorMessages.Notifications.MustBePlayer,
						shouldNotify: true);
				}

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
					var count = 0;

					await foreach (var obj in allObjects)
					{
						var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
						if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
						{
							// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed
							await Mediator.Send(new UnsetObjectZoneCommand(obj));
							count++;
						}
					}

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZonesClearedForOwnerFormat), executor, count, player.Object().Name);
					return CallState.Empty;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj =>
					{
						var allObjects = Mediator.CreateStream(new GetAllTypedObjectsQuery())!;
						var count = 0;

						await foreach (var obj in allObjects)
						{
							var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
							if (objOwner.Object.DBRef.Number == player.Object().DBRef.Number)
							{
								// obj is already AnySharpObject — no secondary GetObjectNodeQuery needed

								if (!await HelperFunctions.SafeToAddZone(Mediator, Database, obj, zoneObj))
								{
									continue;
								}

								await Mediator.Send(new SetObjectZoneCommand(obj, zoneObj));

								if (!preserve && !obj.IsPlayer)
								{
									if (await obj.HasFlag("WIZARD"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
									}
									if (await obj.HasFlag("ROYALTY"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
									}
									if (await obj.HasFlag("TRUST"))
									{
										await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!TRUST", false);
									}

									await ManipulateSharpObjectService.ClearAllPowers(executor, obj, false);
								}

								count++;
							}
						}

						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ZoneSetForOwnerFormat), executor, zoneObj.Object().Name, count, player.Object().Name);
						return CallState.Empty;
					}
				);
			}
		);
	}

	[SharpCommand(Name = "@POLL", Switches = ["CLEAR"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Poll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;

		var pollData = await ObjectDataService.GetExpandedServerDataAsync<PollData>() ?? new PollData();

		if (switches.Contains("CLEAR"))
		{
			if (!await executor.IsWizard() && !await executor.HasPower("POLL"))
			{
				return await NotifyService.NotifyAndReturn(
					executor.Object().DBRef,
					errorReturn: ErrorMessages.Returns.PermissionDenied,
					notifyMessage: ErrorMessages.Notifications.PermissionDenied,
					shouldNotify: true);
			}

			var newPollData = pollData with { Message = null };
			await ObjectDataService.SetExpandedServerDataAsync(newPollData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollMessageCleared), executor);
			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			if (string.IsNullOrEmpty(pollData.Message))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollNoPollMessage), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollCurrentMessageFormat), executor, pollData.Message);
			}
			return CallState.Empty;
		}

		if (!await executor.IsWizard() && !await executor.HasPower("POLL"))
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();
		var newData = pollData with { Message = argText };
		await ObjectDataService.SetExpandedServerDataAsync(newData, ignoreNull: true);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PollMessageSet), executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@WIZMOTD", Switches = ["CLEAR"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD",
		MinArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> WizardMessageOfTheDay(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		// Alias for @motd/wizard
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;
		var argText = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText();

		var motdData = await ObjectDataService.GetExpandedServerDataAsync<MotdData>() ?? new MotdData();

		if (switches.Contains("CLEAR"))
		{
			var newMotdData = motdData with { WizardMotd = null };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdCleared), executor);
		}
		else if (string.IsNullOrEmpty(argText))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdUsage), executor);
		}
		else
		{
			var newMotdData = motdData with { WizardMotd = argText };
			await ObjectDataService.SetExpandedServerDataAsync(newMotdData, ignoreNull: true);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.WizMotdSet), executor);
		}

		return CallState.Empty;
	}

}
