using MoreLinq.Extensions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "atrlock", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> AtrLock(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var attributeName = args["0"].Message!.ToPlainText();

		AnySharpObjectOrErrorCallState target = args.TryGetValue("1", out var objectArg)
			? await LocateService.LocateAndNotifyIfInvalidWithCallState(parser,
				executor, executor, objectArg.Message!.ToPlainText(), LocateFlags.All)
			: executor;

		return target switch
		{
			Error<CallState> error => error.Value,
			AnySharpObject targetObj => await AttributeLockPasses(targetObj)
		};

		async ValueTask<CallState> AttributeLockPasses(AnySharpObject targetObj)
		{
			// Get the attribute's lock (stored in attrname`lock attribute)
			var lockAttrName = $"{attributeName}`LOCK";
			var lockAttr = await AttributeService.GetAttributeAsync(
				executor, targetObj, lockAttrName,
				mode: IAttributeService.AttributeMode.Read,
				parent: false);

			if (lockAttr is not SharpAttribute[] lockChain)
			{
				return "0"; // No lock set means no restriction
			}

			var lockString = lockChain.Last().Value.ToPlainText();
			if (string.IsNullOrWhiteSpace(lockString))
			{
				return "0";
			}

			var passes = await LockService.Evaluate(lockString, targetObj, executor);
			return new CallState(passes ? "1" : "0");
		}
	}

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
