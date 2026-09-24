using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// Reads, and with a second argument writes, an attribute's LOCKED flag.
	/// </summary>
	/// <remarks>
	/// An attribute-flag function despite the name, not a boolexp-lock one: <c>fun_atrlock</c>
	/// (<c>src/fundb.c:2409</c>) takes the whole <c>&lt;object&gt;/&lt;attribute&gt;</c> spec in
	/// argument 0 and answers <c>AF_Locked(ptr)</c>. This read argument 0 as a bare attribute name
	/// and argument 1 as an object, then evaluated a synthetic <c>&lt;attribute&gt;`LOCK</c>
	/// attribute as a boolexp — a lock nothing in the engine writes, so it answered 0 for everything
	/// — and had no side-effect form at all.
	///
	/// <para>The write is <c>do_atrlock</c> (<c>src/attrib.c:2466</c>), which is what
	/// <c>@ATRLOCK</c> runs; PennMUSH gates the function form on
	/// <c>command_check_byname(executor, "@atrlock")</c> (<c>:2428</c>) on top of the global
	/// side-effects switch, which is what <see cref="CanInvokeLockCommandAsync"/> is for. Locking
	/// also reassigns the attribute to the locker's owner (<c>src/attrib.c:2532</c>).</para>
	/// </remarks>
	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2,
		Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi,
		SideEffectMinArgs = 2, ParameterNames = ["object/attribute", "on|off"])]
	public async ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// do_atrlock reads the on/off word before it looks at anything else (src/attrib.c:2474-2485).
		var action = args.TryGetValue("1", out var actionArg) ? actionArg.Message!.ToPlainText() : null;
		if (string.IsNullOrEmpty(action)) action = null;

		bool? shouldLock = null;
		if (action is not null)
		{
			if (AttributeLockSwitch.Parse(action) is not bool parsed)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.InvalidArgument), executor);
				return new CallState(ErrorMessages.Returns.InvalidValue);
			}

			shouldLock = parsed;
			if (!await CanInvokeLockCommandAsync(parser, executor, "@ATRLOCK"))
			{
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}
		}

		if (HelperFunctions.SplitDbRefAndOptionalAttr(args["0"].Message!.ToPlainText())
			is not { Object: var dbref, Attribute: { } attributeName })
		{
			return new CallState(ArgumentMustBeObjectAttribute);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, dbref, LocateFlags.All,
			async target => await AttributeLockAsync(executor, target, attributeName, shouldLock));
	}

	private async ValueTask<CallState> AttributeLockAsync(
		AnySharpObject executor, AnySharpObject target, string attributeName, bool? shouldLock)
	{
		if (shouldLock is not null && !await PermissionService.Controls(executor, target))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// atr_get_noparent plus Can_Read_Attr: an attribute that is not there, and one the caller may
		// not read, are the same bare #-1 (src/fundb.c:2452-2456).
		if (await AttributeService.GetAttributeAsync(executor, target, attributeName,
				IAttributeService.AttributeMode.Read, parent: false) is not SharpAttribute[] chain)
		{
			return new CallState("#-1");
		}

		if (shouldLock is not bool lockIt)
		{
			return new CallState(AttributeLockHelpers.IsLocked(chain) ? "1" : "0");
		}

		return await AttributeLockHelpers.ChangeAsync(PermissionService, AttributeService, NotifyService, Mediator,
			executor, target, attributeName, chain, lockIt);
	}

	/// <summary>
	/// <c>fun_atrlock</c>'s own wording (<c>src/fundb.c:2437,2441</c>), which no other function
	/// says; a constant in <c>ErrorMessages</c> would be a shared file this change has no other
	/// reason to touch.
	/// </summary>
	private const string ArgumentMustBeObjectAttribute = "#-1 ARGUMENT MUST BE OBJ/ATTR";

	[SharpFunction(Name = "testlock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> TestLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		// PennMUSH: testlock(<lock key>, <victim>) - test a lock expression against a victim
		var lockString = args["0"].Message!.ToPlainText();
		var victimName = args["1"].Message!.ToPlainText();

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser,
			executor, executor, victimName, LocateFlags.All,
			async victim =>
			{
				if (await BooleanExpressionParser.BindAsync(lockString, executor) is not string expression)
				{
					return new CallState("#-1 INVALID BOOLEXP");
				}

				// Evaluate the lock: does victim pass the lock expression?
				if (!await PermissionService.CanLocate(executor, victim)) return new CallState(ErrorMessages.Returns.PermissionDenied);
				var passes = await LockService.Evaluate(expression, executor, victim);
				return new CallState(passes ? "1" : "0");
			});
	}

	[SharpFunction(Name = "lock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, SideEffectMinArgs = 2, ParameterNames = ["object", "expression"])]
	public async ValueTask<CallState> Lock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (parser.CurrentState.Arguments.TryGetValue("1", out var expression))
		{
			var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
			if (!await CanInvokeLockCommandAsync(parser, executor, "@LOCK")) return new CallState(ErrorMessages.Returns.PermissionDenied);
			var parts = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Split('/', 2);
			var located = await LocateService.Locate(parser, executor, executor, parts[0], LocateFlags.All);
			if (located is not AnySharpObject target) return new CallState(LocateFailure(located));
			if (await LockService.SetAsync(executor, target, parts.Length > 1 ? parts[1] : "Basic", expression.Message!.ToPlainText()) is Error<string> error)
				await NotifyService.Notify(executor, error.Value);
		}
		return await ReadLockAsync(parser, ErrorMessages.Returns.PermissionDenied, async (executor, _, resolved) => new CallState(resolved is null
			? "*UNLOCKED*" : await BooleanExpressionParser.RenderAsync(resolved.Data.LockString, executor, LockRenderMode.Readback)));
	}

	[SharpFunction(Name = "elock", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object", "victim"])]
	public ValueTask<CallState> EvaluateLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReadLockAsync(parser, ErrorMessages.Returns.PermissionDenied, async (executor, target, resolved) =>
		{
			var victimName = parser.CurrentState.Arguments["1"].Message!.ToPlainText();
			return await LocateService.Locate(parser, executor, executor, victimName, LocateFlags.All) switch
			{
				AnySharpObject victim => new CallState(resolved is null || await LockService.Evaluate(resolved.Data.LockString, target, victim) ? "1" : "0"),
				var missing => new CallState(LocateFailure(missing))
			};
		});

	[SharpFunction(Name = "lset", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX | FunctionFlags.StripAnsi, ParameterNames = ["object", "flags"])]
	public async ValueTask<CallState> SetLockFlagsFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await CanInvokeLockCommandAsync(parser, executor, "@LSET")) return new CallState(ErrorMessages.Returns.PermissionDenied);
		var parts = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Split('/', 2);
		if (parts.Length < 2)
		{
			await NotifyService.Notify(executor, "No lock name given.");
			return CallState.Empty;
		}
		var located = await LocateService.Locate(parser, executor, executor, parts[0], LocateFlags.All);
		if (located is not AnySharpObject target) return new CallState(LocateFailure(located));
		if (await LockService.SetFlagsAsync(executor, target, parts.Length > 1 ? parts[1] : "Basic", parser.CurrentState.Arguments["1"].Message!.ToPlainText()) is Error<string> error)
			await NotifyService.Notify(executor, error.Value);
		return CallState.Empty;
	}

	private async ValueTask<bool> CanInvokeLockCommandAsync(IMUSHCodeParser parser, AnySharpObject executor, string command)
	{
		if (!parser.CommandLibrary.TryGetValue(command, out var definition)) return false;
		var attribute = definition.LibraryInformation.Attribute;
		if (attribute.Behavior.HasFlag(CommandBehavior.NoOp) || attribute.Behavior.HasFlag(CommandBehavior.Internal)) return false;
		if (!await SharpMUSH.Library.Services.CommandRestrictions.PermitsAsync(attribute, executor)) return false;
		return string.IsNullOrEmpty(attribute.CommandLock) || await LockService.Evaluate(attribute.CommandLock, executor, executor);
	}

	private async ValueTask<CallState> ReadLockAsync(IMUSHCodeParser parser, string denied,
		Func<AnySharpObject, AnySharpObject, ResolvedLock?, ValueTask<CallState>> read)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var parts = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Split('/', 2);
		var name = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : "Basic";
		if (name.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) name = name[5..];
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, parts[0], LocateFlags.All,
			async target =>
			{
				var resolved = await LockService.LookupAsync(target, name) is ResolvedLock found ? found : null;
				if (!await PermissionService.CanReadLock(executor, target, resolved?.Data.Flags ?? 0)) return new CallState(denied);
				return await read(executor, target, resolved);
			});
	}

	[SharpFunction(Name = "llockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> LockFlags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReadLockFlagsAsync(parser, true);

	[SharpFunction(Name = "lockflags", MinArgs = 0, MaxArgs = 1,
		Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> LockFlagsObject(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReadLockFlagsAsync(parser, false);

	private ValueTask<CallState> ReadLockFlagsAsync(IMUSHCodeParser parser, bool fullNames)
	{
		string Format(Library.Services.LockService.LockFlags flags) => fullNames
			? string.Join(" ", LockService.LockPrivileges.Where(x => flags.HasFlag(x.Value.Item2)).Select(x => x.Key))
			: LockService.FormatLockFlags(flags);
		if (parser.CurrentState.Arguments.Count == 0 ||
			parser.CurrentState.Arguments.Count == 1 && string.IsNullOrEmpty(parser.CurrentState.Arguments["0"].Message?.ToPlainText()))
			return ValueTask.FromResult(new CallState(fullNames ? string.Join(" ", LockService.LockPrivileges.Keys) : string.Concat(LockService.LockPrivileges.Values.Select(x => x.Item1))));
		return ReadLockAsync(parser, "#-1 NO SUCH LOCK", (_, _, resolved) =>
			ValueTask.FromResult(new CallState(resolved is null ? "#-1 NO SUCH LOCK" : Format(resolved.Data.Flags))));
	}

	[SharpFunction(Name = "llocks", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> Locks(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListLocksAsync(parser);

	[SharpFunction(Name = "locks", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> LocksRequired(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ListLocksAsync(parser);

	private async ValueTask<CallState> ListLocksAsync(IMUSHCodeParser parser)
	{
		if (parser.CurrentState.Arguments.Count == 0 ||
			parser.CurrentState.Arguments.Count == 1 && string.IsNullOrEmpty(parser.CurrentState.Arguments["0"].Message?.ToPlainText())) return new CallState(string.Join(" ", LockService.SystemLocks.Keys.Select(LockNames.Display)));
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var objectName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, objectName, LocateFlags.All,
			target => ValueTask.FromResult(new CallState(string.Join(" ", target.Object().Locks.Keys.Order(StringComparer.Ordinal)
				.Select(name => LockService.SystemLocks.ContainsKey(name) ? LockNames.Display(name) : "USER:" + name)))));
	}

	[SharpFunction(Name = "lockfilter", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["expression", "dbrefs", "delimiter"])]
	public async ValueTask<CallState> LockFilter(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		if (await BooleanExpressionParser.BindAsync(args["0"].Message!.ToPlainText(), executor) is not string expression)
			return new CallState("#-1 INVALID BOOLEXP");
		var delimiter = args.TryGetValue("2", out var separator) ? separator.Message!.ToPlainText() : " ";
		if (delimiter.Length != 1) return new CallState("#-1 SEPARATOR MUST BE ONE CHARACTER");
		var results = new List<string>();
		foreach (var reference in args["1"].Message!.ToPlainText().Split(delimiter, StringSplitOptions.RemoveEmptyEntries))
		{
			if (!DBRef.TryParse(reference.Trim(), out _)) continue;
			if (await LocateService.Locate(parser, executor, executor, reference.Trim(), LocateFlags.All) is not AnySharpObject victim || !await PermissionService.CanLocate(executor, victim)) continue;
			if (await LockService.Evaluate(expression, executor, victim)) results.Add($"#{victim.Object().DBRef.Number}");
		}
		return new CallState(string.Join(delimiter, results));
	}

	[SharpFunction(Name = "lockowner", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi, ParameterNames = ["object"])]
	public ValueTask<CallState> LockOwner(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ReadLockAsync(parser, "#-1 NO SUCH LOCK", (_, _, resolved) => ValueTask.FromResult(new CallState(resolved is null
			? "#-1 NO SUCH LOCK" : resolved.Data.Creator is { } creator ? $"#{creator.Number}" : "#-1")));
}

/// <summary>
/// The words <c>@atrlock</c> and <c>atrlock()</c> both accept for "lock" and "unlock"
/// (<c>src/attrib.c:2474-2485</c>). One declaration so the command and the function cannot drift.
/// </summary>
internal static class AttributeLockSwitch
{
	public static bool? Parse(string value) => value.Trim().ToLowerInvariant() switch
	{
		"on" or "yes" or "1" => true,
		"off" or "no" or "0" => false,
		_ => null
	};
}
