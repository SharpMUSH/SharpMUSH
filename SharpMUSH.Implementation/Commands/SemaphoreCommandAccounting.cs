using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private sealed record CreatedCommandSemaphore(string Id, string Key, string Name, string LongName, int? CommandListIndex, DBRef Owner);

	private sealed record SemaphoreAccounting(Func<int, ValueTask> Persist, Func<ValueTask<bool>> Reconcile);

	private async ValueTask<OneOf<SemaphoreAccounting, Error<string>>> SemaphoreCommandAccounting(
		AnySharpObject target, string[] path, Func<int, int, long> nextCount, bool clearZero)
	{
		var token = ExecutionBudget.CurrentToken;
		var fullTarget = target.Object().DBRef;
		var original = await Mediator.CreateStream(new GetAttributeQuery(fullTarget, path), token).LastOrDefaultAsync(token);
		var oldCount = 0;
		var originalValue = original?.Value.ToPlainText();
		if (!string.IsNullOrEmpty(originalValue) && !int.TryParse(originalValue, out oldCount))
			return new Error<string>($"Semaphore attribute must have a numeric or empty value. Current value: {originalValue}");
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)), token)).AsPlayer;
		int? expected = null;
		CreatedCommandSemaphore? createdIdentity = null;
		var observedFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		async ValueTask ValidateCreated(SharpAttribute attribute)
		{
			var currentToken = ExecutionBudget.CurrentToken;
			var owner = attribute.Owner is null ? null : await attribute.Owner.WithCancellation(currentToken);
			var flags = attribute.Flags.Select(flag => flag.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (owner?.Object.DBRef != god.Object.DBRef || attribute.CommandListIndex is not null
				|| !attribute.Name.Equals(path[^1], StringComparison.OrdinalIgnoreCase)
				|| !attribute.LongName.Equals(string.Join('`', path), StringComparison.OrdinalIgnoreCase)
				|| !int.TryParse(attribute.Value.ToPlainText(), out var count) || count != expected
				|| flags.Any(flag => !SemaphoreAttributes.RequiredFlagNames.Contains(flag, StringComparer.OrdinalIgnoreCase))
				|| !observedFlags.IsSubsetOf(flags)
				|| attribute.Leaves is not null && await (await attribute.Leaves.WithCancellation(currentToken)).AnyAsync(currentToken))
				throw new InvalidOperationException("Created semaphore changed; refusing command metadata repair.");
			var identity = new CreatedCommandSemaphore(attribute.Id, attribute.Key, attribute.Name, attribute.LongName,
				attribute.CommandListIndex, owner.Object.DBRef);
			if (createdIdentity is not null && createdIdentity != identity)
				throw new InvalidOperationException("Created semaphore identity changed; refusing command metadata repair.");
			createdIdentity = identity;
			observedFlags.UnionWith(flags);
		}

		async ValueTask Persist(int selected)
		{
			expected = checked((int)nextCount(oldCount, selected));
			if (original is null && clearZero && expected == 0) return;
			var written = clearZero && expected == 0
				? await Mediator.Send(new ClearAttributeCommand(fullTarget, path), ExecutionBudget.CurrentToken)
				: await Mediator.Send(new SetAttributeCommand(fullTarget, path, MarkupText.Plain(expected.Value.ToString()), god), ExecutionBudget.CurrentToken);
			if (!written) throw new InvalidOperationException("Semaphore count update failed.");
			if (original is null && !(clearZero && expected == 0))
				await SemaphoreAttributes.InitializeAsync(Mediator, fullTarget, path, ValidateCreated);
		}
		async ValueTask<bool> Reconcile()
		{
			if (expected is null) return false; // No provider write was attempted.
			var currentToken = ExecutionBudget.CurrentToken;
			var current = await Mediator.CreateStream(new GetAttributeQuery(fullTarget, path), currentToken).LastOrDefaultAsync(currentToken);
			if (current is null)
			{
				if (original is null) return false;
				throw new InvalidOperationException("Semaphore disappeared during command reconciliation.");
			}
			if (!int.TryParse(current.Value.ToPlainText().Length == 0 ? "0" : current.Value.ToPlainText(), out var value))
				throw new InvalidOperationException("Semaphore count changed during command reconciliation.");
			if (value == expected)
			{
				if (original is null)
					await SemaphoreAttributes.InitializeAsync(Mediator, fullTarget, path, ValidateCreated);
				var validation = await ValidateSemaphoreAttribute(target, path);
				if (validation.IsT1) throw new InvalidOperationException("Semaphore metadata is incomplete or changed; repair it before retrying: " + validation.AsT1.Value);
				return true;
			}
			if (value == oldCount && original is not null) return false;
			throw new InvalidOperationException("Semaphore count is neither its original nor committed value; manual reconciliation is required.");
		}
		return new SemaphoreAccounting(Persist, Reconcile);
	}
}
