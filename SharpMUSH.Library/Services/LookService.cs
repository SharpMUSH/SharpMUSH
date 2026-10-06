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

		var viewingObject = realViewing.Object();

		// LOOK_CLOUDYTRANS (externs.h:248) is the mask of both transparent-exit bits, and look.c:458
		// derives "am I looking through an exit" from either of them being set.
		var lookThroughExit = key.HasFlag(LookKey.Trans) || key.HasFlag(LookKey.Cloudy);

		// look.c:492 and look.c:503: an automatic look — the one a mover gets on arrival — shows a
		// Terse() looker no description at all: one whose owner is a TERSE player, or a TERSE thing
		// (dbdefs.h:91).
		var terse = key.HasFlag(LookKey.Auto) && await IsTerseAsync(looker);

		// look.c:492 for a container viewed from inside, look.c:503-504 for a room: LOOK_TRANS puts
		// the description back even when the look is coming through an exit.
		var showDescription = realViewing.IsRoom
			? (!lookThroughExit && !terse) || key.HasFlag(LookKey.Trans)
			: !terse;

		var lookerLocation = looker.IsContent
			? await looker.AsContent.Location()
			: null;
		var viewingFromInside = lookerLocation != null
			&& lookerLocation.Object().DBRef == viewingObject.DBRef;

		var baseDesc = MarkupText.Empty;
		string? descriptionAttributeName = null;
		var god = await HelperFunctions.GetGod(mediator);
		var lookerEnactor = looker.Object().DBRef;

		// @idescribe is only used for players and things; rooms and exits always use @describe
		// (help @idescribe). Inside formats are discovered independently of @idescribe, however:
		// a present @idescformat formats the fallback @describe too.
		var tryIdesc = viewingFromInside && !lookOutside
			&& (realViewing.IsPlayer || realViewing.IsThing);
		var usedIdesc = false;

		// Deviation from PennMUSH: a reality layer may name its own description attribute, which
		// takes precedence over @idescribe and @describe alike. Unlike those it is read as the
		// looker, so the attribute's own permissions still apply.
		var customDescription = false;
		var layerDescription = await reality.DescriptionAttributeAsync(looker.Object().DBRef, viewingObject.DBRef);
		if (layerDescription is not null)
		{
			var layerAttribute = await attributeService.GetAttributeAsync(looker, realViewing, layerDescription,
				IAttributeService.AttributeMode.Read, true);
			if (layerAttribute is SharpAttribute[] layerChain
					&& await permissionService.CanExecuteAttribute(looker, realViewing, layerChain))
			{
				customDescription = true;
				descriptionAttributeName = layerDescription;
			}
		}

		if (showDescription)
		{
			if (tryIdesc && !customDescription)
			{
				var idescResult = await attributeService.GetAttributeAsync(god, realViewing, "IDESCRIBE",
					IAttributeService.AttributeMode.Read, true);
				if (idescResult is SharpAttribute[] idescChain)
				{
					// A blank @idescribe is meaningful (help @idescribe suggests it to trigger
					// @aidescribe without text), so an empty value stays empty here.
					usedIdesc = true;
					descriptionAttributeName = "IDESCRIBE";
					baseDesc = idescChain.Last().Value;
				}
			}

			if (!usedIdesc && !customDescription)
			{
				var descResult = await attributeService.GetAttributeAsync(god, realViewing, "DESCRIBE",
					IAttributeService.AttributeMode.Read, true);
				if (descResult is SharpAttribute[] descChain)
				{
					descriptionAttributeName = "DESCRIBE";
					baseDesc = descChain.Last().Value;
				}
				else
				{
					baseDesc = MarkupText.Plain("You see nothing special.");
				}
			}

			if (descriptionAttributeName is not null)
			{
				baseDesc = await parser.With(
					state => state with { Enactor = lookerEnactor },
					lookParser => attributeService.EvaluateAttributeFunctionAsync(
						lookParser, looker, realViewing, descriptionAttributeName,
						new Dictionary<string, CallState>(), evalParent: true, ignorePermissions: !customDescription));
			}
		}

		var formattedName = MarkupText.Empty;

		// look.c:469-489: unparse_room, and so @nameformat with it, is skipped entirely when the look
		// arrives through a transparent exit.
		if (!lookThroughExit)
		{
			var defaultFormattedName = await MessageFormatting.FormatObjectWithDbrefMString(viewingObject,
				await FlagView.ForAsync(looker, connectionService));

			formattedName = defaultFormattedName;
			if (realViewing.IsRoom && viewingFromInside)
			{
				var nameFormatArgs = new Dictionary<string, CallState>
				{
					["0"] = new CallState(viewingObject.DBRef.ToString()),
					["1"] = new CallState(defaultFormattedName)
				};

				formattedName = await AttributeHelpers.EvaluateFormatAttribute(
					attributeService, parser, looker, realViewing, "NAMEFORMAT",
					nameFormatArgs, defaultFormattedName, checkParents: true,
					ignorePermissions: true);
			}
		}

		var formattedDesc = baseDesc;

		if (showDescription)
		{
			var formatAttrName = tryIdesc ? "IDESCFORMAT" : "DESCFORMAT";
			var formatAttribute = await attributeService.GetAttributeAsync(
				god, realViewing, formatAttrName, IAttributeService.AttributeMode.Read, true);

			if (tryIdesc && !usedIdesc && formatAttribute.IsNone)
			{
				formatAttrName = "DESCFORMAT";
				formatAttribute = await attributeService.GetAttributeAsync(
					god, realViewing, formatAttrName, IAttributeService.AttributeMode.Read, true);
			}

			if (formatAttribute.IsAttribute)
			{
				var descFormatArgs = new Dictionary<string, CallState>();
				if (descriptionAttributeName is not null)
				{
					descFormatArgs["0"] = new CallState(baseDesc);
				}

				formattedDesc = await parser.With(
					state => state with { Enactor = lookerEnactor },
					lookParser => attributeService.EvaluateAttributeFunctionAsync(
						lookParser, looker, realViewing, formatAttrName,
						descFormatArgs, evalParent: true, ignorePermissions: true));
			}
		}

		if (!lookThroughExit)
		{
			await notifyService.Notify(looker, formattedName, looker);
		}

		if (showDescription && formattedDesc.Length > 0)
		{
			await notifyService.Notify(looker, formattedDesc, looker);
		}

		// look.c:496 and look.c:507: the describe triad carries an o-message and an action, never a
		// message back to the looker — look_description has already shown that. The @idescformat and
		// @descformat fallbacks of the inside view run no triad at all.
		var oDescribeAttribute = tryIdesc ? usedIdesc ? "OIDESCRIBE" : null : "ODESCRIBE";
		var aDescribeAttribute = tryIdesc ? usedIdesc ? "AIDESCRIBE" : null : "ADESCRIBE";

		if (showDescription && oDescribeAttribute is not null)
		{
			await didItService.DidIt(parser, new DidItRequest(
				Player: looker,
				Thing: realViewing,
				OWhat: oDescribeAttribute,
				AWhat: aDescribeAttribute));
		}

		// look.c:510-526: a terse automatic look gets only the o-message and the action of whichever
		// side of the basic lock it landed on; anyone else gets the whole triad, or fail_lock.
		if (realViewing.IsRoom && !lookThroughExit)
		{
			var passes = await permissionService.PassesLock(looker, realViewing, LockType.Basic);

			if (terse)
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: looker,
					Thing: realViewing,
					OWhat: passes ? "OSUCCESS" : "OFAILURE",
					AWhat: passes ? "ASUCCESS" : "AFAILURE"));
			}
			else if (passes)
			{
				await didItService.DidIt(parser, new DidItRequest(
					Player: looker,
					Thing: realViewing,
					What: "SUCCESS",
					OWhat: "OSUCCESS",
					AWhat: "ASUCCESS"));
			}
			else
			{
				await didItService.FailLock(parser, looker, realViewing, LockType.Basic);
			}
		}

		// look.c:528-530: LOOK_NOCONTENTS drops the contents, and so does a look through an exit that
		// is both cloudy and transparent.
		var showContents = !key.HasFlag(LookKey.NoContents)
			&& !(key.HasFlag(LookKey.Trans) && key.HasFlag(LookKey.Cloudy));

		// An opaque container shows no contents from outside.
		var showInventory = showContents
			&& realViewing.IsContainer
			&& !(await realViewing.IsOpaque());

		// look.c:531-533: look_exits is called independently of look_contents, gated only on the look
		// not arriving through an exit. LOOK_NOCONTENTS and an opaque container do not silence it.
		var showExits = realViewing.IsRoom && !lookThroughExit;

		if (realViewing.IsContainer && (showInventory || showExits))
		{
			var allContents = mediator.CreateStream(new GetContentsQuery(realViewing.AsContainer), ExecutionBudget.CurrentToken);

			var visibleContents = new List<AnySharpContent>();
			var visibleExits = new List<AnySharpContent>();

			var canSeeContent = await WorldVisibility.CreateScanAsync(
				looker, realViewing, reality, connectionService, ExecutionBudget.CurrentToken);
			var lookerRef = looker.Object().DBRef;
			await foreach (var item in allContents.WithCancellation(ExecutionBudget.CurrentToken))
			{
				// predicat.c:338-344 (can_see): "your own body isn't listed in a 'look'".
				if (item.Object().DBRef == lookerRef) continue;
				if (!await canSeeContent(item, ExecutionBudget.CurrentToken)) continue;
				if (item.IsExit) visibleExits.Add(item);
				else visibleContents.Add(item);
			}

			if (showInventory && visibleContents.Count > 0)
			{
				var contentDbrefs = string.Join(" ", visibleContents.Select(x => $"#{x.Object().DBRef.Number}"));
				var contentNames = string.Join("|", visibleContents.Select(x => x.Object().Name));
				var contentsLabel = realViewing.IsRoom ? "Contents:" : "Carrying:";

				// PennMUSH: wizards/see_all see Name(#dbrefFlags), mortals see plain Name
				// The flag view is needed only for the Name(#dbrefFlags) form.
				var flagView = await looker.IsSee_All() ? await FlagView.ForAsync(looker, connectionService) : null;
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
					attributeService, parser, looker, realViewing, "CONFORMAT",
					conFormatArgs, defaultContents, checkParents: true,
					ignorePermissions: true);

				await notifyService.Notify(looker, formattedContents, looker);
			}

			if (showExits && visibleExits.Count > 0)
			{
				var exitDbrefs = string.Join(" ", visibleExits.Select(x => $"#{x.Object().DBRef.Number}"));
				var exitFormatArgs = new Dictionary<string, CallState>
				{
					["0"] = new CallState(exitDbrefs)
				};

				var isTransparent = await realViewing.IsTransparent();
				string? lookerLocale = null;
				var firstConnection = await connectionService.Get(looker.Object().DBRef).FirstOrDefaultAsync();
				firstConnection?.Metadata.TryGetValue("Locale", out lookerLocale);

				MString defaultExits;
				if (isTransparent)
				{
					var exitParts = new List<MString>();
					foreach (var exit in visibleExits)
					{
						var exitObj = exit.WithRoomOption().Object();
						var destName = await DestinationNameAsync(exit);

						var exitMString = ExitLink(exitObj.Name);

						if (await exit.WithRoomOption().IsOpaque())
						{
							exitParts.Add(exitMString);
						}
						else
						{
							exitParts.Add(FormatExitNameToDestination(exitMString, destName, lookerLocale));
						}
					}
					defaultExits = MarkupText.Join(MarkupText.NewLine, exitParts);
				}
				else
				{
					var exitMStrings = visibleExits.Select(x => ExitLink(x.Object().Name)).ToList();
					defaultExits = MarkupText.Concat(MarkupText.Plain("Obvious exits:\n"), MessageFormatting.FormatMStringsWithOxfordComma(exitMStrings));
				}

				var formattedExits = await AttributeHelpers.EvaluateFormatAttribute(
					attributeService, parser, looker, realViewing, "EXITFORMAT",
					exitFormatArgs, defaultExits, checkParents: true,
					ignorePermissions: true);

				if (formattedExits == defaultExits && isTransparent)
				{
					foreach (var exit in visibleExits)
					{
						var exitObj = exit.WithRoomOption().Object();
						var destName = await DestinationNameAsync(exit);

						var exitMString = ExitLink(exitObj.Name);

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
				else
				{
					await notifyService.Notify(looker, formattedExits, looker);
				}
			}
		}

		// look_simple (look.c:430-440): an exit set TRANSPARENT or CLOUDY is looked through, after its
		// own name and description, at the room it leads to. CLOUDY alone under LOOK_NOCONTENTS would
		// show nothing there, so it is not looked at all.
		if (realViewing.IsExit)
		{
			var throughKey = key;
			if (await realViewing.IsTransparent()) throughKey |= LookKey.Trans;
			if (await realViewing.IsCloudy()) throughKey |= LookKey.Cloudy;
			var through = throughKey & (LookKey.Trans | LookKey.Cloudy);

			if (through != 0
					&& (!throughKey.HasFlag(LookKey.NoContents) || through != LookKey.Cloudy)
					&& await ExitLookDestinationAsync(god, looker, realViewing.AsContent) is AnySharpContainer beyond)
			{
				await LookRoom(parser, looker, beyond.WithExitOption().WithNoneOption(), throughKey);
			}
		}

		return new CallState(viewingObject.DBRef.ToString());
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
			"home" => looker.IsContent ? await looker.AsContent.Home() : new None(),
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
