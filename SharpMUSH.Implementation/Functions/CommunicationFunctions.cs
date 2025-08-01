using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.ExecutorObject(parser.Mediator);
		var contents = await parser.Mediator.Send(new GetContentsQuery(await executor.WithoutNone().Where())) ?? [];

		var interactableContents = contents
			.ToAsyncEnumerable()
			.WhereAwait(async obj =>
				await parser.PermissionService.CanInteract(obj.WithRoomOption(), executor.WithoutNone(), InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await parser.NotifyService.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor.WithoutNone(),
				INotifyService.NotificationType.Emit);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> LocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var contents =
			await parser.Mediator.Send(new GetContentsQuery(await parser.LocateService.Room(executor))) ?? [];

		var interactableContents = contents.ToAsyncEnumerable()
			.WhereAwait(async obj =>
				await parser.PermissionService.CanInteract(obj.WithRoomOption(), executor, InteractType.Hear));

		await foreach (var obj in interactableContents)
		{
			await parser.NotifyService.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				INotifyService.NotificationType.Emit);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "message", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

		var orderedArgs = parser.CurrentState.ArgumentsOrdered; 
		var recipients = orderedArgs["0"];
		var message = orderedArgs["1"];
		var objectAndAttribute = orderedArgs["2"];
		var inBetweenArgs = orderedArgs.Skip(3).Take(10);
		var switches = NoParseDefaultEvaluatedArgument(parser, 13, "");

		var playerList = Functions.PopulatedNameList(parser, recipients.Message!.ToPlainText());
		
		// Step 1: Evaluate message into the default object/attribute, pass the arguments into it.
		// Step 2: Send the message to all that want to hear it.
		
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nsemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NoSpoofEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).Known();
		var spoofType = await parser.PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var contents = await parser.Mediator.Send(new GetContentsQuery(await executor.Where())) ?? [];

		await foreach (var obj in contents
			               .ToAsyncEnumerable()
			               .WhereAwait(async x 
				               => await parser.PermissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await parser.NotifyService.Notify(
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
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var spoofType = await parser.PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var contents = await parser.Mediator.Send(new GetContentsQuery(await parser.LocateService.Room(executor))) ?? [];

		await foreach (var obj in contents
			               .ToAsyncEnumerable()
			               .WhereAwait(async x 
				               => await parser.PermissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await parser.NotifyService.Notify(
				obj.WithRoomOption(),
				parser.CurrentState.Arguments["0"].Message!,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "NSOEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSPEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSPROMPT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSREMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSZEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> PrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PROMPT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ZEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}