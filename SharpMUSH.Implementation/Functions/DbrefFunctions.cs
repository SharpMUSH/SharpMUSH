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

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";

	[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Location(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var locateResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0,
			LocateFlags.All);

		if (locateResult.IsError)
		{
			return locateResult.AsError;
		}

		var found = locateResult.AsSharpObject;

		return await found.Match<ValueTask<CallState>>(
			async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
			async room =>
			{
				var location = await room.Location.WithCancellation(CancellationToken.None);
				return location.Match(
					player => player.Object.DBRef.ToString(),
					r => r.Object.DBRef.ToString(),
					thing => thing.Object.DBRef.ToString(),
					_ => "#-1");
			},
			async exit =>
			{
				var linkTypeAttr = await AttributeService.GetAttributeAsync(executor, exit, AttrLinkType, IAttributeService.AttributeMode.Read, false);

				if (linkTypeAttr.IsAttribute && linkTypeAttr.AsT0.Length > 0)
				{
					var linkTypeText = linkTypeAttr.AsT0[0].Value.ToPlainText();
					if (!string.IsNullOrEmpty(linkTypeText))
					{
						if (string.Equals(linkTypeText, LinkTypeVariable, StringComparison.OrdinalIgnoreCase))
						{
							return "#-2";
						}
						else if (string.Equals(linkTypeText, LinkTypeHome, StringComparison.OrdinalIgnoreCase))
						{
							return "#-3";
						}
					}
				}

				// PennMUSH fun_loc (fundb.c:1459) returns Location(it), which for an exit is where it
				// leads. The room it sits in is what where() reports.
				var destination = await exit.Home.WithCancellation(CancellationToken.None);

				return destination.IsNone
					? "#-1"
					: destination.WithoutNone().Object().DBRef;
			},
			async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef
		);
	}

	[SharpFunction(Name = "children", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				var children = locate.Object().Children.Value ?? AsyncEnumerable.Empty<SharpObject>();
				return string.Join(" ", await children.Select(x => x.DBRef.ToString()).ToArrayAsync());
			});
	}

	[SharpFunction(Name = "con", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkFirst(parser, WalkType.Contents);

	[SharpFunction(Name = "controls", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var arg1Split = arg1.Split('/', 2);
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All);

		if (maybeLocateObject.IsError)
		{
			return maybeLocateObject.AsError;
		}

		var locateObject = maybeLocateObject.AsSharpObject;

		if (isAttributeCheck)
		{
			var attributeObj = arg1Split[0];
			var attribute = arg1Split[1];
			var maybeLocateAttributeObject = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				attributeObj,
				LocateFlags.All);

			if (maybeLocateAttributeObject.IsError)
			{
				return maybeLocateAttributeObject.AsError;
			}

			var attributeObject = maybeLocateAttributeObject.AsSharpObject;

			var locateAttribute = await AttributeService.GetAttributeAsync(executor, attributeObject, attribute,
				IAttributeService.AttributeMode.Read);

			if (locateAttribute.IsError)
			{
				return locateAttribute.AsError.Value;
			}

			if (locateAttribute.IsNone)
			{
				return ErrorMessages.Returns.NotVisible;
			}

			var foundAttribute = locateAttribute.AsAttribute;

			var controlsAttribute = await PermissionService.Controls(locateObject, attributeObject, foundAttribute);

			return controlsAttribute;
		}

		var maybeLocateVictim = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg1,
			LocateFlags.All);

		if (maybeLocateVictim.IsError)
		{
			return maybeLocateVictim.AsError;
		}

		var locateVictim = maybeLocateVictim.AsSharpObject;

		var controls = await PermissionService.Controls(locateObject, locateVictim);

		return controls;
	}

	[SharpFunction(Name = "entrances", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var locArg))
		{
			var locStr = locArg.Message!.ToPlainText();
			var maybeTarget = await LocateService.Locate(parser, executor, executor, locStr, LocateFlags.All);
			if (!maybeTarget.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidLocation);
			}
			target = maybeTarget.AsAnyObject;
		}

		var typeFilter = "a";
		if (args.TryGetValue("1", out var typeArg))
		{
			typeFilter = typeArg.Message!.ToPlainText()?.ToLower() ?? "a";
		}

		var beginFilter = 0;
		if (args.TryGetValue("2", out var beginArg))
		{
			if (int.TryParse(beginArg.Message!.ToPlainText(), out var begin))
			{
				beginFilter = begin;
			}
		}

		var endFilter = int.MaxValue;
		if (args.TryGetValue("3", out var endArg))
		{
			if (int.TryParse(endArg.Message!.ToPlainText(), out var end))
			{
				endFilter = end;
			}
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
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var targetDbref = found.Object().DBRef.ToString();

				var followers = Mediator.CreateStream(new GetAllObjectsQuery())
					.Where(async (obj, _) => await obj.Attributes.Value
						.AnyAsync(attr => attr.LongName == "FOLLOWING" && attr.Value.Text == targetDbref))
					.Select(obj => obj.DBRef.ToString());

				return new CallState(string.Join(" ", await followers.ToArrayAsync()));
			});
	}

	[SharpFunction(Name = "following", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Following(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var followingAttr = await AttributeService.GetAttributeAsync(
					executor, found, "FOLLOWING", IAttributeService.AttributeMode.Read, false);

				if (followingAttr.IsAttribute)
				{
					return new CallState(followingAttr.AsAttribute.Last().Value.ToPlainText());
				}

				return new CallState(string.Empty);
			});
	}

	[SharpFunction(Name = "home", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var locateResult = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, arg0, LocateFlags.All);

		if (locateResult.IsError)
		{
			return locateResult.AsError;
		}

		var found = locateResult.AsSharpObject;

		return await found.Match<ValueTask<CallState>>(
			async player => (await player.Home.WithCancellation(CancellationToken.None)).Object().DBRef,
			async room =>
			{
				var location = await room.Location.WithCancellation(CancellationToken.None);
				return location.Match(
					player => player.Object.DBRef.ToString(),
					r => r.Object.DBRef.ToString(),
					thing => thing.Object.DBRef.ToString(),
					_ => "#-1");
			},
			// PennMUSH fun_home (fundb.c:1672) returns Source() for an exit — the room it sits in.
			async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef,
			async thing => (await thing.Home.WithCancellation(CancellationToken.None)).Object().DBRef
		);
	}

	[SharpFunction(Name = "llockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			var flags = LockService.LockPrivileges.Keys;
			return new CallState(string.Join(" ", flags));
		}

		var lockType = LockNames.Canonical(args["0"].Message!.ToPlainText());
		if (LockService.SystemLocks.TryGetValue(lockType, out var lockFlags))
		{
			return new CallState(string.Join(" ",
				LockFlagTable.Where(x => lockFlags.HasFlag(x.Flag)).Select(x => x.Name)));
		}

		return new CallState(string.Empty);
	}

	/// <summary>
	/// The lock flags in the order lockflags() and llockflags() list them, with the letter the one
	/// reports and the name the other does; the letters spell the "vncwol" lockflags() answers with no
	/// argument.
	/// </summary>
	private static readonly (Library.Services.LockService.LockFlags Flag, string Name, char Letter)[] LockFlagTable =
	[
		(Library.Services.LockService.LockFlags.Visual, "visual", 'v'),
		(Library.Services.LockService.LockFlags.Private, "no_inherit", 'n'),
		(Library.Services.LockService.LockFlags.NoClone, "no_clone", 'c'),
		(Library.Services.LockService.LockFlags.Wizard, "wizard", 'w'),
		(Library.Services.LockService.LockFlags.Owner, "owner", 'o'),
		(Library.Services.LockService.LockFlags.Locked, "locked", 'l')
	];

	[SharpFunction(Name = "lockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> LockFlagsObject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			// In PennMUSH: v=visual, n=no_inherit, c=no_clone, w=wizard, o=owner, l=locked
			return new CallState("vncwol");
		}

		var argStr = args["0"].Message!.ToPlainText();
		var parts = argStr.Split('/', 2);
		var objectRef = parts[0];
		var lockType = parts.Length > 1 ? parts[1] : "Basic";

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objectRef, LocateFlags.All,
			async found =>
			{
				if (!found.Object().Locks.TryGetValue(LockNames.Canonical(lockType), out var lockData))
				{
					return new CallState("#-1 NO SUCH LOCK");
				}

				// PennMUSH Can_Read_Lock permission check
				if (!await PermissionService.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1 NO SUCH LOCK");
				}

				return new CallState(new string([.. LockFlagTable.Where(x => lockData.Flags.HasFlag(x.Flag)).Select(x => x.Letter)]));
			});
	}

	[SharpFunction(Name = "elock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public async ValueTask<CallState> EvaluateLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH format: elock(<object>/<lock name>, <victim>)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var victimArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var victimResult = await LocateService.Locate(parser, executor, executor, victimArg, LocateFlags.All);
				if (!victimResult.IsValid())
				{
					return new CallState("#-1");
				}
				var victim = victimResult.AsAnyObject;

				// Lock names match case-insensitively per PennMUSH, and "teleport" has to find the
				// lock LockType spells TPort — both of which LockNames owns.
				if (!found.Object().Locks.TryGetValue(LockNames.Canonical(lockName), out var lockData))
				{
					// No lock set = passes (TRUE_BOOLEXP)
					return new CallState("1");
				}

				// PennMUSH Can_Read_Lock: See_All || controls || ((Visual || lock visual) && passes Examine lock)
				if (!await PermissionService.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1");
				}

				var result = await LockService.Evaluate(lockData.LockString, found, victim);
				return new CallState(result ? "1" : "0");
			});
	}

	[SharpFunction(Name = "llocks", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var objArg))
		{
			var objStr = objArg.Message!.ToPlainText();
			var maybeTarget = await LocateService.Locate(parser, executor, executor, objStr, LocateFlags.All);
			if (!maybeTarget.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidObject);
			}
			target = maybeTarget.AsAnyObject;
		}

		var lockNames = target.Object().Locks.Keys;
		return new CallState(string.Join(" ", lockNames));
	}

	[SharpFunction(Name = "locks", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> LocksRequired(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var maybeTarget = await LocateService.Locate(parser, executor, executor, objStr, LocateFlags.All);
		if (!maybeTarget.IsValid())
		{
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}
		var target = maybeTarget.AsAnyObject;

		var lockNames = target.Object().Locks.Keys;
		return new CallState(string.Join(" ", lockNames));
	}

	[SharpFunction(Name = "localize", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse | FunctionFlags.Localize, ParameterNames = ["string"])]
	public async ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser.FunctionParse(parser.CurrentState.Arguments["0"].Message!) ?? CallState.Empty;

	[SharpFunction(Name = "locate", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "name", "type"])]
	public async ValueTask<CallState> Locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var lookerArg = args["0"].Message!.ToPlainText();
		var nameArg = args["1"].Message!.ToPlainText();
		var parametersArg = args["2"].Message!.ToPlainText();

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var maybeLooker =
			await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, lookerArg,
				LocateFlags.All);
		if (maybeLooker.IsError)
		{
			return maybeLooker.AsError;
		}

		var looker = maybeLooker.AsSharpObject;

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
			return "#-1";
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
			return "#-1";
		}

		// fun_locate passes `looker` as match_result's `who` as well as its `where`, so every
		// can_interact / controls / Long_Fingers / nearby question inside the match is asked about the
		// looker. The executor is the subject only of the gates that bracket the call — the 's' check
		// above, this one, and the visibility check below.
		var maybeFound = await LocateService.Locate(parser, looker, looker, nameArg, locateFlags);

		// fun_locate writes the dbref itself on every failure path: safe_str("#-1") for the looker gate,
		// safe_dbref(item) for NOTHING/AMBIGUOUS, safe_dbref(NOTHING) for a failed visibility check. It
		// never emits a "#-1 SOMETHING" string, and softcode compares against these — a decorated one
		// will not `=` a bare #-1.
		if (maybeFound.IsError)
		{
			return maybeFound.AsError.Value == ErrorMessages.Returns.AmbiguousMatch ? "#-2" : "#-1";
		}

		if (maybeFound.IsNone)
		{
			return "#-1";
		}

		var found = maybeFound.WithoutError().WithoutNone();

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
		var loc = await found.Match<ValueTask<AnyOptionalSharpContainer>>(
			async player => (await player.Location.WithCancellation(CancellationToken.None)).WithNoneOption(),
			room => new(room.Location.WithCancellation(CancellationToken.None)),
			exit => new(exit.Home.WithCancellation(CancellationToken.None)),
			async thing => (await thing.Location.WithCancellation(CancellationToken.None)).WithNoneOption());

		// can_interact is the last term of both arms, so it is only asked once Can_Examine has declined
		// and the dark test has passed — as the else-if ordering has it. It can run softcode through an
		// @interact lock, so hoisting it out is neither free nor side-effect-free.
		bool visible;
		if (loc.IsNone)
		{
			visible = (await executor.IsSee_All() || !await found.IsDarkLegal() || await found.IsLight())
								&& await PermissionService.CanInteract(executor, found, IPermissionService.InteractType.See);
		}
		else
		{
			var container = loc.WithoutNone().WithExitOption();
			visible = await PermissionService.CanExamine(executor, container)
								|| ((!await found.IsDarkLegal() || await container.IsLight() || await found.IsLight())
										&& await PermissionService.CanInteract(executor, found, IPermissionService.InteractType.See));
		}

		// No post-hoc type filter: 'F' is MAT_TYPE, which the search itself honours now. Filtering the
		// winner afterwards could only ever turn a legitimate match into #-1 while the wrong-type
		// candidate had already displaced the right-type one during matching.
		return visible ? $"#{found.Object().DBRef.Number}" : "#-1";
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
			if (c != ' ' && !KnownLocateSwitches.Contains(c)) (unknown ??= []).Add(c);

			flags |= c switch
			{
				'N' => LocateFlags.NoTypePreference,
				'E' => LocateFlags.ExitsPreference,
				'P' => LocateFlags.PlayersPreference,
				'R' => LocateFlags.RoomsPreference,
				'T' => LocateFlags.ThingsPreference,
				'L' => LocateFlags.PreferLockPass,
				'F' => LocateFlags.OnlyMatchTypePreference,
				'X' => LocateFlags.UseLastIfAmbiguous,
				'*' => LocateFlags.All | LocateFlags.MatchAgainstLookerLocationName |
							 LocateFlags.ExitsInsideOfLooker,
				'a' => LocateFlags.AbsoluteMatch,
				'c' => LocateFlags.ExitsInsideOfLooker,
				'e' => LocateFlags.ExitsInTheRoomOfLooker,
				'h' => LocateFlags.MatchHereForLookerLocation,
				'i' => LocateFlags.MatchObjectsInLookerInventory,
				'l' => LocateFlags.MatchAgainstLookerLocationName,
				'm' => LocateFlags.MatchMeForLooker,
				'n' => LocateFlags.MatchObjectsInLookerLocation,
				'y' => LocateFlags.MatchOptionalWildCardForPlayerName,
				'p' => LocateFlags.MatchWildCardForPlayerName,
				'z' => LocateFlags.EnglishStyleMatching,
				'x' => LocateFlags.NoPartialMatches,
				's' => LocateFlags.OnlyMatchLookerControlledObjects,
				_ => default
			};
		}

		// NOTYPE is the absence of a preference, not a flag anyone sets alongside one.
		if (Library.Services.LocateService.PreferredTypes(flags) == SharpObjectTypes.None)
			flags |= LocateFlags.NoTypePreference;

		return (flags, (IReadOnlyList<char>?)unknown ?? []);
	}

	/// <summary>Every letter the switch above answers to, in fundb.c's order.</summary>
	private static readonly System.Buffers.SearchValues<char> KnownLocateSwitches =
		System.Buffers.SearchValues.Create("NEPRTLFX*acehilmnypzxs");


	[SharpFunction(Name = "lock", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Lock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH format: lock(<object>[/<lock name>]) - slash syntax in single arg
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				// Lock names match case-insensitively per PennMUSH, and "teleport" has to find the
				// lock LockType spells TPort — both of which LockNames owns.
				if (!found.Object().Locks.TryGetValue(LockNames.Canonical(lockName), out var lockData))
				{
					// PennMUSH returns *UNLOCKED* for unset locks
					return new CallState("*UNLOCKED*");
				}

				// PennMUSH Can_Read_Lock permission check
				if (!await PermissionService.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1");
				}

				return new CallState(lockData.LockString);
			});
	}

	[SharpFunction(Name = "lockfilter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["dbrefs", "lockname", "evaluate"])]
	public async ValueTask<CallState> LockFilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;

		var objListStr = args["0"].Message!.ToPlainText();
		var lockName = args["1"].Message!.ToPlainText();
		var shouldPass = args.TryGetValue("2", out var evalArg)
			? evalArg.Message!.ToPlainText().Equals("1", StringComparison.OrdinalIgnoreCase)
			: true;

		var objList = objListStr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var results = new List<string>();

		foreach (var objRef in objList)
		{
			var maybeObj = await LocateService.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (!maybeObj.IsValid())
			{
				continue;
			}

			var found = maybeObj.AsAnyObject;

			if (!found.Object().Locks.TryGetValue(LockNames.Canonical(lockName), out var lockData))
			{
				// No lock means it passes if we're looking for passes
				if (!shouldPass)
				{
					results.Add(found.Object().DBRef.ToString());
				}
				continue;
			}

			var passes = await LockService.Evaluate(lockData.LockString, found, executor);

			if (passes == shouldPass)
			{
				results.Add(found.Object().DBRef.ToString());
			}
		}

		return new CallState(string.Join(" ", results));
	}

	[SharpFunction(Name = "lockowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> LockOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH tracks per-lock setter; SharpMUSH returns object owner as approximation.
		// If no /lockname, defaults to Basic
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				if (!found.Object().Locks.ContainsKey(LockNames.Canonical(lockName)))
				{
					// PennMUSH: lockowner on nonexistent lock returns the object itself
					return new CallState($"#{found.Object().DBRef.Number}");
				}

				var owner = await found.Object().Owner.WithCancellation(CancellationToken.None);
				return new CallState($"#{owner.Object.DBRef.Number}");
			});
	}

	[SharpFunction(Name = "lparent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListParents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var maybeLocate = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All);

		if (maybeLocate.IsError)
		{
			return maybeLocate.AsError;
		}

		var locate = maybeLocate.AsSharpObject;
		var list = new List<DBRef>();

		while (true)
		{
			var parent = await locate.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				break;
			}

			var knownParent = parent.Known;
			if (!await PermissionService.CanExamine(executor, knownParent))
			{
				break;
			}

			locate = knownParent;
			list.Add(knownParent.Object().DBRef);
		}

		return string.Join(" ", list);
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

		var classArg = args["0"].Message!.ToPlainText();
		AnySharpObject? classObj = null;

		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (!maybeClass.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidClass);
			}
			classObj = maybeClass.AsAnyObject;
		}

		var pairs = new List<SearchSpecEngine.SearchPair>();
		for (int i = 1; i < args.Count; i += 2)
		{
			if (i + 1 >= args.Count)
			{
				break;
			}

			pairs.Add(new SearchSpecEngine.SearchPair(
				args[i.ToString()].Message!.ToPlainText(),
				args[(i + 1).ToString()].Message!.ToPlainText()));
		}

		var matches = await SearchSpecEngine.ExecuteAsync(
			parser, Mediator, LocateService, AttributeService, BooleanExpressionParser, PermissionService,
			executor, classObj?.Object().DBRef, pairs, useRegex);

		var finalResults = matches.Select(obj => new DBRef(obj.Key, obj.CreationTime).ToString());

		return new CallState(string.Join(" ", finalResults));
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
		var namelist = ArgHelpers.NameList(parser.CurrentState.Arguments["0"].Message!.ToPlainText());
		var hasErrorCallback = parser.CurrentState.Arguments.Count > 1
			&& !string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["1"].Message?.ToPlainText());

		AnySharpObject? callbackObject = null;
		string[]? callbackAttribute = null;

		if (hasErrorCallback)
		{
			var callbackSpec = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var slashIndex = callbackSpec.LastIndexOf('/');

			if (slashIndex > 0)
			{
				// Format: object/attribute - Use Span to avoid substring allocations
				var specSpan = callbackSpec.AsSpan();
				var objPart = specSpan.Slice(0, slashIndex).ToString();
				var attrPart = specSpan.Slice(slashIndex + 1).ToString();

				var objResult = await LocateService.Locate(parser, executor, executor, objPart, LocateFlags.All);
				if (objResult.IsValid())
				{
					callbackObject = objResult.WithoutError().WithoutNone();
					callbackAttribute = attrPart.Split('`');
				}
			}
			else
			{
				// Format: just attribute (use executor as object)
				callbackObject = executor;
				callbackAttribute = callbackSpec.Split('`');
			}
		}

		var resultList = new List<string>();

		foreach (var item in namelist)
		{
			DBRef? resolvedDbref = null;
			int errorCode = 0; // 0 = success, -1 = not found, -2 = ambiguous
			string originalName = string.Empty;

			if (item.IsT0)
			{
				var dbref = item.AsT0;
				var exists = await Mediator.Send(new GetBaseObjectNodeQuery(dbref));

				if (exists != null)
				{
					resolvedDbref = dbref;
				}
				else
				{
					errorCode = -1;
					originalName = $"#{dbref.Number}";
				}
			}
			else
			{
				var name = item.AsT1;
				originalName = name;

				var locateResult = await LocateService.Locate(parser, executor, executor, name, LocateFlags.All);

				if (locateResult.IsValid())
				{
					resolvedDbref = locateResult.AsAnyObject.Object().DBRef;
				}
				else if (locateResult.IsT4)
				{
					errorCode = -1;
				}
				else if (locateResult.IsT5)
				{
					var error = locateResult.AsT5;
					if (error.Value.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) ||
							error.Value.Contains("#-2"))
					{
						errorCode = -2;
					}
					else
					{
						errorCode = -1;
					}
				}
				else
				{
					errorCode = -1;
				}
			}

			if (resolvedDbref.HasValue)
			{
				resultList.Add($"#{resolvedDbref.Value.Number}");
			}
			else
			{
				resultList.Add($"#{errorCode}");

				if (hasErrorCallback && callbackObject != null && callbackAttribute != null)
				{
					await AttributeService.EvaluateAttributeFunctionAsync(
						parser, executor, callbackObject, string.Join("`", callbackAttribute),
						new Dictionary<string, CallState>
						{
							["0"] = new(MarkupText.Plain(originalName)),
							["1"] = new(MarkupText.Plain($"#{errorCode}"))
						});
				}
			}
		}

		return string.Join(" ", resultList);
	}

	[SharpFunction(Name = "nchildren", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg1, LocateFlags.All,
			async x =>
			{
				var children = x.Object().Children.Value ?? AsyncEnumerable.Empty<SharpObject>();
				return await children.CountAsync();
			});
	}

	[SharpFunction(Name = "next", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WalkNext(parser);

	[SharpFunction(Name = "nextdbref", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> NextDbReference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// One past the highest key, or #0 for an empty database.
		var maxKey = await Mediator.CreateStream(new GetAllObjectsQuery())
			.Select(o => o.Key)
			.DefaultIfEmpty(-1)
			.MaxAsync();

		// The next dbref with timestamp 0 (set when created)
		return new CallState($"#{maxKey + 1}:0");
	}

	[SharpFunction(Name = "nlsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	public async ValueTask<CallState> NumberOfListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var result = await ListSearch(parser, _2);
		var resultStr = result.Message?.ToPlainText() ?? "";

		if (resultStr.StartsWith("#-1"))
		{
			return result;
		}

		var count = string.IsNullOrWhiteSpace(resultStr)
			? 0
			: resultStr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		return new CallState(count);
	}

	[SharpFunction(Name = "nsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	public ValueTask<CallState> NumberOfSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return NumberOfListSearch(parser, _2);
	}

	[SharpFunction(Name = "num", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Number(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

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

	[SharpFunction(Name = "parent", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<CallState> Parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText();
		var arg1 = args.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (arg1 is null)
		{
			return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.All,
				async found =>
					(await found.Object().Parent.WithCancellation(CancellationToken.None)).Object()
					?.DBRef.ToString() ?? "");
		}


		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService.Controls(executor, target))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				switch (args)
				{
					case { Count: 1 }:
					case { Count: 2 } when args["1"].Message!.ToPlainText()
						.Equals("none", StringComparison.InvariantCultureIgnoreCase):
						await Mediator.Send(new UnsetObjectParentCommand(target));
						return CallState.Empty;
					default:
						return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor, args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent =>
							{
								if (!await PermissionService.Controls(executor, newParent)
										|| (!await target.HasFlag("LINK_OK")
												&& !await PermissionService.PassesLock(executor, newParent, LockType.Parent)))
								{
									return ErrorMessages.Returns.PermissionDenied;
								}

								// PennMUSH's fun_parent (src/fundb.c:1617-1640) calls do_parent directly and
								// otherwise always returns the (possibly-unchanged) current parent - it has
								// no distinguishing #-1-style sentinel for self-reference vs. a cycle, so
								// neither does this: both collapse to the same machine return here, unlike
								// the @PARENT command path's notification text.
								if (await HelperFunctions.SafeToAddParent(Mediator, Database, target, newParent) != RelationshipSafety.Safe)
								{
									return ErrorMessages.Returns.CycleDetected;
								}

								if (await AttributeService.ExceedsMaxParentDepthAsync(newParent))
								{
									return ErrorMessages.Returns.TooManyAncestors;
								}

								await Mediator.Send(new SetObjectParentCommand(target, newParent));
								return newParent;
							}
						);
				}
			}
		);
	}

	[SharpFunction(Name = "pmatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["name"])]
	public async ValueTask<CallState> PlayerMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			x => ValueTask.FromResult<CallState>(x.Object.DBRef));
	}

	[SharpFunction(Name = "rloc", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "levels"])]
	public async ValueTask<CallState> RecursiveLocation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var levelsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(levelsArg, out var levels) || levels < 0)
		{
			return new CallState(ErrorMessages.Returns.InvalidLevel);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var current = found;
				for (var i = 0; i < levels; i++)
				{
					if (current.IsContent)
					{
						var location = await current.AsContent.Location();
						current = location.WithRoomOption();
					}
					else if (current.IsExit)
					{
						// Exits' location is their source room
						var location = await current.AsExit.Location.WithCancellation(CancellationToken.None);
						current = location.WithRoomOption();
					}
					else
					{
						// Rooms don't have locations
						return new CallState("#-1");
					}
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
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
			{
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
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
				await x.Match<ValueTask<string>>(
					async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
					_ => ValueTask.FromResult<string>(ErrorMessages.Returns.ThisIsARoom),
					// For exits, return the location (the room containing the exit)
					async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
					async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString()));
	}

	[SharpFunction(Name = "zone", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object"])]
	public async ValueTask<CallState> Zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText();
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
					return "#-1";
				}

				if (hasArg1)
				{

					var arg1Str = arg1Value!.Message!.ToPlainText();

					if (arg1Str.Equals("none", StringComparison.OrdinalIgnoreCase))
					{
						if (!await PermissionService.Controls(executor, target))
						{
							return ErrorMessages.Returns.PermissionDenied;
						}

						await Mediator.Send(new UnsetObjectZoneCommand(target));
						return string.Empty;
					}

					var maybeZone = await LocateService.Locate(parser, executor, executor, arg1Str, LocateFlags.All);
					if (!maybeZone.IsValid())
					{
						return ErrorMessages.Returns.InvalidZone;
					}

					var zone = maybeZone.AsAnyObject;

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

					if (!await HelperFunctions.SafeToAddZone(Mediator, Database, target, zone))
					{
						return ErrorMessages.Returns.ZoneLoop;
					}

					// Handle flag/power stripping (simplified - no /preserve in function)
					if (!target.IsPlayer)
					{
						if (await target.HasFlag("WIZARD"))
						{
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, target, "!WIZARD", false);
						}
						if (await target.HasFlag("ROYALTY"))
						{
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, target, "!ROYALTY", false);
						}
						if (await target.HasFlag("TRUST"))
						{
							await ManipulateSharpObjectService.SetOrUnsetFlag(executor, target, "!TRUST", false);
						}
					}

					await Mediator.Send(new SetObjectZoneCommand(target, zone));
					return string.Empty;
				}

				// query fresh from database
				var freshTarget = await Mediator.Send(new GetObjectNodeQuery(target.Object().DBRef));
				var zoneObj = await freshTarget.Known.Object().Zone.WithCancellation(CancellationToken.None);
				return zoneObj.IsNone
					? "#-1"
					: zoneObj.Known.Object().DBRef.ToString();
			});
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
		var allFlags = await Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();

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

			// Look up flag by symbol (case-sensitive in PennMUSH)
			var flagDef = allFlags.FirstOrDefault(f => f.Symbol == c.ToString());
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

			bool hasIt = await obj.HasFlag(flagDef.Name);
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
	/// Parses space-separated long flag names like "wizard !puppet connected" for andlflags/orlflags.
	/// Returns null for invalid syntax (e.g. "! puppet" with space between ! and name).
	/// </summary>
	private async ValueTask<bool?> FlagLongNameCheck(AnySharpObject obj, string[] flagTokens, bool orMode)
	{
		var ret = !orMode;
		foreach (var token in flagTokens)
		{
			if (token == "!")
				return null; // Standalone '!' is invalid syntax

			bool negate = token.StartsWith("!");
			var flagName = negate ? token[1..] : token;

			if (string.IsNullOrEmpty(flagName))
				return null;

			bool hasIt;
			switch (flagName.ToUpperInvariant())
			{
				case "PLAYER": hasIt = obj.IsPlayer; break;
				case "ROOM": hasIt = obj.IsRoom; break;
				case "THING": hasIt = obj.IsThing; break;
				case "EXIT": hasIt = obj.IsExit; break;
				case "CONNECTED": hasIt = await ConnectionService.IsOnline(obj); break;
				default: hasIt = await obj.HasFlag(flagName); break;
			}

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

	[SharpFunction(Name = "orflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> OrFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orflags() checks if object has ANY of the specified flags (single-letter format)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var result = await FlagLetterCheck(found, flagsArg, orMode: true);
				if (result is null)
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "orflags"));
				return new CallState(result.Value);
			});
	}

	[SharpFunction(Name = "orlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> OrListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orlflags() checks if object has ANY of the specified flags (long-name format, space-separated)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var tokens = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var result = await FlagLongNameCheck(found, tokens, orMode: true);
				if (result is null)
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "orlflags"));
				return new CallState(result.Value);
			});
	}

	[SharpFunction(Name = "orlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public async ValueTask<CallState> OrListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var powersArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var powers = powersArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		return new CallState(await objList.ToAsyncEnumerable()
			.AnyAsync(async (objRef, _) =>
			{
				var maybeObj = await LocateService.Locate(parser, executor, executor, objRef, LocateFlags.All);
				if (!maybeObj.IsValid()) return false;
				var found = maybeObj.AsAnyObject;
				return await powers.ToAsyncEnumerable()
					.AnyAsync(async (power, _) => await found.HasPower(power));
			}));
	}

	[SharpFunction(Name = "andflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> AndFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andflags() checks if object has ALL of the specified flags (single-letter format)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var result = await FlagLetterCheck(found, flagsArg, orMode: false);
				if (result is null)
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "andflags"));
				return new CallState(result.Value);
			});
	}

	[SharpFunction(Name = "andlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> AndListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andlflags() checks if object has ALL of the specified flags (long-name format, space-separated)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var tokens = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				var result = await FlagLongNameCheck(found, tokens, orMode: false);
				if (result is null)
					return new CallState(string.Format(ErrorMessages.Returns.BadArgumentFormat, "andlflags"));
				return new CallState(result.Value);
			});
	}

	[SharpFunction(Name = "andlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public async ValueTask<CallState> AndListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var powersArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var powers = powersArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (objList.Length == 0)
		{
			return new CallState(false);
		}

		return new CallState(await objList.ToAsyncEnumerable()
			.AllAsync(async (objRef, _) =>
			{
				var maybeObj = await LocateService.Locate(parser, executor, executor, objRef, LocateFlags.All);
				if (!maybeObj.IsValid()) return false;
				var found = maybeObj.AsAnyObject;
				return await powers.ToAsyncEnumerable()
					.AllAsync(async (power, _) => await found.HasPower(power));
			}));
	}

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
}