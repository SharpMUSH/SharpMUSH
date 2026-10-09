using SharpMUSH.Database;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Immutable;
using System.Drawing;
using System.Linq;
using MarkupString;
using MarkupString.Ansi;
using static MarkupString.MStringInterpolation;
using static SharpMUSH.Library.Services.Interfaces.IPermissionService;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Softcode;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Resolves the display width to wrap a formatted attribute block to, for <c>@examine</c> and
	/// <c>@grep/PRINT</c>'s syntax-flagged attribute rendering. NAWS WIDTH is client-controlled,
	/// unvalidated metadata. Per RFC 1073, 0 means "unspecified" from the client's end, not "wrap
	/// every token" -- and it parses fine, so it must be rejected explicitly rather than relying on
	/// TryParse to catch it. A parsed value &lt;= 0 falls back to 78 exactly like a missing one.
	/// </summary>
	private async ValueTask<int> ExecutorFormatWidthAsync(AnySharpObject executor)
	{
		var executorConnection = await ConnectionService.Get(executor.Object().DBRef).FirstOrDefaultAsync();

		return executorConnection is not null
			&& executorConnection.Metadata.TryGetValue("WIDTH", out var widthStr)
			&& int.TryParse(widthStr, out var parsedWidth)
			&& parsedWidth > 0
				? parsedWidth
				: 78;
	}

	[SharpCommand(Name = "HUH_COMMAND", Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> HuhCommand(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HuhTypeHelp), executor);
		return new CallState(ErrorMessages.Returns.Huh);
	}

	[SharpCommand(Name = "LOOK", Output = CommandOutput.Value, Switches = ["OUTSIDE", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> Look(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		// cmds.c:1742: the /opaque switch is LOOK_NOCONTENTS.
		var key = switches.Contains("OPAQUE") ? LookKey.NoContents : LookKey.Normal;
		var lookOutside = switches.Contains("OUTSIDE");

		if (executor.IsPlayer)
		{
			var fallbackHome = new DBRef((int)Configuration.CurrentValue.Database.PlayerStart);
			if (await MoveService.RescueFromVoidAsync(executor, fallbackHome))
			{
				executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			}
		}

		AnyOptionalSharpObject viewing = new None();

		if (lookOutside)
		{
			// PennMUSH do_look_at (look.c:611): looking outside shows the location OF your location, so
			// there is nothing to see when you are standing in a room or inside an opaque container.
			var container = executor.AsOptionalContent is AnySharpContent looker ? await looker.Location() : null;

			if (container is null || container.IsRoom || await container.WithExitOption().IsOpaque())
			{
				await NotifyService.NotifyLocalized(executor,
					nameof(ErrorMessages.Notifications.CantSeeThroughThat), executor);
				return new CallState(ErrorMessages.Returns.CantSeeThroughThat);
			}

			viewing = (await container.Location()).WithExitOption().WithNoneOption();
		}
		else if (args.Count == 1)
		{
			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				args["0"].Message.ToPlainText(),
				LocateFlags.All);

			if (locate is AnySharpObject located)
			{
				viewing = located;
			}
		}
		else
		{
			viewing = (await Mediator.Send(new GetCertainLocationQuery(executor.Id()!, executor.Object().Id!))).WithExitOption()
				.WithNoneOption();
		}

		if (viewing.IsNone)
		{
			return new None();
		}

		return await LookService.LookRoom(parser, executor, viewing, key, lookOutside);
	}

	[SharpCommand(Name = "EXAMINE", Output = CommandOutput.Value, Switches = ["BRIEF", "DEBUG", "MORTAL", "PARENT", "ALL", "OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["object"])]
	public ValueTask<Option<CallState>> Examine(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ExamineAsync(parser, parser.CurrentState.Switches.ToArray());

	/// <summary>
	/// <c>do_examine</c> (<c>src/look.c:780-990</c>). Every object line is <c>object_header</c>
	/// (<see cref="MessageFormatting.FormatObjectWithDbrefMString"/>); <c>FLAGS_ON_EXAMINE</c> gates only
	/// the <c>Type: ... Flags: ...</c> line, and <c>BRIEF</c> skips only the description and attributes.
	/// </summary>
	/// <remarks>
	/// Two early returns, in Penn's order. An attribute pattern is the whole command (<c>:796-801</c>).
	/// Otherwise a viewer who may not examine gets the description and the attributes they may read, then
	/// the owner line in place of everything an examine is really for (<c>:809-823</c>, <c>:896-909</c>).
	/// </remarks>
	private async ValueTask<Option<CallState>> ExamineAsync(IMUSHCodeParser parser, string[] switches)
	{
		var args = parser.CurrentState.Arguments;
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;
		string? attributePattern = null;

		if (args.Count == 1)
		{
			var argText = args["0"].Message.ToPlainText();
			var objectName = argText;

			if (HelperFunctions.SplitDbRefAndOptionalAttr(argText) is { Object: var splitObject, Attribute: var maybeAttributePattern })
			{
				objectName = splitObject;
				attributePattern = maybeAttributePattern;
			}

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				objectName,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			viewing = located;
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing is not AnySharpObject viewingKnown)
		{
			return new None();
		}

		var obj = viewingKnown.Object()!;

		// An attribute pattern is answered on its own and returns (look.c:796-801) -- before the
		// permission and proximity tests below, so no header and no owner line, and EXAM_BRIEF never
		// reaches its own branch.
		if (!string.IsNullOrEmpty(attributePattern))
		{
			await ExamineAttributesAsync(parser, enactor, executor, viewingKnown, attributePattern, switches);
			return new CallState(obj.DBRef.ToString());
		}

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (switches.Contains("MORTAL") && await executor.IsWizard())
		{
			canExamine = await PermissionService.Controls(executor, viewingKnown);
		}

		var publicAttributes = Configuration.CurrentValue.Cosmetic.ExaminePublicAttributes;

		// An object the viewer may not examine and is not standing next to -- or any such object at all
		// when ex_public_attribs is off -- is worth one line, its header and its owner's (look.c:809-823).
		// The injected ILocateService property shadows the type, so nearby() is qualified.
		if (!canExamine
			&& (!publicAttributes || !await Library.Services.LocateService.Nearby(executor, viewingKnown)))
		{
			await NotifyOwnedByAsync(enactor, executor, viewingKnown);
			return new CallState(obj.DBRef.ToString());
		}

		var perceive = await ObserveRealityAsync(parser, executor);

		// Contents at all only when the viewer may examine, or when the thing is neither a room nor
		// opaque (look.c:883-884).
		var showContents = !switches.Contains("OPAQUE") && !viewingKnown.IsExit
			&& (canExamine || (!viewingKnown.IsRoom && !await viewingKnown.IsOpaque()));

		// Contents walk DOLIST_VISIBLE (look.c:885), whose first_visible applies the DARK/LIGHT rules
		// (predicat.c:1130-1160) -- so a mortal who does not control a DARK object never sees it listed.
		// The exits list below is a plain DOLIST (look.c:916) and has no such filter.
		var canSeeContent = await ObserveContentsAsync(parser, executor, viewingKnown, ConnectionService);

		var contents = !showContents || viewingKnown.AsOptionalContainer is not AnySharpContainer viewedContainer
			? []
			// GetContentsQuery also yields exits; Penn's Contents(thing) never does, and exits get their own list.
			: await Mediator.CreateStream(new GetContentsQuery(viewedContainer), ExecutionBudget.CurrentToken)
				.Where(item => !item.IsExit)
				.Where((AnySharpContent item, CancellationToken ct) => canSeeContent(item, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

		var outputSections = new List<MString>();
		var flagView = await FlagView.ForAsync(executor, ConnectionService);

		if (canExamine)
		{
			outputSections.Add(await MessageFormatting.FormatObjectWithDbrefMString(obj, flagView));

			if (Configuration.CurrentValue.Cosmetic.FlagsOnExamine)
			{
				outputSections.Add(MarkupText.Plain(await MessageFormatting.FlagDescriptionAsync(obj, flagView)));
			}
		}

		// atr_get_noparent's raw value, and nothing at all when the object has no DESCRIBE
		// (look.c:832-839). ex_public_attribs gates it for every viewer, examinable or not.
		if (publicAttributes && !switches.Contains("BRIEF")
			&& await AttributeService.GetAttributeAsync(executor, viewingKnown, "DESCRIBE",
				IAttributeService.AttributeMode.Read, false) is SharpAttribute[] describe)
		{
			outputSections.Add(describe.Last().Value);
		}

		if (canExamine)
		{
			var objParent = await obj.Parent.WithCancellation(CancellationToken.None);
			var objZone = await obj.Zone.WithCancellation(CancellationToken.None);
			var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;

			MString zoneSection;
			if (objZone is AnySharpObject zone)
			{
				var zoneLine = await MessageFormatting.FormatObjectWithDbrefMString(zone.Object(), flagView);
				zoneSection = Format($"  Zone: {zoneLine}");
			}
			else
			{
				zoneSection = MarkupText.Plain("  Zone: *NOTHING*");
			}

			var ownerLine = await MessageFormatting.FormatObjectWithDbrefMString(ownerObj, flagView);
			outputSections.Add(Format($"Owner: {ownerLine}{zoneSection}"));

			var parentObject = objParent.Object();
			if (parentObject == null)
			{
				outputSections.Add(MarkupText.Plain("Parent: *NOTHING*"));
			}
			else
			{
				var parentLine = await MessageFormatting.FormatObjectWithDbrefMString(parentObject, flagView);
				outputSections.Add(Format($"Parent: {parentLine}"));
			}

			foreach (var lockKvp in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			{
				outputSections.Add(MarkupText.Plain(await FormatLockLineAsync(executor, lockKvp.Key, lockKvp.Value)));
			}

			var powersList = (await obj.ReadPowersAsync(ExecutionBudget.CurrentToken)).Select(x => x.Name);
			outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));

			// Not PennMUSH: privileges are roles here, so examine says which an object holds and where from.
			var grants = await obj.Grants.WithCancellation(ExecutionBudget.CurrentToken);
			if (ObjectGrantsDisplay.Roles(grants) is { Length: > 0 } rolesLine)
			{
				outputSections.Add(MarkupText.Plain($"Roles: {rolesLine}"));
			}

			if (ObjectGrantsDisplay.Overrides(grants) is { Length: > 0 } overridesLine)
			{
				outputSections.Add(MarkupText.Plain($"Overrides: {overridesLine}"));
			}

			var warningsStr = obj.Warnings != WarningType.None
				? WarningTypeHelper.UnparseWarnings(obj.Warnings)
				: string.Empty;
			outputSections.Add(MarkupText.Plain($"Warnings checked: {warningsStr}"));

			if (switches.Contains("DEBUG") && await executor.IsWizard())
			{
				outputSections.Add(MarkupText.Plain($"Created: {obj.CreationTime} ({DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F})"));
			}
			else
			{
				outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):ddd MMM dd HH:mm:ss yyyy}"));
			}

			outputSections.Add(MarkupText.Plain($"Last modified: {DateTimeOffset.FromUnixTimeMilliseconds(obj.ModifiedTime):ddd MMM dd HH:mm:ss yyyy}"));

			if (viewingKnown is SharpPlayer viewedPlayer)
			{
				outputSections.Add(MarkupText.Plain($"Quota: {viewedPlayer.Quota}"));
			}
		}

		if (outputSections.Count > 0)
		{
			await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);
		}

		if (!switches.Contains("BRIEF"))
		{
			await ExamineAttributesAsync(parser, enactor, executor, viewingKnown, attributePattern, switches);
		}

		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var conFormatResult = await AttributeService.GetAttributeAsync(executor, viewingKnown, "CONFORMAT",
				IAttributeService.AttributeMode.Read, false);

			if (conFormatResult.IsAttribute)
			{
				var contentDbrefs = string.Join(" ", contents.Select(x => x.Object().DBRef.ToString()));
				var contentNames = string.Join("|", contents.Select(x => x.Object().Name));

				var formatArgs = new Dictionary<string, CallState>
				{
					["0"] = new CallState(contentDbrefs),
					["1"] = new CallState(contentNames)
				};

				var formattedContents = await AttributeService.EvaluateAttributeFunctionAsync(
					parser, executor, viewingKnown, "CONFORMAT", formatArgs);

				await NotifyService.Notify(enactor, formattedContents, enactor);
			}
			else
			{
				var contentsLabel = viewingKnown.IsPlayer ? "Carrying:" : "Contents:";
				// object_header of each item (look.c:893), which is unparse_object -- so a viewer who may
				// not see a content's dbref gets its bare name, whatever they may do with the container.
				var contentItems = await contents
					.ToAsyncEnumerable()
					.Select((AnySharpContent content, CancellationToken _) =>
						MessageFormatting.UnparseObjectMStringAsync(PermissionService, executor, content.WithRoomOption(), ConnectionService))
					.Prepend(MarkupText.Plain(contentsLabel))
					.ToListAsync();
				await NotifyService.Notify(enactor,
					MarkupText.Join(MarkupText.Plain("\n"), contentItems), enactor);
			}
		}

		// Everything past the contents belongs to a viewer who may examine; the rest get the owner line
		// and nothing else (look.c:896-909). Penn also shows a room's obvious exits here, which needs a
		// look_exits seam ILookService does not have yet -- tracked with this fix's issue.
		if (!canExamine)
		{
			await NotifyOwnedByAsync(enactor, executor, viewingKnown);
			return new CallState(obj.DBRef.ToString());
		}

		if (!switches.Contains("OPAQUE") && viewingKnown.AsOptionalContainer is AnySharpContainer exitSource)
		{
			var exits = await Mediator.CreateStream(new GetExitsQuery(exitSource), ExecutionBudget.CurrentToken)
				.Where((exit, ct) => perceive(exit.Object.DBRef, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

			if (exits.Length > 0)
			{
				var exitLines = await exits
					.ToAsyncEnumerable()
					.Select((SharpExit exit, CancellationToken _) => MessageFormatting.FormatObjectWithDbrefMString(exit.Object, flagView))
					.Prepend(MarkupText.Plain("Exits:"))
					.ToListAsync();
				await NotifyService.Notify(enactor,
					MarkupText.Join(MarkupText.Plain("\n"), exitLines), enactor);
			}
		}

		if (viewingKnown.AsOptionalContent is AnySharpContent viewedContent)
		{
			var homeContainer = await viewedContent.Home();
			var locationContainer = await viewedContent.Location();

			var locationLine = await MessageFormatting.FormatObjectWithDbrefMString(locationContainer.Object(), flagView);

			// An unlinked exit has no destination to report; PennMUSH shows #-1 for NOTHING.
			if (homeContainer is AnySharpContainer home)
			{
				var homeLine = await MessageFormatting.FormatObjectWithDbrefMString(home.Object(), flagView);
				await NotifyService.Notify(enactor, Format($"Home: {homeLine}"), enactor);
			}
			else
			{
				await NotifyService.Notify(enactor, Format($"Home: #-1"), enactor);
			}

			await NotifyService.Notify(enactor, Format($"Location: {locationLine}"), enactor);
		}

		return new CallState(obj.DBRef.ToString());
	}

	/// <summary>
	/// All an examine says about an object the viewer may not examine: two <c>object_header</c>s, and no
	/// full stop after them (<c>src/look.c:813-818</c> and <c>:900-905</c>).
	/// </summary>
	private async ValueTask NotifyOwnedByAsync(AnySharpObject enactor, AnySharpObject executor, AnySharpObject viewing)
	{
		var owner = await viewing.Object().Owner.WithCancellation(CancellationToken.None);
		var viewedLine = await MessageFormatting.UnparseObjectMStringAsync(PermissionService, executor, viewing, ConnectionService);
		var ownerLine = await MessageFormatting.UnparseObjectMStringAsync(PermissionService, executor, new AnySharpObject(owner), ConnectionService);

		await NotifyService.Notify(enactor, Format($"{viewedLine} is owned by {ownerLine}"), enactor);
	}

	/// <summary>
	/// <c>examine_atrs</c> (<c>src/look.c:372-405</c>): the attribute lines an examine prints. A pattern
	/// that matches nothing answers <c>No matching attributes.</c>; the whole-object form stays silent.
	/// </summary>
	private async ValueTask ExamineAttributesAsync(IMUSHCodeParser parser, AnySharpObject enactor,
		AnySharpObject executor, AnySharpObject viewing, string? attributePattern, string[] switches)
	{
		var checkParents = switches.Contains("PARENT");
		var named = !string.IsNullOrEmpty(attributePattern);

		// Each attribute with the object it was read from: /parent shows an inherited one as
		// #<parent>/NAME (examine_helper, look.c:353-358).
		var atrs = checkParents
			? await InheritedExamineAttributesAsync(executor, viewing, named ? attributePattern! : "*")
			: (named
				? await AttributeService.GetAttributePatternAsync(executor, viewing, attributePattern!, false,
					IAttributeService.AttributePatternMode.Wildcard)
				: await AttributeService.GetVisibleAttributesAsync(executor, viewing)) switch
			{
				SharpAttribute[] own => [.. own.Select(attr => (Attribute: attr, Source: viewing.Object().DBRef))],
				_ => []
			};

		// examine_helper and examine_helper_veiled both drop DESCRIBE from a whole-object listing when
		// ex_public_attribs is on (look.c:310-312, :346-348) -- the description line above already is it.
		var skipDescribe = Configuration.CurrentValue.Cosmetic.ExaminePublicAttributes
			&& (!named || attributePattern == "*");

		var shown = 0;

		if (atrs.Length > 0)
		{
			var showAll = switches.Contains("ALL");

			// Lazily computed: only a flagged attribute needs it, and most @examine calls have none.
			int? width = null;
			var viewMemo = new AttributeViewMemo();

			const string VeiledFlagName = "VEILED";
			var listed = atrs
				.Where(entry => !skipDescribe || !entry.Attribute.LongName.Equals("DESCRIBE", StringComparison.OrdinalIgnoreCase))
				.Where(entry => showAll || !entry.Attribute.Flags.Any(f => f.Name.Equals(VeiledFlagName, StringComparison.OrdinalIgnoreCase)));
			foreach (var (attr, readFrom) in listed)
			{
				var attrOwner = await attr.Owner.WithCancellation(CancellationToken.None);
				var attrFlagsStr = attr.Flags.Any() ? $"{string.Join("", attr.Flags.Select(f => f.Symbol))} " : "";

				if (!await PermissionService.CanViewAttribute(executor, viewing, viewMemo, attr))
				{
					continue;
				}

				shown++;

				var inheritedFrom = readFrom.Number == viewing.Object().DBRef.Number ? "" : $"#{readFrom.Number}/";
				var header = MarkupText.Plain($"{inheritedFrom}{attr.LongName} [{attrFlagsStr}#{attrOwner!.Object.DBRef.Number}]: ").Hilight();
				var parseType = attr.SyntaxParseType();

				if (parseType is null)
				{
					await NotifyService.Notify(enactor, Format($"{header}{attr.Value}"), enactor);
					continue;
				}

				await NotifyService.Notify(enactor, header, enactor);

				if (attr.Value.Length == 0)
				{
					continue;
				}

				width ??= await ExecutorFormatWidthAsync(executor);

				// Cost per flagged, non-empty attribute: 3 lexes and 2 full parses of `source`.
				// Tokenize lexes once (no parse). GetSemanticTokens and ValidateAndGetErrors each
				// independently construct their own lexer + parser and run the parseType grammar
				// rule again -- one to walk the parse tree for token classification, the other with
				// a custom ParserErrorListener attached. Neither IMUSHCodeParser method exposes a
				// way to reuse the other's lex/parse, and there's no third overload that returns
				// tokens + semantic tokens + errors from one pass. Collapsing this to one lex/parse
				// would need a new combined method on IMUSHCodeParser (e.g. an
				// `Analyze(MString, ParseType) -> (TokenInfo[], SemanticToken[], ParseError[])` that
				// attaches an error listener to the same parser instance AnalyzeSemanticTokens already
				// walks) -- an interface change, deliberately not made here per review instruction;
				// left as the known, documented cost of this path instead.
				var source = attr.Value;
				var tokens = parser.Tokenize(source);
				var semanticTokens = parser.GetSemanticTokens(source, parseType.Value);
				var errors = SoftcodeSource.Validate(parser, source, parseType.Value);
				var formatted = SoftcodeFormatter.Format(source, tokens, semanticTokens, errors, width.Value, parser, parseType.Value);

				await NotifyService.Notify(enactor, formatted, enactor);
			}
		}

		if (named && shown == 0)
		{
			await NotifyService.NotifyLocalized(enactor,
				nameof(ErrorMessages.Notifications.ExamineNoMatchingAttributes), enactor);
		}
	}

	/// <summary>
	/// <c>atr_iter_get_parent</c> (<c>src/attrib.c:1501-1530</c>) for <c>examine/parent</c>: a name without
	/// wildcards is looked up as <c>atr_get</c> would, parents and type ancestor included; a pattern walks
	/// the object and its <c>@parent</c> chain, nearest first. Each match carries the object it is on.
	/// </summary>
	private async ValueTask<(SharpAttribute Attribute, DBRef Source)[]> InheritedExamineAttributesAsync(
		AnySharpObject executor, AnySharpObject viewing, string pattern)
	{
		var viewingRef = viewing.Object().DBRef;

		if (!pattern.Contains('*') && !pattern.Contains('?') && !pattern.EndsWith('`'))
		{
			if (await AttributeService.GetAttributeAsync(executor, viewing, pattern, IAttributeService.AttributeMode.Read,
					parent: true) is not SharpAttribute[] chain)
			{
				return [];
			}

			// The same walk GetAttributeAsync took, so a parent past the default depth is still named.
			var path = chain.Last().LongName.Split('`');
			var walk = new InheritanceWalk(await viewing.Ancestor(Configuration),
				(int)Configuration.CurrentValue.Limit.MaxParents);
			var source = await Mediator.CreateStream(new GetAttributeWithInheritanceQuery(viewingRef, path, true, walk))
				.FirstOrDefaultAsync() is { } hit
				? hit.SourceObject
				: viewingRef;

			return [(chain.Last(), source)];
		}

		if (await AttributeService.GetAttributePatternAsync(executor, viewing, pattern, true,
				IAttributeService.AttributePatternMode.Wildcard) is not SharpAttribute[] permitted)
		{
			return [];
		}

		var sources = new Dictionary<string, DBRef>(StringComparer.OrdinalIgnoreCase);
		await foreach (var match in Mediator.CreateStream(new GetAttributesQuery(viewingRef, pattern.ToUpper(), true,
				IAttributeService.AttributePatternMode.Wildcard)))
		{
			sources.TryAdd(match.Attribute.LongName, match.SourceObject);
		}

		return [.. permitted.Select(attr => (attr, sources.GetValueOrDefault(attr.LongName, viewingRef)))];
	}

	private async ValueTask<string> FormatLockLineAsync(AnySharpObject viewer, string name, SharpLockData data)
	{
		var expression = await BooleanExpressionParser.RenderAsync(data.LockString, viewer, LockRenderMode.Examine, ExecutionBudget.CurrentToken);
		var creator = data.Creator is { } identity ? $"#{identity.Number}" : "#-1";
		return $"{LockNames.Display(name)} Lock [{creator}{LockService.FormatLockFlags(data.Flags)}]: {expression}";
	}
}
