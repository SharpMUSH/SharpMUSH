using SharpMUSH.Database;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// The Speech lock every emit-family function has to clear before it may put a message into
	/// <paramref name="room"/>.
	/// <para>
	/// PennMUSH reaches these functions through the very same handlers as the commands —
	/// <c>fun_emit</c> calls <c>do_emit</c>, <c>fun_lemit</c> calls <c>do_lemit</c>,
	/// <c>fun_remit</c> calls <c>do_remit</c> (<c>src/funmisc.c:221-296</c>) — so the
	/// <c>eval_lock_with(..., Speech_Lock, ...)</c> in <c>src/speech.c</c> gates softcode exactly
	/// as it gates the typed command. Enforcing it in the commands alone leaves the lock a
	/// suggestion, since <c>emit()</c> is reachable from any softcode the locked-out player runs.
	/// </para>
	/// </summary>
	/// <param name="room">The room the message would land in.</param>
	/// <param name="executor">Who is trying to speak.</param>
	/// <param name="notificationKey">
	/// <see cref="ErrorMessages.Notifications.MayNotSpeakHere"/> for the executor's own location,
	/// <see cref="ErrorMessages.Notifications.MayNotSpeakThere"/> for any other room, matching
	/// PennMUSH's two <c>fail_lock</c> strings.
	/// </param>
	private async ValueTask<bool> CanSpeakIn(
		AnySharpContainer room,
		AnySharpObject executor,
		string notificationKey)
	{
		if (await LockService.Evaluate(LockType.Speech, room.WithExitOption(), executor))
		{
			return true;
		}

		await NotifyService.NotifyLocalized(executor, notificationKey, executor);
		return false;
	}

	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorLocation = await executor.Where();
		var message = parser.CurrentState.Arguments["0"].Message!;

		if (!await CanSpeakIn(executorLocation, executor,
					nameof(ErrorMessages.Notifications.MayNotSpeakHere)))
		{
			return CallState.Empty;
		}

		await CommunicationService.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> LocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var executorLocation = await executor.OutermostWhere();
		var message = parser.CurrentState.Arguments["0"].Message!;

		if (!await CanSpeakIn(executorLocation, executor,
					nameof(ErrorMessages.Notifications.MayNotSpeakThere)))
		{
			return CallState.Empty;
		}

		await CommunicationService.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	private const int MaxFunctionArguments = 10;

	[SharpFunction(Name = "message", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["object", "recipient", "message"])]
	public async ValueTask<CallState> Message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var recipients = orderedArgs["0"];
		var defmsg = orderedArgs["1"];
		var objectAndAttribute = orderedArgs["2"];
		var inBetweenArgs = orderedArgs.Skip(3).Take(MaxFunctionArguments)
			.Select((kvp, idx) => new KeyValuePair<string, CallState>(idx.ToString(), kvp.Value));

		var switchesText = parser.CurrentState.Arguments.TryGetValue("13", out var switchArg)
			? (await switchArg.ParsedMessage())?.ToPlainText() ?? ""
			: "";
		var switchesList = switchesText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		var isRemit = switchesList.Contains("remit", StringComparer.OrdinalIgnoreCase);
		var isOemit = switchesList.Contains("oemit", StringComparer.OrdinalIgnoreCase);
		var isNospoof = switchesList.Contains("nospoof", StringComparer.OrdinalIgnoreCase);
		var isSpoof = switchesList.Contains("spoof", StringComparer.OrdinalIgnoreCase);

		var result = await MessageHelpers.ProcessMessageAsync(
			parser, Mediator, LocateService, AttributeService, NotifyService,
			PermissionService, CommunicationService, executor,
			recipients.Message!, defmsg.Message!, objectAndAttribute.Message!.ToPlainText(),
			inBetweenArgs, isRemit, isOemit, isNospoof, isSpoof, isSilent: true);

		return CallState.Empty with { HadErrors = result.HadErrors };
	}

	[SharpFunction(Name = "nsemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var spoofType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();

		if (!await CanSpeakIn(executorLocation, executor,
					nameof(ErrorMessages.Notifications.MayNotSpeakHere)))
		{
			return CallState.Empty;
		}

		var contents = executorLocation.Content(Mediator);

		await foreach (var obj in contents
										 .Where(async (x, _)
											 => await PermissionService.CanInteract(executor, x, InteractType.Hear)))
		{
			await NotifyService.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nslemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var spoofType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		// nslemit() is @nslemit is do_lemit, which speaks into absolute_room(), not the immediate
		// location (PennMUSH src/speech.c:1333). Reading Where() here made it a second nsemit().
		var executorLocation = await executor.OutermostWhere();

		if (!await CanSpeakIn(executorLocation, executor,
					nameof(ErrorMessages.Notifications.MayNotSpeakThere)))
		{
			return CallState.Empty;
		}

		var contents = executorLocation.Content(Mediator);

		await foreach (var obj in contents
										 .Where(async (x, _)
											 => await PermissionService.CanInteract(executor, x, InteractType.Hear)))
		{
			await NotifyService.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsoemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		// Support room/obj format like PennMUSH: "room/obj1 obj2" emits to room excluding listed objects
		AnySharpContainer targetRoom;
		var excludeObjects = new HashSet<AnySharpObject>();

		var roomObjFormat = ArgHelpers.ParseRoomObjectFormat(objects);
		if (roomObjFormat.HasValue)
		{
			var (roomName, objectNames) = roomObjFormat.Value;

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				roomName,
				LocateFlags.All);

			if (!locateResult.IsValid())
			{
				return CallState.Empty;
			}

			var locatedObject = locateResult.WithoutError().WithoutNone();
			if (!locatedObject.IsContainer)
			{
				return CallState.Empty;
			}

			targetRoom = locatedObject.AsContainer;

			foreach (var objName in objectNames)
			{
				await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					});
			}
		}
		else
		{
			targetRoom = await executor.Where();
			var objectList = ArgHelpers.NameList(objects);

			foreach (var obj in objectList)
			{
				var objName = obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1;

				await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					});
			}
		}

		await CommunicationService.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			notificationType,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpFunction(Name = "nspemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;

		if (TryParsePorts(recipients, out var ports))
		{
			await CommunicationService.SendToPortsAsync(executor, ports, _ => message, notificationType);
			return CallState.Empty;
		}

		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(executor, target, InteractType.Hear))
					{
						await NotifyService.Notify(target, message, executor, notificationType);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsprompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;

		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(executor, target, InteractType.Hear))
					{
						await NotifyService.Prompt(target, message, executor, notificationType);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsremit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			objects,
			LocateFlags.All,
			async target =>
			{
				if (!target.IsContainer)
				{
					return CallState.Empty;
				}

				var container = target.AsContainer;

				if (!await CanSpeakIn(container, executor,
							nameof(ErrorMessages.Notifications.MayNotSpeakThere)))
				{
					return CallState.Empty;
				}

				await CommunicationService.SendToRoomAsync(
					executor,
					container,
					_ => message,
					notificationType);

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpFunction(Name = "nszemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				var zoneObjects = Mediator.CreateStream(new GetObjectsByZoneQuery(zone));

				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);

				await foreach (var room in rooms)
				{
					var roomContents = Mediator.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService.Notify(content.WithRoomOption(), message, executor, notificationType);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpFunction(Name = "oemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Support room/obj format like PennMUSH: "room/obj1 obj2" emits to room excluding listed objects
		AnySharpContainer targetRoom;
		var excludeObjects = new HashSet<AnySharpObject>();

		var roomObjFormat = ArgHelpers.ParseRoomObjectFormat(objects);
		if (roomObjFormat.HasValue)
		{
			var (roomName, objectNames) = roomObjFormat.Value;

			var locateResult = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				roomName,
				LocateFlags.All);

			if (!locateResult.IsValid())
			{
				return CallState.Empty;
			}

			var locatedObject = locateResult.WithoutError().WithoutNone();
			if (!locatedObject.IsContainer)
			{
				return CallState.Empty;
			}

			targetRoom = locatedObject.AsContainer;

			foreach (var objName in objectNames)
			{
				await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					});
			}
		}
		else
		{
			targetRoom = await executor.Where();
			var objectList = ArgHelpers.NameList(objects);

			foreach (var obj in objectList)
			{
				var objName = obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1;

				await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					});
			}
		}

		await CommunicationService.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			INotifyService.NotificationType.Emit,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpFunction(Name = "pemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> PrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		if (TryParsePorts(recipients, out var ports))
		{
			await CommunicationService.SendToPortsAsync(executor, ports, _ => message,
				INotifyService.NotificationType.Announce);
			return CallState.Empty;
		}

		var recipientList = ArgHelpers.NameListString(recipients);

		foreach (var recipient in recipientList)
		{
			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipient,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(executor, target, InteractType.Hear))
					{
						await NotifyService.Notify(target, message, executor);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	/// <summary>
	/// A recipient list made entirely of descriptor numbers, which pemit() sends to as ports rather
	/// than matching as names.
	/// </summary>
	private static bool TryParsePorts(string recipients, out long[] ports)
	{
		var tokens = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		ports = new long[tokens.Length];

		foreach (var (i, token) in tokens.Index())
		{
			if (!long.TryParse(token, out ports[i]))
			{
				return false;
			}
		}

		return tokens.Length > 0;
	}

	[SharpFunction(Name = "prompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameListString(recipients);

		foreach (var recipient in recipientList)
		{
			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipient,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(executor, target, InteractType.Hear))
					{
						await NotifyService.Prompt(target, message, executor);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "remit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var objectList = ArgHelpers.NameListString(objects);

		foreach (var obj in objectList)
		{
			await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				obj,
				LocateFlags.All,
				async target =>
				{
					if (!target.IsContainer)
					{
						return CallState.Empty;
					}

					if (!await CanSpeakIn(target.AsContainer, executor,
								nameof(ErrorMessages.Notifications.MayNotSpeakThere)))
					{
						return CallState.Empty;
					}

					await CommunicationService.SendToRoomAsync(
						executor,
						target.AsContainer,
						_ => message,
						INotifyService.NotificationType.Emit);

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "zemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				var zoneObjects = Mediator.CreateStream(new GetObjectsByZoneQuery(zone));

				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);

				await foreach (var room in rooms)
				{
					var roomContents = Mediator.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService.Notify(content.WithRoomOption(), message, executor, INotifyService.NotificationType.Emit);
					}
				}

				return CallState.Empty;
			});
	}
}