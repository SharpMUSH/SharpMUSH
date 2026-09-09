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
	private async ValueTask<(Func<int, ValueTask> Persist, Func<ValueTask<bool>> Reconcile)> SemaphoreCommandAccounting(
		AnySharpObject target, string[] path, Func<int, int, long> nextCount, bool clearZero)
	{
		var token = ExecutionBudget.CurrentToken;
		var fullTarget = target.Object().DBRef;
		var original = await Mediator.CreateStream(new GetAttributeQuery(fullTarget, path), token).LastOrDefaultAsync(token);
		var oldCount = original is null || original.Value.Length == 0 ? 0 : int.Parse(original.Value.ToPlainText());
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)), token)).AsPlayer;
		int? expected = null;
		async ValueTask Persist(int selected)
		{
			expected = checked((int)nextCount(oldCount, selected));
			if (original is null && clearZero && expected == 0) return;
			var written = clearZero && expected == 0
				? await Mediator.Send(new ClearAttributeCommand(fullTarget, path), ExecutionBudget.CurrentToken)
				: await Mediator.Send(new SetAttributeCommand(fullTarget, path, MarkupText.Plain(expected.Value.ToString()), god), ExecutionBudget.CurrentToken);
			if (!written) throw new InvalidOperationException("Semaphore count update failed.");
			if (original is null && !(clearZero && expected == 0))
				await SemaphoreAttributes.InitializeAsync(Mediator, fullTarget, path);
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
				var validation = await ValidateSemaphoreAttribute(target, path);
				if (validation.IsT1) throw new InvalidOperationException("Semaphore metadata is incomplete or changed; repair it before retrying: " + validation.AsT1.Value);
				return true;
			}
			if (value == oldCount && original is not null) return false;
			throw new InvalidOperationException("Semaphore count is neither its original nor committed value; manual reconciliation is required.");
		}
		return (Persist, Reconcile);
	}
}
