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
	private ValueTask<CallState> RunEmitFunction(IMUSHCodeParser parser, EmitScope scope, bool noSpoof)
	{
		var args = parser.CurrentState.Arguments;
		var messageIndex = scope is EmitScope.Immediate or EmitScope.Outermost ? 0 : 1;
		return CommunicationService.EmitAsync(parser, EmitHelpers.Create(scope,
			messageIndex == 0 ? "" : args["0"].Message!.ToPlainText(), args[messageIndex.ToString()].Message!,
			list: scope != EmitScope.Zone, silent: scope is EmitScope.Private or EmitScope.Room, noSpoof));
	}


	[SharpFunction(Name = "emit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> Emit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Immediate, false);

	[SharpFunction(Name = "lemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> LocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Outermost, false);

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
		=> await RunEmitFunction(parser, EmitScope.Immediate, true);

	[SharpFunction(Name = "nslemit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Outermost, true);

	[SharpFunction(Name = "nsoemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Omit, true);

	[SharpFunction(Name = "nspemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Private, true);

	[SharpFunction(Name = "nsprompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> NoSpoofPrompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Prompt, true);

	[SharpFunction(Name = "nsremit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Room, true);

	[SharpFunction(Name = "nszemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Zone, true);

	[SharpFunction(Name = "oemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["message"])]
	public async ValueTask<CallState> OmitEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Omit, false);

	[SharpFunction(Name = "pemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> PrivateEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Private, false);


	[SharpFunction(Name = "prompt", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["target", "message"])]
	public async ValueTask<CallState> Prompt(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Prompt, false);

	[SharpFunction(Name = "remit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["room", "message"])]
	public async ValueTask<CallState> RoomEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Room, false);

	[SharpFunction(Name = "zemit", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.NoGagged, ParameterNames = ["zone", "message"])]
	public async ValueTask<CallState> ZoneEmit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await RunEmitFunction(parser, EmitScope.Zone, false);
}
