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
	public static async ValueTask<CallState> Location(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText()!;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, arg0,
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
				var linkTypeAttr = await AttributeService!.GetAttributeAsync(executor, exit, AttrLinkType, IAttributeService.AttributeMode.Read, false);

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
	public static async ValueTask<CallState> Children(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
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
	public static async ValueTask<CallState> Con(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return CallState.Empty;
				}

				var contents = await locate.AsContainer.Content(Mediator!)
					.Take(1)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();
				return string.Join(" ", contents);
			});
	}

	[SharpFunction(Name = "controls", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public static async ValueTask<CallState> Controls(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var arg1Split = arg1.Split('/');
		var isAttributeCheck = arg1Split.Length > 1;

		var maybeLocateObject = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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

			var locateAttribute = await AttributeService!.GetAttributeAsync(executor, attributeObject, attribute,
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

			var controlsAttribute = await PermissionService!.Controls(locateObject, attributeObject, foundAttribute);

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

		var controls = await PermissionService!.Controls(locateObject, locateVictim);

		return controls;
	}

	[SharpFunction(Name = "entrances", MinArgs = 0, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Entrances(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var locArg))
		{
			var locStr = locArg.Message!.ToPlainText();
			var maybeTarget = await LocateService!.Locate(parser, executor, executor, locStr, LocateFlags.All);
			if (!maybeTarget.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidLocation);
			}
			target = maybeTarget.AsAnyObject;
		}

		var exits = Mediator!.CreateStream(new GetEntrancesQuery(target.Object().DBRef));
		var entrances = new List<AnySharpObject>();

		await foreach (var exit in exits)
		{
			entrances.Add(exit);
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

		var filtered = entrances.Where(entrance =>
		{
			var obj = entrance.Object();
			var dbrefNum = obj.DBRef.Number;

			if (dbrefNum < beginFilter || dbrefNum > endFilter)
			{
				return false;
			}

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
	public static async ValueTask<CallState> Exit(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
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
					return ErrorMessages.Returns.ErrorNotARoom;
				}

				var exits = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef)
					.ToArrayAsync();

				return exits.Length != 0
					? exits.First().ToString()
					: string.Empty;
			});
	}

	[SharpFunction(Name = "followers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Followers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var targetDbref = found.Object().DBRef.ToString();
				var followers = new List<string>();

				var allObjects = Mediator!.CreateStream(new GetAllObjectsQuery());

				await foreach (var obj in allObjects)
				{
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
	public static async ValueTask<CallState> Following(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var followingAttr = await AttributeService!.GetAttributeAsync(
					executor, found, "FOLLOWING", IAttributeService.AttributeMode.Read, false);

				if (followingAttr.IsAttribute)
				{
					return new CallState(followingAttr.AsAttribute.Last().Value.ToPlainText());
				}

				return new CallState(string.Empty);
			});
	}

	[SharpFunction(Name = "home", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Home(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var locateResult = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
			async exit => (await exit.Home.WithCancellation(CancellationToken.None)).Object().DBRef,
			async thing => (await thing.Home.WithCancellation(CancellationToken.None)).Object().DBRef
		);
	}

	[SharpFunction(Name = "llockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		await ValueTask.CompletedTask;
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			var flags = LockService!.LockPrivileges.Keys;
			return new CallState(string.Join(" ", flags));
		}

		var lockType = args["0"].Message!.ToPlainText();
		if (LockService!.SystemLocks.TryGetValue(lockType, out var lockFlags))
		{
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
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> LockFlagsObject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objectRef, LocateFlags.All,
			async found =>
			{
				// Get the lock data (case-insensitive per PennMUSH)
				var lockKey = found.Object().Locks.Keys
					.FirstOrDefault(k => string.Equals(k, lockType, StringComparison.OrdinalIgnoreCase));
				if (lockKey == null || !found.Object().Locks.TryGetValue(lockKey, out var lockData))
				{
					return new CallState("#-1 NO SUCH LOCK");
				}

				// PennMUSH Can_Read_Lock permission check
				if (!await PermissionService!.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1 NO SUCH LOCK");
				}

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

				return new CallState(new string(flagChars.ToArray()));
			});
	}

	[SharpFunction(Name = "elock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public static async ValueTask<CallState> EvaluateLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH format: elock(<object>/<lock name>, <victim>)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var victimArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var victimResult = await LocateService!.Locate(parser, executor, executor, victimArg, LocateFlags.All);
				if (!victimResult.IsValid())
				{
					return new CallState("#-1");
				}
				var victim = victimResult.AsAnyObject;

				// Get the lock string from the object (case-insensitive per PennMUSH)
				var lockKey = found.Object().Locks.Keys
					.FirstOrDefault(k => string.Equals(k, lockName, StringComparison.OrdinalIgnoreCase));
				if (lockKey == null || !found.Object().Locks.TryGetValue(lockKey, out var lockData))
				{
					// No lock set = passes (TRUE_BOOLEXP)
					return new CallState("1");
				}

				// PennMUSH Can_Read_Lock: See_All || controls || ((Visual || lock visual) && passes Examine lock)
				if (!await PermissionService!.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1");
				}

				var result = LockService!.Evaluate(lockData.LockString, found, victim);
				return new CallState(result ? "1" : "0");
			});
	}

	[SharpFunction(Name = "llocks", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		AnySharpObject target = executor;
		if (args.TryGetValue("0", out var objArg))
		{
			var objStr = objArg.Message!.ToPlainText();
			var maybeTarget = await LocateService!.Locate(parser, executor, executor, objStr, LocateFlags.All);
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
	public static async ValueTask<CallState> LocksRequired(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objStr = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		var maybeTarget = await LocateService!.Locate(parser, executor, executor, objStr, LocateFlags.All);
		if (!maybeTarget.IsValid())
		{
			return new CallState(ErrorMessages.Returns.InvalidObject);
		}
		var target = maybeTarget.AsAnyObject;

		var lockNames = target.Object().Locks.Keys;
		return new CallState(string.Join(" ", lockNames));
	}

	[SharpFunction(Name = "localize", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse, ParameterNames = ["string"])]
	public static async ValueTask<CallState> Localize(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await parser
				 .With(
					 x => x with { Registers = new([[]]) },
					 newParser => newParser.FunctionParse(parser.CurrentState.Arguments["0"].Message!))
			 ?? CallState.Empty;

	[SharpFunction(Name = "locate", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["player", "name", "type"])]
	public static async ValueTask<CallState> Locate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var lookerArg = args["0"].Message!.ToPlainText();
		var nameArg = args["1"].Message!.ToPlainText();
		var parametersArg = args["2"].Message!.ToPlainText();

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var maybeLooker =
			await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, lookerArg,
				LocateFlags.All);
		if (maybeLooker.IsError)
		{
			return maybeLooker.AsError;
		}

		var looker = maybeLooker.AsSharpObject;

		var locateFlags = ParseLocateParameters(parametersArg);

		var preferredTypes = GetPreferredTypes(parametersArg);
		var requireExactType = parametersArg.Contains('F', StringComparison.OrdinalIgnoreCase);

		var maybeFound = await LocateService.Locate(parser, looker, executor, nameArg, locateFlags);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError.Value;
		}

		if (maybeFound.IsNone)
		{
			return "#-1";
		}

		var found = maybeFound.WithoutError().WithoutNone();

		if (preferredTypes.Any())
		{
			var foundType = GetObjectType(found);
			if (!preferredTypes.Contains(foundType))
			{
				if (requireExactType)
				{
					return "#-1";
				}
			}
		}

		return $"#{found.Object().DBRef.Number}";
	}

	private static LocateFlags ParseLocateParameters(string parameters)
	{
		var flags = LocateFlags.NoTypePreference;
		var paramUpper = parameters.ToUpperInvariant().Replace(" ", "");

		if (paramUpper.Contains('*'))
		{
			return LocateFlags.All;
		}

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

	private static string GetObjectType(AnySharpObject obj)
	{
		return obj.Match(
			_ => "PLAYER",
			_ => "ROOM",
			_ => "EXIT",
			_ => "THING"
		);
	}

	[SharpFunction(Name = "lock", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Lock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH format: lock(<object>[/<lock name>]) - slash syntax in single arg
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				// Get the lock string from the object (case-insensitive per PennMUSH)
				var lockKey = found.Object().Locks.Keys
					.FirstOrDefault(k => string.Equals(k, lockName, StringComparison.OrdinalIgnoreCase));
				if (lockKey == null || !found.Object().Locks.TryGetValue(lockKey, out var lockData))
				{
					// PennMUSH returns *UNLOCKED* for unset locks
					return new CallState("*UNLOCKED*");
				}

				// PennMUSH Can_Read_Lock permission check
				if (!await PermissionService!.CanReadLock(executor, found, lockData.Flags))
				{
					return new CallState("#-1");
				}

				return new CallState(lockData.LockString);
			});
	}

	[SharpFunction(Name = "lockfilter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["dbrefs", "lockname", "evaluate"])]
	public static async ValueTask<CallState> LockFilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
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
			var maybeObj = await LocateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
			if (!maybeObj.IsValid())
			{
				continue;
			}

			var found = maybeObj.AsAnyObject;

			// Check if object has the lock (case-insensitive per PennMUSH)
			var lockKey = found.Object().Locks.Keys
				.FirstOrDefault(k => string.Equals(k, lockName, StringComparison.OrdinalIgnoreCase));
			if (lockKey == null || !found.Object().Locks.TryGetValue(lockKey, out var lockData))
			{
				// No lock means it passes if we're looking for passes
				if (!shouldPass)
				{
					results.Add(found.Object().DBRef.ToString());
				}
				continue;
			}

			var passes = LockService!.Evaluate(lockData.LockString, found, executor);

			if (passes == shouldPass)
			{
				results.Add(found.Object().DBRef.ToString());
			}
		}

		return new CallState(string.Join(" ", results));
	}

	[SharpFunction(Name = "lockowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> LockOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// PennMUSH tracks per-lock setter; SharpMUSH returns object owner as approximation.
		// If no /lockname, defaults to Basic
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		string lockName = "Basic";
		var slashIdx = objArg.IndexOf('/');
		if (slashIdx >= 0)
		{
			lockName = objArg[(slashIdx + 1)..];
			objArg = objArg[..slashIdx];
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, objArg, LocateFlags.All,
			async found =>
			{
				var lockKey = found.Object().Locks.Keys
					.FirstOrDefault(k => string.Equals(k, lockName, StringComparison.OrdinalIgnoreCase));
				if (lockKey == null || !found.Object().Locks.TryGetValue(lockKey, out _))
				{
					// PennMUSH: lockowner on nonexistent lock returns the object itself
					return new CallState($"#{found.Object().DBRef.Number}");
				}

				var owner = await found.Object().Owner.WithCancellation(CancellationToken.None);
				return new CallState($"#{owner.Object.DBRef.Number}");
			});
	}

	[SharpFunction(Name = "lparent", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListParents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var maybeLocate = await LocateService!.LocateAndNotifyIfInvalidWithCallState(parser,
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
			if (!await PermissionService!.CanExamine(executor, knownParent))
			{
				break;
			}

			locate = knownParent;
			list.Add(knownParent.Object().DBRef);
		}

		return string.Join(" ", list);
	}

	[SharpFunction(Name = "lsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["player", "class=restriction..."])]
	public static async ValueTask<CallState> ListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return await ListSearchInternal(parser, _2, useRegex: false);
	}

	private static async ValueTask<CallState> ListSearchInternal(IMUSHCodeParser parser, SharpFunctionAttribute _2, bool useRegex)
	{
		// Per PennMUSH documentation: comma-separated positional arguments, NOT equals syntax
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;

		if (args.Count == 0)
		{
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var classArg = args["0"].Message!.ToPlainText();
		AnySharpObject? classObj = null;

		if (!classArg.Equals("all", StringComparison.OrdinalIgnoreCase))
		{
			var maybeClass = await LocateService!.Locate(parser, executor, executor, classArg, LocateFlags.All);
			if (!maybeClass.IsValid())
			{
				return new CallState(ErrorMessages.Returns.InvalidClass);
			}
			classObj = maybeClass.AsAnyObject;
		}

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

		var appLevelCriteria = new List<(string key, string value)>();

		for (int i = 1; i < args.Count; i += 2)
		{
			if (i + 1 >= args.Count)
			{
				break;
			}

			var classType = args[i.ToString()].Message!.ToPlainText().Trim().ToUpperInvariant();
			var restriction = args[(i + 1).ToString()].Message!.ToPlainText().Trim();

			if (classType.Equals("NONE", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			switch (classType)
			{
				case "TYPE":
					types.Add(restriction.ToUpperInvariant());
					break;
				case "NAME":
					namePattern = restriction;
					break;
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
					var maybeZone = await LocateService!.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeZone.IsValid()) zone = maybeZone.AsAnyObject.Object().DBRef;
					break;
				case "PARENT":
					var maybeParent = await LocateService!.Locate(parser, executor, executor, restriction, LocateFlags.All);
					if (maybeParent.IsValid()) parent = maybeParent.AsAnyObject.Object().DBRef;
					break;
				case "FLAG":
				case "FLAGS":
					hasFlag = restriction;
					break;
				case "LFLAGS":
					// LFLAGS uses space-separated flag names instead of single characters
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
						compiledLocks.Add(BooleanExpressionParser!.Compile(value));
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
					break;
			}
		}

		var listenPattern = appLevelCriteria.FirstOrDefault(x => x.key == "LISTEN").value;
		var commandPattern = appLevelCriteria.FirstOrDefault(x => x.key == "COMMAND").value;
		var hasListenCriteria = !string.IsNullOrEmpty(listenPattern);
		var hasCommandCriteria = !string.IsNullOrEmpty(commandPattern);

		var hasAppLevelCriteria = compiledLocks.Count > 0 || compiledEvals.Count > 0 || hasListenCriteria || hasCommandCriteria;

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

		var filteredObjects = Mediator!.CreateStream(new GetFilteredObjectsQuery(filter));

		if (!hasAppLevelCriteria)
		{
			var results = new List<string>();
			await foreach (var obj in filteredObjects)
			{
				results.Add(new DBRef(obj.Key, obj.CreationTime).ToString());
			}
			return new CallState(string.Join(" ", results));
		}

		// Optimize: Convert to AnySharpObject once per object and evaluate all criteria
		var finalResults = new List<string>();
		await foreach (var obj in filteredObjects)
		{
			var typedObj = await CreateAnySharpObjectFromSharpObject(obj);
			bool matches = true;

			foreach (var compiledLock in compiledLocks)
			{
				if (!compiledLock(typedObj, executor))
				{
					matches = false;
					break;
				}
			}

			if (matches)
			{
				foreach (var (evalExpression, typeFilter) in compiledEvals)
				{
					if (typeFilter != null && !typedObj.Object().Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
					{
						matches = false;
						break;
					}

					// Replace ## with the object's dbref number in the expression
					var objectDbRefNum = typedObj.Object().DBRef.Number.ToString();
					var expression = evalExpression.Replace("##", objectDbRefNum);

					var evalResult = await parser.FunctionParse(MModule.single(expression));
					if (evalResult == null || !evalResult.Message.Truthy())
					{
						matches = false;
						break;
					}
				}
			}

			if (matches && hasListenCriteria)
			{
				var attributesResult = await AttributeService!.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingListen = false;

					foreach (var attr in attributesResult.AsAttributes.Where(a => a.Name.Equals("LISTEN", StringComparison.OrdinalIgnoreCase) ||
																										 a.Name.StartsWith("LISTEN`", StringComparison.OrdinalIgnoreCase)))
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
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
					matches = false;
				}
			}

			if (matches && hasCommandCriteria)
			{
				var attributesResult = await AttributeService!.GetVisibleAttributesAsync(executor, typedObj);
				if (!attributesResult.IsError)
				{
					var hasMatchingCommand = false;

					foreach (var attr in attributesResult.AsAttributes)
					{
						var attrValue = attr.Value?.ToPlainText() ?? "";
						// $-commands are in format: $command-pattern:action
						var dollarIndex = attrValue.IndexOf('$');
						if (dollarIndex >= 0)
						{
							var colonIndex = attrValue.IndexOf(':', dollarIndex);
							if (colonIndex > dollarIndex)
							{
								var commandPart = attrValue.AsSpan(dollarIndex + 1, colonIndex - dollarIndex - 1).ToString();
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
					matches = false;
				}
			}

			if (matches)
			{
				finalResults.Add(typedObj.Object().DBRef.ToString());
			}
		}

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
	private static bool IsWildcardMatch(string value, string pattern)
	{
		var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
		return System.Text.RegularExpressions.Regex.IsMatch(value, regexPattern,
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
	}

	/// <summary>
	/// Creates an AnySharpObject from a SharpObject based on its Type property.
	/// This is needed when we have a raw SharpObject from the database but need to work with the discriminated union.
	/// </summary>
	private static async Task<AnySharpObject> CreateAnySharpObjectFromSharpObject(SharpObject obj)
	{
		var dbref = new DBRef(obj.Key, obj.CreationTime);
		var result = await Mediator!.Send(new GetObjectNodeQuery(dbref));

		if (result.IsNone)
		{
			// This shouldn't happen in normal operation, but handle it gracefully
			throw new InvalidOperationException($"Object {dbref} not found when evaluating lock criteria");
		}

		return result.Known;
	}

	[SharpFunction(Name = "lsearchr", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["object", "class=restriction..."])]
	public static async ValueTask<CallState> ListSearchRegex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var originalArgs = parser.CurrentState.Arguments;

		return await ListSearchInternal(parser, _2, useRegex: true);
	}

	[SharpFunction(Name = "namelist", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["list", "attribute"])]
	public static async ValueTask<CallState> NameList(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
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

				var objResult = await LocateService!.Locate(parser, executor, executor, objPart, LocateFlags.All);
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
				var exists = await Mediator!.Send(new GetBaseObjectNodeQuery(dbref));

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

				var locateResult = await LocateService!.Locate(parser, executor, executor, name, LocateFlags.All);

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
					var attrResult = await AttributeService!.GetAttributeAsync(
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

						await parser.CommandParse(MModule.single(substitutedCommand));
					}
				}
			}
		}

		return string.Join(" ", resultList);
	}

	[SharpFunction(Name = "nchildren", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfChildren(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg1 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, arg1, LocateFlags.All,
			async x =>
			{
				var children = x.Object().Children.Value ?? AsyncEnumerable.Empty<SharpObject>();
				return await children.CountAsync();
			});
	}

	[SharpFunction(Name = "next", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Next(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				AnySharpContainer location;

				if (locate.IsExit)
				{
					var exitLocation = await locate.AsExit.Location.WithCancellation(CancellationToken.None);
					location = exitLocation;
				}
				else if (locate.IsContent)
				{
					location = await locate.AsContent.Location();
				}
				else
				{
					// Rooms don't have a next
					return "#-1";
				}

				var contents = await location.Content(Mediator!).ToListAsync();

				var currentIndex = contents.FindIndex(x => x.Object().DBRef == locate.Object().DBRef);

				if (currentIndex == -1 || currentIndex == contents.Count - 1)
				{
					return "#-1";
				}

				return contents[currentIndex + 1].Object().DBRef;
			});
	}

	[SharpFunction(Name = "nextdbref", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static async ValueTask<CallState> NextDbReference(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var allObjects = await Mediator!.CreateStream(new GetAllObjectsQuery())
			.ToListAsync();

		if (allObjects.Count == 0)
		{
			return new CallState("#0:0");
		}

		// Find the highest dbref key - use DefaultIfEmpty for safety
		var maxKey = allObjects.Select(o => o.Key).DefaultIfEmpty(-1).Max();
		var nextKey = maxKey + 1;

		// Return the next dbref with timestamp 0 (will be set when created)
		return new CallState($"#{nextKey}:0");
	}

	[SharpFunction(Name = "nlsearch", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["class=restriction..."])]
	public static async ValueTask<CallState> NumberOfListSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
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
	public static ValueTask<CallState> NumberOfSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		return NumberOfListSearch(parser, _2);
	}

	[SharpFunction(Name = "num", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Number(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
			parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			found =>
				ValueTask.FromResult<CallState>($"#{found.Object().DBRef.Number}"));
	}

	[SharpFunction(Name = "numversion", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular, ParameterNames = [])]
	public static ValueTask<CallState> NumVersion(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// Format: YYYYMMDDHHMMSS (like PennMUSH)
		return ValueTask.FromResult<CallState>("20250102000000");
	}

	[SharpFunction(Name = "parent", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Parent(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText()!;
		var arg1 = args.TryGetValue("1", out var value)
			? value.Message!.ToPlainText()
			: null;

		if (arg1 is null)
		{
			return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
				parser, executor, executor, arg0, LocateFlags.All,
				async found =>
					(await found.Object().Parent.WithCancellation(CancellationToken.None)).Object()
					?.DBRef.ToString() ?? "");
		}

		if (Configuration!.CurrentValue.Function.FunctionSideEffects == false)
		{
			return ErrorMessages.Returns.NoSideFx;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, args["0"].Message!.ToPlainText(), LocateFlags.All,
			async target =>
			{
				if (!await PermissionService!.Controls(executor, target))
				{
					return ErrorMessages.Returns.PermissionDenied;
				}

				switch (args)
				{
					case { Count: 1 }:
					case { Count: 2 } when args["1"].Message!.ToPlainText()
						.Equals("none", StringComparison.InvariantCultureIgnoreCase):
						await Mediator!.Send(new UnsetObjectParentCommand(target));
						return CallState.Empty;
					default:
						return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
							parser, executor, executor, args["1"].Message!.ToPlainText(), LocateFlags.All,
							async newParent =>
							{
								if (!await PermissionService.Controls(executor, newParent)
										|| (!await target.HasFlag("LINK_OK")
												&& !PermissionService.PassesLock(executor, newParent, LockType.Parent)))
								{
									return ErrorMessages.Returns.PermissionDenied;
								}

								if (!await HelperFunctions.SafeToAddParent(Mediator!, Database!, target, newParent))
								{
									return ErrorMessages.Returns.CycleDetected;
								}

								await Mediator!.Send(new SetObjectParentCommand(target, newParent));
								return newParent;
							}
						);
				}
			}
		);
	}

	[SharpFunction(Name = "pmatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["name"])]
	public static async ValueTask<CallState> PlayerMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			x => ValueTask.FromResult<CallState>(x.Object.DBRef));
	}

	[SharpFunction(Name = "rloc", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "levels"])]
	public static async ValueTask<CallState> RecursiveLocation(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var levelsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		if (!int.TryParse(levelsArg, out var levels) || levels < 0)
		{
			return new CallState(ErrorMessages.Returns.InvalidLevel);
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public static async ValueTask<CallState> Room(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
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
	public static async ValueTask<CallState> Where(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
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

	[SharpFunction(Name = "zone", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> Zone(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var arg0 = args["0"].Message!.ToPlainText()!;
		var hasArg1 = args.TryGetValue("1", out var arg1Value);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async target =>
			{
				if (!await PermissionService!.CanExamine(executor, target))
				{
					return "#-1";
				}

				if (hasArg1)
				{
					if (!Configuration!.CurrentValue.Function.FunctionSideEffects)
					{
						return ErrorMessages.Returns.NoSideFx;
					}

					var arg1Str = arg1Value!.Message!.ToPlainText();

					if (arg1Str.Equals("none", StringComparison.OrdinalIgnoreCase))
					{
						if (!await PermissionService!.Controls(executor, target))
						{
							return ErrorMessages.Returns.PermissionDenied;
						}

						await Mediator!.Send(new UnsetObjectZoneCommand(target));
						return string.Empty;
					}

					var maybeZone = await LocateService!.Locate(parser, executor, executor, arg1Str, LocateFlags.All);
					if (!maybeZone.IsValid())
					{
						return ErrorMessages.Returns.InvalidZone;
					}

					var zone = maybeZone.AsAnyObject;

					// Check permissions - must control both object and zone, or pass ChZone lock
					if (!await PermissionService!.Controls(executor, target))
					{
						return ErrorMessages.Returns.PermissionDenied;
					}

					bool canZone = await PermissionService.Controls(executor, zone);
					if (!canZone && !LockService!.Evaluate(LockType.ChZone, zone, executor))
					{
						return ErrorMessages.Returns.PermissionDenied;
					}

					if (!await HelperFunctions.SafeToAddZone(Mediator!, Database!, target, zone))
					{
						return ErrorMessages.Returns.ZoneLoop;
					}

					// Handle flag/power stripping (simplified - no /preserve in function)
					if (!target.IsPlayer)
					{
						if (await target.HasFlag("WIZARD"))
						{
							await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!WIZARD", false);
						}
						if (await target.HasFlag("ROYALTY"))
						{
							await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!ROYALTY", false);
						}
						if (await target.HasFlag("TRUST"))
						{
							await ManipulateSharpObjectService!.SetOrUnsetFlag(executor, target, "!TRUST", false);
						}
					}

					await Mediator!.Send(new SetObjectZoneCommand(target, zone));
					return string.Empty;
				}

				// query fresh from database
				var freshTarget = await Mediator!.Send(new GetObjectNodeQuery(target.Object().DBRef));
				var zoneObj = await freshTarget.Known.Object().Zone.WithCancellation(CancellationToken.None);
				return zoneObj.IsNone
					? "#-1"
					: zoneObj.Known.Object().DBRef.ToString();
			});
	}

	[SharpFunction(Name = "xthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var things = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", things);
			});
	}

	[SharpFunction(Name = "xvcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(Mediator!)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xvthings", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var paginated = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", paginated);
			});
	}

	[SharpFunction(Name = "xcon", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var contents = await locate.AsContainer.Content(Mediator!)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", contents);
			});
	}

	[SharpFunction(Name = "xexits", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var exits = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", exits);
			});
	}

	[SharpFunction(Name = "xplayers", MinArgs = 3, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ExtractPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var arg0 = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var arg1 = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
		var arg2 = parser.CurrentState.Arguments["2"].Message!.ToPlainText();

		if (!int.TryParse(arg1, out var start) || !int.TryParse(arg2, out var count))
		{
			return ErrorMessages.Returns.InvalidArguments;
		}

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			arg0,
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var players = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.Skip(start - 1)
					.Take(count)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", players);
			});
	}

	[SharpFunction(Name = "lcon", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			 async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var contents = await locate.AsContainer.Content(Mediator!)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();
				return string.Join(" ", contents);
			});
	}

	[SharpFunction(Name = "lexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			 async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var exits = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();
				return string.Join(" ", exits);
			});
	}

	[SharpFunction(Name = "lplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var players = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();
				return string.Join(" ", players);
			});
	}

	[SharpFunction(Name = "lthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var things = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();
				return string.Join(" ", things);
			});
	}

	[SharpFunction(Name = "lvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var visibleContents = await locate.AsContainer.Content(Mediator!)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleContents);
			});
	}

	[SharpFunction(Name = "lvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var visibleExits = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleExits);
			});
	}

	[SharpFunction(Name = "lvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var visiblePlayers = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visiblePlayers);
			});
	}

	[SharpFunction(Name = "lvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> ListVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				var visibleThings = await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.Select(x => x.Object().DBRef.ToString())
					.ToListAsync();

				return string.Join(" ", visibleThings);
			});
	}

	/// <summary>
	/// Checks a single-letter flag string (like "Wc!P") against an object.
	/// Each character is a flag symbol. Preceding '!' negates. 'P','R','T','E' check type.
	/// Returns null if the string has invalid syntax (e.g. trailing '!').
	/// orMode=false → AND (all must match). orMode=true → OR (any must match).
	/// </summary>
	private static async ValueTask<bool?> FlagLetterCheck(AnySharpObject obj, string flagStr, bool orMode)
	{
		var allFlags = await Mediator!.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();

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

			// Special pseudo-flag: 'c' = CONNECTED (runtime state, not stored flag)
			if (c == 'c')
			{
				bool connected = await ConnectionService!.IsConnected(obj);
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
	private static async ValueTask<bool?> FlagLongNameCheck(AnySharpObject obj, string[] flagTokens, bool orMode)
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
				case "PLAYER":    hasIt = obj.IsPlayer; break;
				case "ROOM":      hasIt = obj.IsRoom; break;
				case "THING":     hasIt = obj.IsThing; break;
				case "EXIT":      hasIt = obj.IsExit; break;
				case "CONNECTED": hasIt = await ConnectionService!.IsConnected(obj); break;
				default:          hasIt = await obj.HasFlag(flagName); break;
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
	public static async ValueTask<CallState> OrFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orflags() checks if object has ANY of the specified flags (single-letter format)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public static async ValueTask<CallState> OrListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// orlflags() checks if object has ANY of the specified flags (long-name format, space-separated)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public static async ValueTask<CallState> OrListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objListArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var powersArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		var objList = objListArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var powers = powersArg.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		return new CallState(await objList.ToAsyncEnumerable()
			.AnyAsync(async (objRef, _) =>
			{
				var maybeObj = await LocateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
				if (!maybeObj.IsValid()) return false;
				var found = maybeObj.AsAnyObject;
				return await powers.ToAsyncEnumerable()
					.AnyAsync(async (power, _) => await found.HasPower(power));
			}));
	}

	[SharpFunction(Name = "andflags", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public static async ValueTask<CallState> AndFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andflags() checks if object has ALL of the specified flags (single-letter format)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public static async ValueTask<CallState> AndListFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// andlflags() checks if object has ALL of the specified flags (long-name format, space-separated)
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var objArg = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var flagsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(
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
	public static async ValueTask<CallState> AndListPowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
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
				var maybeObj = await LocateService!.Locate(parser, executor, executor, objRef, LocateFlags.All);
				if (!maybeObj.IsValid()) return false;
				var found = maybeObj.AsAnyObject;
				return await powers.ToAsyncEnumerable()
					.AllAsync(async (power, _) => await found.HasPower(power));
			}));
	}

	[SharpFunction(Name = "ncon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!).CountAsync();
			});
	}

	[SharpFunction(Name = "nexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvcon", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfVisualContents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvexits", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfVisualExits(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsExit)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvplayers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfVisualPlayers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsPlayer)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}

	[SharpFunction(Name = "nvthings", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public static async ValueTask<CallState> NumberOfVisualThings(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor,
			executor,
			parser.CurrentState.Arguments["0"].Message!.ToPlainText(),
			LocateFlags.All,
			async locate =>
			{
				if (!locate.IsContainer)
				{
					return ErrorMessages.Returns.ExitsCannotContainThings;
				}

				return await locate.AsContainer.Content(Mediator!)
					.Where(x => x.IsThing)
					.Where(async (x, _) => await PermissionService!.CanSee(executor, x.Object()))
					.CountAsync();
			});
	}
}