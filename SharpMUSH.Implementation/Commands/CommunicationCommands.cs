using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private async ValueTask<Option<CallState>> RunEmitCommand(IMUSHCodeParser parser, SharpCommandAttribute definition,
		EmitScope scope, bool noSpoof)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var ports = scope == EmitScope.Private && switches.Contains("PORT");
		var contents = scope == EmitScope.Private && !ports && switches.Contains("CONTENTS");
		if (contents) scope = EmitScope.Room;
		if (scope == EmitScope.Immediate && switches.Contains("ROOM")) scope = EmitScope.Outermost;
		var messageIndex = scope is EmitScope.Immediate or EmitScope.Outermost ? 0 : 1;
		if (args.Count <= messageIndex)
		{
			if (definition.Name == "@NSPROMPT" && await RejectIfTooFewArguments(parser, definition) is { } tooFew)
				return tooFew;
			if (scope is EmitScope.Outermost or EmitScope.Room or EmitScope.Omit or EmitScope.Zone || definition.Name == "@NSPEMIT")
			{
				var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.DontYouHaveAnythingToSayDetail), executor);
				return new CallState(ErrorMessages.Returns.NothingToDo);
			}
			return CallState.Empty;
		}
		var messageArgument = args[messageIndex.ToString()];
		var evaluated = definition.Behavior.HasFlag(CB.RSNoParse) && !switches.Contains("NOEVAL")
			? await messageArgument.GetParsedResultAsync() : messageArgument;
		if (evaluated.HadErrors)
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			await NotifyService.Notify(executor, evaluated.Message!, executor);
			return evaluated;
		}
		var message = evaluated.Message!;
		var list = !contents && switches.Contains("LIST") || scope is EmitScope.Prompt or EmitScope.Omit;
		var silent = switches.Contains("SILENT") || (!switches.Contains("NOISY") &&
			(Configuration.CurrentValue.Compatibility.SilentPEmit
			 || scope == EmitScope.Private && list && !ports));
		var outcome = await CommunicationService.EmitWithOutcomeAsync(parser, EmitHelpers.Create(scope,
			messageIndex == 0 ? "" : args["0"].Message!.ToPlainText(), message, list, silent, noSpoof,
			!ports && switches.Contains("SPOOF"), ports));
		if (outcome.Result.HadErrors) return outcome.Result;
		var returnsMessage = definition.Name is "@EMIT" or "@NSEMIT" or "@NSOEMIT" or "@NSPROMPT";
		if ((returnsMessage || definition.Name == "@OEMIT") && outcome.TargetFailure is CallState failure) return failure;
		return returnsMessage && outcome.Admitted ? new CallState(message) : outcome.Result;
	}

	[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF", "ROOM", "SILENT", "NOISY"], Behavior = CB.Default | CB.RSNoParse | CB.NoGagged,
		MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Immediate, false);

	[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> LocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Outermost, false);

	[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT", "NOISY", "SPOOF"], Behavior = CB.Default | CB.RSNoParse | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> NoSpoofEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Immediate, true);

	[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Outermost, true);

	/// <summary>
	/// The no-spoof form of <see cref="OmitEmit"/>: PennMUSH <c>do_oemit_list</c> (<c>speech.c</c>)
	/// emits to everyone in the room EXCEPT the objects listed in argument 0, and accepts the same
	/// <c>&lt;room&gt;/&lt;object list&gt;</c> target syntax. It differs from <c>@OEMIT</c> only in
	/// the permitted no-spoof notification type; /spoof selects the enactor on either form.
	/// </summary>
	[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSNoParse, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["objects", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Omit, true);

	[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "PORT", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Private, true);

	[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 2, MaxArgs = 2, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofPrompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Prompt, true);

	[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["room", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Room, true);

	[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["zone", "message"])]
	public async ValueTask<Option<CallState>> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Zone, true);

	[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["objects", "message"])]
	public async ValueTask<Option<CallState>> OmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Omit, false);

	[SharpCommand(Name = "@PEMIT", Switches = ["LIST", "PORT", "CONTENTS", "SPOOF", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> PrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Private, false);

	[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["target", "message"])]
	public async ValueTask<Option<CallState>> Prompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Prompt, false);

	[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["room", "message"])]
	public async ValueTask<Option<CallState>> RoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Room, false);

	[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["zone", "message"])]
	public async ValueTask<Option<CallState>> ZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> await RunEmitCommand(parser, _2, EmitScope.Zone, false);

	[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 3, MaxArgs = 0, ParameterNames = ["object", "type", "message"])]
	public async ValueTask<Option<CallState>> Message(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		if (await RejectIfTooFewArguments(parser, _2) is { } tooFewArguments) return tooFewArguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var args = parser.CurrentState.ArgumentsOrdered;

		var recipientsArg = args.ElementAtOrDefault(0).Value.Message!;
		var defmsg = args.ElementAtOrDefault(1).Value.Message!;
		var objectAttrArg = args.ElementAtOrDefault(2).Value.Message!.ToPlainText();
		var otherArgs = args
			.Skip(3)
			.Select(x =>
			{
				if (int.TryParse(x.Key, out var numKey))
				{
					return new KeyValuePair<string, CallState>((numKey - 3).ToString(), x.Value);
				}

				return x;
			})
			.ToList();

		var isRemit = switches.Contains("REMIT");
		var isOemit = switches.Contains("OEMIT");
		var isNospoof = switches.Contains("NOSPOOF");
		var isSpoof = switches.Contains("SPOOF");
		var isSilent = switches.Contains("SILENT") || !switches.Contains("NOISY");

		var result = await MessageHelpers.ProcessMessageAsync(
			parser, Mediator, LocateService, AttributeService, NotifyService,
			PermissionService, CommunicationService, executor,
			recipientsArg, defmsg, objectAttrArg, otherArgs,
			isRemit, isOemit, isNospoof, isSpoof, isSilent);

		return result;
	}

	[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 2,
		MaxArgs = 0, ParameterNames = ["object", "verb", "target"])]
	public async ValueTask<Option<CallState>> Verb(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;

		if (args.Count < 2)
		{
			await NotifyService.Notify(executor,
				"Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]", executor);
			return new CallState(ErrorMessages.Returns.CantSeeThat);
		}

		// ElementAtOrDefault past the end yields a default KeyValuePair whose Value is a null CallState,
		// so every optional slot has to be read through a null-safe accessor.
		string ArgAt(int index) => args.ElementAtOrDefault(index).Value?.Message?.ToPlainText() ?? string.Empty;

		var victimName = ArgAt(0);
		var actorName = ArgAt(1);
		var what = ArgAt(2);
		var whatd = ArgAt(3);
		var owhat = ArgAt(4);
		var owhatd = ArgAt(5);
		var awhat = ArgAt(6);

		const int RequiredArgsBeforeStack = 7;
		var stackArgs = args.Skip(RequiredArgsBeforeStack)
			.Select((kvp, idx) => new KeyValuePair<string, CallState>(idx.ToString(), kvp.Value))
			.ToDictionary();

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, victimName, LocateFlags.All) switch
		{
			AnySharpObject victim => await LocateService.LocateAndNotifyIfInvalidWithCallState(
				parser, executor, executor, actorName, LocateFlags.All) switch
			{
				AnySharpObject actor => await VerbAsync(parser, executor, enactor, victim, actor, what, whatd, owhat, owhatd,
					awhat, stackArgs),
				Error<CallState> error => await NotifyAndReturnAsync(executor, error.Value)
			},
			Error<CallState> error => await NotifyAndReturnAsync(executor, error.Value)
		};
	}

	/// <summary>Tells the executor why a lookup failed, and answers with that failure.</summary>
	private async ValueTask<Option<CallState>> NotifyAndReturnAsync(AnySharpObject executor, CallState failure)
	{
		await NotifyService.Notify(executor, failure.Message!, executor);
		return failure;
	}

	/// <summary>PennMUSH <c>do_verb</c> once both objects are known: the permission gate, then the three messages.</summary>
	private async ValueTask<Option<CallState>> VerbAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject enactor, AnySharpObject victim, AnySharpObject actor, string what, string whatd, string owhat,
		string owhatd, string awhat, Dictionary<string, CallState> stackArgs)
	{
		var isWizard = await executor.IsWizard();
		var controlsBoth = await PermissionService.Controls(executor, actor) &&
											 await PermissionService.Controls(executor, victim);
		var enactorIsActor = enactor.Object().DBRef == actor.Object().DBRef;
		var executorPrivileged = await executor.IsRoyalty();
		var executorControlsVictim = await PermissionService.Controls(executor, victim);

		var hasPermission = isWizard || controlsBoth ||
												(enactorIsActor && (executorPrivileged || executorControlsVictim));

		if (!hasPermission)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var actorMessage = await GetAttributeOrDefault(
			parser, AttributeService, executor, victim, actor, what, whatd, stackArgs);

		await NotifyService.Notify(actor, actorMessage, actor);

		var actorLocation = await actor.Where();
		var othersMessage = await GetAttributeOrDefault(
			parser, AttributeService, executor, victim, actor, owhat, owhatd, stackArgs);

		var prependedMessage = MarkupText.Plain($"{actor.Object().Name} {othersMessage.ToPlainText()}");

		await CommunicationService.SendToRoomAsync(
			actor, actorLocation, _ => prependedMessage,
			INotifyService.NotificationType.Emit,
			excludeObjects: [actor]);

		CallState? nestedResult = null;
		if (!string.IsNullOrWhiteSpace(awhat))
		{
			if (await AttributeService.GetAttributeAsync(
					executor, victim, awhat, IAttributeService.AttributeMode.Execute) is SharpAttribute[] awhatChain)
			{
				var attribute = awhatChain.Last();
				nestedResult = await parser.With(
					state => state with
					{
						Executor = victim.Object().DBRef,
						Enactor = actor.Object().DBRef,
						Caller = state.Executor,
						Arguments = stackArgs
					},
					newParser => newParser.WithAttributeDebug(attribute,
						p => p.CommandListParse(attribute.Value)));
			}
		}

		return CallState.Empty with { HadErrors = nestedResult?.HadErrors == true };
	}

	private async ValueTask<MString> GetAttributeOrDefault(
		IMUSHCodeParser parser,
		IAttributeService attributeService,
		AnySharpObject executor,
		AnySharpObject victim,
		AnySharpObject actor,
		string attrName,
		string defaultValue,
		Dictionary<string, CallState> stackArgs)
	{
		if (string.IsNullOrWhiteSpace(attrName))
		{
			return MarkupText.Plain(defaultValue);
		}

		var maybeAttr = await attributeService.GetAttributeAsync(
			executor, victim, attrName, IAttributeService.AttributeMode.Execute);

		if (maybeAttr.IsError || maybeAttr.IsNone)
		{
			return MarkupText.Plain(defaultValue);
		}

		var result = await parser.With(
			state => state with
			{
				Executor = victim.Object().DBRef,
				Enactor = actor.Object().DBRef,
				Caller = state.Executor,
				Arguments = stackArgs
			},
			newParser => attributeService.EvaluateAttributeFunctionAsync(
				newParser, victim, victim, attrName, stackArgs));

		return result ?? MarkupText.Plain(defaultValue);
	}
}
