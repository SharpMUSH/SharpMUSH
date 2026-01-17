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
	// Attribute name constant for link type
	private const string AttrLinkType = "_LINKTYPE";
	private const string LinkTypeVariable = "variable";
	private const string LinkTypeHome = "home";

	[SharpFunction(Name = "loc", MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Location(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		var locateResult = await _locateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0,
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
				var linkTypeAttr = await _attributeService!.GetAttributeAsync(executor, exit, AttrLinkType, IAttributeService.AttributeMode.Read, false);
				
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
				
				try
				{
					var destination = await exit.Location.WithCancellation(CancellationToken.None);
					return destination.Object().DBRef;
				}
				catch (InvalidOperationException)
				{
					return "#-1";
				}
			},
			async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef
		);
	}

	[SharpFunction(Name = "children", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
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
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			locate =>
			{
				if (!locate.IsContainer)
				{
					return CallState.Empty;
				}

				var contents = locate.AsContainer.Content(_mediator!);
				return string.Join(" ", contents.Take(1).Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "controls", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var arg1Split = arg1.Split('/');
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await _locateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
			var attribute = string.Join("/", arg1Split.Skip(1));
			var maybeLocateAttributeObject = await _locateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor,
				executor,
				attributeObj,
				LocateFlags.All);

			if (maybeLocateAttributeObject.IsError)
			{
				return maybeLocateAttributeObject.AsError;
			}

			var attributeObject = maybeLocateAttributeObject.AsSharpObject;

			var locateAttribute = await _attributeService!.GetAttributeAsync(executor, attributeObject, attribute,
				IAttributeService.AttributeMode.Read);

			if (locateAttribute.IsError)
			{
				return locateAttribute.AsError.Value;
			}

			if (locateAttribute.IsNone)
			{
				return Errors.ErrorNotVisible;
			}

			var foundAttribute = locateAttribute.AsAttribute;

			var controlsAttribute = await _permissionService!.Controls(locateObject, attributeObject, foundAttribute);

			return controlsAttribute;
		}

		var maybeLocateVictim = await _locateService.LocateAndNotifyIfInvalidWithCallState(parser,
			executor,
			executor,
			arg1,
			LocateFlags.All);

		if (maybeLocateVictim.IsError)
		{
			return maybeLocateVictim.AsError;
		}

		var locateVictim = maybeLocateVictim.AsSharpObject;

		var controls = await _permissionService!.Controls(locateObject, locateVictim);

		return controls;
	}

	[SharpFunction(Name = "entrances", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// entrances() finds all exits that lead to a location
		// Format: entrances([<location>][, <type>][, <start>][, <count>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var locArg))
		{
			var locStr = locArg.Message!.ToPlainText();
			var maybeTarget = await _locateService!.Locate(parser, executor, executor, locStr, LocateFlags.All);
			if (!maybeTarget.IsValid())
			{
				return new CallState("#-1 INVALID LOCATION");
			}
			target = maybeTarget.AsAnyObject;
		}

		var exits = _mediator!.CreateStream(new GetEntrancesQuery(target.Object().DBRef));
		var entrances = new List<AnySharpObject>();

		await foreach (var exit in exits)
		{
			entrances.Add(exit);
		}

		// Parse type filter (default: all)
		var typeFilter = "a";
		if (args.TryGetValue("1", out var typeArg))
		{
			typeFilter = typeArg.Message!.ToPlainText()?.ToLower() ?? "a";
		}

		// Parse begin filter (default: 0)
		var beginFilter = 0;
		if (args.TryGetValue("2", out var beginArg))
		{
			if (int.TryParse(beginArg.Message!.ToPlainText(), out var begin))
			{
				beginFilter = begin;
			}
		}

		// Parse end filter (default: int.MaxValue)
		var endFilter = int.MaxValue;
		if (args.TryGetValue("3", out var endArg))
		{
			if (int.TryParse(endArg.Message!.ToPlainText(), out var end))
			{
				endFilter = end;
			}
		}

		// Filter by type and dbref range
		var filtered = entrances.Where(entrance =>
		{
			var obj = entrance.Object();
			var dbrefNum = obj.DBRef.Number;

			// Filter by dbref range
			if (dbrefNum < beginFilter || dbrefNum > endFilter)
			{
				return false;
			}

			// Filter by type
			if (typeFilter.Contains('a'))
			{
				return true; // 'a' means all types
			}

			if (typeFilter.Contains('e') && entrance.IsExit) return true;
			if (typeFilter.Contains('t') && entrance.IsThing) return true;
			if (typeFilter.Contains('p') && entrance.IsPlayer) return true;
			if (typeFilter.Contains('r') && entrance.IsRoom) return true;

			return false;
		}).Select(e => e.Object().DBRef.ToString());

		return new CallState(string.Join(" ", filtered));
	}

	[SharpFunction(Name = "exit", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (locate.IsPlayer && enactor.Object().DBRef == locate.Object().DBRef)
				{
					locate = (await locate.AsPlayer.Location.WithCancellation(CancellationToken.None)).WithExitOption();
				}

				if (!locate.IsRoom)
				{
					return Errors.ErrorNotARoom;
				}

				// Todo: Turn Content into async enumerable.
				var exits = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef)
					.ToArrayAsync();

				return exits.Length != 0
					? exits.First().ToString()
					: string.Empty;
			});
	}

	[SharpFunction(Name = "followers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Followers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// followers() returns list of objects following the target
		// Objects follow by setting their FOLLOWING attribute to the target's dbref
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				// Query all objects that have FOLLOWING attribute set to this object's dbref
				var targetDbref = found.Object().DBRef.ToString();
				var followers = new List<string>();
				
				// Use filtered query to get all objects more efficiently
				// Note: We can't filter by attribute value in the database easily, 
				// so we still need to check attributes in application code
				var allObjects = _mediator!.CreateStream(new GetAllObjectsQuery());
				
				await foreach (var obj in allObjects)
				{
					// Get the FOLLOWING attribute for this object
					var objAttributes = obj.Attributes.Value;
					await foreach (var attr in objAttributes)
					{
						if (attr.LongName == "FOLLOWING" && attr.Value.ToPlainText() == targetDbref)
						{
							followers.Add(obj.DBRef.ToString());
							break;
						}
					}
				}
				
				return new CallState(string.Join(" ", followers));
			});
	}

	[SharpFunction(Name = "following", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Following(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// following() returns the object that the target is following
		// Objects track who they follow via the FOLLOWING attribute
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				// Get the FOLLOWING attribute from the target object
				var followingAttr = await _attributeService!.GetAttributeAsync(
					executor, found, "FOLLOWING", IAttributeService.AttributeMode.Read, false);
				
				if (followingAttr.IsAttribute)
				{
					// Return the dbref stored in the FOLLOWING attribute
					return new CallState(followingAttr.AsAttribute.Last().Value.ToPlainText());
				}
				
				return new CallState(string.Empty);
			});
	}

	[SharpFunction(Name = "home", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var locateResult = await _locateService!.LocateAndNotifyIfInvalidWithCallState(parser,
			executor, executor, arg0, LocateFlags.All);

		if (locateResult.IsError)
		{
			return locateResult.AsError;
		}

		var found = locateResult.AsSharpObject;

		return await found.Match<ValueTask<CallState>>(
			// Player - return home
			async player => (await player.Home.WithCancellation(CancellationToken.None)).Object().DBRef,
			// Room - return location (drop-to) or #-1
			async room =>
			{
				var location = await room.Location.WithCancellation(CancellationToken.None);
				return location.Match(
					player => player.Object.DBRef.ToString(),
					r => r.Object.DBRef.ToString(),
					thing => thing.Object.DBRef.ToString(),
					_ => "#-1");
			},
			// Exit - return source room
			async exit => (await exit.Home.WithCancellation(CancellationToken.None)).Object().DBRef,
			// Thing - return home
			async thing => (await thing.Home.WithCancellation(CancellationToken.None)).Object().DBRef
		);
	}

	[SharpFunction(Name = "llockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "lock"])]
	public async ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// llockflags() lists all lock flags
		// Format: llockflags([<lock type>])
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.Arguments;
		
		if (args.Count == 0)
		{
			// Return all available lock flags
			var flags = _lockService!.LockPrivileges.Keys;
			return new CallState(string.Join(" ", flags));
		}
		
		// With argument, return flags for a specific lock type
		var lockType = args["0"].Message!.ToPlainText();
		if (_lockService!.SystemLocks.TryGetValue(lockType, out var lockFlags))
		{
			// Convert flags enum to string representation
			var flagList = new List<string>();
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.Visual))
				flagList.Add("visual");
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.Private))
				flagList.Add("no_inherit");
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.NoClone))
				flagList.Add("no_clone");
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.Wizard))
				flagList.Add("wizard");
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.Owner))
				flagList.Add("owner");
			if (lockFlags.HasFlag(Library.Services.LockService.LockFlags.Locked))
				flagList.Add("locked");
			
			return new CallState(string.Join(" ", flagList));
		}
		
		return new CallState(string.Empty);
	}

	[SharpFunction(Name = "lockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "lock"])]
	public async ValueTask<CallState> LockFlagsObject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lockflags() returns lock flags for a specific lock on an object
		// Format: lockflags([<object>[/<locktype>]])
		var args = parser.CurrentState.Arguments;
		
		if (args.Count == 0)
		{
			// Return all available lock flag letters
			// In PennMUSH: v=visual, n=no_inherit, c=no_clone, w=wizard, o=owner, l=locked
			return new CallState("vncwol");
		}
		
		// Parse object/locktype
		var argStr = args["0"].Message!.ToPlainText();
		var parts = argStr.Split('/', 2);
		var objectRef = parts[0];
		var lockType = parts.Length > 1 ? parts[1] : "Basic";
		
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		
		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objectRef, LocateFlags.All,
			found =>
			{
				// Get the lock data
				if (!found.Object().Locks.TryGetValue(lockType, out var lockData))
				{
					return ValueTask.FromResult(new CallState(string.Empty));
				}
				
				// Convert flags to string
				var flagChars = new List<char>();
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.Visual))
					flagChars.Add('v');
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.Private))
					flagChars.Add('n');
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.NoClone))
					flagChars.Add('c');
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.Wizard))
					flagChars.Add('w');
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.Owner))
					flagChars.Add('o');
				if (lockData.Flags.HasFlag(Library.Services.LockService.LockFlags.Locked))
					flagChars.Add('l');
				
				return ValueTask.FromResult(new CallState(new string(flagChars.ToArray())));
			});
	}

	[SharpFunction(Name = "elock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "locktype"])]
	public async ValueTask<CallState> EvaluateLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// elock() evaluates a lock against an object
		// Format: elock(<object>, <lock name>)
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var lockName = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			found =>
			{
				// Get the lock string from the object
				if (!found.Object().Locks.TryGetValue(lockName, out var lockData))
				{
					return ValueTask.FromResult(new CallState("#-1 NO SUCH LOCK"));
				}

				// Evaluate the lock with the executor as the unlocker
				var result = _lockService!.Evaluate(lockData.LockString, found, executor);
				return ValueTask.FromResult(new CallState(result));
			});
	}

	[SharpFunction(Name = "llocks", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// llocks() lists all locks on an object
		// Format: llocks([<object>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;
		
		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var objArg))
		{
			var objStr = objArg.Message!.ToPlainText();
			var maybeTarget = await _locateService!.Locate(parser, executor, executor, objStr, LocateFlags.All);
			if (!maybeTarget.IsValid())
			{
				return new CallState("#-1 INVALID OBJECT");
			}
			target = maybeTarget.AsAnyObject;
		}

		// Get all lock names from the object
		var lockNames = target.Object().Locks.Keys;
		return new CallState(string.Join(" ", lockNames));
	}

	[SharpFunction(Name = "locks", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> LocksRequired(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// locks() is like llocks() but requires an object argument
		// Format: locks(<object>)
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		
		var maybeTarget = await _locateService!.Locate(parser, executor, executor, objStr, LocateFlags.All);
		if (!maybeTarget.IsValid())
		{
			return new CallState("#-1 INVALID OBJECT");
		}
		var target = maybeTarget.AsAnyObject;

		// Get all lock names from the object
		var lockNames = target.Object().Locks.Keys;
		return new CallState(string.Join(" ", lockNames));
	}

	[SharpFunction(Name = "localize", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse, ParameterNames = ["string"])]
	public async ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser
			   .With(
				   x => x with { Registers = [] },
				   newParser => newParser.FunctionParse(parser.CurrentState.Arguments["0"].Message!))
		   ?? CallState.Empty;

	[SharpFunction(Name = "locate", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "name", "type"])]
	public async ValueTask<CallState> Locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		/*

		 help locate()
LOCATE()
  locate(<looker>, <name>, <parameters>)

  This function attempts to find an object called <name>, relative to the object <looker>. It's similar to the num() function, but you can be more specific about which type of object to find, and where to look for it. When attempting to match objects near to <looker> (anything but absolute, player name or "me" matches), you must control <looker>, have the See_All power or be nearby.

  <parameters> is a string of characters which control the type of the object to find, and where (relative to <looker>) to look for it.

  You can control the preferred types of the match with:
    N - No type (this is the default)
    E - Exits
    L - Prefer an object whose Basic @lock <looker> passes
    P - Players
    R - Rooms
    T - Things
    F - Return #-1 if what's found is of a different type than the preferred one.
    X - Never return #-2. Use the last dbref found if the match is ambiguous.

  If type(s) are given, locate() will attempt to find an object with one of the given types first. If none are found, it will attempt to find any type of object, unless 'F' is specified, in which case it will return #-1.

  You can control where to look with:
    a - Absolute match (match <name> against any dbref)
    c - Exits in the room <looker>
    e - Exits in <looker>'s location
    h - If <name> is "here", return <looker>'s location
    i - Match <name> against the names of objects in <looker>'s inventory
    l - Match <name> against the name of <looker>'s location
    m - If <name> is "me", return <looker>'s dbref
    n - Match <name> against the names of objects in <looker>'s location
    p - If <name> begins with a *, match the rest against player names
    z - English-style matching (my 2nd book) of <name> (see 'help matching')
    * - All of the above (try a complete match). Default when no match parameters are given.
    y - Match <name> against player names whether it begins with a * or not
    x - Only match objects with the exact name <name>, no partial matches
    s - Only match objects which <looker> controls. You must control <looker> or have the See_All power.

  Just string all the parameters together. Spaces are ignored, so you can use spaces between paramaters for clarity if you wish.

  Examples:
  Find the dbref of the player whose name matches %0, or %#'s dbref if %0 is "me".
    > think locate(%#, %0, PFym)
  'PF' matches objects of type 'player' and nothing else, 'm' checks for the string "me", and 'y' matches the names of players.

  Find the dbref of an object near %# called %0, including %# himself and his location. Prefer players or things, but accept rooms or exits if no players or things are found.
    > think locate(%#, %0, PThmlni)
  This prefers 'P'layers or 'T'hings, and compares %0 against the strings "here" and "me", and the names of %#'s location, his neighbours, and his inventory.

  */

		var args = parser.CurrentState.Arguments;
		var lookerArg = args["0"].Message!.ToPlainText();
		var nameArg = args["1"].Message!.ToPlainText();
		var parametersArg = args["2"].Message!.ToPlainText();

		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		// First, locate the looker object
		var maybeLooker =
			await _locateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, lookerArg,
				LocateFlags.All);
		if (maybeLooker.IsError)
		{
			return maybeLooker.AsError;
		}

		var looker = maybeLooker.AsSharpObject;

		// Parse the parameters string into LocateFlags
		var locateFlags = ParseLocateParameters(parametersArg);

		// Check if we need to determine type preferences
		var preferredTypes = GetPreferredTypes(parametersArg);
		var requireExactType = parametersArg.Contains('F', StringComparison.OrdinalIgnoreCase);

		// Perform the locate operation
		var maybeFound = await _locateService.Locate(parser, looker, executor, nameArg, locateFlags);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError.Value;
		}

		if (maybeFound.IsNone)
		{
			return "#-1";
		}

		var found = maybeFound.WithoutError().WithoutNone();

		// Check type preferences if specified
		if (preferredTypes.Any())
		{
			var foundType = GetObjectType(found);
			if (!preferredTypes.Contains(foundType))
			{
				if (requireExactType)
				{
					return "#-1";
				}
				// If not requiring exact type, we still return the found object
			}
		}

		return found.Object().DBRef;
	}

	private static LocateFlags ParseLocateParameters(string parameters)
	{
		var flags = LocateFlags.NoTypePreference;
		var paramUpper = parameters.ToUpperInvariant().Replace(" ", "");

		// Handle special case of "*" meaning all flags
		if (paramUpper.Contains('*'))
		{
			return LocateFlags.All;
		}

		// Parse location flags
		if (paramUpper.Contains('A')) flags |= LocateFlags.AbsoluteMatch;
		if (paramUpper.Contains('C')) flags |= LocateFlags.ExitsInTheRoomOfLooker;
		if (paramUpper.Contains('E')) flags |= LocateFlags.ExitsInsideOfLooker;
		if (paramUpper.Contains('H')) flags |= LocateFlags.MatchHereForLookerLocation;
		if (paramUpper.Contains('I')) flags |= LocateFlags.MatchObjectsInLookerInventory;
		if (paramUpper.Contains('L')) flags |= LocateFlags.MatchAgainstLookerLocationName;
		if (paramUpper.Contains('M')) flags |= LocateFlags.MatchMeForLooker;
		if (paramUpper.Contains('N')) flags |= LocateFlags.MatchObjectsInLookerLocation;
		if (paramUpper.Contains('P')) flags |= LocateFlags.MatchWildCardForPlayerName;
		if (paramUpper.Contains('Y')) flags |= LocateFlags.MatchOptionalWildCardForPlayerName;
		if (paramUpper.Contains('Z')) flags |= LocateFlags.EnglishStyleMatching;
		if (paramUpper.Contains('X')) flags |= LocateFlags.NoPartialMatches | LocateFlags.UseLastIfAmbiguous;
		if (paramUpper.Contains('S')) flags |= LocateFlags.OnlyMatchLookerControlledObjects;

		// Parse type preference flags (note: some letters are reused)
		if (paramUpper.Contains('E')) flags |= LocateFlags.ExitsPreference;
		if (paramUpper.Contains('P')) flags |= LocateFlags.PlayersPreference;
		if (paramUpper.Contains('R')) flags |= LocateFlags.RoomsPreference;
		if (paramUpper.Contains('T')) flags |= LocateFlags.ThingsPreference;
		if (paramUpper.Contains('L')) flags |= LocateFlags.PreferLockPass;
		if (paramUpper.Contains('F')) flags |= LocateFlags.FailIfNotPreferred;

		return flags;
	}

	private static HashSet<string> GetPreferredTypes(string parameters)
	{
		var types = new HashSet<string>();
		var paramUpper = parameters.ToUpperInvariant();

		if (paramUpper.Contains('E')) types.Add("EXIT");
		if (paramUpper.Contains('P')) types.Add("PLAYER");
		if (paramUpper.Contains('R')) types.Add("ROOM");
		if (paramUpper.Contains('T')) types.Add("THING");

		return types;
	}

	private string GetObjectType(AnySharpObject obj)
	{
		return obj.Match(
			_ => "PLAYER",
			_ => "ROOM",
			_ => "EXIT",
			_ => "THING"
		);
	}

	[SharpFunction(Name = "lock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "locktype"])]
	public async ValueTask<CallState> Lock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lock() gets a lock string from an object
		// Format: lock(<object>[, <lock name>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var args = parser.CurrentState.Arguments;
		var lockName = args.TryGetValue("1", out var lockArg) 
			? lockArg.Message!.ToPlainText() 
			: "Basic";

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			found =>
			{
				// Get the lock string from the object
				if (!found.Object().Locks.TryGetValue(lockName, out var lockData))
				{
					return ValueTask.FromResult(new CallState(string.Empty));
				}

				return ValueTask.FromResult(new CallState(lockData.LockString));
			});
	}

	[SharpFunction(Name = "lockfilter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "lock", "list", "delimiter"])]
	public async ValueTask<CallState> LockFilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lockfilter() filters objects by lock evaluation
		// Format: lockfilter(<object list>, <lock name>[, <lock eval>])
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
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
			var maybeObj = await _locateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (!maybeObj.IsValid())
			{
				continue;
			}

			var found = maybeObj.AsAnyObject;

			// Check if object has the lock
			if (!found.Object().Locks.TryGetValue(lockName, out var lockData))
			{
				// No lock means it passes if we're looking for passes
				if (!shouldPass)
				{
					results.Add(found.Object().DBRef.ToString());
				}
				continue;
			}

			// Evaluate the lock
			var passes = _lockService!.Evaluate(lockData.LockString, found, executor);
			
			if (passes == shouldPass)
			{
				results.Add(found.Object().DBRef.ToString());
			}
		}

		return new CallState(string.Join(" ", results));
	}

	[SharpFunction(Name = "lockowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "lock"])]
	public async ValueTask<CallState> LockOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lockowner() returns the owner of an object (who controls its locks)
		// Format: lockowner(<object>)
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var owner = await found.Object().Owner.WithCancellation(CancellationToken.None);
				return new CallState(owner.Object.DBRef);
			});
	}

	[SharpFunction(Name = "lparent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "generations"])]
	public async ValueTask<CallState> ListParents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var maybeLocate = await _locateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
			if (!await _permissionService!.CanExamine(executor, knownParent))
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
		// lsearch() searches the database for objects matching criteria
		// Format: lsearch(<player>, <class1>, <restriction1>, <class2>, <restriction2>, ...)
		// Per PennMUSH documentation: comma-separated positional arguments, NOT equals syntax
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// First argument is the player (who owns the objects to search)
		var classArg = args["0"].Message!.ToPlainText();
		AnySharpObject? classObj = null;

		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await _locateService!.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (!maybeClass.IsValid())
			{
				return new CallState("#-1 INVALID CLASS");
			}
			classObj = maybeClass.AsAnyObject;
		}

		// Build database-level filter from search criteria
		var filter = new ObjectSearchFilter();
		var types = new List<string>();
		var namePattern = (string?)null;
		int? minDbRef = null;
		int? maxDbRef = null;
		DBRef? zone = null;
		DBRef? parent = null;
		string? hasFlag = null;
		string? hasPower = null;
		int? start = null;
		int? count = null;

		// Process criteria as positional class/restriction pairs
		var appLevelCriteria = new List<(string key, string value)>();
		
		// Arguments come in pairs: class, restriction, class, restriction, ...
		for (int i = 1; i < args.Count; i += 2)
		{
			// Need both class and restriction
			if (i + 1 >= args.Count)
			{
				// Odd number of arguments after player - invalid
				break;
			}

			var classType = args[i.ToString()].Message!.ToPlainText().Trim().ToUpperInvariant();
			var restriction = args[(i + 1).ToString()].Message!.ToPlainText().Trim();

			// Skip if class is "none"
			if (classType.Equals("NONE", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			// Categorize criteria: database-level vs application-level
			switch (classType)
			{
				case "TYPE":
					types.Add(restriction.ToUpperInvariant());
					break;
				case "NAME":
					namePattern = restriction;
					break;
				// Type-specific name filters (shortcuts for TYPE + NAME)
				case "EXITS":
					types.Add("EXIT");
					namePattern = restriction;
					break;
				case "THINGS":
				case "OBJECTS":
					types.Add("THING");
					namePattern = restriction;
					break;
				case "ROOMS":
					types.Add("ROOM");
					namePattern = restriction;
					break;
				case "PLAYERS":
					types.Add("PLAYER");
					namePattern = restriction;
					break;
				case "MINDBREF":
				case "MINDB":
					if (int.TryParse(restriction, out var min)) minDbRef = min;
					break;
				case "MAXDBREF":
				case "MAXDB":
					if (int.TryParse(restriction, out var max)) maxDbRef = max;
					break;
				case "START":
					if (int.TryParse(restriction, out var startVal)) start = startVal;
					break;
				case "COUNT":
					if (int.TryParse(restriction, out var countVal)) count = countVal;
					break;
				case "ZONE":
					var maybeZone = await _locateService!.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeZone.IsValid()) zone = maybeZone.AsAnyObject.Object().DBRef;
					break;
				case "PARENT":
					var maybeParent = await _locateService!.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeParent.IsValid()) parent = maybeParent.AsAnyObject.Object().DBRef;
					break;
				case "FLAG":
				case "FLAGS":
					hasFlag = restriction;
					break;
				case "LFLAGS":
					// LFLAGS uses space-separated flag names instead of single characters
					// Store for now - will need to convert to single-char format or handle separately
					hasFlag = restriction;
					break;
				case "POWER":
				case "POWERS":
					hasPower = restriction;
					break;
				default:
					// Lock evaluation, COMMAND, LISTEN, and other criteria must happen in application code
					appLevelCriteria.Add((classType, restriction));
					break;
			}
		}

		// Pre-compile lock strings and eval expressions for efficiency (compile once, evaluate many times)
		// This avoids re-compiling the same lock string or expression for every object in the result set
		var compiledLocks = new List<Func<AnySharpObject, AnySharpObject, bool>>();
		var compiledEvals = new List<(string evalExpression, string? typeFilter)>();
		
		foreach (var (key, value) in appLevelCriteria)
		{
			switch (key)
			{
				case "LOCK" or "ELOCK":
					// Optimize #TRUE - no need to compile
					if (value is "#TRUE" or "")
					{
						compiledLocks.Add((_, _) => true);
					}
					else
					{
						compiledLocks.Add(_booleanExpressionParser!.Compile(value));
					}
					break;
				
				case "EVAL":
					compiledEvals.Add((value, null));
					break;
				case "EPLAYER":
					compiledEvals.Add((value, "PLAYER"));
					break;
				case "EROOM":
					compiledEvals.Add((value, "ROOM"));
					break;
				case "EEXIT":
					compiledEvals.Add((value, "EXIT"));
					break;
				case "ETHING" or "EOBJECT":
					compiledEvals.Add((value, "THING"));
					break;
				
				case "LISTEN":
				case "COMMAND":
					// These require attribute pattern matching - store for app-level evaluation
					// Will be handled in the filtering loop below
					break;
			}
		}
		
		// Extract LISTEN and COMMAND criteria for app-level evaluation
		var listenPattern = appLevelCriteria.FirstOrDefault(x => x.key == "LISTEN").value;
		var commandPattern = appLevelCriteria.FirstOrDefault(x => x.key == "COMMAND").value;
		var hasListenCriteria = !string.IsNullOrEmpty(listenPattern);
		var hasCommandCriteria = !string.IsNullOrEmpty(commandPattern);
		
		// Check if we need to convert SharpObject to AnySharpObject for app-level criteria
		var hasAppLevelCriteria = compiledLocks.Count > 0 || compiledEvals.Count > 0 || hasListenCriteria || hasCommandCriteria;
		
		// Build filter object
		// IMPORTANT: Only apply START/COUNT at database level if there are NO app-level criteria
		// If there are app-level criteria, we must apply START/COUNT after filtering in application code
		filter = new ObjectSearchFilter
		{
			Types = types.Count > 0 ? [.. types] : null,
			NamePattern = namePattern,
			UseRegex = useRegex,
			MinDbRef = minDbRef,
			MaxDbRef = maxDbRef,
			Zone = zone,
			Parent = parent,
			HasFlag = hasFlag,
			HasPower = hasPower,
			Owner = classObj?.Object().DBRef,
			Skip = hasAppLevelCriteria ? null : start,  // Only skip at DB level if no app-level filtering
			Limit = hasAppLevelCriteria ? null : count  // Only limit at DB level if no app-level filtering
		};

		// Query database with filters applied at database level
		var filteredObjects = _mediator!.CreateStream(new GetFilteredObjectsQuery(filter));
		
		if (!hasAppLevelCriteria)
		{
			// No app-level criteria, just convert to dbrefs directly without fetching full objects
			// START/COUNT already applied at database level
			var results = new List<string>();
			await foreach (var obj in filteredObjects)
			{
				results.Add(new DBRef(obj.Key, obj.CreationTime).ToString());
			}
			return new CallState(string.Join(" ", results));
		}

		// Apply application-level criteria (locks, evals, etc.)
		// Optimize: Convert to AnySharpObject once per object and evaluate all criteria
		var finalResults = new List<string>();
		await foreach (var obj in filteredObjects)
		{
			// Convert the raw SharpObject to a properly-typed AnySharpObject once for all evaluations
			var typedObj = await CreateAnySharpObjectFromSharpObject(obj);
			bool matches = true;
			
			// Evaluate pre-compiled lock criteria
			foreach (var compiledLock in compiledLocks)
			{
				if (!compiledLock(typedObj, executor))
				{
					matches = false;
					break;
				}
			}
			
			// Evaluate eval expressions if locks passed
			if (matches)
			{
				foreach (var (evalExpression, typeFilter) in compiledEvals)
				{
					// Check type filter if specified
					if (typeFilter != null && !typedObj.Object().Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
					{
						matches = false;
						break;
					}
					
					// Replace ## with the object's dbref number in the expression
					// Use just the number (e.g., "1") for numeric comparisons
					var objectDbRefNum = typedObj.Object().DBRef.Number.ToString();
					var expression = evalExpression.Replace("##", objectDbRefNum);
					
					// Evaluate the expression
					var evalResult = await parser.FunctionParse(MModule.single(expression));
					if (evalResult == null || !evalResult.Message.Truthy())
					{
						matches = false;
						break;
					}
				}
			}
			
			// Evaluate LISTEN pattern if specified
			if (matches && hasListenCriteria)
			{
				// Check if the object has any @listen attributes matching the pattern
				var attributesResult = await _attributeService!.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingListen = false;
					
					foreach (var attr in attributesResult.AsAttributes.Where(a => a.Name.Equals("LISTEN", StringComparison.OrdinalIgnoreCase) || 
					                                           a.Name.StartsWith("LISTEN`", StringComparison.OrdinalIgnoreCase)))
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
						// Check if the listen pattern matches our search pattern
						// This is a wildcard match where * matches any characters
						if (IsWildcardMatch(attrValue, listenPattern!))
						{
							hasMatchingListen = true;
							break;
						}
					}
					
					if (!hasMatchingListen)
					{
						matches = false;
					}
				}
				else
				{
					// If we can't get attributes, object doesn't match
					matches = false;
				}
			}
			
			// Evaluate COMMAND pattern if specified
			if (matches && hasCommandCriteria)
			{
				// Check if the object has any $-commands matching the pattern
				var attributesResult = await _attributeService!.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingCommand = false;
					
					foreach (var attr in attributesResult.AsAttributes)
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
						// $-commands are in format: $command-pattern:action
						// We need to extract the command pattern (between $ and :) and match it
						var dollarIndex = attrValue.IndexOf('$');
						if (dollarIndex >= 0)
						{
							var colonIndex = attrValue.IndexOf(':', dollarIndex);
							if (colonIndex > dollarIndex)
							{
								// Extract just the command pattern part (after $ and before :)
								var commandPart = attrValue.Substring(dollarIndex + 1, colonIndex - dollarIndex - 1);
								if (IsWildcardMatch(commandPart, commandPattern))
								{
									hasMatchingCommand = true;
									break;
								}
							}
						}
					}
					
					if (!hasMatchingCommand)
					{
						matches = false;
					}
				}
				else
				{
					// If we can't get attributes, object doesn't match
					matches = false;
				}
			}

			if (matches)
			{
				finalResults.Add(typedObj.Object().DBRef.ToString());
			}
		}
		
		// Apply START/COUNT at application level if we had app-level filtering
		// This ensures pagination happens AFTER all runtime filters are applied
		if (start.HasValue || count.HasValue)
		{
			var skipCount = start ?? 0;
			var takeCount = count ?? int.MaxValue;
			finalResults = finalResults.Skip(skipCount).Take(takeCount).ToList();
		}

		return new CallState(string.Join(" ", finalResults));
	}
	
	/// <summary>
	/// Simple wildcard pattern matching for LISTEN and COMMAND searches.
	/// Supports * as a wildcard that matches any sequence of characters.
	/// </summary>
	private bool IsWildcardMatch(string value, string pattern)
	{
		// Convert pattern to regex: escape special chars except *, then replace * with .*
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
		return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern, 
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	/// <summary>
	/// Creates an AnySharpObject from a SharpObject based on its Type property.
	/// This is needed when we have a raw SharpObject from the database but need to work with the discriminated union.
	/// </summary>
	private async Task<AnySharpObject> CreateAnySharpObjectFromSharpObject(SharpObject obj)
	{
		// The object needs to be fetched properly from the database to get the correct type-specific object
		// We use the _mediator to fetch the fully-typed object asynchronously
		var dbref = new DBRef(obj.Key, obj.CreationTime);
		var result = await _mediator!.Send(new GetObjectNodeQuery(dbref));
		
		if (result.IsNone)
		{
			// This shouldn't happen in normal operation, but handle it gracefully
			throw new InvalidOperationException($"Object {dbref} not found when evaluating lock criteria");
		}

		return result.Known;
	}

	[SharpFunction(Name = "lsearchr", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["object", "class=restriction..."])]
	public async ValueTask<CallState> ListSearchRegex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// lsearchr() is like lsearch but with regex support for name patterns
		// We'll modify the behavior by passing a flag through the search
		
		// Temporarily store the original arguments
		var originalArgs = parser.CurrentState.Arguments;
		
		// Call the shared search implementation with regex enabled
		return await ListSearchInternal(parser, _2, useRegex: true);
	}

	[SharpFunction(Name = "namelist", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "attribute"])]
	public async ValueTask<CallState> NameList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var namelist = ArgHelpers.NameList(parser.CurrentState.Arguments["0"].Message!.ToPlainText());
		var hasErrorCallback = parser.CurrentState.Arguments.Count > 1 
			&& !string.IsNullOrWhiteSpace(parser.CurrentState.Arguments["1"].Message?.ToPlainText());
		
		// Parse optional object/attribute parameter for error callback
		AnySharpObject? callbackObject = null;
		string[]? callbackAttribute = null;
		
		if (hasErrorCallback)
		{
			var callbackSpec = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			var slashIndex = callbackSpec.LastIndexOf('/');
			
			if (slashIndex > 0)
			{
				// Format: object/attribute
				var objPart = callbackSpec.Substring(0, slashIndex);
				var attrPart = callbackSpec.Substring(slashIndex + 1);
				
				var objResult = await _locateService!.Locate(parser, executor, executor, objPart, LocateFlags.All);
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
		
		// Process each name in the list
		foreach (var item in namelist)
		{
			DBRef? resolvedDbref = null;
			int errorCode = 0; // 0 = success, -1 = not found, -2 = ambiguous
			string originalName = string.Empty;
			
			if (item.IsT0)
			{
				// Already a dbref - validate it exists
				var dbref = item.AsT0;
				var exists = await _mediator!.Send(new GetBaseObjectNodeQuery(dbref));
				
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
				// String name - need to locate
				var name = item.AsT1;
				originalName = name;
				
				var locateResult = await _locateService!.Locate(parser, executor, executor, name, LocateFlags.All);
				
				if (locateResult.IsValid())
				{
					// Found valid object
					resolvedDbref = locateResult.AsAnyObject.Object().DBRef;
				}
				else if (locateResult.IsT4)
				{
					// None - not found
					errorCode = -1;
				}
				else if (locateResult.IsT5)
				{
					// Error occurred - check if ambiguous or not found
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
					// Unknown error
					errorCode = -1;
				}
			}
			
			if (resolvedDbref.HasValue)
			{
				// Successfully resolved - add the dbref
				resultList.Add($"#{resolvedDbref.Value.Number}");
			}
			else
			{
				// Failed to resolve - add error code
				resultList.Add($"#{errorCode}");
				
				// Call error callback if provided
				if (hasErrorCallback && callbackObject != null && callbackAttribute != null)
				{
					var attrResult = await _attributeService!.GetAttributeAsync(
						executor, callbackObject, string.Join("`", callbackAttribute),
						IAttributeService.AttributeMode.Read, true);
					
					if (!attrResult.IsNone && !attrResult.IsError)
					{
						var attribute = attrResult.AsAttribute.Last();
						var attrValue = attribute.Value;
						
						// Replace %0 with the original name and %1 with the error code
						var substitutedCommand = attrValue.ToString()
							.Replace("%0", originalName)
							.Replace("%1", $"#{errorCode}");
						
						// Parse and execute the command
						await parser.CommandParse(MModule.single(substitutedCommand));
					}
				}
			}
		}
		
		return string.Join(" ", resultList);
	}

	[SharpFunction(Name = "nchildren", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg1, LocateFlags.All,
			async x =>
			{
				var children = x.Object().Children.Value ?? AsyncEnumerable.Empty<SharpObject>();
				return await children.CountAsync();
			});
	}

	[SharpFunction(Name = "next", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				// Get the location of the object
				AnySharpContainer location;

				if (locate.IsExit)
				{
					// For exits, get the source room (location)
					var exitLocation = await locate.AsExit.Location.WithCancellation(CancellationToken.None);
					location = exitLocation;
				}
				else if (locate.IsContent)
				{
					// For things and players, get their location
					location = await locate.AsContent.Location();
				}
				else
				{
					// Rooms don't have a next
					return "#-1";
				}

				// Get all contents of the location
				var contents = await location.Content(_mediator!).ToListAsync();

				// Find the current object in the list
				var currentIndex = contents.FindIndex(x => x.Object().DBRef == locate.Object().DBRef);

				if (currentIndex == -1 || currentIndex == contents.Count - 1)
				{
					// Object not found or is the last item
					return "#-1";
				}

				// Return the next object
				return contents[currentIndex + 1].Object().DBRef;
			});
	}

	[SharpFunction(Name = "nextdbref", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public async ValueTask<CallState> NextDbReference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// nextdbref() returns the next DB reference that will be assigned
		// This requires knowing the highest dbref in the database
		var allObjects = await _mediator!.CreateStream(new GetAllObjectsQuery())
			.ToListAsync();

		if (allObjects.Count == 0)
		{
			// If there are no objects, the next dbref would be #0
			return new CallState("#0:0");
		}

		// Find the highest dbref key - use DefaultIfEmpty for safety
		var maxKey = allObjects.Select(o => o.Key).DefaultIfEmpty(-1).Max();
		var nextKey = maxKey + 1;

		// Return the next dbref with timestamp 0 (will be set when created)
		return new CallState($"#{nextKey}:0");
	}

	[SharpFunction(Name = "nlsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	public async ValueTask<CallState> NumberOfListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// nlsearch() returns the count of objects matching lsearch criteria
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
		// nsearch() is an alias for nlsearch()
		return NumberOfListSearch(parser, _2);
	}

	[SharpFunction(Name = "num", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Number(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			found =>
				ValueTask.FromResult<CallState>(found.Object().DBRef));
	}

	[SharpFunction(Name = "numversion", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public ValueTask<CallState> NumVersion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Return a version number for compatibility
		// Format: YYYYMMDDHHMMSS (like PennMUSH)
		return ValueTask.FromResult<CallState>("20250102000000");
	}

	[SharpFunction(Name = "parent", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText()!;
		var arg1 = args.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (arg1 is null)
		{
			return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.All,
				async found =>
					(await found.Object().Parent.WithCancellation(CancellationToken.None)).Object()
					?.DBRef.ToString() ?? "");
		}

		if (_configuration!.CurrentValue.Function.FunctionSideEffects == false)
		{
			return Errors.ErrorNoSideFx;
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await _permissionService!.Controls(executor, target))
				{
					return Errors.ErrorPerm;
				}

				switch (args)
				{
					case { Count: 1 }:
					case { Count: 2 } when args["1"].Message!.ToPlainText()
						.Equals("none", StringComparison.InvariantCultureIgnoreCase):
						await _mediator!.Send(new UnsetObjectParentCommand(target));
						return CallState.Empty;
					default:
						return await _locateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor, args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent =>
							{
								if (!await _permissionService.Controls(executor, newParent)
								    || (!await target.HasFlag("LINK_OK")
								        && !_permissionService.PassesLock(executor, newParent, LockType.Parent)))
								{
									return Errors.ErrorPerm;
								}

								if (!await HelperFunctions.SafeToAddParent(target, newParent))
								{
									return "#-1 CYCLE DETECTED";
								}

								await _mediator!.Send(new SetObjectParentCommand(target, newParent));
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
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			x => ValueTask.FromResult<CallState>(x.Object.DBRef));
	}

	[SharpFunction(Name = "rloc", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "levels"])]
	public async ValueTask<CallState> RecursiveLocation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// rloc() recursively gets location N levels up
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var levelsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(levelsArg, out var levels) || levels < 0)
		{
			return new CallState("#-1 INVALID LEVEL");
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var current = found;
				for (var i = 0; i < levels; i++)
				{
					// Get location
					if (current.IsContent)
					{
						var location = await current.AsContent.Location();
						current = location.WithRoomOption();
					}
					else if (current.IsExit)
					{
						// Exits' location is their source room
						var location = await current.AsExit.Home.WithCancellation(CancellationToken.None);
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
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
			{
				var room = await _locateService.Room(x);
				return room.Object().DBRef;
			});
	}

	[SharpFunction(Name = "where", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Where(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async x =>
				await x.Match<ValueTask<string>>(
					async player => (await player.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
					_ => ValueTask.FromResult<string>("#-1 THIS IS A ROOM"),
					// For exits, return the location (the room containing the exit)
					async exit => (await exit.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString(),
					async thing => (await thing.Location.WithCancellation(CancellationToken.None)).Object().DBRef.ToString()));
	}

	[SharpFunction(Name = "zone", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> Zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText()!;
		var hasArg1 = args.TryGetValue("1", out var arg1Value);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async target =>
			{
				// Check if we can examine the object
				if (!await _permissionService!.CanExamine(executor, target))
				{
					return "#-1";
				}

				if (hasArg1)
				{
					// Setting zone is a side effect
					if (!_configuration!.CurrentValue.Function.FunctionSideEffects)
					{
						return Errors.ErrorNoSideFx;
					}

					// Handle zone setting like @chzone
					var arg1Str = arg1Value!.Message!.ToPlainText();
					
					// Check if removing zone (setting to "none")
					if (arg1Str.Equals("none", StringComparison.OrdinalIgnoreCase))
					{
						// Check permissions
						if (!await _permissionService!.Controls(executor, target))
						{
							return Errors.ErrorPerm;
						}
						
						await _mediator!.Send(new UnsetObjectZoneCommand(target));
						return string.Empty;
					}
					
					// Locate the zone object
					var maybeZone = await _locateService!.Locate(parser, executor, executor, arg1Str, LocateFlags.All);
					if (!maybeZone.IsValid())
					{
						return "#-1 INVALID ZONE";
					}
					
					var zone = maybeZone.AsAnyObject;
					
					// Check permissions - must control both object and zone, or pass ChZone lock
					if (!await _permissionService!.Controls(executor, target))
					{
						return Errors.ErrorPerm;
					}
					
					bool canZone = await _permissionService.Controls(executor, zone);
					if (!canZone && !_lockService!.Evaluate(LockType.ChZone, zone, executor))
					{
						return Errors.ErrorPerm;
					}
					
					// Handle flag/power stripping (simplified - no /preserve in function)
					if (!target.IsPlayer)
					{
						// Clear privileged flags
						if (await target.HasFlag("WIZARD"))
						{
							await _manipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!WIZARD", false);
						}
						if (await target.HasFlag("ROYALTY"))
						{
							await _manipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!ROYALTY", false);
						}
						if (await target.HasFlag("TRUST"))
						{
							await _manipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!TRUST", false);
						}
					}
					
					await _mediator!.Send(new SetObjectZoneCommand(target, zone));
					return string.Empty;
				}

				// Get zone of the target object - query fresh from database
				var freshTarget = await _mediator!.Send(new GetObjectNodeQuery(target.Object().DBRef));
				var zoneObj = await freshTarget.Known.Object().Zone.WithCancellation(CancellationToken.None);
				return zoneObj.IsNone 
					? "#-1" 
					: zoneObj.Known.Object().DBRef.ToString();
			});
	}

	[SharpFunction(Name = "xthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var things = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", things);
			});
	}

	[SharpFunction(Name = "xvcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(_mediator!)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var contents = await locate.AsContainer.Content(_mediator!)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", contents);
			});
	}

	[SharpFunction(Name = "xexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var exits = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", exits);
			});
	}

	[SharpFunction(Name = "xplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ExtractPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return "#-1 INVALID ARGUMENTS";
		}

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var players = await  locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", players);
			});
	}

	[SharpFunction(Name = "lcon", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			 locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return string.Join(" ",  locate.AsContainer.Content(_mediator!)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			 locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return string.Join(" ", locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.Select(x => x.Object().DBRef.ToString()));
			});
	}

	[SharpFunction(Name = "lvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var visibleContents = await locate.AsContainer.Content(_mediator!)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleContents);
			});
	}

	[SharpFunction(Name = "lvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var visibleExits = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleExits);
			});
	}

	[SharpFunction(Name = "lvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var visiblePlayers = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visiblePlayers);
			});
	}

	[SharpFunction(Name = "lvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> ListVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				var visibleThings = await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleThings);
			});
	}

	[SharpFunction(Name = "orflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> OrFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orflags() checks if object has ANY of the specified flags
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var flags = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				foreach (var flag in flags)
				{
					if (await found.HasFlag(flag))
					{
						return new CallState(true);
					}
				}
				return new CallState(false);
			});
	}

	[SharpFunction(Name = "orlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> OrListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orlflags() checks a list of objects to see if ANY have ANY of the flags
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var flags = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		foreach (var objRef in objList)
		{
			var maybeObj = await _locateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (maybeObj.IsValid())
			{
				var found = maybeObj.AsAnyObject;
				foreach (var flag in flags)
				{
					if (await found.HasFlag(flag))
					{
						return new CallState(true);
					}
				}
			}
		}
		return new CallState(false);
	}

	[SharpFunction(Name = "orlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public async ValueTask<CallState> OrListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orlpowers() checks a list of objects to see if ANY have ANY of the powers
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var powersArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var powers = powersArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		foreach (var objRef in objList)
		{
			var maybeObj = await _locateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (maybeObj.IsValid())
			{
				var found = maybeObj.AsAnyObject;
				foreach (var power in powers)
				{
					if (await found.HasPower(power))
					{
						return new CallState(true);
					}
				}
			}
		}
		return new CallState(false);
	}

	[SharpFunction(Name = "andflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> AndFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andflags() checks if object has ALL of the specified flags
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var flags = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
				foreach (var flag in flags)
				{
					if (!await found.HasFlag(flag))
					{
						return new CallState(false);
					}
				}
				return new CallState(true);
			});
	}

	[SharpFunction(Name = "andlflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> AndListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andlflags() checks if ALL objects in list have ALL of the flags
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var flags = flagsArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (objList.Length == 0)
		{
			return new CallState(false);
		}

		foreach (var objRef in objList)
		{
			var maybeObj = await _locateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (!maybeObj.IsValid())
			{
				return new CallState(false);
			}

			var found = maybeObj.AsAnyObject;
			foreach (var flag in flags)
			{
				if (!await found.HasFlag(flag))
				{
					return new CallState(false);
				}
			}
		}
		return new CallState(true);
	}

	[SharpFunction(Name = "andlpowers", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "powers"])]
	public async ValueTask<CallState> AndListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var powersArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var powers = powersArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		if (objList.Length == 0)
		{
			return new CallState(false);
		}

		foreach (var objRef in objList)
		{
			var maybeObj = await _locateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (!maybeObj.IsValid())
			{
				return new CallState(false);
			}

			var found = maybeObj.AsAnyObject;
			foreach (var power in powers)
			{
				if (!await found.HasPower(power))
				{
					return new CallState(false);
				}
			}
		}
		return new CallState(true);
	}

	[SharpFunction(Name = "ncon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!).CountAsync();
			});
	}

	[SharpFunction(Name = "nexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public async ValueTask<CallState> NumberOfVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator!);

		return await _locateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return Errors.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(_mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await _permissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}
}