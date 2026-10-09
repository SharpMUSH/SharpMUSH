using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Services;
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
	/// <remarks>
	/// <c>fun_pcreate</c> (<c>src/fundb.c:2129-2141</c>) hands <c>args[2]</c> to <c>do_pcreate</c>, which
	/// settles it through <c>make_first_free_wrapper</c> before the name and the password
	/// (<c>src/wiz.c:120</c>).
	/// </remarks>
	[SharpFunction(Name = "pcreate", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.WizardOnly)]
	public async ValueTask<CallState> PCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var result = await BuildingHelpers.WithRequestedDbrefsAsync(Mediator, NotifyService, executor,
			[BuildingHelpers.Argument(args, "2")],
			async at => await CreatedPlayerAsync(parser, executor, at[0]));

		// do_pcreate itself says what it made and queues PLAYER`CREATE (src/wiz.c:140-144), so the
		// function does too: fun_pcreate is nothing but a call to it.
		if (result is DBRef player)
		{
			await BuildingHelpers.AnnouncePlayerCreatedAsync(parser, NotifyService, EventService, executor,
				args["0"].Message.ToPlainText(), args["1"].Message.ToPlainText(), player);
		}

		return result switch
		{
			DBRef created => new CallState($"#{created.Number}:{created.CreationMilliseconds}"),
			Error<string> refused => new CallState(refused.Value)
		};
	}

	/// <summary>do_pcreate's three refusals, in its order, with ok_player_name asked by the executor.</summary>
	private async ValueTask<Result<DBRef>> CreatedPlayerAsync(IMUSHCodeParser parser, AnySharpObject executor,
		DBRef? requested)
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
		var name = args["0"].Message.ToPlainText();
		var password = args["1"].Message.ToPlainText();

		if (await Mediator.CreateStream(new GetPlayerQuery(name))
				.AnyAsync(x => x.Object.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
		{
			return new Error<string>(ErrorMessages.Returns.PlayerNameInUse);
		}

		if (!await ValidateService.ValidPlayerName(MarkupText.Plain(name), executor, new None()))
		{
			return new Error<string>(ErrorMessages.Returns.BadPlayerName);
		}

		if (!await ValidateService.Valid(IValidateService.ValidationType.Password, MarkupText.Plain(password), new None()))
		{
			return new Error<string>(ErrorMessages.Returns.BadPassword);
		}

		return await Mediator.Send(new CreatePlayerCommand(
			name,
			password,
			new DBRef(trueLocation == -1 ? 1 : trueLocation),
			defaultHomeDbref,
			startingQuota,
			RequestedDbref: requested?.Number));
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

		return await BuildingHelpers.CreateThingAsync(parser, Mediator, RelationshipCycles, Configuration, ValidateService,
			NotifyService, EventService, PermissionService, executor, args["0"].Message,
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

		return await BuildingHelpers.DigAsync(parser, Mediator, RelationshipCycles, Configuration, NotifyService, EventService,
			PermissionService, LockService, AttributeService, executor, args["0"].Message,
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
			// fundb.c:2156-2162.
			if (await BuildingHelpers.SourceRoomAsync(parser, LocateService, executor, sourceRoomName.ToPlainText())
					is not AnySharpContainer namedRoom)
			{
				return new CallState(ErrorMessages.Returns.InvalidSourceRoom);
			}

			sourceRoom = namedRoom;
		}

		var opened = await BuildingHelpers.WithRequestedDbrefsAsync(Mediator, NotifyService, executor,
			[BuildingHelpers.Argument(args, "3")],
			async at => await OpenedExitAsync(executor, args, sourceRoom, at[0]));

		// Outside the gate: do_real_open's OBJECT`CREATE (create.c:181) runs its handler inline.
		if (opened is DBRef exit)
		{
			await BuildingHelpers.AnnounceCreatedAsync(parser, EventService, executor, exit);
		}

		return opened switch
		{
			DBRef exitDbRef => new CallState(exitDbRef.ToString()),
			Error<string> refused => new CallState(refused.Value)
		};
	}

	/// <summary>The exit itself, and on success the link <c>fun_open</c>'s second argument asks for.</summary>
	private async ValueTask<Result<DBRef>> OpenedExitAsync(AnySharpObject executor,
		IReadOnlyDictionary<string, CallState> args, AnySharpContainer sourceRoom, DBRef? requestedDbref)
		=> await BuildingHelpers.OpenExitAsync(Mediator, RelationshipCycles, Configuration, NotifyService,
			PermissionService, LockService, executor, args["0"].Message, sourceRoom, requestedDbref) switch
		{
			DBRef exitDbRef => await LinkOpenedExitAsync(executor, args, exitDbRef),
			Error<string> refused => refused
		};

	/// <summary>
	/// <c>fun_open</c>'s second argument, which <c>do_real_open</c> links the new exit to
	/// (<c>create.c:165-178</c>) exactly as <c>@open</c>'s is linked.
	/// </summary>
	private async ValueTask<DBRef> LinkOpenedExitAsync(AnySharpObject executor,
		IReadOnlyDictionary<string, CallState> args, DBRef exit)
	{
		if (BuildingHelpers.Argument(args, "1") is { } destinationName)
		{
			await BuildingHelpers.LinkOpenedExitAsync(Mediator, NotifyService, PermissionService, AttributeService,
				executor, exit, destinationName.ToPlainText());
		}

		return exit;
	}

	/// <remarks>
	/// <c>fun_link</c> (<c>src/fundb.c:2219-2237</c>) is one call to <c>do_link</c>, the same routine
	/// <c>@link</c> reaches, with <c>parse_boolean(args[2])</c> standing in for <c>/PRESERVE</c>.
	/// SharpMUSH wrote a second, thinner copy: only a room was a legal exit destination, there was no
	/// link-lock path and so no ownership seizure, the third argument was declared and never read, and
	/// a room's drop-to needed no control at all.
	/// </remarks>
	[SharpFunction(Name = "link", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> Link(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objectName = args["0"].Message.ToPlainText();
		var destName = args["1"].Message.ToPlainText();
		var preserve = args.TryGetValue("2", out var preserveArg) && preserveArg.Message.Truthy(parser);

		return await LinkHelpers.LinkAsync(parser, Mediator, NotifyService, LocateService, PermissionService,
			LockService, AttributeService, FlagAndPowerService, ConnectionService, executor, objectName,
			destName, preserve) switch
		{
			Success => new CallState("1"),
			Error<string> refused => new CallState(refused.Value)
		};
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
			preserveArg.Message.ToPlainText().Equals("preserve", StringComparison.OrdinalIgnoreCase);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message.ToPlainText(), LocateFlags.All,
			async obj => await BuildingHelpers.CloneAsync(parser, Mediator, RelationshipCycles, Configuration, NotifyService,
				PermissionService, LockService, AttributeService, FlagAndPowerService, DidItService,
				EventService, Logger, executor, obj,
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
		var objAttr = args["0"].Message.ToPlainText();

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

	/// <summary>
	/// The services <see cref="TeleportHelpers"/> works through, the same bundle <c>@TELEPORT</c>
	/// hands it from <c>Commands</c>.
	/// </summary>
	private TeleportServices TeleportServices => new(Mediator, NotifyService, LocateService, AttributeService,
		PermissionService, LockService, MoveService, DidItService, CommunicationService, Configuration);

	/// <remarks>
	/// <c>fun_tel</c> (<c>src/fundb.c:2309-2327</c>) is the side-effect gate, the <c>@tel</c> command
	/// restriction, the two flags, then one call to <c>do_teleport</c> — the same routine
	/// <c>@teleport</c> calls. It is therefore the whole of <c>@teleport</c>'s policy, and shares it
	/// through <see cref="TeleportHelpers"/> rather than owning a second copy.
	/// </remarks>
	[SharpFunction(Name = "tel", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged | FunctionFlags.StripAnsi, ParameterNames = ["object", "destination", "silent", "inside"])]
	public async ValueTask<CallState> Tel(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// fundb.c:2317: command_check_byname(executor, "@tel", …). Penn's command_find resolves that
		// name through the same prefix table the dispatcher uses, so the restriction read is the one
		// belonging to whatever `@tel` would actually run: normally @TELEPORT by abbreviation, but a
		// game that registers an exact @TEL (@command/add, or a plugin) puts that command in front of
		// it for typed dispatch, and tel() has to be held to the same one. A name that resolves to
		// nothing is refused, as command_check_byname's null COMMAND_INFO is.
		var telCommand = CommandTrie.For(parser.CommandLibrary).FindShortestMatch("@TEL")?.CommandName ?? "@TEL";

		if (!await CanInvokeLockCommandAsync(parser, executor, telCommand))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// fundb.c:2320-2323: argument 3 is TEL_SILENT and argument 4 is TEL_INSIDE, the /SILENT and
		// /INSIDE switches under other names.
		var silent = args.TryGetValue("2", out var silentArg) && silentArg.Message.Truthy(parser);
		var inside = args.TryGetValue("3", out var insideArg) && insideArg.Message.Truthy(parser);

		await TeleportHelpers.TeleportAsync(parser, TeleportServices, executor,
			args["0"].Message.ToPlainText(), args["1"].Message.ToPlainText(),
			new TeleportOptions(List: false, Inside: inside, Silent: silent));

		// fun_tel writes nothing to the buffer: every refusal is reported to the executor by
		// do_teleport itself, exactly as the command reports it, and success says nothing either.
		return CallState.Empty;
	}
}
