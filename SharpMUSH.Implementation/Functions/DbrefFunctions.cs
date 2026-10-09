using MoreLinq.Extensions;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Frozen;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";

	[SharpFunction(Name = "loc", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Location(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var locateResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0,
			LocateFlags.All);

		return locateResult switch
		{
			Error<CallState> error => error.Value,
			// fun_loc (src/fundb.c:1458) answers only for what the executor may locate.
			AnySharpObject found when !await PermissionService.CanLocate(executor, found)
				=> ErrorMessages.Returns.PermissionDenied,
			AnySharpObject and SharpPlayer player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
			AnySharpObject and SharpRoom room => await DropToAsync(room),
			AnySharpObject and SharpExit exit => await ExitLocation(exit),
			AnySharpObject and SharpThing thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef
		};

		async ValueTask<CallState> ExitLocation(SharpExit exit)
		{
			var linkTypeAttr = await AttributeService.GetAttributeAsync(executor, exit, AttrLinkType, IAttributeService.AttributeMode.Read, false);

			if (linkTypeAttr is SharpAttribute[] { Length: > 0 } linkType)
			{
				var linkTypeText = linkType[0].Value.ToPlainText();
				if (!string.IsNullOrEmpty(linkTypeText))
				{
					if (string.Equals(linkTypeText, LinkTypeVariable, StringComparison.OrdinalIgnoreCase))
					{
						return ErrorMessages.Returns.VariableDestination;
					}
					else if (string.Equals(linkTypeText, LinkTypeHome, StringComparison.OrdinalIgnoreCase))
					{
						return ErrorMessages.Returns.HomeDestination;
					}
				}
			}

			// PennMUSH fun_loc (fundb.c:1459) returns Location(it), which for an exit is where it
			// leads. The room it sits in is what where() reports.
			var destination = await exit.Home.WithCancellation(CancellationToken.None);

			return destination is AnySharpContainer found
				? found.Object().DBRef
				: ErrorMessages.Returns.NotLinked;
		}
	}

	/// <summary>A room's location is its drop-to, which is usually unset.</summary>
	private static async ValueTask<CallState> DropToAsync(SharpRoom room)
		=> (await room.Location.WithCancellation(CancellationToken.None)).Object()?.DBRef.ToString()
			 ?? ErrorMessages.Returns.NoDropTo;

	/// <summary>
	/// PennMUSH's <c>fun_lsearch</c> called as <c>CHILDREN</c> (<c>src/wiz.c</c>): <c>lsearch()</c> with
	/// <c>PARENT=&lt;object&gt;</c> and no owner, so a searcher without See_All or the Search power finds
	/// only children they own. The object must be a dbref or objid, as the PARENT class requires, or it
	/// is "Unknown parent." and <c>#-1</c>. Dbrefs, not objids, and "Nothing found." when there are none.
	/// </summary>
	[SharpFunction(Name = "children", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		if (!DBRef.TryParse(arg0, out var parentRef)
				|| (await Mediator.Send(new GetObjectNodeQuery(parentRef!.Value))).IsNone)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchUnknownParent), executor);
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		DBRef? owner = await executor.IsSee_All() || await executor.HasPower("Search")
			? null
			: (await executor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

		// A PARENT-only spec has no START or COUNT, so nothing can reject it.
		if (await SearchSpecEngine.ExecuteResultAsync(
					parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
					executor, owner, [new SearchSpecEngine.SearchPair("PARENT", arg0)], useRegex: false)
				is not SearchSpecEngine.SearchResult search)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		if (search.Matches.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SearchNothingFound), executor);
			return new CallState(string.Empty) { HadErrors = search.HadErrors };
		}

		return new CallState(string.Join(" ", search.Matches.Select(child => $"#{child.Key}"))) { HadErrors = search.HadErrors };
	}

	[SharpFunction(Name = "con", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkFirst(parser, WalkType.Contents);

	[SharpFunction(Name = "controls", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message.ToPlainText();

		var arg1Split = arg1.Split('/', 2);
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All);

		return maybeLocateObject switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject locateObject when isAttributeCheck => await ControlsAttribute(locateObject, arg1Split[0], arg1Split[1]),
			AnySharpObject locateObject => await ControlsVictim(locateObject)
		};

		async ValueTask<CallState> ControlsAttribute(AnySharpObject locateObject, string attributeObj, string attribute)
		{
			var maybeLocateAttributeObject = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				attributeObj,
				LocateFlags.All);

			return maybeLocateAttributeObject switch
			{
				Error<CallState> error => error.Value,
				AnySharpObject attributeObject => await ControlsAttributeOn(locateObject, attributeObject, attribute)
			};
		}

		async ValueTask<CallState> ControlsAttributeOn(AnySharpObject locateObject, AnySharpObject attributeObject, string attribute)
		{
			// can_edit_attr (attrib.c:414-421) looks only at the object's own attribute list; an attribute
			// it does not hold yet is judged as one about to be created, which comes down to control.
			var god = await HelperFunctions.GetGod(Mediator);
			var locateAttribute = await AttributeService.GetAttributeAsync(god, attributeObject, attribute,
				IAttributeService.AttributeMode.Read, parent: false);

			return locateAttribute switch
			{
				SharpAttribute[] foundAttribute => await PermissionService.Controls(locateObject, attributeObject, foundAttribute),
				None => await PermissionService.Controls(locateObject, attributeObject),
				Error<string> error => error.Value
			};
		}

		async ValueTask<CallState> ControlsVictim(AnySharpObject locateObject)
		{
			var maybeLocateVictim = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				arg1,
				LocateFlags.All);

			return maybeLocateVictim switch
			{
				Error<CallState> error => error.Value,
				AnySharpObject locateVictim => await PermissionService.Controls(locateObject, locateVictim)
			};
		}
	}

	/// <summary>An <c>entrances()</c> bound: a strict integer or a dbref (<c>src/wiz.c:1809-1812</c>).</summary>
	private static int? EntranceBound(string text)
		=> ArgHelpers.TryStrictInteger(text, out int number) ? number
			: HelperFunctions.ParseDbRef(text) is DBRef dbref ? dbref.Number
			: null;

	[SharpFunction(Name = "entrances", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var locArg))
		{
			var locStr = locArg.Message.ToPlainText();
			var maybeTarget = await LocateService.Locate(parser, executor, executor, locStr, LocateFlags.All);
			if (maybeTarget is not AnySharpObject located)
			{
				return new CallState(ErrorMessages.Returns.InvalidLocation);
			}
			target = located;
		}

		var typeFilter = "a";
		if (args.TryGetValue("1", out var typeArg))
		{
			typeFilter = typeArg.Message.ToPlainText()?.ToLower() ?? "a";
		}

		// fun_entrances takes each bound as a strict integer or a dbref and refuses anything else with
		// e_ints (src/wiz.c:1808-1827); a bound that names no object (negative here) falls back to the
		// whole database (:1828-1833).
		var beginFilter = 0;
		if (args.TryGetValue("2", out var beginArg))
		{
			if (EntranceBound(beginArg.Message.ToPlainText()) is not { } begin)
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}

			beginFilter = begin < 0 ? 0 : begin;
		}

		var endFilter = int.MaxValue;
		if (args.TryGetValue("3", out var endArg))
		{
			if (EntranceBound(endArg.Message.ToPlainText()) is not { } end)
			{
				return new CallState(ErrorMessages.Returns.Integers);
			}

			endFilter = end < 0 ? int.MaxValue : end;
		}

		var entrances = Mediator.CreateStream(new GetEntrancesQuery(target.Object().DBRef))
			.Select(AnySharpObject (exit) => exit)
			.Where(entrance =>
			{
				var dbrefNum = entrance.Object().DBRef.Number;

				if (dbrefNum < beginFilter || dbrefNum > endFilter)
				{
					return false;
				}

				// 'a' means all types
				return typeFilter.Contains('a')
					|| (typeFilter.Contains('e') && entrance.IsExit)
					|| (typeFilter.Contains('t') && entrance.IsThing)
					|| (typeFilter.Contains('p') && entrance.IsPlayer)
					|| (typeFilter.Contains('r') && entrance.IsRoom);
			})
			.Select(e => e.Object().DBRef.ToString());

		return new CallState(string.Join(" ", await entrances.ToArrayAsync()));
	}

	[SharpFunction(Name = "exit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkFirst(parser, WalkType.Exit);

	[SharpFunction(Name = "followers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Followers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				// PennMUSH's fun_followers reads the leader's own FOLLOWERS list, which add_follower and
				// del_follower keep beside each follower's FOLLOWING (MovementCommands does the same), in
				// the order they began following; it does not scan every object's FOLLOWING. Read as GOD,
				// as MovementCommands reads it: FOLLOWERS carries the wizard attribute flag.
				var followers = await AttributeService.GetAttributeAsync(
					await HelperFunctions.GetGod(Mediator), found, "FOLLOWERS",
					IAttributeService.AttributeMode.Read, parent: false);

				return followers is SharpAttribute[] chain
					? new CallState(string.Join(" ", chain.Last().Value.ToPlainText()
						.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
					: new CallState(string.Empty);
			});
	}

	[SharpFunction(Name = "following", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Following(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var followingAttr = await AttributeService.GetAttributeAsync(
					executor, found, "FOLLOWING", IAttributeService.AttributeMode.Read, false);

				if (followingAttr is SharpAttribute[] chain)
				{
					return new CallState(chain.Last().Value.ToPlainText());
				}

				return new CallState(string.Empty);
			});
	}

	[SharpFunction(Name = "home", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, arg0, LocateFlags.All);

		return locateResult switch
		{
			Error<CallState> error => error.Value,
			// fun_home (src/fundb.c:1670) answers only for what the executor may examine.
			AnySharpObject found when !await PermissionService.CanExamine(executor, found)
				=> ErrorMessages.Returns.PermissionDenied,
			AnySharpObject and SharpPlayer player => (await player.Home.WithCancellation(CancellationToken.None)).Object().DBRef,
			AnySharpObject and SharpRoom room => await DropToAsync(room),
			// PennMUSH fun_home (fundb.c:1672) returns Source() for an exit — the room it sits in.
			AnySharpObject and SharpExit exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
			AnySharpObject and SharpThing thing => (await thing.Home.WithCancellation(CancellationToken.None)).Object().DBRef
		};
	}

	/// <summary>Why a silent <see cref="ILocateService.Locate"/> found nothing.</summary>
	private static string LocateFailure(AnyOptionalSharpObjectOrError missing)
		=> missing is Error<string> error ? error.Value : ErrorMessages.Returns.NoMatch;

	[SharpFunction(Name = "localize", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse | FunctionFlags.Localize, ParameterNames = ["string"])]
	public async ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.FunctionParse(parser.CurrentState.Arguments["0"].Message) ?? CallState.Empty;

	[SharpFunction(Name = "locate", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "name", "type"])]
	public async ValueTask<CallState> Locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var lookerArg = args["0"].Message.ToPlainText();
		var nameArg = args["1"].Message.ToPlainText();
		var parametersArg = args["2"].Message.ToPlainText();

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeLooker =
			await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, lookerArg,
				LocateFlags.All);
		return maybeLooker switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject looker => await LocateAs(looker)
		};

		async ValueTask<CallState> LocateAs(AnySharpObject looker)
		{
			var (locateFlags, unknownSwitches) = ParseLocateParameters(parametersArg);

			// fun_locate notifies once per letter it does not recognise and carries on with the rest.
			foreach (var unknown in unknownSwitches)
			{
				await NotifyService.Notify(executor,
					string.Format(ErrorMessages.Notifications.LocateUnknownSwitchFormat, unknown));
			}

			// fun_locate: 's' is refused up front unless the executor controls the looker.
			if (locateFlags.HasFlag(LocateFlags.OnlyMatchLookerControlledObjects)
					&& !await PermissionService.Controls(executor, looker))
			{
				return ErrorMessages.Returns.PermissionDenied;
			}

			// fun_locate injects the default scope set *before* it gates, and the order is load-bearing: a
			// flags string naming no scope ('N' is one) picks up MAT_NEIGHBOR and friends here, and must
			// then clear the gate like any other relative-scope search. Gating on the flags as typed sees no
			// relative-scope bit and lets the call through.
			locateFlags = Library.Services.LocateService.ApplyDefaultScopes(locateFlags);

			// fun_locate's relative-scope gate, and it has to live here: it asks whether *executor* may
			// evaluate against *looker* (fundb.c), while the match below runs with looker as its own
			// permission subject. Folding both into one Locate call makes the gate ask Nearby(looker, looker),
			// which is always true — so a non-privileged executor could search a remote looker's neighbours.
			if ((locateFlags & Library.Services.LocateService.LookerRelativeScopes) != 0
					&& !await executor.IsSee_All()
					&& !await Library.Services.LocateService.Nearby(executor, looker)
					&& !await PermissionService.Controls(executor, looker))
			{
				return ErrorMessages.Returns.PermissionDenied;
			}

			// fun_locate passes `looker` as match_result's `who` as well as its `where`, so every
			// can_interact / controls / Long_Fingers / nearby question inside the match is asked about the
			// looker. The executor is the subject only of the gates that bracket the call — the 's' check
			// above, this one, and the visibility check below.
			var maybeFound = await LocateService.Locate(parser, looker, looker, nameArg, locateFlags);

			// fun_locate writes a bare #-1 or #-2 on every failure path. These say why instead, and keep
			// the prefix softcode tests for: #-2 still means ambiguous.
			if (maybeFound is not AnySharpObject found)
			{
				return LocateFailure(maybeFound);
			}

			// fun_locate's own visibility check, which match_result does not do and no other caller gets:
			//   loc = Location(item);
			//   if (GoodObject(loc)) Can_Examine(executor, loc)
			//                        || ((!DarkLegal(item) || Light(loc) || Light(item)) && can_interact(...))
			//   else                 (See_All(executor) || !DarkLegal(item) || Light(item)) && can_interact(...)
			// A room has no location to examine, which is the `else` — it was missing entirely, and asking
			// Can_Examine about the room itself is a different question with a different answer.
			// PennMUSH's Location(x) is db[x].location, which is none of the three helpers that look like it:
			// FriendlyWhereIs is match.c's `loc` and takes an exit's Source, Room() walks the chain to the
			// enclosing room, and a room's own dbref is not its location. For an exit db[x].location is
			// Destination(); for a room it is the drop-to, which is usually unset — and "unset" is exactly
			// what selects fun_locate's second arm, so this cannot be approximated by `found.IsRoom`.
			var loc = found switch
			{
				SharpPlayer player => (await player.Location.WithCancellation(CancellationToken.None)).WithNoneOption(),
				SharpRoom room => await room.Location.WithCancellation(CancellationToken.None),
				SharpExit exit => await exit.Home.WithCancellation(CancellationToken.None),
				SharpThing thing => (await thing.Location.WithCancellation(CancellationToken.None)).WithNoneOption()
			};

			// can_interact is the last term of both arms, so it is only asked once Can_Examine has declined
			// and the dark test has passed — as the else-if ordering has it. It can run softcode through an
			// @interact lock, so hoisting it out is neither free nor side-effect-free.
			bool visible;
			if (loc is not AnySharpContainer location)
			{
				visible = (await executor.IsSee_All() || !await found.IsDarkLegal() || await found.IsLight())
									&& await PermissionService.CanInteract(executor, found, IPermissionService.InteractType.See);
			}
			else
			{
				var container = location.WithExitOption();
				visible = await PermissionService.CanExamine(executor, container)
									|| ((!await found.IsDarkLegal() || await container.IsLight() || await found.IsLight())
											&& await PermissionService.CanInteract(executor, found, IPermissionService.InteractType.See));
			}

			// No post-hoc type filter: 'F' is MAT_TYPE, which the search itself honours now. Filtering the
			// winner afterwards could only ever turn a legitimate match into #-1 while the wrong-type
			// candidate had already displaced the right-type one during matching.
			// Something the executor may not see was not found, as far as they are concerned.
			return visible ? $"#{found.Object().DBRef.Number}" : ErrorMessages.Returns.NoMatch;
		}
	}

	/// <summary>
	/// fun_locate's switch, which is <b>case-sensitive</b>: uppercase letters name a preferred type or a
	/// modifier, lowercase ones name a place to search. Upper-casing the argument first — which this did
	/// — folded 'l' (match the location's name) into 'L' (prefer a lock pass), 'n' (the looker's
	/// neighbours) into 'N' (no type preference), and 'x' (no partial matches) into 'X' (take the last
	/// of an ambiguous set), so half the switches meant two things at once.
	/// </summary>
	private (LocateFlags Flags, IReadOnlyList<char> Unknown) ParseLocateParameters(string parameters)
	{
		var flags = default(LocateFlags);
		List<char>? unknown = null;

		foreach (var c in parameters)
		{
			// ' ' is skipped rather than reported; every other unrecognised letter is fun_locate's
			// "I don't understand switch '%c'.".
			if (LocateSwitches.TryGetValue(c, out var flag)) flags |= flag;
			else if (c != ' ') (unknown ??= []).Add(c);
		}

		// NOTYPE is the absence of a preference, not a flag anyone sets alongside one.
		if (Library.Services.LocateService.PreferredTypes(flags) == SharpObjectTypes.None)
			flags |= LocateFlags.NoTypePreference;

		return (flags, (IReadOnlyList<char>?)unknown ?? []);
	}

	/// <summary><c>fun_locate</c>'s switch letters (src/fundb.c), in its order, to the match flags each adds.</summary>
	private static readonly FrozenDictionary<char, LocateFlags> LocateSwitches = new Dictionary<char, LocateFlags>
	{
		['N'] = LocateFlags.NoTypePreference,
		['E'] = LocateFlags.ExitsPreference,
		['P'] = LocateFlags.PlayersPreference,
		['R'] = LocateFlags.RoomsPreference,
		['T'] = LocateFlags.ThingsPreference,
		['L'] = LocateFlags.PreferLockPass,
		['F'] = LocateFlags.OnlyMatchTypePreference,
		['X'] = LocateFlags.UseLastIfAmbiguous,
		['*'] = LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName | LocateFlags.ExitsInsideOfLooker,
		['a'] = LocateFlags.AbsoluteMatch,
		['c'] = LocateFlags.ExitsInsideOfLooker,
		['e'] = LocateFlags.ExitsInTheRoomOfLooker,
		['h'] = LocateFlags.MatchHereForLookerLocation,
		['i'] = LocateFlags.MatchObjectsInLookerInventory,
		['l'] = LocateFlags.MatchAgainstLookerLocationName,
		['m'] = LocateFlags.MatchMeForLooker,
		['n'] = LocateFlags.MatchObjectsInLookerLocation,
		['y'] = LocateFlags.MatchOptionalWildCardForPlayerName,
		['p'] = LocateFlags.MatchWildCardForPlayerName,
		['z'] = LocateFlags.EnglishStyleMatching,
		['x'] = LocateFlags.NoPartialMatches,
		['s'] = LocateFlags.OnlyMatchLookerControlledObjects
	}.ToFrozenDictionary();

	[SharpFunction(Name = "lparent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListParents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var maybeLocate = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			LocateFlags.All);

		return maybeLocate switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject located => await ExaminableParents(located)
		};

		// PennMUSH's fun_lparent (src/fundb.c): the object itself, then each parent in turn for as long
		// as the executor can examine the object before it. Dbrefs, not objids.
		async ValueTask<CallState> ExaminableParents(AnySharpObject locate)
		{
			var list = new List<string> { $"#{locate.Object().DBRef.Number}" };

			while (await PermissionService.CanExamine(executor, locate)
						 && await locate.Object().Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent)
			{
				list.Add($"#{parent.Object().DBRef.Number}");
				locate = parent;
			}

			return string.Join(" ", list);
		}
	}

	[SharpFunction(Name = "lsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["player", "class=restriction..."])]
	public async ValueTask<CallState> ListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await ListSearchInternal(parser, _2, useRegex: false);
	}

	private async ValueTask<CallState> ListSearchInternal(IMUSHCodeParser parser, SharpFunctionAttribute _2, bool useRegex)
	{
		// Per PennMUSH documentation: comma-separated positional arguments, NOT equals syntax
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var classArg = args["0"].Message.ToPlainText();
		AnySharpObject? classObj = null;

		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (maybeClass is not AnySharpObject classFound)
			{
				return new CallState(ErrorMessages.Returns.InvalidClass);
			}
			classObj = classFound;
		}

		var pairs = new List<SearchSpecEngine.SearchPair>();
		for (int i = 1; i < args.Count; i += 2)
		{
			if (i + 1 >= args.Count)
			{
				break;
			}

			pairs.Add(new SearchSpecEngine.SearchPair(
				args[i.ToString()].Message.ToPlainText(),
				args[(i + 1).ToString()].Message.ToPlainText()));
		}

		Result<SearchSpecEngine.SearchResult> outcome;
		try
		{
			outcome = await SearchSpecEngine.ExecuteResultAsync(
				parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
				executor, classObj?.Object().DBRef, pairs, useRegex);
		}
		catch (System.Text.RegularExpressions.RegexParseException) when (useRegex)
		{
			// lsearchr()'s name pattern is a regular expression the provider compiles; one that does not
			// compile, or cannot finish a match in SoftcodeRegex.MatchTimeout, is an answer, not a crash.
			return new CallState(ErrorMessages.Returns.RegexpInvalid);
		}
		catch (System.Text.RegularExpressions.RegexMatchTimeoutException) when (useRegex)
		{
			return new CallState(ErrorMessages.Returns.RegexpTimeout);
		}

		return outcome switch
		{
			SearchSpecEngine.SearchResult search => Matched(search),
			Error<string> rejected => await Rejected(rejected.Value)
		};

		// fun_lsearch (src/wiz.c) writes each match with safe_dbref: plain #N, never an objid (#1409).
		static CallState Matched(SearchSpecEngine.SearchResult search)
			=> new(string.Join(" ", search.Matches.Select(obj => $"#{obj.Key}"))) { HadErrors = search.HadErrors };

		// fun_lsearch: fill_search_spec has told the searcher why, and the function returns #-1.
		async ValueTask<CallState> Rejected(string notification)
		{
			await NotifyService.NotifyLocalized(executor, notification, executor);
			return new CallState(ErrorMessages.Returns.Nothing);
		}
	}

	[SharpFunction(Name = "lsearchr", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["object", "class=restriction..."])]
	public async ValueTask<CallState> ListSearchRegex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var originalArgs = parser.CurrentState.Arguments;

		return await ListSearchInternal(parser, _2, useRegex: true);
	}

	[SharpFunction(Name = "namelist", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "attribute"])]
	public async ValueTask<CallState> NameList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var namelist = ArgHelpers.NameList(parser.CurrentState.Arguments["0"].Message.ToPlainText());
		var callback = await NameListCallbackAsync(parser, executor);

		var resultList = new List<string>();
		var hadErrors = false;

		foreach (var item in namelist)
		{
			switch (await NameListMatchAsync(parser, executor, item))
			{
				case DBRef resolved:
					resultList.Add($"#{resolved.Number}");
					break;
				case NameListMiss miss:
					resultList.Add($"#{miss.Code}");
					hadErrors |= callback is NameListCallback onMiss
						&& await NameListCallbackHadErrorsAsync(parser, executor, onMiss, miss);
					break;
			}
		}

		return new CallState(string.Join(" ", resultList)) { HadErrors = hadErrors };
	}

	/// <summary>A namelist() entry that matched nothing: its error code (-1 not found, -2 ambiguous) and how it was written.</summary>
	private sealed record NameListMiss(int Code, string Name);

	/// <summary>The attribute namelist() calls for each entry that matched nothing.</summary>
	private sealed record NameListCallback(AnySharpObject Object, string Attribute);

	/// <summary>A namelist() entry's dbref, or why it has none.</summary>
	private union NameListMatch(DBRef, NameListMiss);

	/// <summary>
	/// namelist()'s second argument: <c>object/attribute</c>, or just an attribute on the executor. An
	/// object that cannot be found means no callback at all.
	/// </summary>
	private async ValueTask<Found<NameListCallback>> NameListCallbackAsync(IMUSHCodeParser parser, AnySharpObject executor)
	{
		var arguments = parser.CurrentState.Arguments;
		if (arguments.Count <= 1 || string.IsNullOrWhiteSpace(arguments["1"].Message.ToPlainText()))
		{
			return new NotFound();
		}

		var callbackSpec = arguments["1"].Message.ToPlainText();
		var slashIndex = callbackSpec.LastIndexOf('/');

		if (slashIndex <= 0)
		{
			// Format: just attribute (use executor as object)
			return new NameListCallback(executor, callbackSpec);
		}

		// Format: object/attribute - Use Span to avoid substring allocations
		var specSpan = callbackSpec.AsSpan();
		var objPart = specSpan.Slice(0, slashIndex).ToString();
		var attrPart = specSpan.Slice(slashIndex + 1).ToString();

		return await LocateService.Locate(parser, executor, executor, objPart, LocateFlags.All) is AnySharpObject callbackFound
			? new NameListCallback(callbackFound, attrPart)
			: new NotFound();
	}

	private async ValueTask<NameListMatch> NameListMatchAsync(IMUSHCodeParser parser, AnySharpObject executor,
		DbRefOrName item)
		=> item switch
		{
			DBRef dbref => (await Mediator.Send(new GetObjectNodeQuery(dbref))).IsNone
				? new NameListMiss(-1, $"#{dbref.Number}")
				: dbref,
			string name => await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) switch
			{
				AnySharpObject located => located.Object().DBRef,
				Error<string> error when error.Value.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)
					|| error.Value.Contains("#-2") => new NameListMiss(-2, name),
				_ => new NameListMiss(-1, name)
			}
		};

	/// <summary>Calls namelist()'s callback with the entry as written (%0) and its error code (%1).</summary>
	private async ValueTask<bool> NameListCallbackHadErrorsAsync(IMUSHCodeParser parser, AnySharpObject executor,
		NameListCallback callback, NameListMiss miss)
	{
		var callbackResult = await AttributeService.EvaluateAttributeFunctionResultAsync(
			parser, executor, callback.Object, callback.Attribute,
			new Dictionary<string, CallState>
			{
				["0"] = new(MarkupText.Plain(miss.Name)),
				["1"] = new(MarkupText.Plain($"#{miss.Code}"))
			});
		return callbackResult.HadErrors;
	}

	[SharpFunction(Name = "nchildren", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg1 = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg1, LocateFlags.All,
			async x => await Mediator.Send(new GetChildCountQuery(x.Object().DBRef)));
	}

	[SharpFunction(Name = "next", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkNext(parser);

	[SharpFunction(Name = "nextdbref", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> NextDbReference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// One past the highest key, or #0 for an empty database.
		var maxKey = await Mediator.Send(new GetHighestDbrefQuery()) switch
		{
			int highest => highest,
			NotFound => -1
		};

		// The next dbref with timestamp 0 (set when created)
		return new CallState($"#{maxKey + 1}:0");
	}

	[SharpFunction(Name = "nlsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	[SharpFunction(Name = "nsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	public async ValueTask<CallState> NumberOfListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var result = await ListSearch(parser, _2);
		var resultStr = result.Message.ToPlainText();

		if (resultStr.StartsWith("#-1"))
		{
			return result;
		}

		var count = string.IsNullOrWhiteSpace(resultStr)
			? 0
			: resultStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		return new CallState(count);
	}

	[SharpFunction(Name = "num", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Number(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			found =>
				ValueTask.FromResult<CallState>($"#{found.Object().DBRef.Number}"));
	}

	[SharpFunction(Name = "numversion", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> NumVersion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Format: YYYYMMDDHHMMSS (like PennMUSH)
		return ValueTask.FromResult<CallState>("20250102000000");
	}

	/// <summary>
	/// PennMUSH's <c>fun_parent</c> (<c>src/fundb.c</c>). With a second argument it first does what
	/// <c>@parent &lt;object&gt;=&lt;parent&gt;</c> does, notices included. Either way it then returns the
	/// object's parent as a dbref, <c>#-1</c> for none, to anyone who can examine the object.
	/// </summary>
	[SharpFunction(Name = "parent", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object", "parent"])]
	public async ValueTask<CallState> Parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message.ToPlainText();

		if (args.TryGetValue("1", out var newParentArg))
		{
			await SetParentAsync(newParentArg.Message.ToPlainText());
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg0, LocateFlags.All,
			async found =>
			{
				if (!await PermissionService.CanExamine(executor, found))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				var parent = await found.Object().Parent.WithCancellation(CancellationToken.None);
				return parent is AnySharpObject known ? $"#{known.Object().DBRef.Number}" : ErrorMessages.Returns.Nothing;
			});

		// PennMUSH's do_parent (src/set.c), which fun_parent calls: it reports every failure itself.
		async ValueTask SetParentAsync(string newParentName)
		{
			if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All)
					is not AnySharpObject target)
			{
				return;
			}

			AnySharpObject? newParent = null;
			if (newParentName.Length > 0 && !newParentName.Equals("none", StringComparison.OrdinalIgnoreCase))
			{
				if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, newParentName, LocateFlags.All)
						is not AnySharpObject found)
				{
					return;
				}

				newParent = found;
			}

			if (!await PermissionService.Controls(executor, target))
			{
				await NotifyService.Notify(executor, ErrorMessages.Notifications.PermissionDenied);
				return;
			}

			if (newParent is null)
			{
				await ObjectRelationshipService.UnsetParent(executor, target, true);
			}
			else
			{
				await ObjectRelationshipService.SetParent(executor, target, newParent, true);
			}
		}
	}

	[SharpFunction(Name = "pmatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			x => ValueTask.FromResult<CallState>(x.Object.DBRef));
	}

	[SharpFunction(Name = "rloc", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "levels"])]
	public async ValueTask<CallState> RecursiveLocation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var levelsArg = parser.CurrentState.Arguments["1"].Message.ToPlainText();

		// fun_rloc (src/fundb.c:1563-1572) reads the depth with parse_integer, which never fails, and
		// clamps it to 0..20: a depth that is not a number, or is negative, climbs no levels at all.
		var levels = Math.Clamp(ArgHelpers.ParseInteger(levelsArg), 0, 20);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				if (!await PermissionService.CanLocate(executor, found))
				{
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				var current = found;
				for (var i = 0; i < levels; i++)
				{
					// The climb stops at a room and answers it (src/fundb.c:1580).
					if (current.AsOptionalContent is not AnySharpContent content)
					{
						break;
					}

					// An exit's location is its source room.
					var location = await content.Location();
					current = location.WithExitOption();
				}

				return new CallState(current.Object().DBRef);
			});
	}

	[SharpFunction(Name = "room", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Room(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			LocateFlags.All,
			async x =>
			{
				// fun_room (src/fundb.c:1550).
				if (!await PermissionService.CanLocate(executor, x))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				var room = await LocateService.Room(x);
				return room.Object().DBRef;
			});
	}

	[SharpFunction(Name = "where", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Where(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message.ToPlainText(),
			LocateFlags.All,
			// fun_where (src/fundb.c:1537).
			async x => !await PermissionService.CanLocate(executor, x) ? ErrorMessages.Returns.PermissionDenied : x switch
			{
				SharpPlayer player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
				SharpRoom => ErrorMessages.Returns.ThisIsARoom,
				// For exits, return the location (the room containing the exit)
				SharpExit exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
				SharpThing thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString()
			});
	}

	[SharpFunction(Name = "zone", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<CallState> Zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message.ToPlainText();
		var hasArg1 = args.TryGetValue("1", out var arg1Value);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async target =>
			{
				if (!await PermissionService.CanExamine(executor, target))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				return hasArg1
					? await ChangeZoneAsync(parser, executor, target, arg1Value!.Message.ToPlainText())
					: await CurrentZoneAsync(target);
			});
	}

	/// <summary>zone() with one argument: the object's zone, queried fresh from the database.</summary>
	private async ValueTask<CallState> CurrentZoneAsync(AnySharpObject target)
	{
		// query fresh from database
		var freshTarget = await Mediator.Send(new GetObjectNodeQuery(target.Object().DBRef));
		return freshTarget is AnySharpObject fresh
					&& await fresh.Object().Zone.WithCancellation(CancellationToken.None) is AnySharpObject zoneObj
			? zoneObj.Object().DBRef.ToString()
			: ErrorMessages.Returns.NoZoneSet;
	}

	/// <summary>zone() with two arguments: <c>none</c> clears the object's zone, anything else is the new zone.</summary>
	private async ValueTask<CallState> ChangeZoneAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject target, string newZone)
	{
		if (newZone.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			if (!await PermissionService.Controls(executor, target))
			{
				return ErrorMessages.Returns.PermissionDenied;
			}

			await Mediator.Send(new UnsetObjectZoneCommand(target));
			return string.Empty;
		}

		var maybeZone = await LocateService.Locate(parser, executor, executor, newZone, LocateFlags.All);
		if (maybeZone is not AnySharpObject zone)
		{
			return ErrorMessages.Returns.InvalidZone;
		}

		// Check permissions - must control both object and zone, or pass ChZone lock
		if (!await PermissionService.Controls(executor, target))
		{
			return ErrorMessages.Returns.PermissionDenied;
		}

		bool canZone = await PermissionService.Controls(executor, zone);
		if (!canZone && !await LockService.Evaluate(LockType.ChZone, zone, executor))
		{
			return ErrorMessages.Returns.PermissionDenied;
		}

		if (await RelationshipCycles.SafeToAddZoneAsync(target, zone) is not RelationshipSafety.Safe)
		{
			return ErrorMessages.Returns.ZoneLoop;
		}

		// do_chzone's reset with no /preserve, which zone() cannot ask for (src/set.c:467-481).
		if (!target.IsPlayer)
		{
			await PrivilegeHelpers.StripPrivilegeAsync(Mediator, FlagAndPowerService, executor, target);
		}

		await Mediator.Send(new SetObjectZoneCommand(target, zone));
		return string.Empty;
	}

	[SharpFunction(Name = "xthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Thing, skipDark: false);

	[SharpFunction(Name = "xvcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Contents, skipDark: true);

	[SharpFunction(Name = "xvexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Exit, skipDark: true);

	[SharpFunction(Name = "xvplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Player, skipDark: true);

	[SharpFunction(Name = "xvthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Thing, skipDark: true);

	[SharpFunction(Name = "xcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Contents, skipDark: false);

	[SharpFunction(Name = "xexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Exit, skipDark: false);

	[SharpFunction(Name = "xplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkWindow(parser, WalkType.Player, skipDark: false);

	[SharpFunction(Name = "lcon", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkContentsWithFilter(parser);

	[SharpFunction(Name = "lexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Exit, skipDark: false);

	[SharpFunction(Name = "lplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Player, skipDark: false);

	[SharpFunction(Name = "lthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Thing, skipDark: false);

	[SharpFunction(Name = "lvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Contents, skipDark: true);

	[SharpFunction(Name = "lvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Exit, skipDark: true);

	[SharpFunction(Name = "lvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Player, skipDark: true);

	[SharpFunction(Name = "lvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkList(parser, WalkType.Thing, skipDark: true);

	/// <summary>
	/// Checks a single-letter flag string (like "Wc!P") against an object.
	/// Each character is a flag symbol. Preceding '!' negates. 'P','R','T','E' check type.
	/// Returns null if the string has invalid syntax (e.g. trailing '!').
	/// orMode=false → AND (all must match). orMode=true → OR (any must match).
	/// </summary>
	private async ValueTask<bool?> FlagLetterCheck(AnySharpObject obj, string flagStr, bool orMode)
	{
		// Both read on the first letter that needs them: a list of type letters and 'c' reads neither.
		List<SharpObjectFlag>? allFlags = null;
		ObjectFlagSet? objectFlags = null;

		var ret = !orMode; // AND starts true, OR starts false
		int i = 0;
		while (i < flagStr.Length)
		{
			bool negate = false;
			if (flagStr[i] == '!')
			{
				negate = true;
				i++;
				if (i >= flagStr.Length)
					return null; // Trailing '!'
			}

			var c = flagStr[i];
			i++;

			if (c is 'P' or 'R' or 'T' or 'E')
			{
				bool typeMatch = c switch
				{
					'P' => obj.IsPlayer,
					'R' => obj.IsRoom,
					'T' => obj.IsThing,
					'E' => obj.IsExit,
					_ => false
				};
				bool effectiveMatch = negate ? !typeMatch : typeMatch;
				if (orMode)
				{
					if (effectiveMatch) return true;
				}
				else
				{
					if (!effectiveMatch) return false;
				}
				continue;
			}

			// Special pseudo-flag: 'c' = CONNECTED (runtime state, not stored flag; portal-only sessions
			// don't count — see IConnectionService.IsOnline)
			if (c == 'c')
			{
				bool connected = await ConnectionService.IsOnline(obj);
				bool effectiveConn = negate ? !connected : connected;
				if (orMode)
				{
					if (effectiveConn) return true;
				}
				else
				{
					if (!effectiveConn) return false;
				}
				continue;
			}

			// Look up flag by symbol (case-sensitive in PennMUSH). Letters are shared across types (A is
			// ABODE on a room and ANSI on a player, x CLOUDY on an exit and TERSE on a thing), so Penn's
			// letter_to_flagptr takes the flag whose type covers the object's.
			var type = obj.Object().Type;
			allFlags ??= await Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();
			var flagDef = allFlags.FirstOrDefault(f => f.Symbol == c.ToString()
				&& (f.TypeRestrictions.Length == 0 || f.TypeRestrictions.Contains(type, StringComparer.OrdinalIgnoreCase)));
			if (flagDef == null)
			{
				// For AND: unknown required flag → false; negated unknown → true (not set)
				// For OR: unknown with negate → true; unknown without → false
				bool effectiveOnUnknown = negate; // !unknown = "not set" = true
				if (orMode)
				{
					if (effectiveOnUnknown) return true;
				}
				else
				{
					if (!effectiveOnUnknown) return false;
				}
				continue;
			}

			objectFlags ??= await obj.ReadFlagsAsync();
			bool hasIt = objectFlags.Has(flagDef.Name);
			bool effective = negate ? !hasIt : hasIt;
			if (orMode)
			{
				if (effective) return true;
			}
			else
			{
				if (!effective) return false;
			}
		}
		return ret;
	}

	/// <summary>
	/// Checks space-separated flag or power names like "wizard !puppet connected" against an object,
	/// as PennMUSH's <c>flaglist_check_long</c> (<c>src/flags.c</c>) does for andlflags/orlflags and,
	/// over the POWER namespace, andlpowers/orlpowers. Returns null for invalid syntax: an empty list,
	/// or a '!' with no name after it ("! puppet").
	/// </summary>
	private async ValueTask<bool?> FlagLongNameCheck(AnySharpObject obj, string list, bool orMode, bool powers)
	{
		var tokens = list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
			return null;

		var ret = !orMode;
		ObjectFlagSet? objectFlags = null;
		foreach (var token in tokens)
		{
			bool negate = token.StartsWith('!');
			var name = negate ? token[1..] : token;

			if (string.IsNullOrEmpty(name))
				return null;

			bool hasIt = powers
				? await obj.HasPower(name)
				: name.ToUpperInvariant() switch
				{
					"PLAYER" => obj.IsPlayer,
					"ROOM" => obj.IsRoom,
					"THING" => obj.IsThing,
					"EXIT" => obj.IsExit,
					"CONNECTED" => await ConnectionService.IsOnline(obj),
					_ => (objectFlags ??= await obj.ReadFlagsAsync()).Has(name)
				};

			bool effective = negate ? !hasIt : hasIt;
			if (orMode)
			{
				if (effective) return true;
			}
			else
			{
				if (!effective) return false;
			}
		}
		return ret;
	}

	private enum FlagListForm { Letters, Names, Powers }

	/// <summary>
	/// The one body behind orflags/andflags, orlflags/andlflags and orlpowers/andlpowers: one object and
	/// a flag list (PennMUSH <c>fun_orflags</c> … <c>fun_andlflags</c>, <c>src/fundb.c</c>; ORLPOWERS and
	/// ANDLPOWERS are <c>fun_orlflags</c>/<c>fun_andlflags</c> called by another name). A malformed list
	/// is <c>#-1 INVALID FLAG</c>, or <c>#-1 INVALID POWER</c> for the power forms.
	/// </summary>
	private async ValueTask<CallState> FlagListFunction(IMUSHCodeParser parser, FlagListForm form, bool orMode)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var listArg = parser.CurrentState.Arguments["1"].Message.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var result = form == FlagListForm.Letters
					? await FlagLetterCheck(found, listArg, orMode)
					: await FlagLongNameCheck(found, listArg, orMode, powers: form == FlagListForm.Powers);
				return result is { } matched
					? new CallState(matched)
					: new CallState(form == FlagListForm.Powers
						? ErrorMessages.Returns.InvalidPower
						: ErrorMessages.Returns.InvalidFlag);
			});
	}

	[SharpFunction(Name = "orflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public ValueTask<CallState> OrFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Letters, orMode: true);

	[SharpFunction(Name = "orlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public ValueTask<CallState> OrListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Names, orMode: true);

	[SharpFunction(Name = "orlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public ValueTask<CallState> OrListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Powers, orMode: true);

	[SharpFunction(Name = "andflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public ValueTask<CallState> AndFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Letters, orMode: false);

	[SharpFunction(Name = "andlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public ValueTask<CallState> AndListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Names, orMode: false);

	[SharpFunction(Name = "andlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public ValueTask<CallState> AndListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> FlagListFunction(parser, FlagListForm.Powers, orMode: false);

	[SharpFunction(Name = "ncon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Contents, skipDark: false);

	[SharpFunction(Name = "nexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Exit, skipDark: false);

	[SharpFunction(Name = "nplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Player, skipDark: false);

	[SharpFunction(Name = "nthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Thing, skipDark: false);

	[SharpFunction(Name = "nvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Contents, skipDark: true);

	[SharpFunction(Name = "nvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Exit, skipDark: true);

	[SharpFunction(Name = "nvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Player, skipDark: true);

	[SharpFunction(Name = "nvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkCount(parser, WalkType.Thing, skipDark: true);

	[SharpFunction(Name = "zfind", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["zone", "flags"])]
	public async ValueTask<CallState> ZoneFind(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = args["0"].Message.ToPlainText();

		var maybeZone = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, arg0, LocateFlags.All);
		if (maybeZone is not AnySharpObject zone)
		{
			return new CallState(maybeZone is Error<string> error ? error.Value : ErrorMessages.Returns.NoMatch);
		}

		var hasSeeAll = await executor.IsSee_All();
		if (!hasSeeAll)
		{
			if (!await LockService.Evaluate(LockType.Zone, zone, executor))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}
		}

		var objectList = await Mediator.CreateStream(new GetObjectsByZoneQuery(zone))
			.Where(async (obj, _) =>
			{
				return await Mediator.Send(new GetObjectNodeQuery(new DBRef(obj.Key))) is AnySharpObject fullObj
					&& (hasSeeAll || await PermissionService.CanExamine(executor, fullObj));
			})
			.Select(obj => $"#{obj.Key}")
			.ToArrayAsync();

		var separator = args.TryGetValue("1", out var arg1Value) && arg1Value.Message.ToPlainText() is { } format
			&& !string.IsNullOrWhiteSpace(format)
				? format
				: " ";

		return new CallState(string.Join(separator, objectList));
	}
}