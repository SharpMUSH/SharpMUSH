using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Implementation.Commands.ChannelCommand;
using SharpMUSH.Implementation.Commands.MailCommand;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private const string DefaultSemaphoreAttribute = "SEMAPHORE";
	private static readonly string[] DefaultSemaphoreAttributeArray = [DefaultSemaphoreAttribute];

	[SharpCommand(Name = "@@", Switches = [], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> At(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult(new Option<CallState>(CallState.Empty));

	[SharpCommand(Name = "THINK", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Think(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (parser.CurrentState.Arguments.Count <= 0)
		{
			return new None();
		}

		await NotifyService!.Notify(executor, parser.CurrentState.Arguments["0"].Message!.ToString());
		return parser.CurrentState.Arguments["0"];
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		await NotifyService!.Notify(executor, "Huh?  (Type \"help\" for help.)");
		return new CallState("#-1 HUH");
	}

	[SharpCommand(Name = "@MAP", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"])]
	public static async ValueTask<Option<CallState>> Map(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @map[/<switches>][/notify][/delimit <delim>] [<object>/]<attribute>=<list>
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify an attribute to map.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.Notify(executor, "You must specify an attribute to map.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		string? listToMap = null;
		if (args.Count >= 2)
		{
			listToMap = args["1"].Message?.ToPlainText();
		}
		
		await NotifyService!.Notify(executor, $"@map: Would iterate over list and execute attribute '{attributePath}'");
		
		if (listToMap != null)
		{
			await NotifyService.Notify(executor, $"  List: {listToMap}");
		}
		
		if (switches.Contains("INLINE"))
		{
			await NotifyService.Notify(executor, "  Mode: Inline execution");
		}
		
		if (switches.Contains("NOTIFY"))
		{
			await NotifyService.Notify(executor, "  Will queue @notify after completion");
		}
		
		if (switches.Contains("CLEARREGS"))
		{
			await NotifyService.Notify(executor, "  Will clear Q-registers");
		}
		
		if (switches.Contains("LOCALIZE"))
		{
			await NotifyService.Notify(executor, "  Will localize Q-registers");
		}
		
		// TODO: Full implementation requires parsing object/attribute path, splitting list into elements,
		// executing the attribute for each element with element as %0, handling enactor preservation
		// and Q-register management, and handling /inline vs queued execution and /notify switch.
		await NotifyService.Notify(executor, "Note: @map command queueing and execution not yet implemented.");
		
		return new CallState("#-1 NOT IMPLEMENTED");
	}

	[SharpCommand(Name = "@DOLIST", Behavior = CB.EqSplit | CB.RSNoParse, MinArgs = 1, MaxArgs = 2,
		Switches = ["CLEARREGS", "DELIMIT", "INLINE", "INPLACE", "LOCALIZE", "NOBREAK", "NOTIFY"])]
	public static async ValueTask<Option<CallState>> DoList(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var switches = parser.CurrentState.Switches;

		if (parser.CurrentState.Arguments.Count < 2)
		{
			await NotifyService!.Notify(enactor, "What do you want to do with the list?");
			return new None();
		}

		var list = MModule.split(" ", parser.CurrentState.Arguments["0"].Message!);
		var command = parser.CurrentState.Arguments["1"].Message!;

		var isInline = switches.Contains("INLINE") || switches.Contains("INPLACE");

		if (isInline)
		{
			var noBreak = switches.Contains("NOBREAK") || switches.Contains("INPLACE");
			var wrappedIteration = new IterationWrapper<MString>
				{ Value = MModule.empty(), Break = false, NoBreak = noBreak, Iteration = 0 };
			parser.CurrentState.IterationRegisters.Push(wrappedIteration);

			using (NotifyService!.BeginBatchingContext())
			{
				var lastCallState = CallState.Empty;
				var visitorFunc = parser.CommandListParseVisitor(command);
				foreach (var item in list)
				{
					wrappedIteration.Value = item!;
					wrappedIteration.Iteration++;

					// TODO: This should not need parsing each time. Just evaluation by getting the Context and visiting the children multiple times.
					lastCallState = await visitorFunc();
				}

				parser.CurrentState.IterationRegisters.TryPop(out _);

				// If /notify switch is present with /inline, queue "@notify me" after inline execution
				if (switches.Contains("NOTIFY"))
				{
					await Mediator!.Send(new QueueCommandListRequest(
						MModule.single("@notify me"),
						parser.CurrentState,
						new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
						-1));
				}

				return lastCallState!;
			}
		}
		else
		{
			var iteration = 0u;
			var noBreak = switches.Contains("NOBREAK") || switches.Contains("INPLACE");
			
			foreach (var item in list)
			{
				iteration++;
				
				var iterationWrapper = new IterationWrapper<MString>
				{
					Value = item!,
					Break = false,
					NoBreak = noBreak,
					Iteration = iteration
				};
				
				var iterationStack = new ConcurrentStack<IterationWrapper<MString>>();
				iterationStack.Push(iterationWrapper);
				
				var stateForIteration = parser.CurrentState with
				{
					IterationRegisters = iterationStack
				};
				
				await Mediator!.Send(new QueueCommandListRequest(
					command,
					stateForIteration,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			// If /notify switch is present, queue "@notify me" after all iterations
			if (switches.Contains("NOTIFY"))
			{
				await Mediator!.Send(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					new DbRefAttribute(enactor.Object().DBRef, DefaultSemaphoreAttributeArray),
					-1));
			}

			return CallState.Empty;
		}
	}

	[SharpCommand(Name = "LOOK", Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var forceOpaque = switches.Contains("OPAQUE");
		var lookOutside = switches.Contains("OUTSIDE");

		AnyOptionalSharpObject viewing = new None();
		
		if (lookOutside && executor.IsContent)
		{
			var container = await executor.AsContent.Location();
			viewing = container.WithRoomOption().Match<AnyOptionalSharpObject>(
				player => player,
				room => room,
				exit => exit,
				thing => thing
			);
		}
		else if (args.Count == 1)
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				args["0"].Message!.ToString(),
				LocateFlags.All);

			if (locate.IsValid())
			{
				viewing = locate.WithoutError();
			}
		}
		else
		{
			viewing = (await Mediator!.Send(new GetCertainLocationQuery(executor.Id()!))).WithExitOption()
				.WithNoneOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var realViewing = viewing.Known;
		var viewingObject = realViewing.Object();
		
		var executorLocation = executor.IsContent 
			? await executor.AsContent.Location()
			: null;
		var viewingFromInside = executorLocation != null 
			&& executorLocation.Object().DBRef == viewingObject.DBRef;

		var baseName = viewingObject.Name;
		var baseDesc = MModule.empty();
		var useIdesc = viewingFromInside && !lookOutside;

		var descAttrName = useIdesc ? "IDESCRIBE" : "DESCRIBE";
		var descResult = await AttributeService!.GetAttributeAsync(executor, realViewing, descAttrName,
			IAttributeService.AttributeMode.Read, false);
		
		if (descResult.IsAttribute)
		{
			var descAttr = descResult.AsAttribute.Last();
			baseDesc = MModule.getLength(descAttr.Value) > 0
				? descAttr.Value
				: (useIdesc ? MModule.empty() : MModule.single("You see nothing special."));
		}
		else if (!useIdesc)
		{
			baseDesc = MModule.single("You see nothing special.");
		}

		var formatArgs = new Dictionary<string, CallState>();
		
		var formattedName = MModule.single(baseName);
		if (realViewing.IsRoom && viewingFromInside)
		{
			var nameFormatResult = await AttributeService.GetAttributeAsync(executor, realViewing, "NAMEFORMAT",
				IAttributeService.AttributeMode.Read, false);
			
			if (nameFormatResult.IsAttribute)
			{
				var flags = await viewingObject.Flags.Value.ToArrayAsync();
				formatArgs["0"] = new CallState(viewingObject.DBRef.ToString());
				formatArgs["1"] = new CallState($"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(baseName))}" +
					$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, flags.Select(x => x.Symbol))})");
				
				formattedName = await AttributeService.EvaluateAttributeFunctionAsync(
					parser, executor, realViewing, "NAMEFORMAT", formatArgs);
			}
			else
			{
				var flags = await viewingObject.Flags.Value.ToArrayAsync();
				formattedName = MModule.single($"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(baseName))}" +
					$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, flags.Select(x => x.Symbol))})");
			}
		}
		else
		{
			var flags = await viewingObject.Flags.Value.ToArrayAsync();
			formattedName = MModule.single($"{MModule.markupSingle2(Ansi.Create(foreground: StringExtensions.rgb(Color.White)), MModule.single(baseName))}" +
				$"(#{viewingObject.DBRef.Number}{string.Join(string.Empty, flags.Select(x => x.Symbol))})");
		}

		var formatAttrName = useIdesc ? "IDESCFORMAT" : "DESCFORMAT";
		var descFormatResult = await AttributeService.GetAttributeAsync(executor, realViewing, formatAttrName,
			IAttributeService.AttributeMode.Read, false);
		
		var formattedDesc = baseDesc;
		if (descFormatResult.IsAttribute)
		{
			formatArgs["0"] = new CallState(baseDesc);
			formattedDesc = await AttributeService.EvaluateAttributeFunctionAsync(
				parser, executor, realViewing, formatAttrName, formatArgs);
		}

		await NotifyService!.Notify(executor, formattedName);
		if (MModule.getLength(formattedDesc) > 0)
		{
			await NotifyService.Notify(executor, formattedDesc);
		}

		var showInventory = realViewing.IsContainer 
			&& !forceOpaque 
			&& !(await realViewing.IsOpaque());

		if (showInventory)
		{
			var allContents = await Mediator!.CreateStream(new GetContentsQuery(realViewing.AsContainer))!.ToListAsync();
			
			var isRoomLight = realViewing.IsRoom && await realViewing.IsLight();
			var isRoomDark = realViewing.IsRoom && await realViewing.IsDarkLegal();
			var canSeeAll = await executor.IsSee_All();
			
			var visibleContents = new List<AnySharpContent>();
			var visibleExits = new List<AnySharpContent>();
			
			foreach (var item in allContents)
			{
				var itemObj = item.WithRoomOption();
				var isDark = await itemObj.IsDarkLegal();
				var isLight = await itemObj.IsLight();
				
				bool visible = false;
				if (isRoomLight)
				{
					visible = true;
				}
				else if (isRoomDark)
				{
					visible = canSeeAll || isLight;
				}
				else
				{
					visible = !isDark || canSeeAll;
				}

				if (visible)
				{
					if (item.IsExit)
					{
						if (!isDark || canSeeAll)
						{
							visibleExits.Add(item);
						}
					}
					else
					{
						visibleContents.Add(item);
					}
				}
			}

			if (visibleContents.Count > 0)
			{
				var conFormatResult = await AttributeService.GetAttributeAsync(executor, realViewing, "CONFORMAT",
					IAttributeService.AttributeMode.Read, false);
				
				if (conFormatResult.IsAttribute)
				{
					var contentDbrefs = string.Join(" ", visibleContents.Select(x => x.Object().DBRef.ToString()));
					var contentNames = string.Join("|", visibleContents.Select(x => x.Object().Name));
					
					formatArgs["0"] = new CallState(contentDbrefs);
					formatArgs["1"] = new CallState(contentNames);
					
					var formattedContents = await AttributeService.EvaluateAttributeFunctionAsync(
						parser, executor, realViewing, "CONFORMAT", formatArgs);
					
					await NotifyService.Notify(executor, formattedContents);
				}
				else
				{
					var contentsLabel = realViewing.IsRoom ? "Contents:" : "Carrying:";
					var contentsList = string.Join("\n", visibleContents.Select(x => x.Object().Name));
					await NotifyService.Notify(executor, $"{contentsLabel}\n{contentsList}");
				}
			}

			if (visibleExits.Count > 0 && realViewing.IsRoom)
			{
				var exitFormatResult = await AttributeService.GetAttributeAsync(executor, realViewing, "EXITFORMAT",
					IAttributeService.AttributeMode.Read, false);
				
				if (exitFormatResult.IsAttribute)
				{
					var exitDbrefs = string.Join(" ", visibleExits.Select(x => x.Object().DBRef.ToString()));
					
					formatArgs["0"] = new CallState(exitDbrefs);
					
					var formattedExits = await AttributeService.EvaluateAttributeFunctionAsync(
						parser, executor, realViewing, "EXITFORMAT", formatArgs);
					
					await NotifyService.Notify(executor, formattedExits);
				}
				else
				{
					var isTransparent = await realViewing.IsTransparent();
					if (isTransparent)
					{
						foreach (var exit in visibleExits)
						{
							var exitObj = exit.WithRoomOption().Object();
							var destination = exit.IsExit ? await exit.AsExit.Home.WithCancellation(CancellationToken.None) : null;
							var destName = destination != null ? destination.Object().Name : "*UNLINKED*";
							
							if (await exit.WithRoomOption().IsOpaque())
							{
								await NotifyService.Notify(executor, exitObj.Name);
							}
							else
							{
								await NotifyService.Notify(executor, $"{exitObj.Name} to {destName}");
							}
						}
					}
					else
					{
						var exitNames = string.Join(", ", visibleExits.Select(x => x.Object().Name));
						await NotifyService.Notify(executor, $"Obvious exits:\n{exitNames}");
					}
				}
			}
		}

		return new CallState(viewingObject.DBRef.ToString());
	}

	[SharpCommand(Name = "EXAMINE", Switches = ["BRIEF", "DEBUG", "MORTAL", "PARENT", "ALL", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		AnyOptionalSharpObject viewing;
		string? attributePattern = null;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToString();
			var split = HelperFunctions.SplitDbRefAndOptionalAttr(argText);
			
			if (split.TryPickT0(out var details, out _))
			{
				var (objectName, maybeAttributePattern) = details;
				attributePattern = maybeAttributePattern;
				
				var locate = await LocateService!.LocateAndNotifyIfInvalid(
					parser,
					enactor,
					enactor,
					objectName,
					LocateFlags.All);

				if (locate.IsValid())
				{
					viewing = locate.WithoutError();
				}
				else
				{
					return new None();
				}
			}
			else
			{
				var locate = await LocateService!.LocateAndNotifyIfInvalid(
					parser,
					enactor,
					enactor,
					argText,
					LocateFlags.All);

				if (locate.IsValid())
				{
					viewing = locate.WithoutError();
				}
				else
				{
					return new None();
				}
			}
		}
		else
		{
			viewing = (await Mediator!.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing.IsNone())
		{
			return new None();
		}

		var viewingKnown = viewing.Known();
		
		var canExamine = await PermissionService!.CanExamine(executor, viewingKnown);
		
		if (switches.Contains("MORTAL") && await executor.IsWizard())
		{
			canExamine = await PermissionService.Controls(executor, viewingKnown);
		}
		
		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService!.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.");
			return new CallState(limitedObj.DBRef.ToString());
		}

		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator!.CreateStream(new GetContentsQuery(viewingKnown.AsContainer))
				.ToArrayAsync();

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var location = obj.Key;
		var contentKeys = contents!.Select(x => x.Object().Name);
		// var exitKeys = await Mediator!.Send(new GetExitsQuery(obj.DBRef));
		// THIS FAILS ^ -- Mediator.InvalidMessageException:
		// Tried to send/publish invalid message type to Mediator: SharpMUSH.Library.Queries.Database.GetExitsQuery
		var description = (await AttributeService!.GetAttributeAsync(enactor, viewingKnown, "DESCRIBE",
				IAttributeService.AttributeMode.Read, false))
			.Match(
				attr => MModule.getLength(attr.Last().Value) == 0
					? MModule.single("There is nothing to see here")
					: attr.Last().Value,
				none => MModule.single("There is nothing to see here"),
				error => MModule.empty());

		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var ownerObjFlags = await ownerObj.Flags.Value.ToArrayAsync();
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);
		var objPowers = obj.Powers.Value;

		var outputSections = new List<MString>();
		
		var showFlags = Configuration!.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MModule.multiple([
				name.Hilight(),
				MModule.single(" "),
				MModule.single($"(#{obj.DBRef.Number}{string.Join(string.Empty, objFlags.Select(x => x.Symbol))})")
			])
			: MModule.concat(name.Hilight(), MModule.single($" (#{obj.DBRef.Number})"));
		
		outputSections.Add(nameRow);

		if (showFlags)
		{
			outputSections.Add(MModule.single($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MModule.single($"Type: {obj.Type}"));
		}
		
		if (!switches.Contains("BRIEF"))
		{
			outputSections.Add(description);
		}
		
		var ownerRow = showFlags
			? MModule.single($"Owner: {ownerName.Hilight()}" +
			                 $"(#{ownerObj.DBRef.Number}{string.Join(string.Empty, ownerObjFlags.Select(x => x.Symbol))})")
			: MModule.single($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);
		
		outputSections.Add(MModule.single($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));
		
		// Display locks with flags
		if (obj.Locks.Count > 0)
		{
			var lockLines = obj.Locks
				.Select(kvp => 
				{
					var lockName = kvp.Key;
					var lockData = kvp.Value;
					var flagsStr = LockService!.FormatLockFlags(lockData.Flags);
					var flagsDisplay = string.IsNullOrEmpty(flagsStr) ? "" : $"[{flagsStr}]";
					return $"{lockName}{flagsDisplay}: {lockData.LockString}";
				})
				.ToList();
			
			outputSections.Add(MModule.single($"Locks:"));
			foreach (var lockLine in lockLines)
			{
				outputSections.Add(MModule.single($"  {lockLine}"));
			}
		}
		
		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MModule.single($"Powers: {string.Join(" ", powersList)}"));
		}
		
		// Display warnings if any are set
		if (obj.Warnings != WarningType.None)
		{
			var warningsList = WarningTypeHelper.UnparseWarnings(obj.Warnings);
			outputSections.Add(MModule.single($"Warnings: {warningsList}"));
		}
		
		// Display channels if player is on any (would require channel membership query)
		// For now, this would need channel service integration
		
		if (switches.Contains("DEBUG") && await executor.IsWizard())
		{
			outputSections.Add(MModule.single($"Created: {obj.CreationTime} ({DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F})"));
		}
		else
		{
			// TODO: Match proper date format: Mon Feb 26 18:05:10 2007
			outputSections.Add(MModule.single($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));
		}

		await NotifyService!.Notify(enactor, MModule.multipleWithDelimiter(MModule.single("\n"), outputSections));

		if (!switches.Contains("BRIEF"))
		{
			var checkParents = switches.Contains("PARENT");
			
			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				var patternMode = IAttributeService.AttributePatternMode.Wildcard;
				
				atrs = await AttributeService.GetAttributePatternAsync(
					enactor, 
					viewingKnown, 
					attributePattern, 
					checkParents, 
					patternMode);
			}
			else
			{
				atrs = await AttributeService.GetVisibleAttributesAsync(enactor, viewingKnown);
			}

			if (atrs.IsAttribute)
			{
				var showPublicOnly = Configuration!.CurrentValue.Cosmetic.ExaminePublicAttributes;
				var showAll = switches.Contains("ALL");
				
				foreach (var attr in atrs.AsAttributes)
				{
					const string VeiledFlagName = "VEILED";
					if (!showAll && attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}

					var attrOwner = await attr.Owner.WithCancellation(CancellationToken.None);
					var attrFlagsStr = attr.Flags.Any() ? $"{string.Join("", attr.Flags.Select(f => f.Symbol))} " : "";

					if (!await PermissionService.CanViewAttribute(enactor, viewingKnown, attr)) 
					    // || showPublicOnly && !showAll && !attr.IsVisual())
					{
						continue;
					}

					await NotifyService!.Notify(enactor,
						MModule.concat(
							MModule.single($"{attr.LongName} [{attrFlagsStr}#{attrOwner!.Object.DBRef.Number}]: ").Hilight(),
							attr.Value));
				}
			}
		}

		if (!switches.Contains("OPAQUE") && !switches.Contains("BRIEF") && contents.Length > 0)
		{
			// TODO: Proper carry format.
			await NotifyService!.Notify(enactor, $"Contents:\n" +
			                                    $"{string.Join("\n", contentKeys)}");
		}

		if (!switches.Contains("BRIEF") && !viewingKnown.IsRoom)
		{
			// TODO: Proper Format.
			await NotifyService.Notify(enactor, $"Home: {(await viewingKnown.MinusRoom().Home()).Object().Name}");
			await NotifyService.Notify(enactor,
				$"Location: {(await viewingKnown.AsContent.Location()).Object().Name}");
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "@PEMIT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> PrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args["1"].Message!.ToString();
		var targetListText = MModule.plainText(args["0"].Message!);
		var nameListTargets = ArgHelpers.NameList(targetListText);

		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		await CommunicationService!.SendToMultipleObjectsAsync(
			parser,
			executor,
			enactor,
			nameListTargets.ToAsyncEnumerable(),
			_ => notification,
			INotifyService.NotificationType.Announce,
			notifyOnPermissionFailure: true);

		return new None();
	}

	[SharpCommand(Name = "GOTO", Behavior = CB.Default, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> GoTo(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		if (parser.CurrentState.Arguments.Count == 0)
		{
			await NotifyService!.Notify(executor, "You can't go that way.");
			return CallState.Empty;
		}

		var exit = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			args["0"].Message!.ToString(),
			LocateFlags.ExitsInTheRoomOfLooker
			| LocateFlags.EnglishStyleMatching
			| LocateFlags.ExitsPreference
			| LocateFlags.OnlyMatchTypePreference
			| LocateFlags.FailIfNotPreferred);

		if (!exit.IsValid())
		{
			return CallState.Empty;
		}

		var exitObj = exit.AsExit;
		
		var homeLocation = await exitObj.Home.WithCancellation(CancellationToken.None);
		AnySharpContainer destination;
		
		if (homeLocation.Object().DBRef.Number == -1)
		{
			var destAttr = await AttributeService!.GetAttributeAsync(
				executor, exitObj, "DESTINATION", IAttributeService.AttributeMode.Read, false);
			
			if (destAttr.IsNone || destAttr.IsError)
			{
				await NotifyService!.Notify(executor, "That exit doesn't go anywhere.");
				return CallState.Empty;
			}
			
			var destValue = destAttr.AsAttribute.Last().Value.ToPlainText();
			var located = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				destValue!,
				LocateFlags.All);
			
			if (!located.IsValid())
			{
				await NotifyService!.Notify(executor, "That exit doesn't go anywhere.");
				return CallState.Empty;
			}
			
			var locatedObj = located.WithoutError().WithoutNone();
			if (!locatedObj.IsContainer)
			{
				await NotifyService!.Notify(executor, "That exit doesn't go to a valid location.");
				return CallState.Empty;
			}
			
			destination = locatedObj.AsContainer;
		}
		else
		{
			destination = homeLocation;
		}

		if (!await PermissionService!.CanGoto(executor, exitObj, destination))
		{
			await NotifyService!.Notify(executor, "You can't go that way.");
			return CallState.Empty;
		}

		if (await MoveService!.WouldCreateLoop(executor.AsContent, destination))
		{
			await NotifyService!.Notify(executor, "You can't go that way - it would create a containment loop.");
			return CallState.Empty;
		}

		await Mediator!.Send(new MoveObjectCommand(executor.AsContent, destination));

		return new CallState(destination.ToString());
	}


	[SharpCommand(Name = "@TELEPORT", Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2,
		Switches = ["LIST", "INSIDE", "QUIET"])]
	public static async ValueTask<Option<CallState>> Teleport(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var destinationString = MModule.plainText(args.Count == 1 ? args["0"].Message : args["1"].Message);
		var toTeleport = MModule.plainText(args.Count == 1 ? MModule.single(executor.ToString()) : args["0"].Message);

		var isList = parser.CurrentState.Switches.Contains("LIST");

		IEnumerable<OneOf<DBRef, string>> toTeleportList;
		if (isList)
		{
			toTeleportList = ArgHelpers.NameList(toTeleport);
		}
		else
		{
			var isDbRef = DBRef.TryParse(toTeleport, out var objToTeleport);
			toTeleportList = [isDbRef ? objToTeleport!.Value : toTeleport];
		}

		var toTeleportStringList = toTeleportList.Select(x => x.ToString());

		var destination = await LocateService!.LocateAndNotifyIfInvalid(parser,
			executor,
			executor,
			destinationString,
			LocateFlags.All);

		if (!destination.IsValid())
		{
			await NotifyService!.Notify(executor, "You can't go that way.");
			return CallState.Empty;
		}

		var validDestination = destination.WithoutError().WithoutNone();

		if (validDestination.IsExit)
		{
			// TODO: Implement Teleporting through an Exit.
			return CallState.Empty;
		}

		var destinationContainer = validDestination.AsContainer;

		foreach (var obj in toTeleportStringList)
		{
			var locateTarget = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, obj,
				LocateFlags.All);
			if (!locateTarget.IsValid() || locateTarget.IsRoom)
			{
				await NotifyService!.Notify(executor, Errors.ErrorNotVisible);
				continue;
			}

			var target = locateTarget.WithoutError().WithoutNone();
			var targetContent = target.AsContent;
			if (!await PermissionService!.Controls(executor, target))
			{
				await NotifyService!.Notify(executor, Errors.ErrorCannotTeleport);
				continue;
			}

			// Execute the move using the MoveService which handles:
			// - Containment loop checking
			// - Permission validation
			// - Enter/Leave/Teleport hook triggering
			// - Notifications
			var isSilent = parser.CurrentState.Switches.Contains("QUIET");
			var moveResult = await MoveService!.ExecuteMoveAsync(
				parser,
				targetContent,
				destinationContainer,
				executor.Object().DBRef,
				"teleport",
				isSilent);
			
			if (moveResult.IsT1)
			{
				await NotifyService!.Notify(executor, moveResult.AsT1.Value);
				continue;
			}
			
			// If the target is a player and not silent, notify them of the teleport
			if (target.IsPlayer && !isSilent)
			{
				// Notify the target player that they were teleported
				await NotifyService!.Notify(target.Object().DBRef, "You have been teleported.");
				
				// TODO: Show the target player their new location (equivalent to LOOK)
				// This requires executing commands in the target's parser context, not the executor's
				// For now, the player can manually type 'look' to see their surroundings
			}
		}

		return new CallState(destination.ToString());
	}

	[SharpCommand(Name = "@FIND", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged,
		MinArgs = 0, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Find(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		string? searchName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			searchName = args["0"].Message?.ToPlainText();
		}
		
		int? beginDbref = null;
		int? endDbref = null;
		
		if (args.Count >= 2 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}
		
		if (args.Count >= 3 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}
		
		var matchCount = 0;
		
		await NotifyService!.Notify(executor, 
			$"@find: Searching for objects{(searchName != null ? $" matching '{searchName}'" : "")}...");
		
		// TODO: Implement full database query to find matching objects. This would require
		// querying all objects in the database (or within range), checking if executor controls each object,
		// matching object names against searchName pattern, and displaying results.
		
		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.Notify(executor, 
				$"Range: {beginDbref ?? 0} to {endDbref?.ToString() ?? "end"}");
		}
		
		await NotifyService.Notify(executor, 
			"Note: Full @find database search not yet implemented.");
		await NotifyService.Notify(executor, 
			$"Found {matchCount} matching objects.");
		
		return new CallState(matchCount.ToString());
	}

	[SharpCommand(Name = "@HALT", Switches = ["ALL", "NOEVAL", "PID"], Behavior = CB.Default | CB.EqSplit | CB.RSBrace,
		MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Halt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @halt[/noeval] <object>[=<action list>] 
		// @halt/pid <pid>
		// @halt/all or @allhalt
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();
		
		// @halt/all - halt all objects in the game (wizard only)
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			// Halt all objects in the game
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				await scheduler.Halt(obj.DBRef);
			}
			
			await NotifyService!.Notify(executor, "All objects halted.");
			return CallState.Empty;
		}
		
		// @halt/pid - halt specific queue entry (not yet fully implemented)
		if (switches.Contains("PID"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService!.Notify(executor, "You must specify a process ID.");
				return new CallState("#-1 NO PID SPECIFIED");
			}
			
			// This would require additional TaskScheduler methods to halt by PID
			await NotifyService!.Notify(executor, $"@halt/pid: PID-specific halting not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// @halt with no arguments - clear executor's queue without setting HALT flag
		if (args.Count == 0)
		{
			await scheduler.Halt(executor.Object().DBRef);
			await NotifyService!.Notify(executor, "Halted.");
			return CallState.Empty;
		}
		
		// @halt <object>[=<actions>]
		var targetName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(targetName))
		{
			await NotifyService!.Notify(executor, "You must specify a target object.");
			return new CallState("#-1 NO TARGET SPECIFIED");
		}
		
		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);
		
		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}
		
		var target = maybeTarget.WithoutError().WithoutNone();
		
		var hasHaltPower = await executor.HasPower("HALT");
		var canHalt = await PermissionService!.Controls(executor, target) || 
		              await executor.IsWizard() || hasHaltPower;
		
		if (!canHalt)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState(Errors.ErrorPerm);
		}
		
		var targetObject = target.Object();
		var hasReplacementActions = args.Count >= 2;
		var replacementActions = hasReplacementActions ? args["1"].Message : null;
		
		if (target.IsPlayer)
		{
			await scheduler.Halt(targetObject.DBRef);
			
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await scheduler.Halt(obj.DBRef);
				}
			}
			
			if (hasReplacementActions)
			{
				await scheduler.WriteCommandList(replacementActions!, parser.CurrentState);
			}
			
			await NotifyService!.Notify(executor, $"Halted {targetObject.Name} and all their objects.");
		}
		else
		{
			await scheduler.Halt(targetObject.DBRef);
			
			if (hasReplacementActions)
			{
				await scheduler.WriteCommandList(replacementActions!, parser.CurrentState);
				await NotifyService!.Notify(executor, $"Halted {targetObject.Name} with replacement actions.");
			}
			else
			{
				var haltFlag = await Mediator!.Send(new GetObjectFlagQuery("HALT"));
				if (haltFlag != null)
				{
					await Mediator.Send(new SetObjectFlagCommand(target, haltFlag));
				}
				await NotifyService!.Notify(executor, $"Halted {targetObject.Name}.");
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@NOTIFY", Switches = ["ALL", "ANY", "SETQ", "QUIET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 1, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Notify(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.Except(["QUIET"]).ToArray();
		var notifyType = "ANY";
		var args = parser.CurrentState.Arguments;

		if ((parser.CurrentState.Arguments.Count == 0) || string.IsNullOrEmpty(args["0"].Message?.ToPlainText()))
		{
			await NotifyService!.Notify(executor, "You must specify an object to use for the semaphore.");
			return new None();
		}

		switch (switches)
		{
			case ["ALL"]:
				notifyType = "ALL";
				break;
			case ["ANY"]:
				notifyType = "ANY";
				break;
			case ["SETQ"]:
				notifyType = "SETQ";
				break;
			case []:
				// No switches - default to ANY
				break;
			default:
				return new CallState(Errors.ErrorTooManySwitches);
		}

		var objectAndAttribute = HelperFunctions.SplitDbRefAndOptionalAttr(args["0"].Message!.ToPlainText());
		if (objectAndAttribute.IsT1 && objectAndAttribute.AsT1 == false)
		{
			await NotifyService!.Notify(executor,
				"You must specify a valid object with an optional valid attribute to use for the semaphore.");
			return new None();
		}

		var (db, maybeAttributeString) = objectAndAttribute.AsT0;
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
			db, LocateFlags.All);

		if (maybeObject.IsError) return maybeObject.AsError;
		var objectToNotify = maybeObject.AsSharpObject;

		var attribute = string.IsNullOrEmpty(maybeAttributeString) ? DefaultSemaphoreAttribute : maybeAttributeString;

		var attributeContents = await AttributeService!.GetAttributeAsync(executor, objectToNotify, attribute,
			IAttributeService.AttributeMode.Execute, false);

		if (attributeContents.IsError)
		{
			return new CallState(attributeContents.AsError.Value);
		}

		int oldSemaphoreCount = 0;
		if (attributeContents.IsAttribute &&
		    int.TryParse(attributeContents.AsAttribute.Last().Value.ToPlainText(), out var semaphoreCount))
		{
			oldSemaphoreCount = semaphoreCount;
		}

		// Check for =<number> parameter or =<qreg>,<value> pairs for /setq
		int notifyCount = 1;
		Dictionary<string, MString>? qRegisters = null;
		
		if (notifyType == "SETQ")
		{
			// With CB.RSArgs, each comma-separated value becomes a separate argument
			// So @notify/setq obj=0,val1,1,val2 becomes: args[0]=obj, args[1]=0, args[2]=val1, args[3]=1, args[4]=val2
			if (args.Count < 3) // Need at least object, qreg, and value
			{
				await NotifyService!.Notify(executor, "You must specify Q-register assignments.");
				return new CallState("#-1 MISSING QREG ASSIGNMENTS");
			}
			
			var qregArgCount = args.Count - 1; // Subtract 1 for arg[0] which is the object
			if (qregArgCount % 2 != 0)
			{
				await NotifyService!.Notify(executor, "Q-register assignments must be in pairs: qreg,value[,qreg,value...]");
				return new CallState("#-1 INVALID QREG PAIRS");
			}
			
			qRegisters = new Dictionary<string, MString>();
			for (var i = 1; i < args.Count; i += 2)
			{
				var qregName = args[i.ToString()].Message!.ToPlainText().Trim();
				var qregValue = args[(i + 1).ToString()].Message!.ToPlainText();
				qRegisters[qregName] = MModule.single(qregValue);
			}
		}
		else if (args.Count > 1 && args.TryGetValue("1", out var arg1))
		{
			var countArg = arg1.Message?.ToPlainText();
			if (!string.IsNullOrEmpty(countArg) &&
			    (!int.TryParse(countArg, out notifyCount) || notifyCount < 1))
			{
				await NotifyService!.Notify(executor, "Invalid number specified.");
				return new CallState("#-1 INVALID NUMBER");
			}
		}

		var dbRefAttribute = new DbRefAttribute(objectToNotify.Object().DBRef, attribute.Split("`"));

		switch (notifyType)
		{
			case "ANY":
				// Notify specified number of tasks (default 1)
				await Mediator!.Send(new NotifySemaphoreRequest(dbRefAttribute, oldSemaphoreCount, notifyCount));
				var newCount = oldSemaphoreCount - notifyCount;
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(newCount.ToString()));
				break;
			case "ALL":
				await Mediator!.Send(new NotifyAllSemaphoreRequest(dbRefAttribute));
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(0.ToString()));
				break;
			case "SETQ":
				// Modify Q-registers of the first waiting task, then trigger it
				var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();
				var modified = await scheduler.ModifyQRegisters(dbRefAttribute, qRegisters!);
				if (!modified)
				{
					await NotifyService!.Notify(executor, "No task is waiting on that semaphore.");
					return new CallState("#-1 NO WAITING TASK");
				}
				// After modifying Q-registers, trigger the task execution (same as regular @notify but only 1 task)
				await Mediator!.Send(new NotifySemaphoreRequest(dbRefAttribute, oldSemaphoreCount, 1));
				var newCountSetQ = oldSemaphoreCount - 1;
				await AttributeService!.SetAttributeAsync(executor, objectToNotify, attribute,
					MModule.single(newCountSetQ.ToString()));
				// Don't show "Notified." for /setq (different from regular @notify)
				return new None();
		}

		if (!parser.CurrentState.Switches.Contains("QUIET"))
		{
			await NotifyService!.Notify(executor, "Notified.");
		}

		return new None();
	}

	[SharpCommand(Name = "@NSPROMPT", Switches = ["SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> NoSpoofPrompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Noisy, Silent, NoEval

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var target = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var message = parser.CurrentState.Arguments["1"].Message!;

		var maybeFound =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, target,
				LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject;

		if (!await PermissionService!.CanInteract(found, executor, IPermissionService.InteractType.Hear))
		{
			await NotifyService!.Notify(executor, $"{found.Object().Name} does not want to hear from you.");
			return CallState.Empty;
		}

		await NotifyService!.Prompt(found, message, executor, INotifyService.NotificationType.NSEmit);

		return new CallState(message);
	}

	[SharpCommand(Name = "@SCAN", Switches = ["ROOM", "SELF", "ZONE", "GLOBALS"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Scan(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var switches = parser.CurrentState.Switches.Any()
			? parser.CurrentState.Switches.ToArray()
			: ["ROOM", "SELF", "ZONE", "GLOBALS"];

		List<string> runningOutput = [];
		
		// Helper to check if executor can scan an object
		// Can scan if executor controls the object OR object has VISUAL flag
		async Task<bool> CanScan(AnySharpObject obj)
		{
			var controls = await PermissionService!.Controls(executor, obj);
			if (controls) return true;
			
			var isVisual = await obj.HasFlag("VISUAL");
			return isVisual;
		}

		if (executor.IsContent && switches.Contains("ROOM"))
		{
			var where = await executor.AsContent.Location();
			var whereContent = where.Content(Mediator!);

			// notify: Matches on contents of this room:
			var matchedContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					whereContent.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					// Check permission before showing
					if (await CanScan(obj))
					{
						runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
						await NotifyService!.Notify(executor,
							$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
					}
				}
			}
		}

		if (executor.IsContainer && switches.Contains("SELF"))
		{
			var executorContents = executor.AsContainer.Content(Mediator!);

			// notify: Matches on carried objects:
			var matchedContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					executorContents.Select(x => x.WithRoomOption()),
					arg0);

			if (matchedContent.IsSome())
			{
				foreach (var (i, (obj, attr, _)) in matchedContent.AsValue().Index())
				{
					// Check permission before showing
					if (await CanScan(obj))
					{
						runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
						await NotifyService!.Notify(executor,
							$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
					}
				}
			}
		}

		if (switches.Contains("ZONE"))
		{
			// Get the zone of the executor's location
			if (executor.IsContent)
			{
				var location = await executor.AsContent.Location();
				var locationZone = await location.Object().Zone.WithCancellation(CancellationToken.None);
				
				if (!locationZone.IsNone)
				{
					var zoneObject = locationZone.Known;
					
					// Get contents of the zone master object
					var zoneContents = Mediator!.CreateStream(new GetContentsQuery(zoneObject.Object().DBRef))
						?? AsyncEnumerable.Empty<AnySharpContent>();
					
					// Match user-defined commands in zone contents
					var zoneMatched =
						await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
							zoneContents.Select(x => x.WithRoomOption()),
							arg0);
					
					if (zoneMatched.IsSome())
					{
						foreach (var (i, (obj, attr, _)) in zoneMatched.AsValue().Index())
						{
							// Check permission before showing
							if (await CanScan(obj))
							{
								runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
								await NotifyService!.Notify(executor,
									$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
							}
						}
					}
				}
			}
		}

		if (switches.Contains("GLOBAL"))
		{
			var masterRoom = new DBRef(Convert.ToInt32(Configuration!.CurrentValue.Database.MasterRoom));
			var masterRoomContents = Mediator!.CreateStream(new GetContentsQuery(masterRoom))
			                         ?? AsyncEnumerable.Empty<AnySharpContent>();

			var masterRoomContent =
				await CommandDiscoveryService!.MatchUserDefinedCommand(parser,
					masterRoomContents.Select(x => x.WithRoomOption()),
					arg0);

			foreach (var (i, (obj, attr, _)) in masterRoomContent.AsValue().Index())
			{
				// Check permission before showing
				if (await CanScan(obj))
				{
					runningOutput.Add($"#{obj.Object().DBRef.Number}/{attr.LongName}");
					await NotifyService!.Notify(executor,
						$"{obj.Object().Name}\t[{i}: #{obj.Object().DBRef.Number}/{attr.LongName}]");
				}
			}
		}

		return new CallState(string.Join(" ", runningOutput));
	}

	[SharpCommand(Name = "@SWITCH",
		Switches = ["NOTIFY", "FIRST", "ALL", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 3, MaxArgs = int.MaxValue)]
	public static async ValueTask<Option<CallState>> Switch(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		//  @switch[/<switch>] <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]
		//  @switch/all runs <action>s for all matching <expr>s. Default for @switch.
		//  @switch/first runs <action> for the first matching <expr> only. Same as @select, and often the desired behaviour.
		//	@switch/notify queues "@notify me" after the last <action>. 
		//	@switch/inline runs all actions in place, instead of creating a new queue entry for them.
		//	@switch/regexp makes <expr>s case-insensitive regular expressions, not wildcard/glob patterns.

		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var strArg = args["0"];
		Option<MString> defaultArg = new None();
		var pairs = args.Values.Skip(1).Pairwise();
		var matched = false;

		if (args.Count % 2 == 0)
		{
			defaultArg = args.Last().Value.Message!;
		}

		foreach (var (expr, action) in pairs)
		{
			if (expr is null) break;

			// TODO: Make this use a glob.
			if (expr.Message! == strArg)
			{
				matched = true;
				// This is Inline.
				await parser.CommandListParseVisitor(action.Message!)();
			}
		}

		if (defaultArg.IsSome() && !matched)
		{
			await parser.CommandListParseVisitor(defaultArg.AsValue())();
		}

		return new CallState(matched);
	}

	[SharpCommand(Name = "@WAIT", Switches = ["PID", "UNTIL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Wait(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments.GetValueOrDefault("0")?.Message!.ToPlainText()!;
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		/*
		 *  @wait/pid <pid>=<seconds>
		 *	@wait/pid <pid>=[+-]<adjustment>
		 *	@wait/pid/until <pid>=<time>
		 */
		if (switches.Contains("PID"))
		{
			return await AtWaitForPid(parser, arg0, executor, arg1?.ToPlainText(), switches);
		}

		if (arg1 is null)
		{
			await NotifyService!.Notify(executor, "Command list missing");
			return new CallState("#-1 MISSING COMMAND LIST ARGUMENT");
		}

		//  @wait[/until] <time>=<command_list>
		if (double.TryParse(arg0, out var time))
		{
			TimeSpan convertedTime;
			if (!switches.Contains("UNTIL"))
			{
				convertedTime = DateTimeOffset.FromUnixTimeSeconds((long)time) - DateTimeOffset.UtcNow;
			}
			else
			{
				convertedTime = TimeSpan.FromSeconds(time);
			}

			await Mediator!.Publish(new QueueDelayedCommandListRequest(arg1, parser.CurrentState, convertedTime));
			return CallState.Empty;
		}

		var splitBySlashes = arg0.Split('/');

		// @wait <object>=<command_list>
		if (splitBySlashes.Length == 1)
		{
			var maybeObject = await
				LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0, LocateFlags.All);

			if (maybeObject.IsError)
			{
				return maybeObject.AsError;
			}

			var located = maybeObject.AsSharpObject;
			if (!await PermissionService!.Controls(executor, located))
			{
				await NotifyService!.Notify(executor, "Permission Denied.");
				return new CallState(Errors.ErrorPerm);
			}

			await QueueSemaphore(parser, located, DefaultSemaphoreAttributeArray, arg1);
			return CallState.Empty;
		}

		var maybeLocate =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, splitBySlashes[0],
				LocateFlags.All);
		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var foundObject = maybeLocate.AsSharpObject;
		var untilTime = 0.0d;

		switch (splitBySlashes.Length)
		{
			// @wait[/until] <object>/<time>=<command_list>
			// @wait <object>/<attribute>=<command_list>
			case 2 when switches.Contains("UNTIL"):
			{
				if (!double.TryParse(splitBySlashes[1], out untilTime))
				{
					await NotifyService!.Notify(executor, "Invalid time argument format");
					return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));
				}

				var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;

				await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, newUntilTime, arg1);
				return CallState.Empty;
			}

			case 2 when double.TryParse(splitBySlashes[1], out untilTime):
				await QueueSemaphoreWithDelay(parser, foundObject, DefaultSemaphoreAttributeArray, TimeSpan.FromSeconds(untilTime), arg1);
				return CallState.Empty;

			// TODO: Ensure the attribute has the same flags as the SEMAPHORE @attribute, otherwise it can't be used!
			case 2:
				await QueueSemaphore(parser, foundObject, splitBySlashes[1].Split('`'), arg1);
				return CallState.Empty;

			// @wait[/until] <object>/<attribute>/<time>=<command list>
			case 3 when !double.TryParse(splitBySlashes[2], out untilTime):
				await NotifyService!.Notify(executor, "Invalid time argument format");
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "TIME ARGUMENT"));

			// TODO: Validate valid attribute value.
			case 3 when switches.Contains("UNTIL"):
			{
				var newUntilTime = DateTimeOffset.FromUnixTimeSeconds((long)untilTime) - DateTimeOffset.UtcNow;
				await QueueSemaphoreWithDelay(parser, foundObject, splitBySlashes[1].Split('`'), newUntilTime, arg1);
				return CallState.Empty;
			}

			case 3:
				await QueueSemaphoreWithDelay(parser, foundObject, splitBySlashes[1].Split('`'),
					TimeSpan.FromSeconds(untilTime), arg1);
				return CallState.Empty;

			default:
				await NotifyService!.Notify(executor, "Invalid first argument format");
				return new CallState(string.Format(Errors.ErrorBadArgumentFormat, "FIRST ARGUMENT"));
		}
	}

	private static async ValueTask QueueSemaphore(IMUSHCodeParser parser, AnySharpObject located, string[] attribute,
		MString arg1)
	{
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));
		var attrValues = Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute));
		var attrValue = await attrValues.LastOrDefaultAsync();

		if (attrValue is null)
		{
			
			await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));
			
			var dbRefAttr = new DbRefAttribute(located.Object().DBRef, attribute);
			
			await Mediator.Send(new QueueCommandListRequest(arg1, parser.CurrentState,
				dbRefAttr, 0));
			
			return;
		}

		if (!int.TryParse(attrValue.Value.ToPlainText(), out var last))
		{
			await NotifyService!.Notify(executor, Errors.ErrorInteger);
			return;
		}

		await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));
		
		var dbRefAttr2 = new DbRefAttribute(located.Object().DBRef, attribute);
		
		await Mediator.Send(new QueueCommandListRequest(arg1, parser.CurrentState,
			dbRefAttr2, last));
		
	}

	private static async ValueTask QueueSemaphoreWithDelay(IMUSHCodeParser parser, AnySharpObject located,
		string[] attribute, TimeSpan delay, MString arg1)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));
		var attrValues = Mediator.CreateStream(new GetAttributeQuery(located.Object().DBRef, attribute));
		var attrValue = await attrValues.LastOrDefaultAsync();

		if (attrValue is null)
		{
			await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single("0"),
				one.AsPlayer));
			await Mediator.Send(new QueueCommandListWithTimeoutRequest(arg1, parser.CurrentState,
				new DbRefAttribute(located.Object().DBRef, attribute), 0, delay));
			return;
		}

		if (!int.TryParse(attrValue.Value.ToPlainText(), out var last))
		{
			await NotifyService!.Notify(executor, Errors.ErrorInteger);
			return;
		}

		await Mediator.Send(new SetAttributeCommand(located.Object().DBRef, attribute, MModule.single($"{last + 1}"),
			one.AsPlayer));
		await Mediator.Send(new QueueCommandListWithTimeoutRequest(arg1, parser.CurrentState,
			new DbRefAttribute(located.Object().DBRef, attribute), last, delay));
	}

	private static async ValueTask<Option<CallState>> AtWaitForPid(IMUSHCodeParser parser, string? arg0,
		AnySharpObject executor, string? arg1,
		string[] switches)
	{
		if (!int.TryParse(arg0, out var pid))
		{
			await NotifyService!.Notify(executor, "Invalid PID specified.");
			return new CallState("#-1 INVALID PID");
		}

		if (string.IsNullOrEmpty(arg1))
		{
			await NotifyService!.Notify(executor, "What do you want to do with the process?");
			return new CallState(string.Format(Errors.ErrorTooFewArguments, "@WAIT", 2, 1));
		}

		var exists = Mediator!.CreateStream(new ScheduleSemaphoreQuery(pid));
		var maybeFoundPid = await exists.FirstOrDefaultAsync();

		if (maybeFoundPid is null)
		{
			await NotifyService!.Notify(executor, "Invalid PID specified.");
			return new CallState("#-1 INVALID PID");
		}

		var timeArg = arg1;

		if (switches.Contains("UNTIL"))
		{
			if (!DateTimeOffset.TryParse(timeArg, out var dateTimeOffset))
			{
				await NotifyService!.Notify(executor, "Invalid time specified.");
				return new CallState("#-1 INVALID TIME");
			}

			var until = DateTimeOffset.UtcNow - dateTimeOffset;
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));

			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('+') || arg1.StartsWith('-'))
		{
			timeArg = arg1.Skip(1).ToString();
		}

		if (!long.TryParse(timeArg, out var secs))
		{
			await NotifyService!.Notify(executor, "Invalid time specified.");
			return new CallState("#-1 INVALID TIME");
		}

		if (arg1.StartsWith('+'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) + TimeSpan.FromSeconds(secs);
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		if (arg1.StartsWith('-'))
		{
			var until = (maybeFoundPid.RunDelay ?? TimeSpan.Zero) - TimeSpan.FromSeconds(secs);
			await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, until));
			return new CallState(maybeFoundPid.Pid.ToString());
		}

		await Mediator.Send(new RescheduleSemaphoreRequest(maybeFoundPid.Pid, TimeSpan.FromSeconds(secs)));
		return new CallState(maybeFoundPid.Pid.ToString());
	}

	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify a command name.");
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}
		
		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService!.Notify(executor, "You must specify a command name.");
			return new CallState("#-1 NO COMMAND SPECIFIED");
		}
		
		var isQuiet = switches.Contains("QUIET");
		
		// Administrative switches - wizard only (except DELETE which requires God)
		if (switches.Any(s => new[] { "ADD", "ALIAS", "CLONE", "DELETE", "DISABLE", "ENABLE", "RESTRICT" }.Contains(s)))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			// Handle administrative operations
			if (switches.Contains("ADD"))
			{
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/add: Dynamic command creation not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("ALIAS"))
			{
				var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(aliasName))
				{
					await NotifyService!.Notify(executor, "You must specify an alias name.");
					return new CallState("#-1 NO ALIAS SPECIFIED");
				}
				
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/alias: Dynamic command aliasing not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("CLONE"))
			{
				var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(cloneName))
				{
					await NotifyService!.Notify(executor, "You must specify a clone name.");
					return new CallState("#-1 NO CLONE NAME SPECIFIED");
				}
				
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/clone: Command cloning not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("DELETE"))
			{
				if (!executor.IsGod())
				{
					await NotifyService!.Notify(executor, "Only God can delete commands.");
					return new CallState(Errors.ErrorPerm);
				}
				
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/delete: Command deletion not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("DISABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/disable: Command disabling not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("ENABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/enable: Command enabling not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
			
			if (switches.Contains("RESTRICT"))
			{
				var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (!isQuiet)
				{
					await NotifyService!.Notify(executor, $"@command/restrict: Command restriction not yet implemented.");
				}
				return new CallState("#-1 NOT IMPLEMENTED");
			}
		}
		
		// No switches - display command information
		if (CommandLibrary == null)
		{
			await NotifyService!.Notify(executor, "Command library unavailable.");
			return new CallState("#-1 LIBRARY UNAVAILABLE");
		}
		
		// Try to find the command in the library
		if (!CommandLibrary.TryGetValue(commandName, out var commandInfo))
		{
			await NotifyService!.Notify(executor, $"Command '{commandName}' not found.");
			return new CallState("#-1 COMMAND NOT FOUND");
		}
		
		var (definition, isSystem) = commandInfo;
		var attr = definition.Attribute;
		
		await NotifyService!.Notify(executor, $"Command: {attr.Name}");
		await NotifyService.Notify(executor, $"  Type: {(isSystem ? "Built-in" : "User-defined")}");
		await NotifyService.Notify(executor, $"  Min Args: {attr.MinArgs}");
		await NotifyService.Notify(executor, $"  Max Args: {attr.MaxArgs}");
		
		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			await NotifyService.Notify(executor, $"  Switches: {string.Join(", ", attr.Switches)}");
		}
		
		var behaviors = new List<string>();
		if ((attr.Behavior & CB.Default) != 0) behaviors.Add("Default");
		if ((attr.Behavior & CB.EqSplit) != 0) behaviors.Add("EqSplit");
		if ((attr.Behavior & CB.LSArgs) != 0) behaviors.Add("LSArgs");
		if ((attr.Behavior & CB.RSArgs) != 0) behaviors.Add("RSArgs");
		if ((attr.Behavior & CB.RSNoParse) != 0) behaviors.Add("RSNoParse");
		if ((attr.Behavior & CB.NoGagged) != 0) behaviors.Add("NoGagged");
		if ((attr.Behavior & CB.NoParse) != 0) behaviors.Add("NoParse");
		
		if (behaviors.Count > 0)
		{
			await NotifyService.Notify(executor, $"  Behavior: {string.Join(" | ", behaviors)}");
		}
		
		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			await NotifyService.Notify(executor, $"  Lock: {attr.CommandLock}");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@DRAIN", Switches = ["ALL", "ANY"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 1,
		MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Drain(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments.GetValueOrDefault("1")?.Message?.ToPlainText();
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var one = await Mediator!.Send(new GetObjectNodeQuery(new DBRef(1)));

		if (switches.Length > 1)
		{
			await NotifyService!.Notify(executor, Errors.ErrorTooManySwitches);
			return new CallState(Errors.ErrorTooManySwitches);
		}

		var maybeObjectAndAttribute = HelperFunctions.SplitDbRefAndOptionalAttr(arg0);
		if (maybeObjectAndAttribute is { IsT1: true, AsT1: false })
		{
			await NotifyService!.Notify(executor, Errors.ErrorCantSeeThat);
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var (target, maybeAttribute) = maybeObjectAndAttribute.AsT0;
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalid(parser, executor, executor, target,
			LocateFlags.All);

		switch (maybeObject)
		{
			case { IsError: true }:
				return new CallState(maybeObject.AsError.Value);
			case { IsNone: true }:
				return new CallState(Errors.ErrorCantSeeThat);
		}

		var objectToDrain = maybeObject.AsAnyObject;
		var attribute = maybeAttribute?.Split("`") ?? DefaultSemaphoreAttributeArray;
		var hasAll = switches.Contains("ALL");
		var hasAny = switches.Contains("ANY");

		// Parse the number parameter if provided
		int? drainCount = null;
		if (!string.IsNullOrEmpty(arg1))
		{
			if (!int.TryParse(arg1, out var count) || count < 1)
			{
				await NotifyService!.Notify(executor, "Invalid number specified.");
				return new CallState("#-1 INVALID NUMBER");
			}
			drainCount = count;
		}

		// Cannot specify both /any and a specific attribute
		if (hasAny && maybeAttribute is not null)
		{
			await NotifyService!.Notify(executor, "You may not specify both /any and a specific attribute.");
			return new CallState("#-1 INVALID COMBINATION");
		}

		// Cannot specify both /all and a number
		if (hasAll && drainCount.HasValue)
		{
			await NotifyService!.Notify(executor, "You may not specify both /all and a number.");
			return new CallState("#-1 INVALID COMBINATION");
		}

		//   @drain[/any][/all] <object>[/<attribute>][=<number>]
		if (hasAny)
		{
			// Drain all semaphores on the object
			var pids = Mediator!.CreateStream(new ScheduleSemaphoreQuery(objectToDrain.Object().DBRef));
			var filteredPids = pids
				.GroupBy(data => string.Join('`', data.SemaphoreSource.Attribute), x => x.SemaphoreSource)
				.Select(x => x.First());

			await foreach (var uniqueAttribute in filteredPids)
			{
				var dbRefAttrToDrain = uniqueAttribute;
				if (hasAll || !drainCount.HasValue)
				{
					// Drain all entries and clear attribute
					await Mediator.Publish(new DrainSemaphoreRequest(dbRefAttrToDrain, null));
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute,
						MModule.single("0"),
						one.AsPlayer));
				}
				else
				{
					// Drain specified number
					await Mediator.Publish(new DrainSemaphoreRequest(dbRefAttrToDrain, drainCount.Value));
					// Adjust semaphore count
					var currentAttr = await Mediator.CreateStream(
						new GetAttributeQuery(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute)).LastOrDefaultAsync();
					if (currentAttr is not null && int.TryParse(currentAttr.Value.ToPlainText(), out var currentCount))
					{
						var newCount = currentCount + drainCount.Value;
						await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, dbRefAttrToDrain.Attribute,
							MModule.single(newCount.ToString()),
							one.AsPlayer));
					}
				}
			}
		}
		else
		{
			// Drain specific semaphore (or SEMAPHORE if not specified)
			var dbRefAttribute = new DbRefAttribute(objectToDrain.Object().DBRef, attribute);
			
			if (hasAll || !drainCount.HasValue)
			{
				// Drain all entries (default if no number specified)
				await Mediator!.Publish(new DrainSemaphoreRequest(dbRefAttribute, null));
				if (hasAll)
				{
					// /all also clears the semaphore attribute
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single("0"),
						one.AsPlayer));
				}
				else
				{
					// Without /all, just set to -1 to indicate no tasks waiting
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single("-1"),
						one.AsPlayer));
				}
			}
			else
			{
				// Drain specified number
				await Mediator!.Publish(new DrainSemaphoreRequest(dbRefAttribute, drainCount.Value));
				// Adjust semaphore count
				var currentAttr = await Mediator.CreateStream(
					new GetAttributeQuery(objectToDrain.Object().DBRef, attribute)).LastOrDefaultAsync();
				if (currentAttr is not null && int.TryParse(currentAttr.Value.ToPlainText(), out var currentCount))
				{
					var newCount = currentCount + drainCount.Value;
					await Mediator.Send(new SetAttributeCommand(objectToDrain.Object().DBRef, attribute,
						MModule.single(newCount.ToString()),
						one.AsPlayer));
				}
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FORCE", Switches = ["NOEVAL", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Force(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var objArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MModule.empty());
		var cmdListArg = ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty());
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeFound =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objArg.ToPlainText(),
				LocateFlags.All);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject;

		if (!await PermissionService!.Controls(executor, found))
		{
			await NotifyService!.Notify(executor, "Permission denied. You do not control the target.");
			return new CallState(Errors.ErrorPerm);
		}

		if (cmdListArg.Length < 1)
		{
			await NotifyService!.Notify(executor, "Force them to do what?");
			return new CallState(Errors.NothingToDo);
		}

		// Note: Queue infrastructure available via QueueCommandListRequest if needed
		// Currently executes inline for immediate response (default PennMUSH behavior)
		await parser.With(state => state with { Executor = found.Object().DBRef },
			async newParser => await newParser.CommandListParseVisitor(cmdListArg)());

		return CallState.Empty;
	}

	[SharpCommand(Name = "@IFELSE", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 2, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> IfElse(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var truthy = Predicates.Truthy(parsedIfElse!);

		if (truthy)
		{
			await parser.CommandListParse(parser.CurrentState.Arguments["1"].Message!);
		}
		else if (parser.CurrentState.Arguments.TryGetValue("2", out var arg2))
		{
			await parser.CommandListParse(arg2.Message!);
		}

		return new CallState(truthy);
	}

	[SharpCommand(Name = "@NSEMIT", Switches = ["ROOM", "NOEVAL", "SILENT"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isSpoof = true;
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor,
					InteractType.Hear));

		if (isSpoof)
		{
			var canSpoof = await executor.HasPower("CAN_SPOOF");
			var controlsExecutor = await PermissionService!.Controls(executor, enactor);

			if (!canSpoof && !controlsExecutor)
			{
				await NotifyService!.Notify(executor, "You do not have permission to spoof emits.");
				return new CallState(Errors.ErrorPerm);
			}
		}

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				isSpoof ? enactor : executor,
				INotifyService.NotificationType.NSEmit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@NSREMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofRoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		var notificationType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
				await CommunicationService!.SendToRoomAsync(
					executor,
					container,
					_ => message,
					notificationType);

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@PROMPT", Switches = ["SILENT", "NOISY", "NOEVAL", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Prompt(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			return new CallState(string.Empty);
		}

		var notification = args["1"].Message!.ToString();
		var targetListText = MModule.plainText(args["0"].Message!);
		var nameListTargets = ArgHelpers.NameList(targetListText);

		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		foreach (var target in nameListTargets)
		{
			var targetString = target.Match(dbref => dbref.ToString(), str => str);
			var maybeLocateTarget = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, enactor, enactor,
				targetString,
				LocateFlags.All);

			if (maybeLocateTarget.IsError)
			{
				await NotifyService!.Notify(executor, maybeLocateTarget.AsError.Message!);
				continue;
			}

			var locateTarget = maybeLocateTarget.AsSharpObject;

			if (!await PermissionService!.CanInteract(locateTarget, executor, IPermissionService.InteractType.Hear))
			{
				await NotifyService!.Notify(executor, $"{locateTarget.Object().Name} does not want to hear from you.");
				continue;
			}

			await NotifyService!.Prompt(locateTarget, notification);
		}

		return new None();
	}

	[SharpCommand(Name = "@SEARCH", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Search(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// @search [<player>] [<classN>=<restrictionN>[,...]][,<begin>,<end>]
		// This is a complex command that searches the database with multiple filters
		
		string? playerName = null;
		string? searchCriteria = null;
		int? beginDbref = null;
		int? endDbref = null;
		
		// Parse arguments
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var arg0 = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(arg0))
			{
				// Could be player name or search criteria
				playerName = arg0;
			}
		}
		
		if (args.Count > 1 && args.ContainsKey("1"))
		{
			searchCriteria = args["1"].Message?.ToPlainText();
		}
		
		if (args.Count > 2 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}
		
		await NotifyService!.Notify(executor, "@search: Advanced database search");
		
		if (playerName != null)
		{
			await NotifyService.Notify(executor, $"  Player filter: {playerName}");
		}
		
		if (searchCriteria != null)
		{
			await NotifyService.Notify(executor, $"  Criteria: {searchCriteria}");
		}
		
		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.Notify(executor, $"  Range: {beginDbref ?? 0} to {endDbref?.ToString() ?? "end"}");
		}
		
		// TODO: Implement full database search with filters:
		// TYPE, NAME, ZONE, PARENT, EXITS, THINGS, ROOMS, PLAYERS, FLAGS, etc.
		await NotifyService.Notify(executor, "Note: Full @search database query not yet implemented.");
		await NotifyService.Notify(executor, "0 objects found.");
		
		return new CallState("0");
	}

	[SharpCommand(Name = "@WHEREIS", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 1, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> WhereIs(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify a player to locate.");
			return new CallState("#-1 NO PLAYER SPECIFIED");
		}

		var targetName = args["0"].Message!.ToPlainText();

		// Locate the target player
		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);

		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}

		var target = maybeTarget.WithoutError().WithoutNone();

		// Check if target is a player
		if (!target.IsPlayer)
		{
			await NotifyService!.Notify(executor, "You can only @whereis players.");
			return new CallState("#-1 NOT A PLAYER");
		}

		var targetPlayer = target.AsPlayer;
		var targetObject = target.Object();
		
		// Check if target is UNFINDABLE
		var targetFlags = await targetObject.Flags.Value.ToListAsync();
		var isUnfindable = targetFlags.Any(f => f.Symbol == "U" || f.Name.Equals("UNFINDABLE", StringComparison.OrdinalIgnoreCase));

		// Notify the target that someone is trying to find them
		if (isUnfindable)
		{
			await NotifyService!.Notify(target, 
				$"{executor.Object().Name} tried to locate you, but was unable to.");
			await NotifyService.Notify(executor, 
				$"{targetObject.Name} is UNFINDABLE.");
			return new CallState("#-1 UNFINDABLE");
		}

		// Get the target's location
		var targetLocation = await target.AsContent.Location();
		var locationName = targetLocation.Object().Name;

		// Notify the target that they were found
		await NotifyService!.Notify(target, 
			$"{executor.Object().Name} has just located your position.");

		// Notify the executor of the target's location
		await NotifyService.Notify(executor, 
			$"{targetObject.Name} is in {locationName}.");

		return new CallState(targetLocation.Object().DBRef.ToString());
	}

	[SharpCommand(Name = "@BREAK", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Break(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Inline does nothing.
		var args = parser.CurrentState.Arguments;
		var nargs = args.Count;
		switch (nargs)
		{
			case 0:
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				break;
			case 1:
				if (args["0"].Message.Truthy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"];
			case 2:
				if (args["0"].Message.Truthy())
				{
					var command = await args["1"].ParsedMessage();
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"];
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CONFIG", Switches = ["SET", "SAVE", "LOWERCASE", "LIST"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Config(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var useLowercase = switches.Contains("LOWERCASE");

		// Get all configuration categories using reflection
		var optionsType = typeof(SharpMUSHOptions);
		var categoryProperties = optionsType.GetProperties();
		var allCategories = categoryProperties.Select(p => p.Name).OrderBy(n => n).ToList();

		// Helper to get all config options with metadata
		var getAllOptions = () => categoryProperties
			.SelectMany(category =>
			{
				var categoryType = category.PropertyType;
				var props = categoryType.GetProperties();
				return props.Select(prop =>
				{
					var attr = prop.GetCustomAttribute<SharpConfigAttribute>();
					if (attr == null) return null;
					var value = prop.GetValue(category.GetValue(Configuration!.CurrentValue));
					return new
					{
						Category = category.Name,
						PropertyName = prop.Name,
						ConfigAttr = attr,
						Value = value
					};
				}).Where(x => x != null);
			})
			.Select(x => x!)
			.ToList();

		// @config/set or @config/save - requires wizard/god permissions
		if (switches.Contains("SET") || switches.Contains("SAVE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}

			if (switches.Contains("SAVE") && !executor.IsGod())
			{
				await NotifyService!.Notify(executor, "Only God can use /save switch.");
				return new CallState(Errors.ErrorPerm);
			}

			// /set and /save not yet implemented - would require runtime config modification
			await NotifyService!.Notify(executor, "@config/set and @config/save are not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}

		// @config with no arguments - list categories
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "Configuration Categories:");
			foreach (var cat in allCategories)
			{
				await NotifyService.Notify(executor, $"  {cat}");
			}
			await NotifyService.Notify(executor, "Use '@config <category>' to see options in a category.");
			await NotifyService.Notify(executor, "Use '@config <option>' to see the value of an option.");
			return CallState.Empty;
		}

		var searchTerm = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";

		// Check if searchTerm is a category
		var matchingCategory = allCategories.FirstOrDefault(c =>
			c.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingCategory != null)
		{
			// List all options in the category
			var categoryOptions = getAllOptions()
				.Where(opt => opt.Category.Equals(matchingCategory, StringComparison.OrdinalIgnoreCase))
				.OrderBy(opt => opt.ConfigAttr.Name)
				.ToList();

			if (categoryOptions.Count == 0)
			{
				await NotifyService!.Notify(executor, $"No options found in category '{matchingCategory}'.");
				return CallState.Empty;
			}

			await NotifyService!.Notify(executor, $"Options in {matchingCategory}:");
			foreach (var opt in categoryOptions)
			{
				var name = useLowercase ? opt.ConfigAttr.Name.ToLower() : opt.ConfigAttr.Name;
				var value = opt.Value?.ToString() ?? "null";
				await NotifyService.Notify(executor, $"  {name}: {value}");
			}
			return CallState.Empty;
		}

		// Check if searchTerm is a specific option
		var allOptions = getAllOptions();
		var matchingOption = allOptions.FirstOrDefault(opt =>
			opt.ConfigAttr.Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

		if (matchingOption != null)
		{
			var name = useLowercase ? matchingOption.ConfigAttr.Name.ToLower() : matchingOption.ConfigAttr.Name;
			var value = matchingOption.Value?.ToString() ?? "null";
			var desc = matchingOption.ConfigAttr.Description;

			await NotifyService!.Notify(executor, $"{name}: {value}");
			await NotifyService.Notify(executor, $"  Description: {desc}");
			await NotifyService.Notify(executor, $"  Category: {matchingOption.Category}");
			return new CallState(value);
		}

		// No match found
		await NotifyService!.Notify(executor, $"No configuration category or option named '{searchTerm}'.");
		return new CallState("#-1 NOT FOUND");
	}

	[SharpCommand(Name = "@EDIT", Switches = ["FIRST", "CHECK", "QUIET", "REGEXP", "NOCASE", "ALL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 1, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Edit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		// Parse object/attribute pattern (left side of =)
		var objAttrArg = args.ElementAtOrDefault(0).Value;
		if (objAttrArg == null || objAttrArg.Message == null)
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @edit.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		var objAttrText = MModule.plainText(objAttrArg.Message);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _) || string.IsNullOrEmpty(details.Attribute))
		{
			await NotifyService!.Notify(executor, "Invalid format. Use: object/attribute=search,replace");
			return new CallState("#-1 INVALID FORMAT");
		}

		var (dbref, attrPattern) = details;

		// Locate object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor, executor, dbref, LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Check permissions
		var canModify = await PermissionService!.Controls(executor, targetObject);
		if (!canModify)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState("#-1 PERMISSION DENIED");
		}

		// Parse search and replace strings (right side of =)
		// With RSArgs, the arguments after = are split by comma
		var searchArg = args.ElementAtOrDefault(1).Value;
		var replaceArg = args.ElementAtOrDefault(2).Value;
		
		if (searchArg == null || searchArg.Message == null)
		{
			await NotifyService!.Notify(executor, "You must specify search and replace strings.");
			return new CallState("#-1 MISSING ARGUMENTS");
		}

		var search = searchArg.Message.ToPlainText();
		var replace = replaceArg?.Message != null ? replaceArg.Message.ToPlainText() : string.Empty;

		// Get matching attributes
		var attributes = await AttributeService!.GetAttributePatternAsync(
			executor, targetObject, attrPattern, false, IAttributeService.AttributePatternMode.Wildcard);

		if (attributes.IsError)
		{
			await NotifyService!.Notify(executor, attributes.AsError.Value);
			return new CallState(attributes.AsError.Value);
		}

		var attrList = attributes.AsAttributes.ToList();
		if (attrList.Count == 0)
		{
			await NotifyService!.Notify(executor, "No matching attributes found.");
			return new CallState("#-1 NO MATCH");
		}

		// Process each attribute
		int modifiedCount = 0;
		int unchangedCount = 0;
		var isRegexp = switches.Contains("REGEXP");
		var isFirst = switches.Contains("FIRST");
		var isCheck = switches.Contains("CHECK");
		var isQuiet = switches.Contains("QUIET");
		var isAll = switches.Contains("ALL");
		var isNoCase = switches.Contains("NOCASE");

		foreach (var attr in attrList)
		{
			var attrName = attr.LongName!;
			var attrValue = attr.Value;
			var originalText = attrValue.ToPlainText();
			string newText;

			if (isRegexp)
			{
				// Regex mode
				newText = await PerformRegexEdit(parser, originalText, search, replace, isAll, isNoCase);
			}
			else
			{
				// Simple string replacement mode
				newText = PerformSimpleEdit(originalText, search, replace, isFirst);
			}

			if (newText == originalText)
			{
				unchangedCount++;
				continue;
			}

			modifiedCount++;

			if (!isQuiet && !isCheck)
			{
				await NotifyService!.Notify(executor, $"{attrName} - Set: {newText}");
			}
			else if (!isQuiet && isCheck)
			{
				// Show changes with highlighting (simple version for now)
				await NotifyService!.Notify(executor, $"{attrName} - Would change to: {newText}");
			}

			// Actually set the attribute unless in check mode
			if (!isCheck)
			{
				await AttributeService!.SetAttributeAsync(executor, targetObject, attrName, MModule.single(newText));
			}
		}

		// Summary message
		if (isQuiet || (modifiedCount + unchangedCount > 1))
		{
			var checkPrefix = isCheck ? "Would edit" : "Edited";
			await NotifyService!.Notify(executor, 
				$"{checkPrefix} {modifiedCount} attribute{(modifiedCount != 1 ? "s" : "")}. {unchangedCount} unchanged.");
		}

		return new CallState(string.Empty);
	}

	/// <summary>
	/// Split search/replace text by comma, respecting curly brace escaping
	/// </summary>
	private static string[] SplitSearchReplace(string text)
	{
		var parts = new List<string>();
		var current = new StringBuilder();
		int braceDepth = 0;

		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			
			if (c == '{')
			{
				braceDepth++;
				current.Append(c);
			}
			else if (c == '}')
			{
				braceDepth--;
				current.Append(c);
			}
			else if (c == ',' && braceDepth == 0)
			{
				parts.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}
		
		parts.Add(current.ToString());
		
		// Trim and remove outer braces if present
		for (int i = 0; i < parts.Count; i++)
		{
			var part = parts[i].Trim();
			if (part.StartsWith('{') && part.EndsWith('}'))
			{
				part = part[1..^1];
			}
			parts[i] = part;
		}
		
		return [.. parts];
	}

	/// <summary>
	/// Perform simple string replacement
	/// </summary>
	private static string PerformSimpleEdit(string text, string search, string replace, bool firstOnly)
	{
		if (search == "^")
		{
			// Prepend
			return replace + text;
		}
		else if (search == "$")
		{
			// Append
			return text + replace;
		}
		else if (firstOnly)
		{
			// Replace only first occurrence
			int index = text.IndexOf(search);
			if (index >= 0)
			{
				return text[..index] + replace + text[(index + search.Length)..];
			}
			return text;
		}
		else
		{
			// Replace all occurrences
			return text.Replace(search, replace);
		}
	}

	/// <summary>
	/// Perform regex replacement with evaluation
	/// </summary>
	private static async ValueTask<string> PerformRegexEdit(IMUSHCodeParser parser, string text, 
		string pattern, string replaceTemplate, bool all, bool nocase)
	{
		try
		{
			var options = RegexOptions.None;
			if (nocase)
			{
				options |= RegexOptions.IgnoreCase;
			}

			var regex = new Regex(pattern, options);

			if (all)
			{
				// Replace all matches, working backwards
				var matches = regex.Matches(text).Cast<Match>().Reverse().ToList();
				foreach (var match in matches)
				{
					var replacement = await EvaluateRegexReplacement(parser, regex, match, replaceTemplate);
					text = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
				}
			}
			else
			{
				// Replace only first match
				var match = regex.Match(text);
				if (match.Success)
				{
					var replacement = await EvaluateRegexReplacement(parser, regex, match, replaceTemplate);
					text = text[..match.Index] + replacement + text[(match.Index + match.Length)..];
				}
			}

			return text;
		}
		catch (ArgumentException)
		{
			return text; // Return unchanged on regex error
		}
	}

	/// <summary>
	/// Evaluate replacement template with captured groups
	/// </summary>
	private static async ValueTask<string> EvaluateRegexReplacement(IMUSHCodeParser parser, 
		Regex regex, Match match, string template)
	{
		var replacement = template;

		// Replace $0, $1, etc. with captured groups
		for (int j = 0; j < match.Groups.Count; j++)
		{
			replacement = replacement.Replace($"${j}", match.Groups[j].Value);
		}

		// Replace named captures
		foreach (var groupName in regex.GetGroupNames().Where(groupName => !int.TryParse(groupName, out _)))
		{
			var group = match.Groups[groupName];
			if (group.Success)
			{
				replacement = replacement.Replace($"$<{groupName}>", group.Value);
			}
		}

		// Evaluate the replacement
		var evaluatedReplacement = await parser.FunctionParse(MModule.single(replacement));
		return evaluatedReplacement?.Message?.ToPlainText() ?? replacement;
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 5)]
	public static async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		// No arguments - list all user-defined functions
		if (args.Count == 0)
		{
			if (FunctionLibrary == null)
			{
				await NotifyService!.Notify(executor, "Function library unavailable.");
				return new CallState("#-1 LIBRARY UNAVAILABLE");
			}
			
			await NotifyService!.Notify(executor, "Global user-defined functions:");
			
			// Check if executor has Functions power or is wizard
			var canSeeDetails = await executor.IsWizard();
			
			var userFunctions = FunctionLibrary.Where(kvp => !kvp.Value.IsSystem).ToArray();
			var builtinFunctions = FunctionLibrary.Where(kvp => kvp.Value.IsSystem).ToArray();
			
			if (canSeeDetails)
			{
				await NotifyService.Notify(executor, $"  User-defined: {userFunctions.Length}");
				foreach (var (name, (def, _)) in userFunctions.Take(10))
				{
					await NotifyService.Notify(executor, $"    {name}: {def.Attribute.MinArgs}-{def.Attribute.MaxArgs} args, Flags: {def.Attribute.Flags}");
				}
				if (userFunctions.Length > 10)
				{
					await NotifyService.Notify(executor, $"    ... and {userFunctions.Length - 10} more");
				}
				
				await NotifyService.Notify(executor, $"  Built-in: {builtinFunctions.Length}");
			}
			else
			{
				await NotifyService.Notify(executor, $"  {userFunctions.Length} user-defined functions");
				await NotifyService.Notify(executor, $"  {builtinFunctions.Length} built-in functions");
			}
			
			return CallState.Empty;
		}
		
		var functionName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(functionName))
		{
			await NotifyService!.Notify(executor, "You must specify a function name.");
			return new CallState("#-1 NO FUNCTION SPECIFIED");
		}
		
		// Handle administrative switches
		if (switches.Contains("ALIAS"))
		{
			var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(aliasName))
			{
				await NotifyService!.Notify(executor, "You must specify an alias name.");
				return new CallState("#-1 NO ALIAS SPECIFIED");
			}
			
			await NotifyService!.Notify(executor, $"@function/alias: Would create alias '{aliasName}' for function '{functionName}'.");
			await NotifyService.Notify(executor, "Note: Function aliasing not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("CLONE"))
		{
			var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(cloneName))
			{
				await NotifyService!.Notify(executor, "You must specify a clone name.");
				return new CallState("#-1 NO CLONE NAME SPECIFIED");
			}
			
			await NotifyService!.Notify(executor, $"@function/clone: Would clone function '{functionName}' as '{cloneName}'.");
			await NotifyService.Notify(executor, "Note: Function cloning not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("DELETE"))
		{
			await NotifyService!.Notify(executor, $"@function/delete: Would delete function '{functionName}'.");
			await NotifyService.Notify(executor, "Note: Function deletion not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("DISABLE"))
		{
			await NotifyService!.Notify(executor, $"@function/disable: Would disable function '{functionName}'.");
			await NotifyService.Notify(executor, "Note: Function disabling not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("ENABLE"))
		{
			await NotifyService!.Notify(executor, $"@function/enable: Would enable function '{functionName}'.");
			await NotifyService.Notify(executor, "Note: Function enabling not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("RESTRICT"))
		{
			var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			await NotifyService!.Notify(executor, $"@function/restrict: Would restrict function '{functionName}' to: {restriction ?? "none"}");
			await NotifyService.Notify(executor, "Note: Function restriction not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// Check if defining a new function: @function <name>=<obj>,<attrib>[,<min>,<max>[,<restrictions>]]
		if (args.Count >= 2)
		{
			var defString = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(defString))
			{
				// Parse definition: obj, attrib[, min, max[, restrictions]]
				await NotifyService!.Notify(executor, $"@function: Would define function '{functionName}' as: {defString}");
				
				// Parse min/max args if provided
				if (args.Count >= 3)
				{
					var minArgs = args.GetValueOrDefault("2")?.Message?.ToPlainText();
					await NotifyService.Notify(executor, $"  Min args: {minArgs ?? "none"}");
				}
				
				if (args.Count >= 4)
				{
					var maxArgs = args.GetValueOrDefault("3")?.Message?.ToPlainText();
					await NotifyService.Notify(executor, $"  Max args: {maxArgs ?? "none"}");
				}
				
				if (args.Count >= 5)
				{
					var restrictions = args.GetValueOrDefault("4")?.Message?.ToPlainText();
					await NotifyService.Notify(executor, $"  Restrictions: {restrictions ?? "none"}");
				}
				
				await NotifyService.Notify(executor, "Note: Dynamic function definition not yet implemented.");
				return new CallState("#-1 NOT IMPLEMENTED");
			}
		}
		
		// Single argument - show function information
		if (FunctionLibrary == null)
		{
			await NotifyService!.Notify(executor, "Function library unavailable.");
			return new CallState("#-1 LIBRARY UNAVAILABLE");
		}
		
		// Try to find the function in the library
		var functionNameUpper = functionName.ToUpper();
		if (!FunctionLibrary.TryGetValue(functionNameUpper, out var functionInfo))
		{
			await NotifyService!.Notify(executor, $"Function '{functionName}' not found.");
			return new CallState("#-1 FUNCTION NOT FOUND");
		}
		
		var (definition, isSystem) = functionInfo;
		var attr = definition.Attribute;
		
		await NotifyService!.Notify(executor, $"Function: {attr.Name}");
		await NotifyService.Notify(executor, $"  Type: {(isSystem ? "Built-in" : "User-defined")}");
		await NotifyService.Notify(executor, $"  Min Args: {attr.MinArgs}");
		await NotifyService.Notify(executor, $"  Max Args: {attr.MaxArgs}");
		
		var flags = new List<string>();
		if ((attr.Flags & FunctionFlags.Regular) != 0) flags.Add("Regular");
		if ((attr.Flags & FunctionFlags.StripAnsi) != 0) flags.Add("StripAnsi");
		if ((attr.Flags & FunctionFlags.NoParse) != 0) flags.Add("NoParse");
		if ((attr.Flags & FunctionFlags.Localize) != 0) flags.Add("Localize");
		if ((attr.Flags & FunctionFlags.Literal) != 0) flags.Add("Literal");
		
		if (flags.Count > 0)
		{
			await NotifyService.Notify(executor, $"  Flags: {string.Join(" | ", flags)}");
		}
		
		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			await NotifyService.Notify(executor, $"  Restrictions: {string.Join(", ", attr.Restrict)}");
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@LEMIT", Switches = ["NOEVAL", "NOISY", "SILENT", "SPOOF"], Behavior = CB.Default | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> LocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 1)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var executorLocation = await executor.OutermostWhere();
		var message = args["0"].Message!;

		await CommunicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSLEMIT", Switches = ["NOEVAL", "NOISY", "SILENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofLocationEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 1)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var spoofType = await PermissionService!.CanNoSpoof(executor)
			? INotifyService.NotificationType.NSEmit
			: INotifyService.NotificationType.Emit;

		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var message = args["0"].Message!;

		await foreach (var obj in contents
			               .Where(async (x, _)
				               => await PermissionService.CanInteract(x.WithRoomOption(), executor, InteractType.Hear)))
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				executor,
				spoofType);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var zoneName = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

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
				// Find all objects in the zone
				var zoneObjects = Mediator!.CreateStream(new GetObjectsByZoneQuery(zone));
				
				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);
				
				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = Mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService!.Notify(content.WithRoomOption(), message, executor, notificationType);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@PS", Switches = ["ALL", "SUMMARY", "COUNT", "QUICK", "DEBUG"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> ProcessStatus(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @ps[/<switch>] [<player>]
		// @ps[/debug] <pid>
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		// Check for /summary switch
		if (switches.Contains("SUMMARY"))
		{
			await NotifyService!.Notify(executor, "@ps/summary: Queue totals");
			await NotifyService.Notify(executor, "  Command queue: 0/0");
			await NotifyService.Notify(executor, "  Wait queue: 0/0");
			await NotifyService.Notify(executor, "  Semaphore queue: 0/0");
			await NotifyService.Notify(executor, "  Load average: 0.0, 0.0, 0.0");
			await NotifyService.Notify(executor, "Note: Queue management not yet implemented.");
			return CallState.Empty;
		}
		
		// Check for /quick switch
		if (switches.Contains("QUICK"))
		{
			await NotifyService!.Notify(executor, "@ps/quick: Your queue totals");
			await NotifyService.Notify(executor, "  Command queue: 0/0");
			await NotifyService.Notify(executor, "  Wait queue: 0/0");
			await NotifyService.Notify(executor, "  Semaphore queue: 0/0");
			await NotifyService.Notify(executor, "Note: Queue management not yet implemented.");
			return CallState.Empty;
		}
		
		// Check if showing debug info for a specific PID
		if (switches.Contains("DEBUG"))
		{
			var pidStr = args.GetValueOrDefault("0")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(pidStr))
			{
				await NotifyService!.Notify(executor, "You must specify a process ID.");
				return new CallState("#-1 NO PID SPECIFIED");
			}
			
			await NotifyService!.Notify(executor, $"@ps/debug: Would show debug info for PID {pidStr}");
			await NotifyService.Notify(executor, "  Would show: Arguments, Q-registers, executor, enactor, caller");
			await NotifyService.Notify(executor, "Note: Queue management not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// Check for /all switch (wizard only)
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			await NotifyService!.Notify(executor, "@ps/all: Would show full queue for all objects");
			await NotifyService.Notify(executor, "Note: Queue management not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// Show queue for specific player or self
		string targetName = "you";
		if (args.Count > 0)
		{
			var playerName = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(playerName))
			{
				targetName = playerName;
			}
		}
		
		await NotifyService!.Notify(executor, $"@ps: Queue for {targetName}");
		await NotifyService.Notify(executor, "  Command queue: 0/0");
		await NotifyService.Notify(executor, "  Wait queue: 0/0");
		await NotifyService.Notify(executor, "  Semaphore queue: 0/0");
		await NotifyService.Notify(executor, "  Load average: 0.0, 0.0, 0.0");
		
		// TODO: Full implementation requires:
		// - Queue management system to track all queued commands
		// - Process IDs for each queue entry
		// - Ability to list queue entries with format: [PID] <semaphore> <wait> <object> <command>
		// - Load average tracking
		// - Permission checks for viewing other players' queues
		await NotifyService.Notify(executor, "Note: Queue management not yet implemented.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SELECT",
		Switches = ["NOTIFY", "REGEXP", "INPLACE", "INLINE", "LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse, MinArgs = 1, MaxArgs = int.MaxValue)]
	public static async ValueTask<Option<CallState>> Select(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @select <string>=<expr1>, <action1> [,<exprN>, <actionN>]... [,<default>]
		// Like @switch but only runs the first matching action
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		var testString = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(testString))
		{
			await NotifyService!.Notify(executor, "You must specify a test string.");
			return new CallState("#-1 NO TEST STRING");
		}
		
		await NotifyService!.Notify(executor, $"@select: Testing string '{testString}'");
		
		// Count expression/action pairs (args are: 0=test string, then pairs of expr,action)
		int pairCount = (args.Count - 1) / 2;
		bool hasDefault = (args.Count - 1) % 2 == 1;
		
		await NotifyService.Notify(executor, $"  Expression/action pairs: {pairCount}");
		if (hasDefault)
		{
			await NotifyService.Notify(executor, "  Has default action");
		}
		
		// Check switches
		if (switches.Contains("REGEXP"))
		{
			await NotifyService.Notify(executor, "  Mode: Regular expression matching");
		}
		else
		{
			await NotifyService.Notify(executor, "  Mode: Wildcard pattern matching");
		}
		
		if (switches.Contains("INLINE") || switches.Contains("INPLACE"))
		{
			await NotifyService.Notify(executor, "  Execution: Inline (immediate)");
			
			if (switches.Contains("NOBREAK"))
			{
				await NotifyService.Notify(executor, "  @break won't propagate to caller");
			}
			
			if (switches.Contains("LOCALIZE"))
			{
				await NotifyService.Notify(executor, "  Q-registers will be localized");
			}
			
			if (switches.Contains("CLEARREGS"))
			{
				await NotifyService.Notify(executor, "  Q-registers will be cleared");
			}
		}
		else
		{
			await NotifyService.Notify(executor, "  Execution: Queued");
		}
		
		if (switches.Contains("NOTIFY"))
		{
			await NotifyService.Notify(executor, "  Will queue @notify after completion");
		}
		
		// TODO: Full implementation requires:
		// - Pattern matching (wildcard or regexp based on switch)
		// - Capture group handling ($0-$9 for matches)
		// - #$ substitution in actions (replaced with evaluated test string)
		// - Queue or inline execution based on switches
		// - Q-register management (localize, clearregs)
		// - @break propagation handling
		// - Only execute first matching action (unlike @switch which executes all)
		await NotifyService.Notify(executor, "Note: Pattern matching and action execution not yet implemented.");
		
		return new CallState("#-1 NOT IMPLEMENTED");
	}

	[SharpCommand(Name = "@TRIGGER",
		Switches = ["CLEARREGS", "SPOOF", "INLINE", "NOBREAK", "LOCALIZE", "INPLACE", "MATCH"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = 31)]
	public static async ValueTask<Option<CallState>> Trigger(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @trigger[/<switches>] <object>/<attribute>[=<arg0>, ..., <arg29>]
		// @trigger/match[/<switches>] <object>/<attribute>=<string>
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.Notify(executor, "You must specify an object/attribute to trigger.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		// Parse object/attribute
		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService!.Notify(executor, "You must specify an object/attribute path.");
			return new CallState("#-1 INVALID PATH");
		}
		
		var objectName = parts[0];
		var attributeName = parts[1];
		
		// Locate the target object
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, enactor, objectName, LocateFlags.All);
			
		if (maybeObject.IsError)
		{
			return maybeObject.AsError;
		}
		
		var targetObject = maybeObject.AsSharpObject;
		
		// Check control permissions
		if (!await PermissionService!.Controls(executor, targetObject))
		{
			await NotifyService!.Notify(executor, "Permission denied. You do not control that object.");
			return new CallState("#-1 PERMISSION DENIED");
		}
		
		// Get the attribute - must be visible to enactor
		var attributeResult = await AttributeService!.GetAttributeAsync(
			enactor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false);
		
		if (attributeResult.IsError)
		{
			await NotifyService!.Notify(executor, $"No such attribute: {attributeName}");
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}
		
		if (attributeResult.IsNone)
		{
			// Empty attribute - nothing to trigger
			return CallState.Empty;
		}
		
		var attributeContent = attributeResult.AsAttribute.Last().Value;
		var attributeText = attributeContent.ToPlainText();
		
		if (string.IsNullOrWhiteSpace(attributeText))
		{
			// Empty attribute - nothing to trigger
			return CallState.Empty;
		}
		
		// Determine enactor/executor for execution based on /spoof switch
		// /spoof: enactor stays the same (original caller)
		// no /spoof: target object becomes both enactor and executor
		var executionEnactor = switches.Contains("SPOOF") ? enactor.Object().DBRef : targetObject.Object().DBRef;
		
		// Environment arguments and Q-registers are handled by the hook system
		// TODO: Handle /match for pattern matching when pattern engine available
		// Note: INLINE switch executes immediately (current default behavior)
		// Queue dispatch available via QueueCommandListRequest if needed for future enhancements
		
		// Execute inline using CommandListParse (functionally correct for /inline mode)
		await parser.With(state => state with { 
			Executor = targetObject.Object().DBRef,
			Enactor = executionEnactor 
		}, async newParser => await newParser.CommandListParseVisitor(MModule.single(attributeText))());
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@ZEMIT", Switches = ["NOISY", "SILENT"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ZoneEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var zoneName = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Locate the zone object
		await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			zoneName,
			LocateFlags.All,
			async zone =>
			{
				// Find all objects in the zone
				var zoneObjects = Mediator!.CreateStream(new GetObjectsByZoneQuery(zone));
				
				// Get all rooms in the zone
				var rooms = zoneObjects.Where(obj => obj.Type == DatabaseConstants.TypeRoom);
				
				// Send message to each room
				await foreach (var room in rooms)
				{
					var roomContents = Mediator!.CreateStream(new GetContentsQuery(new DBRef(room.Key)))!;
					await foreach (var content in roomContents)
					{
						await NotifyService!.Notify(content.WithRoomOption(), message, executor, INotifyService.NotificationType.Emit);
					}
				}

				return CallState.Empty;
			});

		return CallState.Empty;
	}

	[SharpCommand(Name = "@CHANNEL",
		Switches =
		[
			"LIST", "ADD", "DELETE", "RENAME", "MOGRIFIER", "NAME", "PRIVS", "QUIET", "DECOMPILE", "DESCRIBE", "CHOWN",
			"WIPE", "MUTE", "UNMUTE", "GAG", "UNGAG", "HIDE", "UNHIDE", "WHAT", "TITLE", "BRIEF", "RECALL", "BUFFER",
			"COMBINE", "UNCOMBINE", "ON", "JOIN", "OFF", "LEAVE", "WHO"
		], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Channel(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("QUIET") && (!switches.Contains("LIST") || !switches.Contains("RECALL")))
		{
			return new CallState("CHAT: INCORRECT COMBINATION OF SWITCHES");
		}

		// TODO: Channel Visibility on most of these commands.
		return switches switch
		{
			[.., "LIST"] => await ChannelCommand.ChannelList.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["WHAT"] => await ChannelWhat.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!),
			["WHO"] => await ChannelWho.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!),
			["ON"] or ["JOIN"] => await ChannelOn.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message),
			["OFF"] or ["LEAVE"] => await ChannelOff.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message),
			["GAG"] => await ChannelGag.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!, switches),
			["MUTE"] => await ChannelMute.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["HIDE"] => await ChannelHide.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["COMBINE"] => await ChannelCombine.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["TITLE"] => await ChannelTitle.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			[.., "RECALL"] => await ChannelRecall.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["ADD"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelAdd.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					Configuration!, args["0"].Message!, args["1"].Message!),
			["PRIVS"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelPrivs.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					args["0"].Message!, args["1"].Message!),
			["DESCRIBE"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelDescribe.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					args["0"].Message!, args["1"].Message!),
			["BUFFER"] when args.ContainsKey("0") && args.ContainsKey("1")
				=> await ChannelBuffer.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
					Configuration!, args["0"].Message!, args["1"].Message!),
			[.., "DECOMPILE"] => await ChannelDecompile.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!, switches),
			["CHOWN"] => await ChannelChown.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["RENAME"] => await ChannelRename.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				Configuration!, args["0"].Message!, args["1"].Message!),
			["WIPE"] => await ChannelWipe.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["DELETE"] => await ChannelDelete.Handle(parser, LocateService!, PermissionService!, Mediator!, NotifyService!,
				args["0"].Message!, args["1"].Message!),
			["MOGRIFIER"] => await ChannelMogrifier.Handle(parser, LocateService!, PermissionService!, Mediator!,
				NotifyService!, args["0"].Message!, args["1"].Message!),
			_ => new CallState("What do you want to do with the channel?")
		};
	}

	[SharpCommand(Name = "@DECOMPILE", Switches = ["DB", "NAME", "PREFIX", "TF", "FLAGS", "ATTRIBS", "SKIPDEFAULTS"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Decompile(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Parse arguments: object[/attribute pattern][=prefix]
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify an object to decompile.");
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}
		
		var objectSpec = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(objectSpec))
		{
			await NotifyService!.Notify(executor, "You must specify an object to decompile.");
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}
		
		// Parse prefix if provided in arg 1 (from = split)
		var prefix = args.Count >= 2 ? args["1"].Message?.ToPlainText() ?? "" : "";
		
		// Handle /tf switch - sets prefix to TFPREFIX attribute or default
		if (switches.Contains("TF"))
		{
			var tfPrefixAttr = await AttributeService!.GetAttributeAsync(executor, executor, "TFPREFIX",
				IAttributeService.AttributeMode.Read, false);
			
			prefix = tfPrefixAttr.Match(
				attr => attr.Last().Value.ToPlainText(),
				none => "FugueEdit > ",
				error => "FugueEdit > ");
		}
		
		string? attributePattern = null;
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objectSpec);
		AnyOptionalSharpObject target;
		
		if (split.TryPickT0(out var details, out _))
		{
			var (objectName, maybeAttributePattern) = details;
			attributePattern = maybeAttributePattern;
			
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				objectName,
				LocateFlags.All);
			
			if (locate.IsValid())
			{
				target = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}
		else
		{
			var locate = await LocateService!.LocateAndNotifyIfInvalid(
				parser,
				enactor,
				enactor,
				objectSpec,
				LocateFlags.All);
			
			if (locate.IsValid())
			{
				target = locate.WithoutError();
			}
			else
			{
				return new None();
			}
		}
		
		if (target.IsNone())
		{
			return new None();
		}
		
		var targetKnown = target.Known();
		
		var canExamine = await PermissionService!.CanExamine(executor, targetKnown);
		if (!canExamine)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState(Errors.ErrorPerm);
		}
		
		var obj = targetKnown.Object();
		var useDbRef = switches.Contains("DB");
		var useName = switches.Contains("NAME") || !useDbRef; // NAME is default
		var showFlags = switches.Contains("FLAGS") || (!switches.Contains("ATTRIBS") && string.IsNullOrEmpty(attributePattern));
		var showAttribs = switches.Contains("ATTRIBS") || (!switches.Contains("FLAGS") && string.IsNullOrEmpty(attributePattern)) || !string.IsNullOrEmpty(attributePattern);
		var skipDefaults = switches.Contains("SKIPDEFAULTS");
		var isTf = switches.Contains("TF");
		
		if (!string.IsNullOrEmpty(attributePattern))
		{
			showFlags = false;
			showAttribs = true;
		}
		
		var objectRef = useDbRef ? $"#{obj.DBRef.Number}" : obj.Name;
		var outputs = new List<string>();
		
		if (showFlags)
		{
			// @create command based on object type
			var createCmd = obj.Type.ToUpperInvariant() switch
			{
				"ROOM" => $"@dig {objectRef}",
				"EXIT" => $"@open {objectRef}",
				"THING" => $"@create {objectRef}",
				"PLAYER" => $"@pcreate {objectRef}",
				_ => $"@create {objectRef}"
			};
			outputs.Add($"{prefix}{createCmd}");
			
			var flags = await obj.Flags.Value.ToArrayAsync();
			foreach (var flag in flags)
			{
				if (skipDefaults && IsDefaultFlag(obj.Type, flag.Name))
				{
					continue;
				}
				outputs.Add($"{prefix}@set {objectRef}={flag.Name}");
			}
			
			var powers = await obj.Powers.Value.ToArrayAsync();
			foreach (var power in powers)
			{
				outputs.Add($"{prefix}@power {objectRef}={power.Name}");
			}
			
			foreach (var lockEntry in obj.Locks)
			{
				var lockName = lockEntry.Key;
				var lockData = lockEntry.Value;
				var lockValue = lockData.LockString;
				
				if (lockName.Equals("Basic", StringComparison.OrdinalIgnoreCase))
				{
					outputs.Add($"{prefix}@lock {objectRef}={lockValue}");
				}
				else
				{
					outputs.Add($"{prefix}@lock/{lockName} {objectRef}={lockValue}");
				}
			}
			
			// TODO: Set parent if not default
		}
		
		if (showAttribs)
		{
			SharpAttributesOrError atrs;
			if (!string.IsNullOrEmpty(attributePattern))
			{
				atrs = await AttributeService!.GetAttributePatternAsync(
					enactor,
					targetKnown,
					attributePattern,
					false, // don't check parents for decompile
					IAttributeService.AttributePatternMode.Wildcard);
			}
			else
			{
				atrs = await AttributeService!.GetVisibleAttributesAsync(enactor, targetKnown);
			}
			
			if (atrs.IsAttribute)
			{
				foreach (var attr in atrs.AsAttributes)
				{
					// Skip VEILED attributes
					const string VeiledFlagName = "VEILED";
					if (attr.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)))
					{
						continue;
					}
					
					// Check if attribute contains ANSI color
					var hasAnsi = ContainsAnsiMarkup(attr.Value);
					
					if (hasAnsi)
					{
						// Use @set format with decomposed value to ensure evaluation
						var decomposedValue = DecomposeAttributeValue(attr.Value);
						outputs.Add($"{prefix}@set {objectRef}={attr.Name}:{decomposedValue}");
					}
					else
					{
						// Use &attr format for non-ANSI attributes
						var plainValue = attr.Value.ToPlainText();
						outputs.Add($"{prefix}&{attr.Name} {objectRef}={plainValue}");
					}
					
					// Output attribute flags if not in TF mode and not skipdefaults
					if (!isTf && attr.Flags.Any())
					{
						if (!skipDefaults || !AreDefaultAttrFlags(attr.Name, attr.Flags))
						{
							foreach (var flag in attr.Flags)
							{
								outputs.Add($"{prefix}@set {objectRef}/{attr.Name}={flag.Name}");
							}
						}
					}
				}
			}
		}
		
		// Send output to player
		foreach (var output in outputs)
		{
			await NotifyService!.Notify(executor, output);
		}
		
		return CallState.Empty;
	}
	
	/// <summary>
	/// Checks if an MString contains ANSI markup
	/// </summary>
	private static bool ContainsAnsiMarkup(MString str)
	{
		var hasAnsi = false;
		MModule.evaluateWith((markupType, innerText) =>
		{
			if (markupType is MModule.MarkupTypes.MarkedupText { Item: Ansi })
			{
				hasAnsi = true;
			}
			return innerText;
		}, str);
		return hasAnsi;
	}
	
	/// <summary>
	/// Decomposes an attribute value using the decompose() logic from StringFunctions
	/// </summary>
	private static string DecomposeAttributeValue(MString input)
	{
		// Use same logic as decompose() function from StringFunctions
		var reconstructed = MModule.evaluateWith((markupType, innerText) =>
		{
			return markupType switch
			{
				MModule.MarkupTypes.MarkedupText { Item: Ansi ansiMarkup }
					=> Functions.Functions.ReconstructAnsiCall(ansiMarkup.Details, innerText),
				_ => innerText
			};
		}, input);
		
		var result = reconstructed
			.Replace("\\", @"\\")
			.Replace("%", "\\%")
			.Replace(";", "\\;")
			.Replace("[", "\\[")
			.Replace("]", "\\]")
			.Replace("{", "\\{")
			.Replace("}", "\\}")
			.Replace("(", "\\(")
			.Replace(")", "\\)")
			.Replace(",", "\\,")
			.Replace("^", "\\^")
			.Replace("$", "\\$");
		
		result = Regex.Replace(result, @"\s{2,}", m => string.Join("", Enumerable.Repeat("%b", m.Length)));
		
		result = result.Replace("\r", "%r").Replace("\n", "%r").Replace("\t", "%t");
		
		return result;
	}
	
	/// <summary>
	/// Checks if a flag is a default flag for the object type
	/// </summary>
	private static bool IsDefaultFlag(string type, string flagName)
	{
		// TODO: Implement proper default flag checking based on object type
		// For now, return false to show all flags
		return false;
	}
	
	/// <summary>
	/// Checks if attribute flags are the default for that attribute
	/// </summary>
	private static bool AreDefaultAttrFlags(string attrName, IEnumerable<SharpAttributeFlag> flags)
	{
		// TODO: Implement proper default attribute flag checking
		// For now, return false to show all flags
		return false;
	}

	[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.RSNoParse | CB.NoGagged,
		MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Make NoEval work
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var isSpoof = parser.CurrentState.Switches.Contains("SPOOF");
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		if (isSpoof)
		{
			var canSpoof = await executor.HasPower("CAN_SPOOF");
			var controlsExecutor = await PermissionService!.Controls(executor, enactor);

			if (!canSpoof && !controlsExecutor)
			{
				await NotifyService!.Notify(executor, "You do not have permission to spoof emits.");
				return new CallState(Errors.ErrorPerm);
			}
		}

		await CommunicationService!.SendToRoomAsync(
			executor,
			executorLocation,
			_ => message,
			INotifyService.NotificationType.Emit,
			sender: isSpoof ? enactor : executor);

		return new CallState(message);
	}

	[SharpCommand(Name = "@LISTMOTD", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ListMessageOfTheDay(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		
		// Check if executor is wizard/royalty to see wizard MOTD
		var isWizard = await executor.IsWizard();
		
		// Get MOTD file paths from configuration
		var motdFile = Configuration!.CurrentValue.Message.MessageOfTheDayFile;
		var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;
		
		await NotifyService!.Notify(executor, "Current Message of the Day settings:");
		await NotifyService.Notify(executor, $"  Connect MOTD File: {motdFile ?? "(not set)"}");
		await NotifyService.Notify(executor, $"  Connect MOTD HTML: {motdHtmlFile ?? "(not set)"}");
		
		if (isWizard)
		{
			var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
			var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;
			
			await NotifyService.Notify(executor, $"  Wizard MOTD File: {wizmotdFile ?? "(not set)"}");
			await NotifyService.Notify(executor, $"  Wizard MOTD HTML: {wizmotdHtmlFile ?? "(not set)"}");
		}
		
		// Get temporary MOTD data from ExpandedServerData
		var motdData = await ObjectDataService!.GetExpandedServerDataAsync<MotdData>();
		if (motdData != null)
		{
			await NotifyService.Notify(executor, "");
			await NotifyService.Notify(executor, "Temporary Message of the Day (cleared on restart):");
			await NotifyService.Notify(executor, $"  Connect MOTD: {(string.IsNullOrEmpty(motdData.ConnectMotd) ? "(not set)" : motdData.ConnectMotd)}");
			
			if (isWizard)
			{
				await NotifyService.Notify(executor, $"  Wizard MOTD:  {(string.IsNullOrEmpty(motdData.WizardMotd) ? "(not set)" : motdData.WizardMotd)}");
				await NotifyService.Notify(executor, $"  Down MOTD:    {(string.IsNullOrEmpty(motdData.DownMotd) ? "(not set)" : motdData.DownMotd)}");
				await NotifyService.Notify(executor, $"  Full MOTD:    {(string.IsNullOrEmpty(motdData.FullMotd) ? "(not set)" : motdData.FullMotd)}");
			}
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@NSOEMIT", Switches = ["NOEVAL"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSNoParse, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofOmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);
		var executorLocation = await executor.Where();
		var contents = executorLocation.Content(Mediator!);
		var isNoEvaluation = parser.CurrentState.Switches.Contains("NOEVAL");
		var message = isNoEvaluation
			? ArgHelpers.NoParseDefaultNoParseArgument(args, 1, MModule.empty())
			: await ArgHelpers.NoParseDefaultEvaluatedArgument(parser, 1, MModule.empty());

		var interactableContents = contents
			.Where(async (obj, _) =>
				await PermissionService!.CanInteract(obj.WithRoomOption(), executor,
					IPermissionService.InteractType.Hear));

		var canSpoof = await executor.HasPower("CAN_SPOOF");
		var controlsExecutor = await PermissionService!.Controls(executor, enactor);

		if (!canSpoof && !controlsExecutor)
		{
			await NotifyService!.Notify(executor, "You do not have permission to spoof emits.");
			return new CallState(Errors.ErrorPerm);
		}

		await foreach (var obj in interactableContents)
		{
			await NotifyService!.Notify(
				obj.WithRoomOption(),
				message,
				enactor,
				INotifyService.NotificationType.Emit);
		}

		return new CallState(message);
	}

	[SharpCommand(Name = "@OEMIT", Switches = ["NOEVAL", "SPOOF"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> OmitEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// For simplicity: emit to executor's location, excluding the specified objects
		// TODO: Support room/obj format like PennMUSH
		var targetRoom = await executor.Where();
		var objectList = ArgHelpers.NameList(objects);
		var excludeObjects = new List<AnySharpObject>();

		// Resolve all objects to exclude
		_ = await objectList
			.ToAsyncEnumerable()
			.Select(obj => obj.IsT0 ? obj.AsT0.ToString() : obj.AsT1)
			.Select(objName =>
				LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser,
					executor,
					executor,
					objName,
					LocateFlags.All,
					target =>
					{
						excludeObjects.Add(target);
						return CallState.Empty;
					}))
			.ToArrayAsync();

		await CommunicationService!.SendToRoomAsync(
			executor,
			targetRoom,
			_ => message,
			INotifyService.NotificationType.Emit,
			excludeObjects: excludeObjects);

		return CallState.Empty;
	}

	[SharpCommand(Name = "@REMIT", Switches = ["LIST", "NOEVAL", "NOISY", "SILENT", "SPOOF"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> RoomEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var objects = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

		// Send message to contents of all specified objects
		var objectList = ArgHelpers.NameListString(objects);

		foreach (var obj in objectList)
		{
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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

					await CommunicationService!.SendToRoomAsync(
						executor,
						target.AsContainer,
						_ => message,
						INotifyService.NotificationType.Emit);

					return CallState.Empty;
				});
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@STATS", Switches = ["CHUNKS", "FREESPACE", "PAGING", "REGIONS", "TABLES", "FLAGS"],
		Behavior = CB.Default, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Stats(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		// Check for specialized switches
		if (switches.Contains("TABLES"))
		{
			await NotifyService!.Notify(executor, "@stats/tables: Internal table statistics not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("FLAGS"))
		{
			await NotifyService!.Notify(executor, "@stats/flags: Flag system statistics not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("CHUNKS") || switches.Contains("FREESPACE") || 
		    switches.Contains("PAGING") || switches.Contains("REGIONS"))
		{
			await NotifyService!.Notify(executor, "@stats memory switches not yet implemented.");
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// Basic @stats - show object counts
		string? playerName = null;
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			playerName = args["0"].Message?.ToPlainText();
		}
		
		await NotifyService!.Notify(executor, "Database Statistics:");
		
		if (playerName != null)
		{
			await NotifyService.Notify(executor, $"  For player: {playerName}");
		}
		
		// TODO: Query actual database statistics
		await NotifyService.Notify(executor, "  Rooms: (query pending)");
		await NotifyService.Notify(executor, "  Exits: (query pending)");
		await NotifyService.Notify(executor, "  Things: (query pending)");
		await NotifyService.Notify(executor, "  Players: (query pending)");
		await NotifyService.Notify(executor, "  Total: (query pending)");
		await NotifyService.Notify(executor, "Note: Full database statistics not yet implemented.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@VERB", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0,
		MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Verb(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.ArgumentsOrdered;

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor,
				"Usage: @verb <victim>=<actor>,<what>,<whatd>,<owhat>,<owhatd>,<awhat>[,<args>]");
			return new CallState(Errors.ErrorCantSeeThat);
		}

		var victimName = args.ElementAtOrDefault(0).Value.Message!.ToPlainText();
		var actorName = args.ElementAtOrDefault(1).Value.Message!.ToPlainText();
		var what = args.ElementAtOrDefault(2).Value.Message!.ToPlainText();
		var whatd = args.ElementAtOrDefault(3).Value.Message!.ToPlainText();
		var owhat = args.ElementAtOrDefault(4).Value.Message!.ToPlainText();
		var owhatd = args.ElementAtOrDefault(5).Value.Message!.ToPlainText();
		var awhat = args.ElementAtOrDefault(6).Value.Message!.ToPlainText();

		const int RequiredArgsBeforeStack = 7;
		var stackArgs = args.Skip(RequiredArgsBeforeStack)
			.Select((kvp, idx) => new KeyValuePair<string, CallState>(idx.ToString(), kvp.Value))
			.ToDictionary();

		// Locate victim
		var maybeVictim = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, victimName, LocateFlags.All);

		if (maybeVictim.IsError)
		{
			await NotifyService!.Notify(executor, maybeVictim.AsError.Message!);
			return maybeVictim.AsError;
		}

		var victim = maybeVictim.AsSharpObject;

		// Locate actor
		var maybeActor = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, executor, actorName, LocateFlags.All);

		if (maybeActor.IsError)
		{
			await NotifyService!.Notify(executor, maybeActor.AsError.Message!);
			return maybeActor.AsError;
		}

		var actor = maybeActor.AsSharpObject;

		var isWizard = await executor.IsWizard();
		var controlsBoth = await PermissionService!.Controls(executor, actor) &&
		                   await PermissionService!.Controls(executor, victim);
		var enactorIsActor = enactor.Object().DBRef == actor.Object().DBRef;
		var executorPrivileged = await executor.IsRoyalty();
		var executorControlsVictim = await PermissionService!.Controls(executor, victim);

		var hasPermission = isWizard || controlsBoth ||
		                    (enactorIsActor && (executorPrivileged || executorControlsVictim));

		if (!hasPermission)
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState(Errors.ErrorPerm);
		}

		var actorMessage = await GetAttributeOrDefault(
			parser, AttributeService!, executor, victim, actor, what, whatd, stackArgs);

		await NotifyService!.Notify(actor, actorMessage);

		var actorLocation = await actor.Where();
		var othersMessage = await GetAttributeOrDefault(
			parser, AttributeService!, executor, victim, actor, owhat, owhatd, stackArgs);

		var prependedMessage = MModule.single($"{actor.Object().Name} {othersMessage.ToPlainText()}");

		await CommunicationService!.SendToRoomAsync(
			actor, actorLocation, _ => prependedMessage,
			INotifyService.NotificationType.Emit,
			excludeObjects: [actor]);

		if (!string.IsNullOrWhiteSpace(awhat))
		{
			var maybeAwhatAttr = await AttributeService!.GetAttributeAsync(
				executor, victim, awhat, IAttributeService.AttributeMode.Execute);

			if (!maybeAwhatAttr.IsError)
			{
				await parser.With(
					state => state with
					{
						Executor = victim.Object().DBRef,
						Enactor = actor.Object().DBRef,
						Arguments = stackArgs
					},
					newParser => newParser.CommandListParse(maybeAwhatAttr.AsAttribute.Last().Value));
			}
		}

		return CallState.Empty;
	}

	private static async ValueTask<MString> GetAttributeOrDefault(
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
			return MModule.single(defaultValue);
		}

		var maybeAttr = await attributeService.GetAttributeAsync(
			executor, victim, attrName, IAttributeService.AttributeMode.Execute);

		if (maybeAttr.IsError || maybeAttr.IsNone)
		{
			return MModule.single(defaultValue);
		}

		var result = await parser.With(
			state => state with
			{
				Executor = victim.Object().DBRef,
				Enactor = actor.Object().DBRef,
				Arguments = stackArgs
			},
			newParser => attributeService.EvaluateAttributeFunctionAsync(
				newParser, victim, victim, attrName, stackArgs));

		return result ?? MModule.single(defaultValue);
	}

	[SharpCommand(Name = "@ENTRANCES", Switches = ["EXITS", "THINGS", "PLAYERS", "ROOMS"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 3)]
	public static async ValueTask<Option<CallState>> Entrances(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		// @entrances[/<switch>] [<object>][=<begin>[, <end>]]
		// Shows all objects linked to <object>
		
		AnySharpObject targetObject;
		
		// Get target object (defaults to current location if not specified)
		if (args.Count > 0 && args.ContainsKey("0"))
		{
			var targetName = args["0"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(targetName))
			{
				var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
					parser, executor, executor, targetName, LocateFlags.All);
				
				if (!maybeTarget.IsValid())
				{
					return new CallState("#-1 NOT FOUND");
				}
				
				targetObject = maybeTarget.WithoutError().WithoutNone();
			}
			else
			{
				var location = await executor.AsContent.Location();
				targetObject = location.WithRoomOption();
			}
		}
		else
		{
			var location = await executor.AsContent.Location();
			targetObject = location.WithRoomOption();
		}
		
		// Parse range if specified
		int? beginDbref = null;
		int? endDbref = null;
		
		if (args.Count > 1 && args.ContainsKey("1"))
		{
			var beginStr = args["1"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(beginStr) && int.TryParse(beginStr, out var begin))
			{
				beginDbref = begin;
			}
		}
		
		if (args.Count > 2 && args.ContainsKey("2"))
		{
			var endStr = args["2"].Message?.ToPlainText();
			if (!string.IsNullOrEmpty(endStr) && int.TryParse(endStr, out var end))
			{
				endDbref = end;
			}
		}
		
		var targetObj = targetObject.Object();
		await NotifyService!.Notify(executor, $"Entrances to {targetObj.Name}:");
		
		// Filter by switch type
		var filterTypes = new List<string>();
		if (switches.Contains("EXITS")) filterTypes.Add("exits");
		if (switches.Contains("THINGS")) filterTypes.Add("things");
		if (switches.Contains("PLAYERS")) filterTypes.Add("players");
		if (switches.Contains("ROOMS")) filterTypes.Add("rooms");
		
		if (filterTypes.Count > 0)
		{
			await NotifyService.Notify(executor, $"  Filtering for: {string.Join(", ", filterTypes)}");
		}
		
		if (beginDbref.HasValue || endDbref.HasValue)
		{
			await NotifyService.Notify(executor, $"  Range: {beginDbref ?? 0} to {endDbref?.ToString() ?? "end"}");
		}
		
		// TODO: Query database for objects linked to target
		// - Exits linked to target
		// - Things with home = target  
		// - Players with home = target
		// - Rooms with drop-to = target
		await NotifyService.Notify(executor, "Note: Database query for linked objects not yet implemented.");
		await NotifyService.Notify(executor, "0 entrances found.");
		
		return new CallState("0");
	}

	[SharpCommand(Name = "@GREP", Switches = ["LIST", "PRINT", "ILIST", "IPRINT", "REGEXP", "WILD", "NOCASE", "PARENT"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 2, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Grep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!args.TryGetValue("0", out var objAttrArg) || !args.TryGetValue("1", out var patternArg))
		{
			await NotifyService!.Notify(executor, "Invalid arguments to @grep.");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse object/attribute pattern
		var objAttrText = MModule.plainText(objAttrArg.Message!);
		var pattern = MModule.plainText(patternArg.Message!);
		var split = HelperFunctions.SplitDbRefAndOptionalAttr(objAttrText);

		if (!split.TryPickT0(out var details, out _))
		{
			await NotifyService!.Notify(executor, "I don't see that here.");
			return new CallState("#-1 INVALID OBJECT");
		}

		var (dbref, maybeAttributePattern) = details;
		var attributePattern = string.IsNullOrEmpty(maybeAttributePattern) ? "*" : maybeAttributePattern;

		// Locate the object
		var locate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			enactor,
			executor,
			dbref,
			LocateFlags.All);

		if (locate.IsError)
		{
			return locate.AsError;
		}

		var targetObject = locate.AsSharpObject;

		// Determine pattern matching mode for attribute values
		var isWild = switches.Contains("WILD");
		var isRegexp = switches.Contains("REGEXP");
		var isNoCase = switches.Contains("NOCASE") || switches.Contains("ILIST") || switches.Contains("IPRINT");
		var isPrint = switches.Contains("PRINT") || switches.Contains("IPRINT");
		var checkParents = switches.Contains("PARENT");

		// Get attributes matching the attribute pattern
		var attributePatternMode = attributePattern == "**" 
			? IAttributeService.AttributePatternMode.Wildcard 
			: IAttributeService.AttributePatternMode.Wildcard;
		
		var attributes = await AttributeService!.GetAttributePatternAsync(
			executor, 
			targetObject, 
			attributePattern, 
			checkParents, 
			attributePatternMode);

		if (attributes.IsError)
		{
			await NotifyService!.Notify(executor, $"Error reading attributes: {attributes.AsError.Value}");
			return new CallState($"#-1 {attributes.AsError.Value}");
		}

		// Filter attributes by pattern match in their values
		var matchingAttributes = new List<SharpAttribute>();
		
		foreach (var attr in attributes.AsAttributes)
		{
			var attrValue = MModule.plainText(attr.Value);
			bool matches = false;

			if (isRegexp)
			{
				// Regex match
				try
				{
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, pattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService!.Notify(executor, $"Regular expression timed out: {pattern}");
					return new CallState("#-1 REGEXP TIMEOUT");
				}
				catch
				{
					await NotifyService!.Notify(executor, $"Invalid regular expression: {pattern}");
					return new CallState("#-1 INVALID REGEXP");
				}
			}
			else if (isWild)
			{
				// Wildcard match
				try
				{
					var regexPattern = MModule.getWildcardMatchAsRegex2(pattern);
					var regexOptions = isNoCase ? System.Text.RegularExpressions.RegexOptions.IgnoreCase : System.Text.RegularExpressions.RegexOptions.None;
					matches = System.Text.RegularExpressions.Regex.IsMatch(attrValue, regexPattern, regexOptions, TimeSpan.FromSeconds(1));
				}
				catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
				{
					await NotifyService!.Notify(executor, $"Wildcard pattern timed out: {pattern}");
					return new CallState("#-1 PATTERN TIMEOUT");
				}
				catch
				{
					await NotifyService!.Notify(executor, $"Invalid wildcard pattern: {pattern}");
					return new CallState("#-1 INVALID PATTERN");
				}
			}
			else
			{
				// Substring match
				var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
				matches = attrValue.Contains(pattern, comparison);
			}

			if (matches)
			{
				matchingAttributes.Add(attr);
			}
		}

		// Display results
		if (matchingAttributes.Count == 0)
		{
			await NotifyService!.Notify(executor, "No matching attributes found.");
			return new CallState(string.Empty);
		}

		if (isPrint)
		{
			// Print attribute names and values with highlighting
			foreach (var attr in matchingAttributes)
			{
				var attrValue = attr.Value;
				
				// Highlight matching substrings in the value
				MString displayValue;
				if (isRegexp || isWild)
				{
					// For regex/wildcard, just show the value as-is
					displayValue = attr.Value;
				}
				else
				{
					// For substring match, highlight the matching parts
					var plainValue = MModule.plainText(attr.Value);
					var comparison = isNoCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
					var index = plainValue.IndexOf(pattern, comparison);
					
					if (index >= 0)
					{
						var before = plainValue.Substring(0, index);
						var match = plainValue.Substring(index, pattern.Length);
						var after = plainValue.Substring(index + pattern.Length);
						
						displayValue = MModule.concat(
							MModule.concat(
								MModule.single(before),
								MModule.single(match).Hilight()
							),
							MModule.single(after)
						);
					}
					else
					{
						displayValue = attr.Value;
					}
				}

				await NotifyService!.Notify(executor,
					MModule.concat(
						MModule.single($"{attr.Name}: ").Hilight(),
						displayValue));
			}
		}
		else
		{
			// List mode - just show attribute names
			var attrNames = string.Join(" ", matchingAttributes.Select(a => a.Name));
			await NotifyService!.Notify(executor, attrNames);
		}

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@INCLUDE", Switches = ["LOCALIZE", "CLEARREGS", "NOBREAK"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 1, MaxArgs = 31)]
	public static async ValueTask<Option<CallState>> Include(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @include[/<switches>] <object>/<attribute>[=<arg1>,<arg2>,...]
		// Inserts attribute contents in-place without adding a new queue entry
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = (await parser.CurrentState.EnactorObject(Mediator!)).WithoutNone();
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		var attributePath = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attributePath))
		{
			await NotifyService!.Notify(executor, "You must specify an object/attribute to include.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		// Parse object/attribute
		var parts = attributePath.Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService!.Notify(executor, "You must specify an object/attribute path.");
			return new CallState("#-1 INVALID PATH");
		}
		
		var objectName = parts[0];
		var attributeName = parts[1];
		
		// Locate the target object
		var maybeObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(
			parser, executor, enactor, objectName, LocateFlags.All);
		
		if (maybeObject.IsError)
		{
			return maybeObject.AsError;
		}
		
		var targetObject = maybeObject.AsSharpObject;
		
		// Get the attribute - must be visible to enactor
		var attributeResult = await AttributeService!.GetAttributeAsync(
			enactor, targetObject, attributeName, IAttributeService.AttributeMode.Read, false);
		
		if (attributeResult.IsError)
		{
			await NotifyService!.Notify(executor, $"No such attribute: {attributeName}");
			return new CallState("#-1 NO SUCH ATTRIBUTE");
		}
		
		if (attributeResult.IsNone)
		{
			await NotifyService!.Notify(executor, $"Attribute {attributeName} is empty.");
			return CallState.Empty;
		}
		
		var attributeContent = attributeResult.AsAttribute.Last().Value;
		var attributeText = attributeContent.ToPlainText();
		
		// Strip ^...: or $...: prefixes for listen/command patterns
		if (attributeText.StartsWith("^") || attributeText.StartsWith("$"))
		{
			var colonIndex = attributeText.IndexOf(':');
			if (colonIndex > 0)
			{
				attributeText = attributeText.Substring(colonIndex + 1).TrimStart();
			}
		}
		
		// Q-register management is now handled by the hook system
		// CLEARREGS and LOCALIZE switches are implemented there
		
		// Environment argument substitution is handled by the hook system
		// Arguments %0-%9 are properly managed during command execution
		
		// Execute the attribute content in-place using CommandListParse
		// This evaluates the command list without creating a queue entry
		try
		{
			var result = await parser.CommandListParse(MModule.single(attributeText));
			
			// TODO: Handle NOBREAK switch
			// When set, @break/@assert from included code shouldn't propagate to calling list
			// This requires break/assert propagation system
			
			return result ?? CallState.Empty;
		}
		catch (Exception ex)
		{
			await NotifyService!.Notify(executor, $"Error executing included attribute: {ex.Message}");
			return new CallState($"#-1 ERROR: {ex.Message}");
		}
	}

	[SharpCommand(Name = "@MAIL",
		Switches =
		[
			"NOEVAL", "NOSIG", "STATS", "CSTATS", "DSTATS", "FSTATS", "DEBUG", "NUKE", "FOLDERS", "UNFOLDER", "LIST", "READ",
			"UNREAD", "CLEAR", "UNCLEAR", "STATUS", "PURGE", "FILE", "TAG", "UNTAG", "FWD", "FORWARD", "SEND", "SILENT",
			"URGENT", "REVIEW", "RETRACT"
		], Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Mail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		parser.CurrentState.Arguments.TryGetValue("0", out var arg0CallState);
		parser.CurrentState.Arguments.TryGetValue("1", out var arg1CallState);
		MString? arg0, arg1;
		var switches = parser.CurrentState.Switches.ToArray();
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var caller = (await parser.CurrentState.CallerObject(Mediator!)).Known();
		string[] sendSwitches = ["SEND", "URGENT", "NOSIG", "SILENT", "NOEVAL"];

		if (switches.Except(sendSwitches).Any() && switches.Length > 1)
		{
			await NotifyService!.Notify(executor, "Error: Too many switches passed to @mail.", caller);
			return new CallState(Errors.ErrorTooManySwitches);
		}

		if (!switches.Contains("NOEVAL"))
		{
			arg0 = await (arg0CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
			arg1 = await (arg1CallState?.ParsedMessage() ?? ValueTask.FromResult<MString?>(null));
		}
		else
		{
			arg0 = arg0CallState?.Message;
			arg1 = arg1CallState?.Message;
		}

		var response = switches.AsSpan() switch
		{
			[.., "FOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "UNFOLDER"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "FILE"] when executor.IsPlayer => await FolderMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, switches),
			[.., "CLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "CLEAR"),
			[.., "UNCLEAR"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNCLEAR"),
			[.., "TAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "TAG"),
			[.., "UNTAG"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNTAG"),
			[.., "UNREAD"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "UNREAD"),
			[.., "STATUS"] when executor.IsPlayer => await StatusMail.Handle(parser, ObjectDataService!, Mediator!,
				NotifyService!, arg0, arg1, "STATUS"),
			[.., "CSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "STATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "DSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "FSTATS"] when executor.IsPlayer => await StatsMail.Handle(parser, ObjectDataService!, LocateService!,
				Mediator!, NotifyService!, arg0, switches),
			[.., "DEBUG"] => await AdminMail.Handle(parser, Mediator!, NotifyService!, switches),
			[.., "NUKE"] => await AdminMail.Handle(parser, Mediator!, NotifyService!, switches),
			[.., "REVIEW"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await ReviewMail.Handle(parser, LocateService!, ObjectDataService!, Mediator!, NotifyService!, arg0, arg1,
					switches),
			[.., "RETRACT"] when (arg0?.Length ?? 0) != 0 && (arg1?.Length ?? 0) != 0
				=> await RetractMail.Handle(parser, ObjectDataService!, LocateService!, Mediator!, NotifyService!,
					arg0!.ToPlainText(), arg1!.ToPlainText()),
			[.., "FWD"] when executor.IsPlayer && int.TryParse(arg0?.ToPlainText(), out var number) &&
			                 (arg1?.Length ?? 0) != 0
				=> await ForwardMail.Handle(parser, ObjectDataService!, LocateService!, PermissionService!, Mediator!, number,
					arg1!.ToPlainText()),
			[.., "SEND"] or [.., "URGENT"] or [.., "SILENT"] or [.., "NOSIG"] or []
				when arg0?.Length != 0 && arg1?.Length != 0
				=> await SendMail.Handle(parser, PermissionService!, ObjectDataService!, Mediator!, NotifyService!, arg0!,
					arg1!, switches),
			[.., "READ"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0 &&
			                        int.TryParse(arg0?.ToPlainText(), out var number)
				=> await ReadMail.Handle(parser, ObjectDataService!, Mediator!, NotifyService!, Math.Max(0, number - 1),
					switches),
			[.., "LIST"] or [] when executor.IsPlayer && (arg1?.Length ?? 0) == 0
				=> await ListMail.Handle(parser, ObjectDataService!, Mediator!, NotifyService!, arg0, arg1, switches),
			_ => MModule.single("#-1 BAD ARGUMENTS TO MAIL COMMAND")
		};

		return new CallState(response);
	}

	[SharpCommand(Name = "@NSPEMIT", Switches = ["LIST", "SILENT", "NOISY", "NOEVAL"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> NoSpoofPrivateEmit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		if (args.Count < 2)
		{
			await NotifyService!.Notify(executor, "Don't you have anything to say?");
			return new CallState("#-1 Don't you have anything to say?");
		}

		var recipients = MModule.plainText(args["0"].Message!);
		var message = args["1"].Message!;

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

			await CommunicationService!.SendToPortsAsync(executor, ports, _ => message, notificationType);
			return CallState.Empty;
		}

		// Handle object/player-based messaging
		var recipientList = ArgHelpers.NameList(recipients);

		foreach (var recipient in recipientList)
		{
			var recipientName = recipient.IsT0 ? recipient.AsT0.ToString() : recipient.AsT1;

			// Use LocateAndNotifyIfInvalidWithCallStateFunction for proper error handling
			await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser,
				executor,
				executor,
				recipientName,
				LocateFlags.All,
				async target =>
				{
					if (await PermissionService.CanInteract(target, executor, InteractType.Hear))
					{
						await NotifyService!.Notify(target, message, executor, notificationType);
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

	[SharpCommand(Name = "@PASSWORD", Switches = [],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Password(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var oldPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var newPassword = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		if (!executor.IsPlayer)
		{
			await NotifyService!.Notify(executor, "Only players have passwords.");
			return new CallState("#-1 INVALID OBJECT TYPE.");
		}

		var isValidPassword = PasswordService!.PasswordIsValid(executor.Object().DBRef.ToString(), oldPassword,
			executor.AsPlayer.PasswordHash);
		if (!isValidPassword)
		{
			await NotifyService!.Notify(executor, "Invalid password.");
			return new CallState("#-1 INVALID PASSWORD.");
		}

		var hashedPassword = PasswordService.HashPassword(executor.Object().DBRef.ToString(), newPassword);
		await PasswordService.SetPassword(executor.AsPlayer, hashedPassword);

		return new CallState(string.Empty);
	}

	[SharpCommand(Name = "@RESTART", Switches = ["ALL"], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Restart(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var scheduler = parser.ServiceProvider.GetRequiredService<ITaskScheduler>();
		
		// Check for /all switch - wizard only
		if (switches.Contains("ALL"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			// Halt all objects, then trigger @STARTUP on all objects that have it
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				// Halt the object's queue
				await scheduler.Halt(obj.DBRef);
				
				// Trigger @STARTUP attribute if it exists (non-inherited)
				try
				{
					var objNode = await Mediator.Send(new GetObjectNodeQuery(obj.DBRef));
					if (!objNode.IsNone)
					{
						await AttributeService!.EvaluateAttributeFunctionAsync(
							parser, executor, objNode.Known, "STARTUP", 
							new Dictionary<string, CallState>(), 
							evalParent: false);
					}
				}
				catch
				{
					// Ignore errors from @STARTUP - they're non-fatal
				}
			}
			
			await NotifyService!.Notify(executor, "All objects restarted.");
			return CallState.Empty;
		}
		
		// Get target object
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify an object to restart.");
			return new CallState("#-1 NO OBJECT SPECIFIED");
		}
		
		var targetName = args["0"].Message!.ToPlainText();
		
		// Locate the target object
		var maybeTarget = await LocateService!.LocateAndNotifyIfInvalid(
			parser,
			executor,
			executor,
			targetName,
			LocateFlags.All);
		
		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 NOT FOUND");
		}
		
		var target = maybeTarget.WithoutError().WithoutNone();
		
		// Check control permissions
		if (!await PermissionService!.Controls(executor, target))
		{
			await NotifyService!.Notify(executor, "Permission denied.");
			return new CallState(Errors.ErrorPerm);
		}
		
		var targetObject = target.Object();
		
		// Halt the object's queue first
		await scheduler.Halt(targetObject.DBRef);
		
		// For players, restart all owned objects too
		if (target.IsPlayer)
		{
			// Halt and restart all objects owned by the player
			await foreach (var obj in Mediator!.CreateStream(new GetAllObjectsQuery()))
			{
				var owner = await obj.Owner.WithCancellation(CancellationToken.None);
				if (owner.Object.DBRef == targetObject.DBRef)
				{
					await scheduler.Halt(obj.DBRef);
					
					// Trigger @STARTUP if it exists
					try
					{
						var objNode = await Mediator.Send(new GetObjectNodeQuery(obj.DBRef));
						if (!objNode.IsNone)
						{
							await AttributeService!.EvaluateAttributeFunctionAsync(
								parser, executor, objNode.Known, "STARTUP", 
								new Dictionary<string, CallState>(), 
								evalParent: false);
						}
					}
					catch
					{
						// Ignore @STARTUP errors - they're non-fatal
					}
				}
			}
			
			await NotifyService!.Notify(executor, $"Restarted {targetObject.Name} and all their objects.");
		}
		else
		{
			await NotifyService!.Notify(executor, $"Restarted {targetObject.Name}.");
		}
		
		// Trigger @STARTUP attribute if it exists (never inherited per PennMUSH spec)
		try
		{
			await AttributeService!.EvaluateAttributeFunctionAsync(
				parser, executor, target, "STARTUP", 
				new Dictionary<string, CallState>(), 
				evalParent: false);
		}
		catch
		{
			// Ignore @STARTUP errors - they're non-fatal
		}
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SWEEP", Switches = ["CONNECTED", "HERE", "INVENTORY", "EXITS"], Behavior = CB.Default,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Sweep(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Parse switches
		var switches = parser.CurrentState.Switches.ToHashSet();
		var connectFlag = switches.Contains("CONNECTED");
		var hereFlag = switches.Contains("HERE");
		var inventoryFlag = switches.Contains("INVENTORY");
		var exitsFlag = switches.Contains("EXITS");

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var location = await executor.Where();
		var locationObj = location.Object();
		var locationAnyObject = location.WithRoomOption();
		var locationOwner = await locationObj.Owner.WithCancellation(CancellationToken.None);

		// ROOM sweep
		if (!inventoryFlag && !exitsFlag)
		{
			await NotifyService!.Notify(executor, "Listening in ROOM:");

			if (connectFlag)
			{
				if (await IsConnectedOrPuppetConnected(locationAnyObject))
				{
					if (location.IsPlayer)
					{
						await NotifyService.Notify(executor, $"{locationObj.Name} is listening.");
					}
					else
					{
						await NotifyService.Notify(executor,
							$"{locationObj.Name} [owner: {locationOwner.Object.Name}] is listening.");
					}
				}
			}
			else
			{
				if (await locationAnyObject.IsHearer(ConnectionService!, AttributeService!) ||
				    await locationAnyObject.IsListener())
				{
					if (await ConnectionService!.IsConnected(locationAnyObject))
						await NotifyService.Notify(executor, $"{locationObj.Name} (this room) [speech]. (connected)");
					else
						await NotifyService.Notify(executor, $"{locationObj.Name} (this room) [speech].");
				}

				if (await locationAnyObject.HasActiveCommands(AttributeService!))
					await NotifyService.Notify(executor, $"{locationObj.Name} (this room) [commands].");
				if (await locationAnyObject.IsAudible())
					await NotifyService.Notify(executor, $"{locationObj.Name} (this room) [broadcasting].");
			}

			// Contents of the room
			var contents = location.Content(Mediator!);
			await foreach (var obj in contents)
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.Notify(executor, $"{obj.Object().Name} is listening.");
						}
						else
						{
							await NotifyService.Notify(executor,
								$"{obj.Object().Name} [owner: {objOwner.Object.Name}] is listening.");
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService!, AttributeService!) || await fullObj.IsListener())
					{
						if (await ConnectionService!.IsConnected(fullObj))
							await NotifyService.Notify(executor, $"{obj.Object().Name} [speech]. (connected)");
						else
							await NotifyService.Notify(executor, $"{obj.Object().Name} [speech].");
					}

					if (await fullObj.HasActiveCommands(AttributeService!))
						await NotifyService.Notify(executor, $"{obj.Object().Name} [commands].");
				}
			}
		}

		// EXITS sweep (only if not connectFlag and not inventoryFlag and location is a room)
		if (!connectFlag && !inventoryFlag && location.IsRoom && exitsFlag)
		{
			await NotifyService!.Notify(executor, "Listening EXITS:");
			if (await locationAnyObject.IsAudible())
			{
				var exits = (location.Content(Mediator!)).Where(x => x.IsExit);
				await foreach (var exit in exits)
				{
					if (await exit.WithRoomOption().IsAudible())
					{
						await NotifyService.Notify(executor, $"{exit.Object().Name} [broadcasting].");
					}
				}
			}
		}

		// INVENTORY sweep (if not hereFlag and not exitFlag)
		if (!hereFlag && !exitsFlag && inventoryFlag)
		{
			await NotifyService!.Notify(executor, "Listening in your INVENTORY:");
			await foreach (var obj in executor.AsContainer.Content(Mediator!))
			{
				var fullObj = obj.WithRoomOption();
				var objOwner = await obj.Object().Owner.WithCancellation(CancellationToken.None);
				if (connectFlag)
				{
					if (await IsConnectedOrPuppetConnected(fullObj))
					{
						if (obj.IsPlayer)
						{
							await NotifyService.Notify(executor, $"{obj.Object().Name} is listening.");
						}
						else
						{
							await NotifyService.Notify(executor,
								$"{obj.Object().Name} [owner: {objOwner.Object.Name}] is listening.");
						}
					}
				}
				else
				{
					if (await fullObj.IsHearer(ConnectionService!, AttributeService!) || await fullObj.IsListener())
					{
						if (await ConnectionService!.IsConnected(fullObj))
							await NotifyService.Notify(executor, $"{obj.Object().Name} [speech]. (connected)");
						else
							await NotifyService.Notify(executor, $"{obj.Object().Name} [speech].");
					}

					if (await fullObj.HasActiveCommands(AttributeService!))
						await NotifyService.Notify(executor, $"{obj.Object().Name} [commands].");
				}
			}
		}

		return CallState.Empty;

		static async Task<bool> IsConnectedOrPuppetConnected(AnySharpObject obj)
		{
			if (await ConnectionService!.IsConnected(obj)) return true;

			return await obj.IsPuppet()
			       && await ConnectionService!.IsConnected(await obj.Object().Owner.WithCancellation(CancellationToken.None));
		}
	}

	[SharpCommand(Name = "@VERSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Version(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// TODO: Last Restarted
		var result = MModule.multipleWithDelimiter(
			MModule.single("\n"),
			[
				MModule.concat(MModule.single("You are connected to "),
					MModule.single(Configuration!.CurrentValue.Net.MudName)),
				MModule.concat(MModule.single("Address: "), MModule.single(Configuration.CurrentValue.Net.MudUrl)),
				MModule.single("SharpMUSH version 0")
			]);

		await NotifyService!.Notify(executor, result);

		return new CallState(result);
	}

	[SharpCommand(Name = "@RETRY", Switches = [],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Retry(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var predicate = args["0"];

		var peek = parser.State.TakeLast(2);
		if (peek.Count() != 2)
		{
			await NotifyService!.Notify(executor, "Nothing to retry.");
			return new CallState("#-1 RETRY: NO COMMAND TO RETRY.");
		}

		var previousCommand = parser.State.First();
		// var retryState = parser.State.Peek();
		var limit = 1000;

		while ((await parser.FunctionParse(predicate.Message!))!.Message.Truthy() && limit > 0)
		{
			// TODO: I think I need a way to REWIND the stack in the PARSER.
			// This is going to be tricky.
			// Todo: Parse arguments?
			await parser.With(
				state => state with { Arguments = args.Skip(1).ToDictionary() },
				async newParser => await previousCommand.CommandInvoker(newParser));
			limit--;
		}

		return new CallState(1000 - limit);
	}

	[SharpCommand(Name = "@ASSERT", Switches = ["INLINE", "QUEUED"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.RSBrace, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Assert(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var nargs = args.Count;
		
		// Note: INLINE is default behavior (immediate execution)
		// QUEUED switch queues the command for later execution via task scheduler
		var useQueue = switches.Contains("QUEUED");
		
		switch (nargs)
		{
			case 0:
				// Do nothing.
				break;
			case 1:
				if (args["0"].Message.Falsy())
				{
					parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));
				}

				return args["0"];
			case 2 when args["0"].Message.Falsy():
				var command = await args["1"].ParsedMessage();
				
				if (useQueue)
				{
					// Queue the command for later execution
					var executor = parser.CurrentState.Executor ?? throw new InvalidOperationException("Executor cannot be null");
					await Mediator!.Send(new QueueCommandListRequest(
						command!, 
						parser.CurrentState, 
						new DbRefAttribute(executor, ["ASSERT"]),
						-1));
				}
				else
				{
					// Execute inline (default)
					var commandList = parser.CommandListParseVisitor(command!);
					await commandList();
				}
				
				parser.CurrentState.ExecutionStack.Push(new Execution(CommandListBreak: true));

				return args["0"];
			case 2:
				return args["0"];
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// @attribute <attrib> - Display attribute information
		// @attribute/access[/retroactive] <attrib>=<flag list> - Add/modify standard attribute
		// @attribute/delete <attrib> - Remove standard attribute
		// @attribute/rename <attrib>=<new name> - Rename standard attribute
		// @attribute/limit <attrib>=<regexp pattern> - Restrict values to pattern
		// @attribute/enum [<delim>] <attrib>=<list> - Restrict values to list
		// @attribute/decompile[/retroactive] [<pattern>] - Decompile attribute table
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		
		// Check for decompile switch first (can work with 0 or 1 arg)
		if (switches.Contains("DECOMPILE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "*";
			var retroactive = switches.Contains("RETROACTIVE");
			
			await NotifyService!.Notify(executor, "@attribute/decompile: Decompiling attribute table");
			await NotifyService.Notify(executor, $"  Pattern: {pattern}");
			if (retroactive)
			{
				await NotifyService.Notify(executor, "  Including /retroactive switch");
			}
			
			// TODO: Full implementation requires:
			// - Iterate through all standard attributes in the attribute table
			// - Filter by pattern (wildcard matching)
			// - Output @attribute/access commands for each matching attribute
			// - Include attribute flags and creator dbref
			await NotifyService.Notify(executor, "Note: Attribute table decompilation not yet implemented.");
			
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// All other operations require at least one argument
		if (args.Count == 0)
		{
			await NotifyService!.Notify(executor, "You must specify an attribute.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		var attrName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService!.Notify(executor, "You must specify an attribute.");
			return new CallState("#-1 NO ATTRIBUTE SPECIFIED");
		}
		
		// Check for various switches
		if (switches.Contains("ACCESS"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			if (args.Count < 2)
			{
				await NotifyService!.Notify(executor, "You must specify attribute flags.");
				return new CallState("#-1 NO FLAGS SPECIFIED");
			}
			
			var flagList = args["1"].Message?.ToPlainText() ?? "none";
			var retroactive = switches.Contains("RETROACTIVE");
			
			// Parse the flag list - space-separated flag names
			var flagNames = flagList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(f => f.ToUpper())
				.ToArray();
			
			// Validate that all flags exist
			var allFlags = await Mediator!.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();
			foreach (var flagName in flagNames)
			{
				if (!allFlags.Any(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService!.Notify(executor, $"Unknown attribute flag: {flagName}");
					return new CallState("#-1 UNKNOWN FLAG");
				}
			}
			
			// Create or update the attribute entry
			var entry = await Mediator!.Send(new CreateAttributeEntryCommand(attrName.ToUpper(), flagNames));
			if (entry == null)
			{
				await NotifyService!.Notify(executor, "Failed to create attribute entry.");
				return new CallState("#-1 CREATE FAILED");
			}
			
			await NotifyService!.Notify(executor, $"Attribute '{attrName}' set with flags: {string.Join(" ", flagNames)}");
			
			// TODO: If retroactive, update all existing copies
			if (retroactive)
			{
				await NotifyService.Notify(executor, "Note: Retroactive flag updating not yet implemented.");
			}
			
			return CallState.Empty;
		}
		
		if (switches.Contains("DELETE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			await NotifyService!.Notify(executor, $"@attribute/delete: Removing attribute '{attrName}' from table");
			
			// TODO: Full implementation requires:
			// - Remove attribute from standard attribute table
			// - Existing copies remain but are no longer "standard"
			// - Save changes to persist across reboots
			await NotifyService.Notify(executor, "Note: Attribute table modification not yet implemented.");
			
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("RENAME"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			if (args.Count < 2)
			{
				await NotifyService!.Notify(executor, "You must specify a new name.");
				return new CallState("#-1 NO NEW NAME SPECIFIED");
			}
			
			var newName = args["1"].Message?.ToPlainText();
			await NotifyService!.Notify(executor, $"@attribute/rename: Renaming '{attrName}' to '{newName}'");
			
			// TODO: Full implementation requires:
			// - Rename attribute in standard attribute table
			// - Update all references to use new name
			// - Save changes to persist across reboots
			await NotifyService.Notify(executor, "Note: Attribute table modification not yet implemented.");
			
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("LIMIT"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			if (args.Count < 2)
			{
				await NotifyService!.Notify(executor, "You must specify a regexp pattern.");
				return new CallState("#-1 NO PATTERN SPECIFIED");
			}
			
			var pattern = args["1"].Message?.ToPlainText();
			await NotifyService!.Notify(executor, $"@attribute/limit: Setting pattern for '{attrName}'");
			await NotifyService.Notify(executor, $"  Pattern: {pattern}");
			await NotifyService.Notify(executor, "  New values must match this pattern (case insensitive)");
			
			// TODO: Full implementation requires:
			// - Store regexp pattern with attribute in table
			// - Validate all new attribute values against pattern
			// - Pattern is case insensitive unless (?-i) is used
			await NotifyService.Notify(executor, "Note: Attribute validation not yet implemented.");
			
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		if (switches.Contains("ENUM"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService!.Notify(executor, "Permission denied.");
				return new CallState(Errors.ErrorPerm);
			}
			
			if (args.Count < 2)
			{
				await NotifyService!.Notify(executor, "You must specify a list of choices.");
				return new CallState("#-1 NO CHOICES SPECIFIED");
			}
			
			var choices = args["1"].Message?.ToPlainText();
			await NotifyService!.Notify(executor, $"@attribute/enum: Setting choices for '{attrName}'");
			await NotifyService.Notify(executor, $"  Choices: {choices}");
			await NotifyService.Notify(executor, "  New values must match one of these choices");
			
			// TODO: Full implementation requires:
			// - Store enumeration list with attribute in table
			// - Validate all new attribute values against list
			// - Support partial matching like grab()
			// - Support custom delimiters (default is space)
			await NotifyService.Notify(executor, "Note: Attribute validation not yet implemented.");
			
			return new CallState("#-1 NOT IMPLEMENTED");
		}
		
		// No switches - display attribute information
		await NotifyService!.Notify(executor, $"@attribute: Information for '{attrName}'");
		await NotifyService.Notify(executor, "  Full name: (attribute lookup pending)");
		await NotifyService.Notify(executor, "  Flags: (attribute table query pending)");
		await NotifyService.Notify(executor, "  Created by: (attribute table query pending)");
		
		// TODO: Full implementation requires:
		// - Query attribute table for attribute information
		// - Display full name (canonical form)
		// - Display default attribute flags
		// - Display dbref of object that added it to table
		await NotifyService.Notify(executor, "Note: Attribute table query not yet implemented.");
		
		return CallState.Empty;
	}

	[SharpCommand(Name = "@SKIP", Switches = ["IFELSE"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.RSNoParse,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Skip(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var parsedIfElse = await parser.CurrentState.Arguments["0"].ParsedMessage();
		var falsey = Predicates.Falsy(parsedIfElse!);

		if (parser.CurrentState.Arguments.TryGetValue("1", out var arg1))
		{
			await parser.CommandListParse(arg1.Message!);
		}

		return new CallState(!falsey);
	}

	// TODO: Handle switches
	[SharpCommand(Name = "@MESSAGE", Switches = ["NOEVAL", "SPOOF", "NOSPOOF", "REMIT", "OEMIT", "SILENT", "NOISY"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 3, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> Message(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
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
			parser, Mediator!, LocateService!, AttributeService!, NotifyService!,
			PermissionService!, CommunicationService!, executor,
			recipientsArg, defmsg, objectAttrArg, otherArgs,
			isRemit, isOemit, isNospoof, isSpoof, isSilent);

		return result;
	}
}