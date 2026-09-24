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
		var name = args["0"].Message!.ToPlainText();
		var password = args["1"].Message!.ToPlainText();

		// do_pcreate's three refusals, in its order, with ok_player_name asked by the executor.
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await Mediator.CreateStream(new GetPlayerQuery(name))
				.AnyAsync(x => x.Object.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
		{
			return new CallState(ErrorMessages.Returns.PlayerNameInUse);
		}

		if (!await ValidateService.ValidPlayerName(MarkupText.Plain(name), executor, new None()))
		{
			return new CallState(ErrorMessages.Returns.BadPlayerName);
		}

		if (!await ValidateService.Valid(IValidateService.ValidationType.Password, MarkupText.Plain(password), new None()))
		{
			return new CallState(ErrorMessages.Returns.BadPassword);
		}

		var created = await Mediator.Send(new CreatePlayerCommand(
			name,
			password,
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

	/// <remarks>
	/// <c>fun_dig</c> (<c>src/fundb.c:2177-2189</c>) hands <c>args</c> straight to <c>do_dig</c>, so it
	/// opens and links both exits and reads all three requested dbrefs. SharpMUSH wrote a thinner copy
	/// that dug the room and nothing else.
	/// </remarks>
	[SharpFunction(Name = "dig", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Dig(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await BuildingHelpers.DigAsync(Mediator, Database, Configuration, NotifyService, PermissionService,
			LockService, executor, args["0"].Message!,
			BuildingHelpers.Argument(args, "1"), BuildingHelpers.Argument(args, "2"),
			BuildingHelpers.Argument(args, "3"), BuildingHelpers.Argument(args, "4"),
			BuildingHelpers.Argument(args, "5")) switch
		{
			DBRef room => new CallState(room.ToString()),
			Error<string> refused => new CallState(refused.Value)
		};
	}

	/// <remarks>
	/// <c>fun_open</c> (<c>src/fundb.c:2144-2173</c>) is <em>not</em> <c>@open</c>'s argument mapping:
	/// it is exit, destination, source room, requested dbref, and it has no return exit at all. A
	/// source room that matches nothing is <c>#-1 INVALID SOURCE ROOM</c>. SharpMUSH declared all four
	/// and read none of them, so every call opened an unlinked exit wherever the caller stood.
	/// </remarks>
	[SharpFunction(Name = "open", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged)]
	public async ValueTask<CallState> Open(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var sourceRoom = await executor.Where();
		if (BuildingHelpers.Argument(args, "2") is { } sourceRoomName)
		{
			if (await LocateService.Locate(parser, executor, executor, sourceRoomName.ToPlainText(),
					LocateFlags.All) is not (AnySharpObject and SharpRoom namedRoom))
			{
				return new CallState(ErrorMessages.Returns.InvalidSourceRoom);
			}

			sourceRoom = namedRoom;
		}

		return await BuildingHelpers.RequestedDbrefsAsync(Database, NotifyService, executor,
			BuildingHelpers.Argument(args, "3")) switch
		{
			DBRef?[] at => await OpenedExitAsync(parser, executor, args, sourceRoom, at[0]),
			Error<string> refused => new CallState(refused.Value)
		};
	}

	/// <summary>The exit itself, and on success the link <c>fun_open</c>'s second argument asks for.</summary>
	private async ValueTask<CallState> OpenedExitAsync(IMUSHCodeParser parser, AnySharpObject executor,
		IReadOnlyDictionary<string, CallState> args, AnySharpContainer sourceRoom, DBRef? requestedDbref)
		=> await BuildingHelpers.OpenExitAsync(Mediator, Database, Configuration, NotifyService,
			PermissionService, LockService, executor, args["0"].Message!, sourceRoom, requestedDbref) switch
		{
			DBRef exitDbRef => new CallState(await LinkOpenedExitAsync(parser, executor, args, exitDbRef)),
			Error<string> refused => new CallState(refused.Value)
		};

	/// <summary>
	/// <c>fun_open</c>'s second argument, which <c>do_real_open</c> links the new exit to
	/// (<c>create.c:160-172</c>). An exit may lead to any container; anywhere else, or anywhere the
	/// executor may not link into, leaves the exit unlinked and says so, as Penn does.
	/// </summary>
	private async ValueTask<string> LinkOpenedExitAsync(IMUSHCodeParser parser, AnySharpObject executor,
		IReadOnlyDictionary<string, CallState> args, DBRef exit)
	{
		if (BuildingHelpers.Argument(args, "1") is not { } destinationName)
		{
			return exit.ToString();
		}

		if (await LocateService.Locate(parser, executor, executor, destinationName.ToPlainText(), LocateFlags.All)
				is not AnySharpObject destination
			|| !destination.IsContainer
			|| !await PermissionService.CanLinkToAsync(executor, destination))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CantLinkToThat), executor);
			return exit.ToString();
		}

		if (await Mediator.Send(new GetObjectNodeQuery(exit)) is not (AnySharpObject and SharpExit exitObj))
		{
			throw new InvalidOperationException("The exit just opened must exist.");
		}

		await Mediator.Send(new LinkExitCommand(exitObj, destination.AsContainer));

		return exit.ToString();
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
	/// <c>@clone</c> makes, with <c>preserve</c> as a fourth argument instead of a switch and
	/// <c>args[2]</c> as the requested dbref (<c>fundb.c:2192-2212</c>).
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
			async obj => await BuildingHelpers.CloneAsync(parser, Mediator, Configuration, NotifyService, PermissionService,
				AttributeService, ManipulateSharpObjectService, DidItService, EventService, Logger, executor, obj,
				args.TryGetValue("1", out var newName) ? newName.Message : null, preserve,
				BuildingHelpers.Argument(args, "2")) switch
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
