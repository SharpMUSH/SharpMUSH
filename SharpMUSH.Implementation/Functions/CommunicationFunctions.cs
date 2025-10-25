using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var message = parser.CurrentState.Arguments["0"].Message!;

		await CommunicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			(_, msg) => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> LocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);

		var interactableContents = contents
			.Where(async (obj,_) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor, InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				INotifyService.NotificationType.Emit);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "message", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		/*
	MESSAGE()
  message(<recipients>, <message>, [<object>/]<attribute>[, <arg0>[, ... , <arg9>][, <switches>]])

  message() is the function form of @message/silent, and sends a message, formatted through an attribute, to a list of objects. See 'help @message' for more information.
  
  <switches> is a space-separated list of one or more of "nospoof", "spoof", "oemit" and "remit", and makes message() behaviour as per @message/<switches>. For backwards-compatability reasons, all ten <arg> arguments must be given (even if empty) to use <switches>.
  
  Examples:
  > &formatter #123
  > think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
  Foo Bar Baz
  > &formatter #123=Formatted> [iter(%0,capstr(%i0))]
  > think message(me, Default> foo bar baz, #123/formatter, foo bar baz)
  Formatted> Foo Bar Baz
  
  > think message(here, default, #123/formatter, backwards compatability is annoying sometimes,,,,,,,,,,remit)
  Formatted> Backwards Compatability Is Annoying Sometimes
		 */

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var orderedArgs = parser.CurrentState.ArgumentsOrdered; 
		var recipients = orderedArgs["0"];
		var message = orderedArgs["1"];
		var objectAndAttribute = orderedArgs["2"];
		var inBetweenArgs = orderedArgs.Skip(3).Take(10);
		var switches = ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 13, "");
	
		var playerList = ArgHelpers.NameList(recipients.Message!.ToPlainText());
		
		// Step 1: Evaluate message into the default object/attribute, pass the arguments into it.
		// Step 2: Send the message to all that want to hear it.
		
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nsemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NoSpoofEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var spoofType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);

		await foreach (var obj in contents
			               .Where(async (x,_) 
				               => await PermissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nslemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var spoofType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = await executorLocation.Content(Mediator!);

		await foreach (var obj in contents
			               .Where(async (x,_) 
				               => await PermissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nsoemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;
		
		// For simplicity: emit to executor's location, excluding the specified objects
		// TODO: Support room/obj format like PennMUSH
		var targetRoom = await executor.Where();
		var objectList = ArgHelpers.NameList(objects);
		var excludeObjects = new List<AnySharpObject>();
		
		// Resolve all objects to exclude
		foreach (var obj in objectList)
		{
			var objName = obj.IsT0 ? obj.AsT0.ToString()! : obj.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				objName,
				LocateFlags.All,
				async target =>
				{
					excludeObjects.Add(target);
					return CallState.Empty;
				});
		}
		
		await CommunicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			(_, msg) => message,
			notificationType,
			excludeObjects: excludeObjects);
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "nspemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;
		
		// Check if first argument is an integer list (port list)
		if (IsIntegerList(recipients))
		{
			// Handle port-based messaging using CommunicationService
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();
			
			await CommunicationService!.SendToPortsAsync(executor, ports, (_, msg) => message, notificationType);
			return CallState.Empty;
		}
		
		// Handle object/player-based messaging
		var recipientList = ArgHelpers.NameList(recipients);
		
		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString()! : recipient.AsT1;
			
			// Use LocateAndNotifyIfInvalidWithCallStateFunction for proper error handling
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await NotifyService!.Notify(target, message, executor, notificationType);
					}
					
					return CallState.Empty;
				});
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "nsprompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSAnnounce
			: INotifyService.NotificationType.Announce;
		
		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameList(recipients);
		
		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString()! : recipient.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await NotifyService!.Prompt(target, message, executor, notificationType);
					}
					
					return CallState.Empty;
				});
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "nsremit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;
		
		// Send message to contents of all specified objects
		var objectList = ArgHelpers.NameList(objects);
		
		foreach (var obj in objectList)
		{
			var objName = obj.IsT0 ? obj.AsT0.ToString()! : obj.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				objName,
				LocateFlags.All,
				async target =>
				{
					// Check if target is a container (has contents)
					if (target.IsT0 || target.IsT1)
					{
						var container = target.IsT0 ? (AnySharpContainer)target.AsT0 : target.AsT1;
						await CommunicationService!.SendToRoomAsync(
							executor,
							container,
							(_, msg) => message,
							notificationType);
					}
					
					return CallState.Empty;
				});
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "nszemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Determine notification type based on nospoof permissions
		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;
		
		// Locate the zone object
		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, 
			executor, 
			executor, 
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// TODO: Implement zone emission - requires zone system support
				// For now, this is a placeholder that would need to:
				// 1. Find all rooms with zone == zone parameter
				// 2. Send message to each of those rooms
				// This requires zone system infrastructure not yet implemented
				
				return CallState.Empty;
			});
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "oemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// For simplicity: emit to executor's location, excluding the specified objects
		// TODO: Support room/obj format like PennMUSH
		var targetRoom = await executor.Where();
		var objectList = ArgHelpers.NameList(objects);
		var excludeObjects = new List<AnySharpObject>();
		
		// Resolve all objects to exclude
		foreach (var obj in objectList)
		{
			var objName = obj.IsT0 ? obj.AsT0.ToString()! : obj.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				objName,
				LocateFlags.All,
				async target =>
				{
					excludeObjects.Add(target);
					return CallState.Empty;
				});
		}
		
		await CommunicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			(_, msg) => message,
			INotifyService.NotificationType.Emit,
			excludeObjects: excludeObjects);
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "pemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> PrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Check if first argument is an integer list (port list)
		if (IsIntegerList(recipients))
		{
			// Handle port-based messaging using CommunicationService
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();
			
			await CommunicationService!.SendToPortsAsync(executor, ports, (_, msg) => message, INotifyService.NotificationType.Announce);
			return CallState.Empty;
		}
		
		// Handle object/player-based messaging
		var recipientList = ArgHelpers.NameList(recipients);
		
		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString()! : recipient.AsT1;
			
			// Use LocateAndNotifyIfInvalidWithCallStateFunction for proper error handling
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await NotifyService!.Notify(target, message, executor, INotifyService.NotificationType.Announce);
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

	[SharpFunction(Name = "prompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var recipients = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Handle object/player-based messaging (prompt doesn't support ports)
		var recipientList = ArgHelpers.NameList(recipients);
		
		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString()! : recipient.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService!.CanInteract(target, executor, InteractType.Hear))
					{
						await NotifyService!.Prompt(target, message, executor, INotifyService.NotificationType.Announce);
					}
					
					return CallState.Empty;
				});
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "remit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objects = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Send message to contents of all specified objects
		var objectList = ArgHelpers.NameList(objects);
		
		foreach (var obj in objectList)
		{
			var objName = obj.IsT0 ? obj.AsT0.ToString()! : obj.AsT1;
			
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, 
				executor, 
				executor, 
				objName,
				LocateFlags.All,
				async target =>
				{
					// Check if target is a container (has contents)
					if (target.IsT0 || target.IsT1)
					{
						var container = target.IsT0 ? (AnySharpContainer)target.AsT0 : target.AsT1;
						await CommunicationService!.SendToRoomAsync(
							executor,
							container,
							(_, msg) => message,
							INotifyService.NotificationType.Emit);
					}
					
					return CallState.Empty;
				});
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "zemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX)]
	public static async ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
		{
			return new CallState(Errors.ErrorNoSideFx);
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var zoneName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;
		
		// Locate the zone object
		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, 
			executor, 
			executor, 
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// TODO: Implement zone emission - requires zone system support
				// For now, this is a placeholder that would need to:
				// 1. Find all rooms with zone == zone parameter
				// 2. Send message to each of those rooms
				// This requires zone system infrastructure not yet implemented
				
				return CallState.Empty;
			});
		
		return CallState.Empty;
	}
}