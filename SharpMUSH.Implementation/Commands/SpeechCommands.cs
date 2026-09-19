using System.Buffers;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using System.Collections.Immutable;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["comment"])]
	public ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(CallState.Empty));

	// PennMUSH src/command.c: {"THINK", "NOEVAL", cmd_think, CMD_T_ANY | CMD_T_NOGAGGED, 0, 0}.
	[SharpCommand(Name = "THINK", Switches = ["NOEVAL"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["expression"])]
	public async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// PennMUSH cmd_think (cmds.c:1769) is an unconditional notify, so a bare `think` prints a
		// blank line rather than nothing at all. A bare `think` has no "0" argument at all, so the
		// return has to come off the same check as the notify.
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var thought))
		{
			await NotifyService.Notify(executor, string.Empty, executor);
			return CallState.Empty;
		}

		// The MString, NOT ToString(): rendering it here bakes the colour into the text as ANSI escape
		// characters and hands a plain string onward, so the markup is gone before the transport sees
		// it. A browser has no ANSI decoder and printed the escapes as literal text.
		await NotifyService.Notify(executor, thought.Message!, executor);
		return thought;
	}

	[SharpCommand(Name = "SAY", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Say(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, "\"");
	}

	[SharpCommand(Name = "POSE", Switches = ["NOEVAL", "NOSPACE"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 1, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> Pose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, parser.CurrentState.Switches.Contains("NOSPACE") ? ";" : ":");
	}

	[SharpCommand(Name = "SEMIPOSE", Switches = ["NOEVAL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0,
		MaxArgs = 0, ParameterNames = ["message"])]
	public async ValueTask<Option<CallState>> SemiPose(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var message = parser.CurrentState.Switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		return await CommunicationService.SpeechAsync(parser, message, ";");
	}

	[SharpCommand(Name = "PAGE", Switches = ["LIST", "NOEVAL", "PORT", "OVERRIDE"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Page(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var isNoEval = parser.CurrentState.Switches.Contains("NOEVAL");
		var isOverride = parser.CurrentState.Switches.Contains("OVERRIDE");
		var isList = parser.CurrentState.Switches.Contains("LIST");
		if (isList)
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			var lastPagedText = lastPagedAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: string.Empty;

			if (string.IsNullOrWhiteSpace(lastPagedText))
			{
				await NotifyService.Notify(executor, "You haven't paged anyone since connecting.", executor);
				return CallState.Empty;
			}

			var lastPagedNames = new List<string>();
			foreach (var recipientRef in lastPagedText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				if (!DBRef.TryParse(recipientRef, out var dbref))
				{
					continue;
				}

				if (await Mediator.Send(new GetObjectNodeQuery(dbref!.Value)) is AnySharpObject recipient)
				{
					lastPagedNames.Add(recipient.Object().Name);
				}
			}

			if (lastPagedNames.Count == 0)
			{
				await NotifyService.Notify(executor, "I can't find who you last paged.", executor);
			}
			else
			{
				var recipientList = MessageFormatting.FormatWithOxfordComma(lastPagedNames);
				await NotifyService.Notify(executor, $"You last paged {recipientList}.", executor);
			}

			return CallState.Empty;
		}

		var recipientsArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		string recipientsText;

		// If no recipients are provided, use the last successful page targets.
		if (string.IsNullOrWhiteSpace(recipientsArg.ToPlainText()) &&
			!string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			var lastPagedAttr = await AttributeService.GetAttributeAsync(
				executor, executor, "LASTPAGED", IAttributeService.AttributeMode.Read, false);
			recipientsText = lastPagedAttr is SharpAttribute[] attr
				? attr.Last().Value.ToPlainText()
				: string.Empty;

			if (string.IsNullOrWhiteSpace(recipientsText))
			{
				await NotifyService.Notify(executor, "Who do you want to page?", executor);
				return CallState.Empty;
			}
		}
		else
		{
			recipientsText = recipientsArg.ToPlainText();
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "What do you want to page?", executor);
			return CallState.Empty;
		}

		var pageType = messageArg.ToPlainText()[0] switch
		{
			':' => PageMessageType.Pose,
			';' => PageMessageType.SemiPose,
			_ => PageMessageType.Speech
		};
		var message = pageType == PageMessageType.Speech
			? messageArg
			: messageArg.Substring(1);

		var recipientNames = recipientsText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var successfulRecipients = new List<AnySharpObject>();

		foreach (var recipientName in recipientNames)
		{
			if (await LocateService.LocateAndNotifyIfInvalidWithCallState(
					parser, executor, executor, recipientName, LocateFlags.All | LocateFlags.MatchForPage)
				is not AnySharpObject recipient)
			{
				continue;
			}

			if (!isOverride)
			{
				var recipientFlags = recipient.Object().Flags.Value;
				if (await recipientFlags.AnyAsync(f => f.Name.Equals("HAVEN", StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.Notify(executor, $"{recipient.Object().Name} is not accepting pages.", executor);
					continue;
				}
			}

			if (!isOverride)
			{
				// The interaction filter is its own gate and carries no failure triad: `fails_lock` at
				// speech.c:924-925 is `eval_lock_with(executor, target, Page_Lock, pe_info)` alone, and
				// only it reaches the fail_lock at :948.
				if (!await PermissionService.CanInteract(executor, recipient,
							IPermissionService.InteractType.Page))
				{
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					continue;
				}

				if (!await LockService.Evaluate(LockType.Page, recipient, executor))
				{
					// speech.c:944-948: the pager is told, and then
					// fail_lock(executor, target, Page_Lock, NULL, NOTHING). The Page lock is not in
					// lock_msgs, so its failure attributes are the derived PAGE_LOCK`FAILURE /
					// `OFAILURE / `AFAILURE (lock.c:861-870) that LockMessages.FailureAttributes
					// builds, and FailLock evaluates them as the recipient. No default: Penn passes
					// NULL.
					await NotifyService.Notify(executor,
						string.Format(ErrorMessages.Notifications.NotAcceptingYourPages, recipient.Object().Name),
						executor);

					await DidItService.FailLock(parser, executor, recipient, LockType.Page);

					continue;
				}
			}

			successfulRecipients.Add(recipient);
		}

		if (successfulRecipients.Count > 0)
		{
			var recipientList = MessageFormatting.FormatWithOxfordComma(
				successfulRecipients.Select(r => r.Object().Name).ToArray());
			var recipientRefs = string.Join(" ",
				successfulRecipients.Select(r => $"#{r.Object().DBRef.Number}"));
			var pageAlias = executor is SharpPlayer executorPlayer
				? executorPlayer.Aliases?.FirstOrDefault() ?? string.Empty
				: string.Empty;
			var senderName = Configuration.CurrentValue.Cosmetic.PageAliases && !string.IsNullOrEmpty(pageAlias)
				? $"{executor.Object().Name} ({pageAlias})"
				: executor.Object().Name;
			var recipientSuffix = successfulRecipients.Count > 1 ? $" (to {recipientList})" : string.Empty;

			var incomingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain(successfulRecipients.Count > 1
						? $"{senderName} pages {recipientList}: "
						: $"{senderName} pages: "),
					message
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"From afar{recipientSuffix}, {senderName}"),
					message
				])
			};
			var outgoingDefault = pageType switch
			{
				PageMessageType.Speech => MarkupText.Concat([
					MarkupText.Plain($"You paged {recipientList} with '"),
					message,
					MarkupText.Plain("'")
				]),
				PageMessageType.Pose => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name} "),
					message
				]),
				_ => MarkupText.Concat([
					MarkupText.Plain($"Long distance to {recipientList}: {executor.Object().Name}"),
					message
				])
			};
			var pageTypeToken = pageType switch
			{
				PageMessageType.Pose => ":",
				PageMessageType.SemiPose => ";",
				_ => "\""
			};
			var lastPagedText = string.Join(" ", successfulRecipients.Select(r => r.Object().DBRef));
			var lastPagedResult = await AttributeService.SetAttributeAsync(
				await HelperFunctions.GetGod(Mediator), executor, "LASTPAGED", MarkupText.Plain(lastPagedText));
			if (lastPagedResult is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				return CallState.Empty;
			}

			var outPageFormatArgs = PageFormatArguments(
				message, pageTypeToken, pageAlias, recipientRefs, outgoingDefault);
			var outgoing = await parser.With(
				state => state with
				{
					Executor = executor.Object().DBRef,
					Caller = executor.Object().DBRef,
					Enactor = executor.Object().DBRef
				},
				pageParser => AttributeHelpers.EvaluateFormatAttribute(
					AttributeService, pageParser, executor, executor, "OUTPAGEFORMAT",
					outPageFormatArgs, outgoingDefault, checkParents: true));
			await NotifyService.Notify(executor, outgoing, executor);

			foreach (var recipient in successfulRecipients)
			{
				var pageFormatArgs = PageFormatArguments(
					message, pageTypeToken, pageAlias, recipientRefs, incomingDefault);
				var incoming = await parser.With(
					state => state with
					{
						Executor = recipient.Object().DBRef,
						Caller = recipient.Object().DBRef,
						Enactor = executor.Object().DBRef
					},
					pageParser => AttributeHelpers.EvaluateFormatAttribute(
						AttributeService, pageParser, recipient, recipient, "PAGEFORMAT",
						pageFormatArgs, incomingDefault, checkParents: true));
				await NotifyService.Notify(recipient, incoming, executor, INotifyService.NotificationType.Say);
			}
		}
		else if (recipientNames.Length > 0)
		{
			await NotifyService.Notify(executor, "No one to page.", executor);
		}

		return CallState.Empty;
	}

	private static Dictionary<string, CallState> PageFormatArguments(
		MString message, string pageType, string alias, string recipientRefs, MString defaultMessage) => new()
		{
			["0"] = new CallState(message),
			["1"] = new CallState(pageType),
			["2"] = new CallState(alias),
			["3"] = new CallState(recipientRefs),
			["4"] = new CallState(defaultMessage)
		};

	private enum PageMessageType
	{
		Speech,
		Pose,
		SemiPose
	}

	[SharpCommand(Name = "WHISPER", Switches = ["LIST", "NOISY", "SILENT", "NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["player", "message"])]
	public async ValueTask<Option<CallState>> Whisper(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;

		var executorLocation = await executor.Where();

		if (switches.Contains("LIST"))
		{
			var perceive = await ObserveRealityAsync(parser, executor);
			var players = await executorLocation.Content(Mediator)
				.Where(obj => obj.IsPlayer && !obj.Object().DBRef.Equals(executor.Object().DBRef))
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.Select(obj => obj.Object().Name)
				.ToListAsync(ExecutionBudget.CurrentToken);

			if (players.Count == 0)
			{
				await NotifyService.Notify(executor, "There is no one here to whisper to.", executor);
			}
			else
			{
				await NotifyService.Notify(executor, $"You can whisper to: {string.Join(", ", players)}", executor);
			}

			return CallState.Empty;
		}

		var isNoEval = switches.Contains("NOEVAL");
		var targetArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);
		var messageArg = isNoEval
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MarkupText.Empty);

		if (string.IsNullOrWhiteSpace(targetArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper to whom?", executor);
			return CallState.Empty;
		}

		if (string.IsNullOrWhiteSpace(messageArg.ToPlainText()))
		{
			await NotifyService.Notify(executor, "Whisper what?", executor);
			return CallState.Empty;
		}

		var successfulTargets = new List<AnySharpObject>();
		var unable = new List<string>();

		// speech.c do_whisper: next_in_list takes a "quoted name" whole, and each name is matched with
		// match_result(player, name, TYPE_PLAYER, MAT_NEAR_THINGS | MAT_CONTAINER). The type is a
		// preference, so any nearby object is a recipient, the whisperer included. A name that matches
		// nothing, or an object that cannot hear the whisperer, lands in one `Unable to whisper to:`
		// line — the deaf one also gets its own `can't hear you` — and the hundredth good target ends
		// the scan.
		foreach (var targetName in WhisperTargetNames(targetArg.ToPlainText()))
		{
			var found = await LocateService.Locate(parser, executor, executor, targetName, WhisperTargetFlags);
			if (found is not AnySharpObject target
					|| !await PermissionService.CanInteract(executor, target, IPermissionService.InteractType.Hear))
			{
				unable.Add(targetName.Contains(' ') ? $"\"{targetName}\"" : targetName);
				if (found is AnySharpObject deaf)
				{
					await NotifyService.Notify(executor, $"{deaf.Object().Name} can't hear you.", executor);
				}

				continue;
			}

			successfulTargets.Add(target);
			if (successfulTargets.Count >= MaxWhisperTargets)
			{
				await NotifyService.Notify(executor, "Too many people to whisper to.", executor);
				break;
			}
		}

		if (unable.Count > 0)
		{
			await NotifyService.Notify(executor, $"Unable to whisper to: {string.Join(' ', unable)}", executor);
		}

		if (successfulTargets.Count == 0)
		{
			return CallState.Empty;
		}

		// PennMUSH cmd_whisper (src/cmds.c): `noisy = SW_ISSET(NOISY) || (!SW_ISSET(SILENT) &&
		// NOISY_WHISPER)`, and `noisy` governs ONLY whether the room may overhear. The whisperer's own
		// echo is unconditional — `whisper/silent X=hi` still says "You whisper, ..." to the whisperer.
		var isNoisy = switches.Contains("NOISY")
									|| (!switches.Contains("SILENT") && Configuration.CurrentValue.Command.NoisyWhisper);

		// "Drunk wizards...": a DARK whisperer is never overheard. Otherwise each recipient rolls
		// get_random_u32(0, 100) against whisper_loudness, and one roll under it is enough — unless some
		// recipient is not standing in the whisperer's location, which keeps the whole whisper private.
		var loudness = Configuration.CurrentValue.Limit.WhisperLoudness;
		var overheard = isNoisy
										&& !await executor.IsDark()
										&& successfulTargets.Any(_ => Random.Shared.Next(0, 101) < loudness)
										&& await successfulTargets.ToAsyncEnumerable().AllAsync(async (target, _)
											=> await LocatedIn(target, executorLocation.Object().DBRef));
		var messageText = messageArg.ToPlainText();

		// PennMUSH do_whisper (src/speech.c) reads the message type off the first character exactly as
		// do_pose does: ';' is a pose with no gap, ':' a pose with one, anything else plain speech.
		// The two kinds have completely different wording — the pose kind is "senses", not "whispers".
		var gap = messageText.StartsWith(';') ? string.Empty : " ";
		var isPose = messageText.StartsWith(':') || messageText.StartsWith(';');
		var body = isPose ? messageText[1..] : messageText;

		var targetList = MessageFormatting.FormatWithOxfordComma(
			[.. successfulTargets.Select(t => t.Object().Name)]);

		if (isPose)
		{
			var sensed = $"{executor.Object().Name}{gap}{body}";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"You sense: {sensed}", executor, INotifyService.NotificationType.Say);
			}

			var verb = successfulTargets.Count > 1 ? "sense" : "senses";
			await NotifyService.Notify(executor, $"{targetList} {verb}: {sensed}", executor);
		}
		else
		{
			var heading = successfulTargets.Count > 1
				? $"{executor.Object().Name} whispers to {targetList}"
				: $"{executor.Object().Name} whispers";
			foreach (var target in successfulTargets)
			{
				await NotifyService.Notify(target, $"{heading}: {body}", executor, INotifyService.NotificationType.Say);
			}

			await NotifyService.Notify(executor, $"You whisper, \"{body}\" to {targetList}.", executor);
		}

		if (overheard)
		{
			var contents = executorLocation.Content(Mediator);
			await foreach (var obj in contents)
			{
				if (obj.Object().DBRef.Equals(executor.Object().DBRef) ||
						successfulTargets.Any(t => t.Object().DBRef.Equals(obj.Object().DBRef)))
				{
					continue;
				}

				await NotifyService.Notify(obj.WithRoomOption(),
					$"{executor.Object().Name} whispers to {targetList}.", executor);
			}
		}

		return new CallState(messageArg);
	}

	/// <summary>
	/// <c>MAT_NEAR_THINGS | MAT_CONTAINER</c> with a <c>TYPE_PLAYER</c> preference: <c>MAT_ME</c>,
	/// <c>MAT_ABSOLUTE</c>, <c>MAT_PLAYER</c>, <c>MAT_NEIGHBOR</c>, <c>MAT_POSSESSION</c> and the
	/// looker's location by name, every match required to be nearby.
	/// </summary>
	private const LocateFlags WhisperTargetFlags =
		LocateFlags.MatchMeForLooker | LocateFlags.AbsoluteMatch | LocateFlags.MatchWildCardForPlayerName |
		LocateFlags.MatchObjectsInLookerLocation | LocateFlags.MatchObjectsInLookerInventory |
		LocateFlags.MatchAgainstLookerLocationName | LocateFlags.OnlyMatchObjectsInLookerLocation |
		LocateFlags.PlayersPreference;

	/// <summary>speech.c: <c>dbref good[100]</c>.</summary>
	private const int MaxWhisperTargets = 100;

	private static readonly SearchValues<char> WhisperNameBreaks = SearchValues.Create(" \"");

	/// <summary>
	/// strutil.c <c>next_in_list</c>: spaces separate names, a leading <c>"</c> takes everything up to
	/// the next <c>"</c> as one name, and an unquoted name also stops at a <c>"</c>. Nothing else splits
	/// a name — <c>#12Lamp</c> is one token, which <c>parse_dbref</c> then refuses as a whole.
	/// </summary>
	private static IEnumerable<string> WhisperTargetNames(string list)
	{
		var head = 0;
		while (true)
		{
			while (head < list.Length && list[head] == ' ') head++;
			if (head >= list.Length) yield break;

			if (list[head] == '"')
			{
				var close = list.IndexOf('"', head + 1);
				var end = close < 0 ? list.Length : close;
				var quoted = list[(head + 1)..end];
				head = close < 0 ? list.Length : close + 1;
				if (quoted.Length > 0) yield return quoted;
				continue;
			}

			var stop = list.AsSpan(head).IndexOfAny(WhisperNameBreaks);
			var next = stop < 0 ? list.Length : head + stop;
			yield return list[head..next];
			head = next;
		}
	}

	/// <summary>
	/// PennMUSH's <c>Location()</c>, which reads the raw location field: a room's is its drop-to and an
	/// exit's its destination, where <see cref="AnySharpObject.Where"/> would answer with the room itself
	/// or the exit's source.
	/// </summary>
	private static async ValueTask<bool> LocatedIn(AnySharpObject target, DBRef location) => target switch
	{
		SharpRoom room => await room.Location.WithCancellation(CancellationToken.None) is AnySharpContainer dropTo
											&& dropTo.Object().DBRef.Equals(location),
		SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
											&& destination.Object().DBRef.Equals(location),
		SharpPlayer or SharpThing => (await target.Where()).Object().DBRef.Equals(location)
	};

	/// <summary>
	/// Who a wall broadcast reaches. PennMUSH's do_wall() (src/speech.c) picks a flag mask per
	/// command and hands it to flag_broadcast() (src/notify.c), which delivers only to connected
	/// players whose flags satisfy the mask.
	/// </summary>
	private enum WallAudience
	{
		/// <summary>No mask: every connected player. @wall.</summary>
		Everyone,

		/// <summary>Mask "WIZARD ROYALTY", which flaglist_check_long() reads as any-of. @rwall.</summary>
		RoyaltyAndWizards,

		/// <summary>Mask "WIZARD". @wizwall.</summary>
		Wizards
	}

	/// <summary>
	/// The one body behind @wall, @rwall and @wizwall — PennMUSH's do_wall(), which the three
	/// commands differ from each other only in the prefix and the flag mask they pass it.
	/// </summary>
	private async ValueTask<Option<CallState>> WallCore(IMUSHCodeParser parser, SharpCommandAttribute attribute,
		WallAudience audience, string prefix)
	{
		if (await RejectIfTooFewArguments(parser, attribute) is { } tooFewArguments) return tooFewArguments;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var shout = switches.Contains("NOEVAL")
			? ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 0, MarkupText.Empty);

		if (!switches.Contains("EMIT"))
		{
			shout = MarkupText.Concat(MarkupText.Plain(prefix + " "), shout);
		}

		await foreach (var connection in ConnectionService.GetAll())
		{
			// PennMUSH iterates DESC_ITER_CONN, so a socket sitting at the connect screen hears nothing.
			if (connection.State is not IConnectionService.ConnectionState.LoggedIn || connection.Ref is null)
			{
				continue;
			}

			if (!await WallAudienceIncludes(audience, connection.Ref.Value))
			{
				continue;
			}

			await NotifyService.Notify(connection.Handle, shout, executor);
		}

		return new CallState(shout);
	}

	/// <summary>PennMUSH flag_broadcast()'s per-listener mask test.</summary>
	private async ValueTask<bool> WallAudienceIncludes(WallAudience audience, DBRef listener)
	{
		if (audience is WallAudience.Everyone)
		{
			return true;
		}

		if (await Mediator.Send(new GetObjectNodeQuery(listener)) is not AnySharpObject known)
		{
			return false;
		}

		return audience switch
		{
			WallAudience.Wizards => await known.IsWizard(),
			WallAudience.RoyaltyAndWizards => await known.IsWizard() || await known.IsRoyalty(),
			_ => true
		};
	}

	[SharpCommand(Name = "@WALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY|POWER^ANNOUNCE", MinArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> Wall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.Everyone, Configuration.CurrentValue.Cosmetic.WallPrefix);

	[SharpCommand(Name = "@RWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD|FLAG^ROYALTY", MinArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> RoyaltyWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.RoyaltyAndWizards, Configuration.CurrentValue.Cosmetic.RoyaltyWallPrefix);

	[SharpCommand(Name = "@WIZWALL", Switches = ["NOEVAL", "EMIT"], Behavior = CB.Default | CB.NoParse,
		CommandLock = "FLAG^WIZARD", MinArgs = 1, MaxArgs = 1, ParameterNames = ["message"])]
	public ValueTask<Option<CallState>> WizardWall(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> WallCore(parser, _2, WallAudience.Wizards, Configuration.CurrentValue.Cosmetic.WizardWallPrefix);
}
