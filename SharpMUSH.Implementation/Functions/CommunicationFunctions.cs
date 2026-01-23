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
	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["message"])]
	public async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var executorLocation = await executor.Where();
		var message = parser.CurrentState.Arguments["0"].Message!;

		await _communicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["message"])]
	public async ValueTask<CallState> LocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var executorLocation = await executor.OutermostWhere();
		var message = parser.CurrentState.Arguments["0"].Message!;

		await _communicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	private const int MaxFunctionArguments = 10;

	[SharpFunction(Name = "message", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["object", "recipient", "message"])]
	public async ValueTask<CallState> Message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var orderedArgs = parser.CurrentState.ArgumentsOrdered;
		var recipients = orderedArgs["0"];
		var defmsg = orderedArgs["1"];
		var objectAndAttribute = orderedArgs["2"];
		var inBetweenArgs = orderedArgs.Skip(3).Take(MaxFunctionArguments)
			.Select((kvp, idx) => new KeyValuePair<string, CallState>(idx.ToString(), kvp.Value))
			.ToList();

		var switchesText = parser.CurrentState.Arguments.TryGetValue("13", out var switchArg)
			? (await switchArg.ParsedMessage())?.ToPlainText() ?? ""
			: "";
		var switchesList = switchesText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		
		var isRemit = switchesList.Contains("remit", StringComparer.OrdinalIgnoreCase);
		var isOemit = switchesList.Contains("oemit", StringComparer.OrdinalIgnoreCase);
		var isNospoof = switchesList.Contains("nospoof", StringComparer.OrdinalIgnoreCase);
		var isSpoof = switchesList.Contains("spoof", StringComparer.OrdinalIgnoreCase);

		await MessageHelpers.ProcessMessageAsync(
			parser, _mediator!, _locateService!, _attributeService!, _notifyService!,
			_permissionService!, _communicationService!, executor,
			recipients.Message!, defmsg.Message!, objectAndAttribute.Message!.ToPlainText(),
			inBetweenArgs, isRemit, isOemit, isNospoof, isSpoof, isSilent: true);

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var spoofType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(_mediator!);

		await foreach (var obj in contents
			               .Where(async (x, _)
				               => await _permissionService.CanInteract(x, executor, InteractType.Hear)))
		{
			await _notifyService!.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nslemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var spoofType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(_mediator!);

		await foreach (var obj in contents
			               .Where(async (x, _)
				               => await _permissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await _notifyService!.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsoemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		// Support room/obj format like PennMUSH: "room/obj1 obj2" emits to room excluding listed objects
		AnySharpContainer targetRoom;
		var excludeObjects = new HashSet<AnySharpObject>();

		var roomObjFormat = ArgHelpers.ParseRoomObjectFormat(objects);
		if (roomObjFormat.HasValue)
		{
			// Parse room/obj format
			var (roomName, objectNames) = roomObjFormat.Value;

			// Locate the target room
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
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

			// Resolve objects to exclude
			foreach (var objName in objectNames)
			{
				await _locateService.LocateAndNotifyIfInvalidWithCallStateFunction(
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
			// Traditional format: emit to executor's location, excluding listed objects
			targetRoom = await executor.Where();
			var objectList = ArgHelpers.NameList(objects);

			foreach (var obj in objectList)
			{
				var objName = obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1;

				await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

		await _communicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			notificationType,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpFunction(Name = "nspemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Determine notification type based on nospoof permissions
		var notificationType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;

		// Check if first argument is an integer list (port list)
		if (IsIntegerList(recipients))
		{
			// Handle port-based messaging using CommunicationService
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();

			await _communicationService!.SendToPortsAsync(executor, ports, _ => message, notificationType);
			return CallState.Empty;
		}

		// Handle object/player-based messaging
		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			// Use LocateAndNotifyIfInvalidWithCallStateFunction for proper error handling
			await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await _permissionService.CanInteract(target, executor, InteractType.Hear))
					{
						await _notifyService!.Notify(target, message, executor, notificationType);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsprompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Determine notification type based on nospoof permissions
		var notificationType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;

		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await _permissionService.CanInteract(target, executor, InteractType.Hear))
					{
						await _notifyService!.Prompt(target, message, executor, notificationType);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsremit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var notificationType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
				await _communicationService!.SendToRoomAsync(
					executor,
					container,
					_ => message,
					notificationType);

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpFunction(Name = "nszemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Determine notification type based on nospoof permissions
		var notificationType = await _permissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		// Locate the zone object
		await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// Find all objects in the zone
				var zoneObjects = _mediator!.CreateStream(new GetObjectsByZoneQuery(zone));
				
				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);
				
				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = _mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await _notifyService!.Notify(content.WithRoomOption(), message, executor, notificationType);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpFunction(Name = "oemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["message"])]
	public async ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Support room/obj format like PennMUSH: "room/obj1 obj2" emits to room excluding listed objects
		AnySharpContainer targetRoom;
		var excludeObjects = new HashSet<AnySharpObject>();

		var roomObjFormat = ArgHelpers.ParseRoomObjectFormat(objects);
		if (roomObjFormat.HasValue)
		{
			// Parse room/obj format
			var (roomName, objectNames) = roomObjFormat.Value;

			// Locate the target room
			var locateResult = await _locateService!.LocateAndNotifyIfInvalid(
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

			// Resolve objects to exclude
			foreach (var objName in objectNames)
			{
				await _locateService.LocateAndNotifyIfInvalidWithCallStateFunction(
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
			// Traditional format: emit to executor's location, excluding listed objects
			targetRoom = await executor.Where();
			var objectList = ArgHelpers.NameList(objects);

			// Resolve all objects to exclude
			foreach (var obj in objectList)
			{
				var objName = obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1;

				await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

		await _communicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			INotifyService.NotificationType.Emit,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpFunction(Name = "pemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> PrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		if (IsIntegerList(recipients))
		{
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();

			await _communicationService!.SendToPortsAsync(executor, ports, _ => message,
				INotifyService.NotificationType.Announce);
			return CallState.Empty;
		}

		var recipientList = ArgHelpers.NameListString(recipients);

		foreach (var recipient in recipientList)
		{
			await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipient,
				LocateFlags.All,
				async target =>
				{
					if (await _permissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await _notifyService!.Notify(target, message, executor);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	private static bool IsIntegerList(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return false;

		var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return tokens.Length > 0 && tokens.All(token => long.TryParse(token, out _));
	}

	[SharpFunction(Name = "prompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameListString(recipients);

		foreach (var recipient in recipientList)
		{
			await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipient,
				LocateFlags.All,
				async target =>
				{
					if (await _permissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await _notifyService!.Prompt(target, message, executor, INotifyService.NotificationType.Announce);
					}

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "remit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Send message to contents of all specified objects
		var objectList = ArgHelpers.NameListString(objects);

		foreach (var obj in objectList)
		{
			await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

					await _communicationService!.SendToRoomAsync(
						executor,
						target.AsContainer,
						_ => message,
						INotifyService.NotificationType.Emit);

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "zemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		// Locate the zone object
		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// Find all objects in the zone
				var zoneObjects = _mediator!.CreateStream(new GetObjectsByZoneQuery(zone));
				
				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);
				
				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = _mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await _notifyService!.Notify(content.WithRoomOption(), message, executor, INotifyService.NotificationType.Emit);
					}
				}

				return CallState.Empty;
			});
	}
}