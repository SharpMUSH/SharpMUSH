using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@CHOWN", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2,
		MaxArgs = 2, ParameterNames = ["object", "player"])]
	public async ValueTask<Option<CallState>> ChangeOwner(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var newOwnerName = args["1"].Message!.ToPlainText();
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.PermissionDenied,
						notifyMessage: ErrorMessages.Notifications.PermissionDenied,
						shouldNotify: true);
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, newOwnerName, LocateFlags.All,
					async newOwnerObj =>
					{
						if (newOwnerObj is not SharpPlayer newOwnerPlayer)
						{
							return await NotifyService.NotifyAndReturn(
								executor.Object().DBRef,
								errorReturn: ErrorMessages.Returns.InvalidPlayer,
								notifyMessage: ErrorMessages.Notifications.MustBePlayer,
								shouldNotify: true);
						}

						var result = await ManipulateSharpObjectService.SetOwner(executor, obj, newOwnerPlayer, true);

						if (!preserve)
						{
							if (await obj.HasFlag("WIZARD"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!WIZARD", false);
							}
							if (await obj.HasFlag("ROYALTY"))
							{
								await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "!ROYALTY", false);
							}
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, obj, "HALT", false);
						}

						return result;
					}
				);
			}
		);
	}

	/// <remarks>
	/// PennMUSH <c>do_chzone</c> (<c>src/set.c:373-489</c>), which <c>do_chzoneall</c> calls once per
	/// object (<c>src/wiz.c:1046-1054</c>), so the whole body lives in
	/// <see cref="ZoneHelpers.ChangeZoneAsync"/> and <c>@CHZONEALL</c> reaches it too.
	/// <para><c>MinArgs = 1</c>: an absent or empty right-hand side is <c>none</c>, as it is in
	/// <c>do_chzone</c> (<c>src/set.c:384</c>).</para>
	/// </remarks>
	[SharpCommand(Name = "@CHZONE", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "zone"])]
	public async ValueTask<Option<CallState>> ChangeZone(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var targetName = args["0"].Message!.ToPlainText();
		var zoneName = args.TryGetValue("1", out var zoneArg) ? zoneArg.Message!.ToPlainText() : string.Empty;
		var preserve = parser.CurrentState.Switches.Contains("PRESERVE");

		// set.c:380: MAT_NEARBY for the object, which is MAT_EVERYTHING plus MAT_NEAR — you re-zone what
		// is in front of you, not anything you can name. The zone below stays MAT_EVERYTHING (:386).
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, targetName, LocateFlags.All | LocateFlags.OnlyMatchObjectsInLookerLocation,
			async obj => zoneName.Length == 0 || zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase)
				? Reported(await ChangeZoneAsync(parser, executor, obj, new AnyOptionalSharpObject(new None()), preserve,
					noisy: true))
				: await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj => Reported(await ChangeZoneAsync(parser, executor, obj,
						new AnyOptionalSharpObject(zoneObj), preserve, noisy: true)))
		);
	}

	/// <summary>Binds this command instance's services to <see cref="ZoneHelpers.ChangeZoneAsync"/>.</summary>
	private ValueTask<Result<Success>> ChangeZoneAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, AnyOptionalSharpObject zone, bool preserve, bool noisy)
		=> ZoneHelpers.ChangeZoneAsync(parser, Mediator, Database, NotifyService, PermissionService, LockService,
			DidItService, ManipulateSharpObjectService, Configuration, executor, target, zone, preserve, noisy);

	/// <summary>
	/// <c>do_chzone</c> reports its own refusals and returns 0; the command turns that into the error
	/// return softcode sees.
	/// </summary>
	private static CallState Reported(Result<Success> changed)
		=> changed switch
		{
			Success => CallState.Empty,
			Error<string> refused => new CallState(refused.Value)
		};

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

	/// <remarks>
	/// PennMUSH <c>do_chzoneall</c> (<c>src/wiz.c:1015-1056</c>) matches the owner and the zone once,
	/// then calls <c>do_chzone</c> per object with <c>noisy</c> off — "This keeps consistency on things
	/// like flag resetting, etc...". <see cref="ZoneHelpers.ChangeZoneAsync"/> is that call, so the
	/// destination's <c>@lock/chzone</c>, the cycle walk, <c>check_zone_lock</c>'s default lock and the
	/// already-in-that-zone skip all apply here as they do to <c>@CHZONE</c>.
	/// </remarks>
	[SharpCommand(Name = "@CHZONEALL", Switches = ["PRESERVE"], Behavior = CB.Default | CB.EqSplit, CommandLock = "FLAG^WIZARD",
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["old-zone", "new-zone"])]
	public async ValueTask<Option<CallState>> ChangeZoneAll(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var playerName = args["0"].Message!.ToPlainText();
		var zoneName = args.TryGetValue("1", out var zoneArg) ? zoneArg.Message!.ToPlainText() : string.Empty;
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

				// wiz.c:1032-1035: unlike @CHZONE, an empty right-hand side here is a usage error and not
				// "none" — only the literal word unzones.
				if (zoneName.Length == 0)
				{
					return await NotifyService.NotifyAndReturn(
						executor.Object().DBRef,
						errorReturn: ErrorMessages.Returns.MissingArguments,
						notifyMessage: ErrorMessages.Notifications.NoZoneSpecified,
						shouldNotify: true);
				}

				if (zoneName.Equals("none", StringComparison.InvariantCultureIgnoreCase))
				{
					return await ReZoneOwnedAsync(parser, executor, player, new AnyOptionalSharpObject(new None()),
						preserve);
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, zoneName, LocateFlags.All,
					async zoneObj => await ReZoneOwnedAsync(parser, executor, player,
						new AnyOptionalSharpObject(zoneObj), preserve)
				);
			}
		);
	}

	/// <summary>
	/// <c>do_chzoneall</c>'s loop (<c>src/wiz.c:1046-1055</c>): every object the owner owns goes through
	/// <c>do_chzone</c>, and the count is how many of those returned 1.
	/// </summary>
	private async ValueTask<CallState> ReZoneOwnedAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject owner, AnyOptionalSharpObject zone, bool preserve)
	{
		var count = 0;

		await foreach (var obj in Mediator.CreateStream(new GetAllTypedObjectsQuery()))
		{
			var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
			if (objOwner.Object.DBRef.Number != owner.Object().DBRef.Number)
			{
				continue;
			}

			if (await ChangeZoneAsync(parser, executor, obj, zone, preserve, noisy: false) is Success)
			{
				count++;
			}
		}

		await NotifyService.NotifyLocalized(executor,
			nameof(ErrorMessages.Notifications.ZoneChangedForObjectsFormat), executor, count);
		return CallState.Empty;
	}
}
