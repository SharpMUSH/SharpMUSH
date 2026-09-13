using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services;

public partial class LockService
{
	public ValueTask<Result<string>> BindAsync(string expression, AnySharpObject executor, CancellationToken cancellationToken = default)
		=> bep.BindAsync(expression, executor, cancellationToken);

	public bool IsBound(string expression) => bep.IsBound(expression);

	public async ValueTask<Result<string>> ResolveWriteNameAsync(AnySharpObject target, string name, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(name)) return "Basic";
		var canonical = LockNames.Canonical(name);
		if (SystemLocks.ContainsKey(canonical) || target.Object().Locks.ContainsKey(canonical)) return canonical;
		if (!name.StartsWith("user:", StringComparison.OrdinalIgnoreCase) && await LookupAsync(target, canonical, cancellationToken) is ResolvedLock) return canonical;
		if (!name.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) return new Error<string>("Unknown lock type.");
		var custom = name[5..].ToUpperInvariant();
		if (custom.Length is 0 or > 1024 || custom.StartsWith('`') || custom.EndsWith('`') || custom.Contains("``") ||
			custom.Any(c => !"!\"#$&'*+,-./0123456789;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ_`~".Contains(c)))
			return new Error<string>("That is not a valid lock name.");
		return LockNames.Canonical(custom);
	}

	public async ValueTask<Result<Success>> SetAsync(AnySharpObject executor, AnySharpObject target, string name, string expression, CancellationToken cancellationToken = default)
	{
		if (expression.Length == 0) return await UnsetAsync(executor, target, name, cancellationToken);
		return await mediator.Send(new SetLockCommand(target.Object(), name, expression, executor), cancellationToken);
	}

	public ValueTask<Result<Success>> UnsetAsync(AnySharpObject executor, AnySharpObject target, string name, CancellationToken cancellationToken = default)
		=> mediator.Send(new UnsetLockCommand(target.Object(), name, executor), cancellationToken);

	public async ValueTask<Result<Success>> SetFlagsAsync(AnySharpObject executor, AnySharpObject target, string name, string flags, CancellationToken cancellationToken = default)
	{
		var canonical = LockNames.Canonical(name);
		if (!target.Object().Locks.TryGetValue(canonical, out var data) || !await permissions.Value.CanReadLock(executor, target, data.Flags))
			return new Error<string>("No such lock.");
		var clear = flags.StartsWith('!');
		var input = clear ? flags[1..] : flags;
		var yes = LockFlags.Default;
		var no = LockFlags.Default;
		var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		foreach (var word in words)
		{
			var subtract = word.StartsWith('!');
			var token = subtract ? word[1..] : word;
			if (token.Length == 0) continue;
			var matched = token.Length == 1 ? LockPrivileges.Values.FirstOrDefault(x => x.Item1 == token).Item2 : LockFlags.Default;
			if (matched == 0) matched = LockPrivileges.FirstOrDefault(x => x.Key.StartsWith(token, StringComparison.OrdinalIgnoreCase)).Value.Item2;
			if (subtract) no |= matched;
			else yes |= matched;
		}
		if (yes == 0 && no == 0 && words.Length == 1)
		{
			var subtract = false;
			foreach (var letter in input)
			{
				if (letter == '!') { subtract = true; continue; }
				var bit = LockPrivileges.Values.FirstOrDefault(x => x.Item1 == letter.ToString()).Item2;
				if (subtract) no |= bit;
				else yes |= bit;
				subtract = false;
			}
		}
		// Penn do_lset applies one mask; inner negation excludes bits from that mask.
		var selected = yes & ~no;
		if (selected == 0 || selected.HasFlag(LockFlags.Wizard) && !await executor.IsSee_All())
			return new Error<string>("Unrecognized lock flag.");
		return await mediator.Send(new SetLockFlagsCommand(target.Object(), canonical, selected, clear, executor), cancellationToken);
	}

	public async ValueTask<bool> CanWriteAsync(AnySharpObject executor, AnySharpObject target, SharpLockData data)
	{
		if (!await permissions.Value.Controls(executor, target)) return false;
		if (executor.IsGod()) return true;
		if (target.IsGod()) return false;
		if (await executor.IsWizard()) return true;
		if (data.Flags.HasFlag(LockFlags.Wizard)) return false;
		var owner = await target.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		if (data.Flags.HasFlag(LockFlags.Owner) && executor.Object().DBRef != owner.Object.DBRef) return false;
		if (!data.Flags.HasFlag(LockFlags.Locked)) return true;
		if (data.Creator is not { } creator) return false;
		var executorOwner = await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return executor.Object().DBRef.Matches(creator) || executorOwner.Object.DBRef.Matches(creator);
	}

	public async ValueTask<Found<ResolvedLock>> LookupAsync(AnySharpObject target, string name, CancellationToken cancellationToken = default)
	{
		if (name.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) name = name[5..];
		name = LockNames.Canonical(string.IsNullOrEmpty(name) ? "Basic" : name);
		var current = target;
		var seen = new HashSet<DBRef>();
		var ancestorNumber = target switch
		{
			SharpPlayer => options.CurrentValue.Database.AncestorPlayer,
			SharpRoom => options.CurrentValue.Database.AncestorRoom,
			SharpExit => options.CurrentValue.Database.AncestorExit,
			_ => options.CurrentValue.Database.AncestorThing
		};
		var usedAncestor = false;
		for (var count = 0; count <= 100; count++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!seen.Add(current.Object().DBRef)) return new NotFound();
			if (current.Object().Locks.TryGetValue(name, out var data))
			{
				if (current.Object().DBRef != target.Object().DBRef && data.Flags.HasFlag(LockFlags.Private)) return new NotFound();
				return new ResolvedLock(name, current, data);
			}
			if (await current.Object().Parent.WithCancellation(cancellationToken) is AnySharpObject parent)
			{
				current = parent;
				continue;
			}
			if (usedAncestor || ancestorNumber is null || await target.HasFlag("ORPHAN")) return new NotFound();
			usedAncestor = true;
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef((int)ancestorNumber.Value)), cancellationToken) is not AnySharpObject ancestor)
				return new NotFound();
			current = ancestor;
		}
		return new NotFound();
	}

	public async ValueTask<bool> EvaluateType(string name, AnySharpObject target, AnySharpObject unlocker)
		=> await LookupAsync(target, name, ExecutionBudget.CurrentToken) switch
		{
			ResolvedLock resolved => await Evaluate(resolved.Data.LockString, target, unlocker),
			_ => await Evaluate("#TRUE", target, unlocker)
		};
}
