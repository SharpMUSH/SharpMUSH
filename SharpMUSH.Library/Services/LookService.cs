using Mediator;
using MarkupString;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

public class LookService(
	IMediator mediator,
	IAttributeService attributeService,
	INotifyService notifyService,
	IPermissionService permissionService,
	IDidItService didItService,
	IConnectionService connectionService,
	IRealityPolicy reality,
	ILocalizationService localizationService) : ILookService
{
	public async ValueTask<CallState> LookRoom(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnyOptionalSharpObject viewing,
		LookKey key,
		bool lookOutside = false)
	{
		// look_room (look.c:461): NOTHING is shown nothing.
		if (viewing is not AnySharpObject realViewing)
		{
			return CallState.Empty;
		}

		// Deviation from PennMUSH: the reality layer has no Penn counterpart. A room the looker
		// cannot perceive answers as no match rather than as a room with nothing in it.
		if (!await reality.CanPerceiveAsync(looker.Object().DBRef, realViewing.Object().DBRef))
		{
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		var look = await BeginLookAsync(parser, looker, realViewing, key, lookOutside);

		var description = await ResolveDescriptionAsync(look);
		var formattedName = await FormatNameAsync(look);
		var formattedDesc = await FormatDescriptionAsync(look, description);

		await ShowNameAndDescriptionAsync(look, formattedName, formattedDesc);
		await RunDescribeTriadAsync(look, description);
		await RunRoomLockTriadAsync(look);
		await ShowContentsAndExitsAsync(look);
		await LookThroughExitAsync(look);

		return new CallState(look.ViewingObject.DBRef.ToString());
	}

	/// <summary>Everything one look decides up front about what it will show.</summary>
	private sealed record Look(
		IMUSHCodeParser Parser,
		AnySharpObject Looker,
		AnySharpObject Viewing,
		SharpObject ViewingObject,
		LookKey Key,
		AnySharpObject God,
		bool LookThroughExit,
		bool Terse,
		bool ShowDescription,
		bool ViewingFromInside,
		bool TryIdesc);

	/// <summary>
	/// The description a look shows before any format attribute: its text, the attribute it came from
	/// (none for the "nothing special" fallback), whether that was @idescribe, and whether it was the
	/// reality layer's own attribute.
	/// </summary>
	private readonly record struct Description(MString Base, string? AttributeName, bool UsedIdesc, bool Custom);

	private async ValueTask<Look> BeginLookAsync(
		IMUSHCodeParser parser,
		AnySharpObject looker,
		AnySharpObject viewing,
		LookKey key,
		bool lookOutside)
	{
		var viewingObject = viewing.Object();

		// LOOK_CLOUDYTRANS (externs.h:248) is the mask of both transparent-exit bits, and look.c:458
		// derives "am I looking through an exit" from either of them being set.
		var lookThroughExit = key.HasFlag(LookKey.Trans) || key.HasFlag(LookKey.Cloudy);

		// look.c:492 and look.c:503: an automatic look — the one a mover gets on arrival — shows a
		// Terse() looker no description at all: one whose owner is a TERSE player, or a TERSE thing
		// (dbdefs.h:91).
		var terse = key.HasFlag(LookKey.Auto) && await IsTerseAsync(looker);

		// look.c:492 for a container viewed from inside, look.c:503-504 for a room: LOOK_TRANS puts
		// the description back even when the look is coming through an exit.
		var showDescription = viewing.IsRoom
			? (!lookThroughExit && !terse) || key.HasFlag(LookKey.Trans)
			: !terse;

		var lookerLocation = looker is SharpRoom ? null : await looker.Where();
		var viewingFromInside = lookerLocation != null
			&& lookerLocation.Object().DBRef == viewingObject.DBRef;

		var god = await HelperFunctions.GetGod(mediator);

		// @idescribe is only used for players and things; rooms and exits always use @describe
		// (help @idescribe). Inside formats are discovered independently of @idescribe, however:
		// a present @idescformat formats the fallback @describe too.
		var tryIdesc = viewingFromInside && !lookOutside
			&& (viewing.IsPlayer || viewing.IsThing);

		return new Look(parser, looker, viewing, viewingObject, key, god,
			lookThroughExit, terse, showDescription, viewingFromInside, tryIdesc);
	}

	private async ValueTask<Description> ResolveDescriptionAsync(Look look)
	{
		var layerDescription = await LayerDescriptionAttributeAsync(look);
		var custom = layerDescription is not null;

		if (!look.ShowDescription)
		{
			return new Description(MarkupText.Empty, layerDescription, UsedIdesc: false, custom);
		}

		var description = custom
			? new Description(MarkupText.Empty, layerDescription, UsedIdesc: false, Custom: true)
			: await StoredDescriptionAsync(look);

		if (description.AttributeName is not { } attributeName)
		{
			return description;
		}

		var evaluated = await look.Parser.With(
			state => state with { Enactor = look.Looker.Object().DBRef },
			lookParser => attributeService.EvaluateAttributeFunctionAsync(
				lookParser, look.Looker, look.Viewing, attributeName,
				new Dictionary<string, CallState>(), evalParent: true, ignorePermissions: !custom));

		return description with { Base = evaluated };
	}

	/// <summary>
	/// Deviation from PennMUSH: a reality layer may name its own description attribute, which
	/// takes precedence over @idescribe and @describe alike. Unlike those it is read as the
	/// looker, so the attribute's own permissions still apply.
	/// </summary>
	private async ValueTask<string?> LayerDescriptionAttributeAsync(Look look)
	{
		var layerDescription = await reality.DescriptionAttributeAsync(look.Looker.Object().DBRef, look.ViewingObject.DBRef);
		if (layerDescription is null)
		{
			return null;
		}

		var layerAttribute = await attributeService.GetAttributeAsync(look.Looker, look.Viewing, layerDescription,
			IAttributeService.AttributeMode.Read, true);

		return layerAttribute is SharpAttribute[] layerChain
			&& await permissionService.CanExecuteAttribute(look.Looker, look.Viewing, layerChain)
				? layerDescription
				: null;
	}

	/// <summary>@idescribe from inside a player or thing when it has one, else @describe, else the fallback text.</summary>
	private async ValueTask<Description> StoredDescriptionAsync(Look look)
	{
		if (look.TryIdesc
				&& await attributeService.GetAttributeAsync(look.God, look.Viewing, "IDESCRIBE",
					IAttributeService.AttributeMode.Read, true) is SharpAttribute[] idescChain)
		{
			// A blank @idescribe is meaningful (help @idescribe suggests it to trigger
			// @aidescribe without text), so an empty value stays empty here.
			return new Description(idescChain.Last().Value, "IDESCRIBE", UsedIdesc: true, Custom: false);
		}

		return await attributeService.GetAttributeAsync(look.God, look.Viewing, "DESCRIBE",
				IAttributeService.AttributeMode.Read, true) is SharpAttribute[] descChain
			? new Description(descChain.Last().Value, "DESCRIBE", UsedIdesc: false, Custom: false)
			: new Description(MarkupText.Plain("You see nothing special."), null, UsedIdesc: false, Custom: false);
	}

	private async ValueTask<MString> FormatNameAsync(Look look)
	{
		// look.c:469-489: unparse_room, and so @nameformat with it, is skipped entirely when the look
		// arrives through a transparent exit.
		if (look.LookThroughExit)
		{
			return MarkupText.Empty;
		}

		var defaultFormattedName = await MessageFormatting.FormatObjectWithDbrefMString(look.ViewingObject,
			await FlagView.ForAsync(look.Looker, connectionService));

		if (!look.Viewing.IsRoom || !look.ViewingFromInside)
		{
			return defaultFormattedName;
		}

		var nameFormatArgs = new Dictionary<string, CallState>
		{
			["0"] = new CallState(look.ViewingObject.DBRef.ToString()),
			["1"] = new CallState(defaultFormattedName)
		};

		return await AttributeHelpers.EvaluateFormatAttribute(
			attributeService, look.Parser, look.Looker, look.Viewing, "NAMEFORMAT",
			nameFormatArgs, defaultFormattedName);
	}

	private async ValueTask<MString> FormatDescriptionAsync(Look look, Description description)
	{
		if (!look.ShowDescription)
		{
			return description.Base;
		}

		var formatAttrName = look.TryIdesc ? "IDESCFORMAT" : "DESCFORMAT";
		var formatAttribute = await attributeService.GetAttributeAsync(
			look.God, look.Viewing, formatAttrName, IAttributeService.AttributeMode.Read, true);

		if (look.TryIdesc && !description.UsedIdesc && formatAttribute.IsNone)
		{
			formatAttrName = "DESCFORMAT";
			formatAttribute = await attributeService.GetAttributeAsync(
				look.God, look.Viewing, formatAttrName, IAttributeService.AttributeMode.Read, true);
		}

		// As in AttributeHelpers.EvaluateFormatAttribute: after a tripped limit the format would show its code.
		if (!formatAttribute.IsAttribute || look.Parser.CurrentState.LimitExceeded?.IsExceeded == true)
		{
			return description.Base;
		}

		var descFormatArgs = new Dictionary<string, CallState>();
		if (description.AttributeName is not null)
		{
			descFormatArgs["0"] = new CallState(description.Base);
		}

		return await look.Parser.With(
			state => state with { Enactor = look.Looker.Object().DBRef },
			lookParser => attributeService.EvaluateAttributeFunctionAsync(
				lookParser, look.Looker, look.Viewing, formatAttrName,
				descFormatArgs, evalParent: true, ignorePermissions: true));
	}

	private async ValueTask ShowNameAndDescriptionAsync(Look look, MString formattedName, MString formattedDesc)
	{
		if (!look.LookThroughExit)
		{
			await notifyService.Notify(look.Looker, formattedName, look.Looker);
		}

		if (look.ShowDescription && formattedDesc.Length > 0)
		{
			await notifyService.Notify(look.Looker, formattedDesc, look.Looker);
		}
	}

	private async ValueTask RunDescribeTriadAsync(Look look, Description description)
	{
		// look.c:496 and look.c:507: the describe triad carries an o-message and an action, never a
		// message back to the looker — look_description has already shown that. The @idescformat and
		// @descformat fallbacks of the inside view run no triad at all.
		var oDescribeAttribute = look.TryIdesc ? description.UsedIdesc ? "OIDESCRIBE" : null : "ODESCRIBE";
		var aDescribeAttribute = look.TryIdesc ? description.UsedIdesc ? "AIDESCRIBE" : null : "ADESCRIBE";

		if (look.ShowDescription && oDescribeAttribute is not null)
		{
			await didItService.DidIt(look.Parser, new DidItRequest(
				Player: look.Looker,
				Thing: look.Viewing,
				OWhat: oDescribeAttribute,
				AWhat: aDescribeAttribute));
		}
	}

	private async ValueTask RunRoomLockTriadAsync(Look look)
	{
		if (!look.Viewing.IsRoom || look.LookThroughExit)
		{
			return;
		}

		// look.c:510-526: a terse automatic look gets only the o-message and the action of whichever
		// side of the basic lock it landed on; anyone else gets the whole triad, or fail_lock.
		var passes = await permissionService.PassesLock(look.Looker, look.Viewing, LockType.Basic);

		if (look.Terse)
		{
			await didItService.DidIt(look.Parser, new DidItRequest(
				Player: look.Looker,
				Thing: look.Viewing,
				OWhat: passes ? "OSUCCESS" : "OFAILURE",
				AWhat: passes ? "ASUCCESS" : "AFAILURE"));
		}
		else if (passes)
		{
			await didItService.DidIt(look.Parser, new DidItRequest(
				Player: look.Looker,
				Thing: look.Viewing,
				What: "SUCCESS",
				OWhat: "OSUCCESS",
				AWhat: "ASUCCESS"));
		}
		else
		{
			await didItService.FailLock(look.Parser, look.Looker, look.Viewing, LockType.Basic);
		}
	}

	private async ValueTask ShowContentsAndExitsAsync(Look look)
	{
		// look.c:528-530: LOOK_NOCONTENTS drops the contents, and so does a look through an exit that
		// is both cloudy and transparent.
		var showContents = !look.Key.HasFlag(LookKey.NoContents)
			&& !(look.Key.HasFlag(LookKey.Trans) && look.Key.HasFlag(LookKey.Cloudy));

		AnySharpContainer? container = look.Viewing switch
		{
			SharpPlayer player => player,
			SharpRoom room => room,
			SharpThing thing => thing,
			_ => null
		};

		// An opaque container shows no contents from outside.
		var showInventory = showContents
			&& container is not null
			&& !(await look.Viewing.IsOpaque());

		// look.c:531-533: look_exits is called independently of look_contents, gated only on the look
		// not arriving through an exit. LOOK_NOCONTENTS and an opaque container do not silence it.
		var showExits = look.Viewing.IsRoom && !look.LookThroughExit;

		if (container is null || !(showInventory || showExits))
		{
			return;
		}

		var (visibleContents, visibleExits) = await VisibleContentsAsync(look, container);

		if (showInventory && visibleContents.Count > 0)
		{
			await ShowContentsAsync(look, visibleContents);
		}

		if (showExits && visibleExits.Count > 0)
		{
			await ShowExitsAsync(look, visibleExits);
		}
	}

	/// <summary>What the looker can see in the container, split into exits and everything else.</summary>
	private async ValueTask<(List<AnySharpContent> Contents, List<AnySharpContent> Exits)> VisibleContentsAsync(
		Look look, AnySharpContainer container)
	{
		var allContents = mediator.CreateStream(new GetContentsQuery(container), ExecutionBudget.CurrentToken);

		var visibleContents = new List<AnySharpContent>();
		var visibleExits = new List<AnySharpContent>();

		var canSeeContent = await WorldVisibility.CreateScanAsync(
			look.Looker, look.Viewing, reality, connectionService, ExecutionBudget.CurrentToken);
		var lookerRef = look.Looker.Object().DBRef;
		// predicat.c:338-344 (can_see): "your own body isn't listed in a 'look'".
		var visible = allContents
			.Where(item => item.Object().DBRef != lookerRef)
			.Where((item, _) => canSeeContent(item, ExecutionBudget.CurrentToken));
		await foreach (var item in visible.WithCancellation(ExecutionBudget.CurrentToken))
		{
			if (item.IsExit) visibleExits.Add(item);
			else visibleContents.Add(item);
		}

		return (visibleContents, visibleExits);
	}

	private async ValueTask ShowContentsAsync(Look look, List<AnySharpContent> visibleContents)
	{
		var contentDbrefs = string.Join(" ", visibleContents.Select(x => $"#{x.Object().DBRef.Number}"));
		var contentNames = string.Join("|", visibleContents.Select(x => x.Object().Name));
		var contentsLabel = look.Viewing.IsRoom ? "Contents:" : "Carrying:";

		// PennMUSH: wizards/see_all see Name(#dbrefFlags), mortals see plain Name
		// The flag view is needed only for the Name(#dbrefFlags) form.
		var flagView = await look.Looker.IsSee_All() ? await FlagView.ForAsync(look.Looker, connectionService) : null;
		var contentMStrings = await Task.WhenAll(visibleContents.Select(async item =>
		{
			if (flagView is not null)
			{
				return await MessageFormatting.FormatObjectWithDbrefMString(item.Object(), flagView);
			}
			return MarkupText.Plain(item.Object().Name);
		}));
		var defaultContents = MarkupText.Join(MarkupText.NewLine, new[] { MarkupText.Plain(contentsLabel) }.Concat(contentMStrings));

		var conFormatArgs = new Dictionary<string, CallState>
		{
			["0"] = new CallState(contentDbrefs),
			["1"] = new CallState(contentNames)
		};

		var formattedContents = await AttributeHelpers.EvaluateFormatAttribute(
			attributeService, look.Parser, look.Looker, look.Viewing, "CONFORMAT",
			conFormatArgs, defaultContents);

		await notifyService.Notify(look.Looker, formattedContents, look.Looker);
	}

	private async ValueTask ShowExitsAsync(Look look, List<AnySharpContent> visibleExits)
	{
		var exitDbrefs = string.Join(" ", visibleExits.Select(x => $"#{x.Object().DBRef.Number}"));
		var exitFormatArgs = new Dictionary<string, CallState>
		{
			["0"] = new CallState(exitDbrefs)
		};

		var isTransparent = await look.Viewing.IsTransparent();
		var defaultExits = isTransparent
			? await TransparentExitListAsync(look.Looker, visibleExits)
			: MarkupText.Concat(MarkupText.Plain("Obvious exits:\n"),
				MessageFormatting.FormatMStringsWithOxfordComma(visibleExits.Select(x => ExitLink(x.Object().Name)).ToList()));

		var formattedExits = await AttributeHelpers.EvaluateFormatAttribute(
			attributeService, look.Parser, look.Looker, look.Viewing, "EXITFORMAT",
			exitFormatArgs, defaultExits);

		if (formattedExits == defaultExits && isTransparent)
		{
			await NotifyTransparentExitsAsync(look.Looker, visibleExits);
		}
		else
		{
			await notifyService.Notify(look.Looker, formattedExits, look.Looker);
		}
	}

	/// <summary>A transparent room's exits, one per line, each with where it leads unless it is opaque.</summary>
	private async ValueTask<MString> TransparentExitListAsync(AnySharpObject looker, List<AnySharpContent> visibleExits)
	{
		string? lookerLocale = null;
		var firstConnection = await connectionService.Get(looker.Object().DBRef).FirstOrDefaultAsync();
		firstConnection?.Metadata.TryGetValue("Locale", out lookerLocale);

		var exitParts = new List<MString>();
		foreach (var exit in visibleExits)
		{
			var destName = await DestinationNameAsync(exit);
			var exitMString = ExitLink(exit.Object().Name);

			exitParts.Add(await exit.WithRoomOption().IsOpaque()
				? exitMString
				: FormatExitNameToDestination(exitMString, destName, lookerLocale));
		}

		return MarkupText.Join(MarkupText.NewLine, exitParts);
	}

	/// <summary>Sends a transparent room's exits one line at a time, in the looker's own locale.</summary>
	private async ValueTask NotifyTransparentExitsAsync(AnySharpObject looker, List<AnySharpContent> visibleExits)
	{
		foreach (var exit in visibleExits)
		{
			var destName = await DestinationNameAsync(exit);
			var exitMString = ExitLink(exit.Object().Name);

			if (await exit.WithRoomOption().IsOpaque())
			{
				await notifyService.Notify(looker, exitMString, looker);
			}
			else
			{
				await notifyService.NotifyLocalizedMarkup(
					looker,
					nameof(ErrorMessages.Notifications.ExitNameToDestFormat),
					looker,
					exitMString,
					MarkupText.Plain(destName));
			}
		}
	}

	/// <summary>
	/// look_simple (look.c:430-440): an exit set TRANSPARENT or CLOUDY is looked through, after its
	/// own name and description, at the room it leads to. CLOUDY alone under LOOK_NOCONTENTS would
	/// show nothing there, so it is not looked at all.
	/// </summary>
	private async ValueTask LookThroughExitAsync(Look look)
	{
		if (look.Viewing is not SharpExit exit)
		{
			return;
		}

		var throughKey = look.Key;
		if (await look.Viewing.IsTransparent()) throughKey |= LookKey.Trans;
		if (await look.Viewing.IsCloudy()) throughKey |= LookKey.Cloudy;
		var through = throughKey & (LookKey.Trans | LookKey.Cloudy);

		if (through != 0
				&& (!throughKey.HasFlag(LookKey.NoContents) || through != LookKey.Cloudy)
				&& await ExitLookDestinationAsync(look.God, look.Looker, exit) is AnySharpContainer beyond)
		{
			await LookRoom(look.Parser, look.Looker, beyond.WithExitOption().WithNoneOption(), throughKey);
		}
	}

	/// <summary>
	/// Where look_simple looks through an exit (<c>look.c:437-440</c>): the looker's own home for an exit
	/// linked to HOME, the stored destination otherwise, and nowhere for a variable or unlinked exit.
	/// SharpMUSH records HOME and variable links in <c>_LINKTYPE</c> rather than in the destination.
	/// </summary>
	private async ValueTask<AnyOptionalSharpContainer> ExitLookDestinationAsync(
		AnySharpObject god, AnySharpObject looker, AnySharpContent exit)
	{
		var linkType = await attributeService.GetAttributeAsync(god, exit.WithRoomOption(), "_LINKTYPE",
			IAttributeService.AttributeMode.Read, false) is SharpAttribute[] { Length: > 0 } chain
			? chain[0].Value.ToPlainText().Trim().ToLowerInvariant()
			: string.Empty;

		return linkType switch
		{
			"variable" => new None(),
			"home" => looker.AsOptionalContent is AnySharpContent lookerContent ? await lookerContent.Home() : new None(),
			_ => await exit.Home()
		};
	}

	private static async ValueTask<string> DestinationNameAsync(AnySharpContent exit)
		=> exit is SharpExit linked && await linked.Home.WithCancellation(CancellationToken.None) is AnySharpContainer destination
			? destination.Object().Name
			: "*UNLINKED*";

	/// <summary>
	/// An exit name as a command link that walks through it. The renderer writes it in the client's own
	/// dialect — <c>&lt;A XCH_CMD&gt;</c> for Pueblo, <c>&lt;SEND HREF&gt;</c> for MXP, a clickable anchor
	/// in the portal — and as the bare name for everyone else. The first alias (before ';') is the
	/// command; every alias, pipe-delimited, is the hint, which BeipMU offers as a right-click menu.
	/// </summary>
	private static MString ExitLink(string exitName)
	{
		var aliases = exitName.Split(';');
		var command = aliases[0];
		var hint = aliases.Length > 1 ? string.Join("|", aliases) : $"Go {command}";
		return MarkupText.Wrap(
			AnsiMarkup.Create(linkUrl: command, linkKind: LinkKind.Command, linkText: hint),
			MarkupText.Plain(command));
	}

	private MString FormatExitNameToDestination(MString exitName, string destName, string? locale = null)
	{
		var template = localizationService.Get(nameof(ErrorMessages.Notifications.ExitNameToDestFormat), locale)
			?? ErrorMessages.Notifications.ExitNameToDestFormat;
		return MarkupTemplateFormatter.Format(template, exitName, MarkupText.Plain(destName));
	}

	/// <summary>dbdefs.h <c>Terse(x)</c>: the object's owner is a TERSE player, or it is itself a TERSE thing.</summary>
	private static async ValueTask<bool> IsTerseAsync(AnySharpObject looker)
	{
		if (looker.IsThing && await looker.HasFlag("TERSE"))
		{
			return true;
		}

		AnySharpObject owner = await looker.Object().Owner.WithCancellation(CancellationToken.None);
		return await owner.HasFlag("TERSE");
	}
}
