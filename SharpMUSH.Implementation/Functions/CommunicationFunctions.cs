using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
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

	[SharpFunction(Name = "nsoemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
			// Handle port-based messaging
			// IsIntegerList already validated these are parseable integers
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();
			
			await NotifyService!.Notify(ports, message, executor, notificationType);
		}
		else
		{
			// Handle object/player-based messaging
			var recipientList = ArgHelpers.NameList(recipients);
			
			foreach (var recipient in recipientList)
			{
				await recipient.Match(
					async dbref =>
					{
						await NotifyService!.Notify(dbref, message, executor, notificationType);
					},
					async name =>
					{
						var maybeFound = await LocateService!.LocatePlayer(parser, executor, executor, name);
						if (maybeFound.TryPickT0(out var player, out var _))
						{
							await NotifyService!.Notify(player, message, executor, notificationType);
						}
					});
			}
		}
		
		return CallState.Empty;
	}

	[SharpFunction(Name = "nsprompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nsremit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nszemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "oemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
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
			// Handle port-based messaging
			// IsIntegerList already validated these are parseable integers
			var ports = recipients.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(long.Parse)
				.ToArray();
			
			await NotifyService!.Notify(ports, message, executor, INotifyService.NotificationType.Announce);
		}
		else
		{
			// Handle object/player-based messaging
			var recipientList = ArgHelpers.NameList(recipients);
			
			foreach (var recipient in recipientList)
			{
				await recipient.Match(
					async dbref =>
					{
						await NotifyService!.Notify(dbref, message, executor, INotifyService.NotificationType.Announce);
					},
					async name =>
					{
						var maybeFound = await LocateService!.LocatePlayer(parser, executor, executor, name);
						if (maybeFound.TryPickT0(out var player, out var _))
						{
							await NotifyService!.Notify(player, message, executor, INotifyService.NotificationType.Announce);
						}
					});
			}
		}
		
		return CallState.Empty;
	}
	
	private static bool IsIntegerList(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return false;
		
		var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return tokens.Length > 0 && tokens.All(token => long.TryParse(token, out _));
	}

	[SharpFunction(Name = "prompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "remit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "zemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}