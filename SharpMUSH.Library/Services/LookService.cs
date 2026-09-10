using System.Net;
using Mediator;
using MarkupString;
using MarkupString.Html;
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
using static MarkupString.MStringInterpolation;

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
		if (viewing.IsNone())
		{
			return CallState.Empty;
		}

		var realViewing = viewing.Known;

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
		// TERSE player no description at all.
		var terse = key.HasFlag(LookKey.Auto) && await looker.HasFlag("TERSE");

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

		var baseName = viewingObject.Name;
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
			if (layerAttribute.IsAttribute
					&& await permissionService.CanExecuteAttribute(looker, realViewing, layerAttribute.AsAttribute))
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
				if (idescResult.IsAttribute)
				{
					// A blank @idescribe is meaningful (help @idescribe suggests it to trigger
					// @aidescribe without text), so an empty value stays empty here.
					usedIdesc = true;
					descriptionAttributeName = "IDESCRIBE";
					baseDesc = idescResult.AsAttribute.Last().Value;
				}
			}

			if (!usedIdesc && !customDescription)
			{
				var descResult = await attributeService.GetAttributeAsync(god, realViewing, "DESCRIBE",
					IAttributeService.AttributeMode.Read, true);
				if (descResult.IsAttribute)
				{
					descriptionAttributeName = "DESCRIBE";
					baseDesc = descResult.AsAttribute.Last().Value;
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
			var flags = await viewingObject.Flags.Value.ToArrayAsync();
			var flagStr = string.Join(string.Empty, flags.Select(x => x.Symbol));
			var defaultFormattedName = Format($"{baseName.Hilight()}(#{viewingObject.DBRef.Number}{flagStr})");

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
					nameFormatArgs, defaultFormattedName, checkParents: false);
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

			var canSeeAll = await looker.IsSee_All();

			var visibleContents = new List<AnySharpContent>();
			var visibleExits = new List<AnySharpContent>();

			var canSeeContent = await WorldVisibility.CreateScanAsync(
				looker, realViewing, reality, connectionService, ExecutionBudget.CurrentToken);
			await foreach (var item in allContents.WithCancellation(ExecutionBudget.CurrentToken))
			{
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
				var contentMStrings = await Task.WhenAll(visibleContents.Select(async item =>
				{
					if (canSeeAll)
					{
						return await MessageFormatting.FormatObjectWithDbrefMString(item.Object());
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
					conFormatArgs, defaultContents, checkParents: false);

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
						var destination = exit.IsExit
							? await exit.AsExit.Home.WithCancellation(CancellationToken.None)
							: new AnyOptionalSharpContainer(new OneOf.Types.None());
						var destName = destination.IsNone ? "*UNLINKED*" : destination.WithoutNone().Object().Name;

						var exitMString = WrapExitInSendTag(exitObj.Name);

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
					var exitMStrings = visibleExits.Select(x => WrapExitInSendTag(x.Object().Name)).ToList();
					defaultExits = MarkupText.Concat(MarkupText.Plain("Obvious exits:\n"), MessageFormatting.FormatMStringsWithOxfordComma(exitMStrings));
				}

				var formattedExits = await AttributeHelpers.EvaluateFormatAttribute(
					attributeService, parser, looker, realViewing, "EXITFORMAT",
					exitFormatArgs, defaultExits, checkParents: false);

				if (formattedExits == defaultExits && isTransparent)
				{
					foreach (var exit in visibleExits)
					{
						var exitObj = exit.WithRoomOption().Object();
						var destination = exit.IsExit
							? await exit.AsExit.Home.WithCancellation(CancellationToken.None)
							: new AnyOptionalSharpContainer(new OneOf.Types.None());
						var destName = destination.IsNone ? "*UNLINKED*" : destination.WithoutNone().Object().Name;

						var exitMString = WrapExitInSendTag(exitObj.Name);

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

		return new CallState(viewingObject.DBRef.ToString());
	}

	/// <summary>
	/// Wraps an exit name in a &lt;send&gt; HtmlMarkup tag for Pueblo/MXP clients.
	/// The first alias (before ';') is used as the href command.
	/// All aliases are pipe-delimited in the hint for right-click menus (BeipMU pattern).
	/// For ANSI clients, HtmlMarkup passes through as plain text (only the display name).
	/// </summary>
	private static MString WrapExitInSendTag(string exitName)
	{
		var aliases = exitName.Split(';');
		var displayName = aliases[0];
		var command = WebUtility.HtmlEncode(aliases[0]);
		var hint = aliases.Length > 1
			? WebUtility.HtmlEncode(string.Join("|", aliases))
			: $"Go {command}";
		var sendMarkup = HtmlMarkup.Create("send", $"href=\"{command}\" hint=\"{hint}\"");
		return MarkupText.Wrap(sendMarkup, MarkupText.Plain(displayName));
	}

	private MString FormatExitNameToDestination(MString exitName, string destName, string? locale = null)
	{
		var template = localizationService.Get(nameof(ErrorMessages.Notifications.ExitNameToDestFormat), locale)
			?? ErrorMessages.Notifications.ExitNameToDestFormat;
		return MarkupTemplateFormatter.Format(template, exitName, MarkupText.Plain(destName));
	}
}
