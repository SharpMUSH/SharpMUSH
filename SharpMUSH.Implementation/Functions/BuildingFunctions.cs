using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	// This is not directly compatible with functions that expect just a DBREF (#1234).
	// Consider adding a configuration option for backward compatibility mode.
	[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.WizardOnly)]
	public async ValueTask<CallState> PCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var defaultHome = Configuration.CurrentValue.Database.DefaultHome;
		var defaultHomeDbref = new DBRef((int)defaultHome);
		var startingQuota = (int)Configuration.CurrentValue.Limit.StartingQuota;
		var args = parser.CurrentState.Arguments;
		var location = await Mediator.Send(new GetObjectNodeQuery(new DBRef
		{
			Number = Convert.ToInt32(Configuration.CurrentValue.Database.PlayerStart)
		}));

		var trueLocation = location.Object()?.Key ?? -1;

		var created = await Mediator.Send(new CreatePlayerCommand(
			args["0"].Message!.ToPlainText(),
			args["1"].Message!.ToPlainText(),
			new DBRef(trueLocation == -1 ? 1 : trueLocation),
			defaultHomeDbref,
			startingQuota));

		return new CallState($"#{created.Number}:{created.CreationMilliseconds}");
	}

	/// <remarks>
	/// <c>fun_create</c> (<c>src/fundb.c</c>) passes <c>args[2]</c> straight to <c>do_create</c>, so the
	/// function's third argument is the same requested dbref the command's is, under the same
	/// <c>Pick_DBRefs</c> gate.
	/// </remarks>
	[SharpFunction(Name = "create", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Create(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		return await BuildingHelpers.CreateThingAsync(parser, Mediator, Database, Configuration, ValidateService,
			NotifyService, EventService, PermissionService, executor, args["0"].Message!,
			args.TryGetValue("2", out var requestedDbref) ? requestedDbref.Message : null) switch
		{
			// PennMUSH fun_create hands do_create's dbref to safe_dbref, which writes #n and not an
			// objid (src/fundb.c).
			DBRef thing => new CallState($"#{thing.Number}"),
			Error<string> error => new CallState(error.Value)
		};
	}

	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var roomName = args["0"].Message!.ToPlainText();

		if (string.IsNullOrWhiteSpace(roomName))
		{
			return ErrorMessages.Returns.BadObjectName;
		}

		var response = await Mediator.Send(new CreateRoomCommand(
			roomName,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new CallState(response.ToString());
	}

	[SharpFunction(Name = "open", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var exitName = args["0"].Message!.ToPlainText();

		// Parse exit name and aliases
		var exitParts = exitName.Split(';');
		var primaryName = exitParts[0];
		var aliases = exitParts[1..];

		// Get source location (default to executor's location)
		var sourceRoom = await executor.Where();

		// Optional: arg 1 could be destination, arg 2 could be source room
		// For now, keep it simple - create exit at executor's location

		// Check permissions
		if (!await PermissionService.Controls(executor, sourceRoom.WithExitOption()))
		{
			return ErrorMessages.Returns.PermissionDenied;
		}

		// Create the exit
		var exitDbRef = await Mediator.Send(new CreateExitCommand(
			primaryName,
			aliases,
			sourceRoom,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new CallState(exitDbRef.ToString());
	}

	[SharpFunction(Name = "link", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objectName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async exitObj =>
			{
				if (!await PermissionService.Controls(executor, exitObj))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				if (exitObj is SharpExit exit)
				{
					if (destName.Equals(LinkTypeHome, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Plain(LinkTypeHome));
						return "1";
					}
					else if (destName.Equals(LinkTypeVariable, StringComparison.InvariantCultureIgnoreCase))
					{
						await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Plain(LinkTypeVariable));
						return "1";
					}

					// Link to a room
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (destObj is not SharpRoom destinationRoom)
							{
								return ErrorMessages.Returns.InvalidDestination;
							}

							if (!await PermissionService.Controls(executor, destObj) && !await destObj.HasFlag("LINK_OK"))
							{
								return ErrorMessages.Returns.PermissionDenied;
							}

							await AttributeService.SetAttributeAsync(executor, exitObj, AttrLinkType, MarkupText.Empty);
							await Mediator.Send(new LinkExitCommand(exit, destinationRoom));

							return "1";
						}
					);
				}
				else if (exitObj is SharpThing or SharpPlayer)
				{
					// Set home for thing or player
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							// create.c:395-399: a home is anything that is not an exit, and not the object itself.
							if (!destObj.IsContainer || destObj.Object().DBRef.Equals(exitObj.Object().DBRef))
							{
								return ErrorMessages.Returns.InvalidDestination;
							}

							// create.c:404. ABODE is ROOM-only in the flag seed, as in PennMUSH, so a
							// player or thing destination is gated on control alone. Penn's following
							// room == HOME guard (create.c:412) is unreachable: this branch matches
							// with MAT_EVERYTHING, which has no home entry, and only
							// parse_linkable_room ever yields HOME.
							if (!await PermissionService.Controls(executor, destObj) && !await destObj.HasFlag("ABODE"))
							{
								return ErrorMessages.Returns.PermissionDenied;
							}

							await Mediator.Send(new SetObjectHomeCommand(exitObj.AsContent, destObj.AsContainer));
							return "1";
						}
					);
				}
				else if (exitObj is SharpRoom room)
				{
					// Set drop-to for room
					return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
						executor, executor, destName, LocateFlags.All,
						async destObj =>
						{
							if (destObj is not SharpRoom dropTo)
							{
								return ErrorMessages.Returns.InvalidDestination;
							}

							await Mediator.Send(new LinkRoomCommand(room, dropTo));
							return "1";
						}
					);
				}

				return ErrorMessages.Returns.InvalidObjectType;
			});
	}

	/// <remarks>
	/// <c>fun_clone</c> (<c>src/fundb.c</c>) is one call to <c>do_clone</c>, the same one
	/// <c>@clone</c> makes, with <c>preserve</c> as a fourth argument instead of a switch. The third
	/// argument is a requested dbref, which SharpMUSH does not yet read here (#1084 covers
	/// <c>@create</c>/<c>create()</c> only).
	/// </remarks>
	[SharpFunction(Name = "clone", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Clone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var preserve = args.TryGetValue("3", out var preserveArg) &&
			preserveArg.Message!.ToPlainText().Equals("preserve", StringComparison.OrdinalIgnoreCase);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async obj => await BuildingHelpers.CloneAsync(parser, Mediator, NotifyService, PermissionService,
				AttributeService, ManipulateSharpObjectService, DidItService, EventService, Logger, executor, obj,
				args.TryGetValue("1", out var newName) ? newName.Message : null, preserve) switch
			{
				DBRef clone => new CallState(clone.ToString()),
				Error<string> error => new CallState(error.Value)
			}
		);
	}

	[SharpFunction(Name = "wipe", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public async ValueTask<CallState> Wipe(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objAttr = args["0"].Message!.ToPlainText();

		// wipe(<object>[/<attribute pattern>]) - the same argument @wipe takes, because
		// PennMUSH's fun_wipe hands it straight to do_wipe (src/set.c). The pattern half is not
		// optional decoration: without it every call wiped the whole object.
		if (HelperFunctions.SplitDbRefAndOptionalAttr(objAttr) is not { Object: var objectName, Attribute: var maybeAttribute })
		{
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async obj =>
			{
				if (!await PermissionService.Controls(executor, obj))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				if (await obj.HasFlag("SAFE"))
				{
					await NotifyService.NotifyLocalized(executor,
						nameof(ErrorMessages.Notifications.ObjectIsProtectedSafe), executor);
					return ErrorMessages.Returns.Safe;
				}

				// Everything else - the per-match write gate, the ancestor walk, the wizard- and
				// safe-attribute guards, and do_wipe's own per-match/tally reporting - belongs to
				// ClearAttributeAsync's wipe branch, exactly as it does for @WIPE. Enumerating the
				// attributes here and firing raw ClearAttributeCommands skipped all of it.
				var attributePattern = string.IsNullOrEmpty(maybeAttribute) ? "**" : maybeAttribute;
				await AttributeService.ClearAttributeAsync(executor, obj, attributePattern,
					IAttributeService.AttributePatternMode.Wildcard);

				// PennMUSH's wipe() "returns nothing" (help WIPE()); it is @wipe's side effect
				// exposed as a function, not a reporting call.
				return string.Empty;
			});
	}

	[SharpFunction(Name = "tel", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objectName = args["0"].Message!.ToPlainText();
		var destName = args["1"].Message!.ToPlainText();

		// fundb.c:2321-2322: the third argument is TEL_SILENT, which safe_tel passes through as
		// nomovemsgs. TEL_DEFAULT carries no silence, so an unqualified tel() announces the move.
		// The fourth, TEL_INSIDE, only decides the player-into-player case, which this does not model.
		var quiet = args.TryGetValue("2", out var quietArg) && quietArg.Message!.Truthy();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, objectName, LocateFlags.All,
			async targetObj =>
			{
				if (targetObj.IsRoom)
				{
					return ErrorMessages.Returns.CannotTeleport;
				}

				if (!await PermissionService.Controls(executor, targetObj))
				{
					return ErrorMessages.Returns.CannotTeleport;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
					executor, executor, destName, LocateFlags.All,
					async destObj =>
					{
						if (destObj.IsExit)
						{
							return ErrorMessages.Returns.InvalidDestination;
						}

						var destinationContainer = destObj.AsContainer;
						var targetContent = targetObj.AsContent;

						if (await MoveService.WouldCreateLoop(targetContent, destinationContainer))
						{
							return ErrorMessages.Returns.WouldCreateLoop;
						}

						// fundb.c:2326 hands the whole thing to do_teleport, whose move is safe_tel
						// (wiz.c:578): the move triads fire, and STICKY luggage is stripped on a
						// cross-owner hop.
						var moveResult = await MoveService.SafeTel(
							parser, targetContent, destinationContainer, quiet,
							executor.Object().DBRef, "tel()");

						return moveResult is Error<string>
							? ErrorMessages.Returns.CannotTeleport
							: "1";
					});
			});
	}
}
