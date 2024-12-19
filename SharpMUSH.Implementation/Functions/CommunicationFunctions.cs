using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using static SharpMUSH.Library.Services.IPermissionService;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.ExecutorObject(parser.Mediator);
		var contents = await parser.Mediator.Send(new GetContentsQuery(executor.WithoutNone().Where)) ?? [];
		var heard = new List<DBRef>();

		foreach (var obj in contents)
		{
			if (parser.PermissionService.CanInteract(obj.WithRoomOption(), executor.WithoutNone(), InteractType.Hear))
			{
				heard.Add(obj.Object().DBRef);

				await parser.NotifyService.Notify(
					obj.WithRoomOption(),
					parser.CurrentState.Arguments["0"].Message!,
					executor.WithoutNone(),
					INotifyService.NotificationType.Emit);
			}
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> lemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.ExecutorObject(parser.Mediator);
		var contents = await parser.Mediator.Send(new GetContentsQuery(parser.LocateService.Room(executor.WithoutNone()))) ?? [];
		var heard = new List<DBRef>();

		foreach (var obj in contents)
		{
			if (parser.PermissionService.CanInteract(obj.WithRoomOption(), executor.WithoutNone(), InteractType.Hear))
			{
				heard.Add(obj.Object().DBRef);

				await parser.NotifyService.Notify(
					obj.WithRoomOption(),
					parser.CurrentState.Arguments["0"].Message!,
					executor.WithoutNone(),
					INotifyService.NotificationType.Emit);
			}
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "MESSAGE", MinArgs = 3, MaxArgs = 14, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> message(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "nsemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> NSEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var spoofType = parser.PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var contents = await parser.Mediator.Send(new GetContentsQuery(executor.Where)) ?? [];
		var heard = new List<DBRef>();

		foreach (var obj in contents)
		{
			if (parser.PermissionService.CanInteract(obj.WithRoomOption(), executor, InteractType.Hear))
			{
				heard.Add(obj.Object().DBRef);

				await parser.NotifyService.Notify(
					obj.WithRoomOption(),
					parser.CurrentState.Arguments["0"].Message!,
					executor,
					spoofType);
			}
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "nslemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> nslemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
		var spoofType = parser.PermissionService.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var contents = await parser.Mediator.Send(new GetContentsQuery(parser.LocateService.Room(executor))) ?? [];
		var heard = new List<DBRef>();

		foreach (var obj in contents)
		{
			if (parser.PermissionService.CanInteract(obj.WithRoomOption(), executor, InteractType.Hear))
			{
				heard.Add(obj.Object().DBRef);

				await parser.NotifyService.Notify(
					obj.WithRoomOption(),
					parser.CurrentState.Arguments["0"].Message!,
					executor,
					spoofType);
			}
		}

		return CallState.Empty;
	}

	[SharpFunction(Name = "NSOEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nsoemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSPEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nspemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSPROMPT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nsprompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSREMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nsremit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "NSZEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> nzemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "OEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> oemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> pemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "PROMPT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "REMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> remit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}

	[SharpFunction(Name = "ZEMIT", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> zemit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		throw new NotImplementedException();
	}
}