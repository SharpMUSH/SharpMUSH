using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>Index rows one page of an <c>indexed()</c> walk reads.</summary>
	private const int IndexedPageSize = 256;

	/// <summary>
	/// <c>indexed(&lt;attr&gt;, &lt;op&gt;, &lt;value&gt;[, &lt;start&gt;, &lt;count&gt;])</c>: the objects whose own copy of
	/// an indexed attribute (<c>@attribute/index</c>) matches, in the index's order with ties in dbref order. Only copies
	/// the caller could <c>get()</c> are listed, and <c>start</c> (from 1) and <c>count</c> count those. <c>count</c>
	/// answers how many copies equal the value, again only those the caller could read.
	/// </summary>
	[SharpFunction(Name = "indexed", MinArgs = 2, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["attribute", "operation", "value", "start", "count"])]
	public async ValueTask<CallState> Indexed(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var name = args["0"].Message.ToPlainText().Trim().ToUpperInvariant();
		var opText = args["1"].Message.ToPlainText().Trim().ToLowerInvariant();
		var value = args.TryGetValue("2", out var valueArg) ? valueArg.Message.ToPlainText() : string.Empty;

		var start = 1;
		var count = int.MaxValue;
		if (args.TryGetValue("3", out var startArg) && startArg.Message.ToPlainText().Trim() is { Length: > 0 } startText
				&& (!int.TryParse(startText, out start) || start < 1))
		{
			return new CallState(ErrorMessages.Returns.PositiveInteger);
		}

		if (args.TryGetValue("4", out var countArg) && countArg.Message.ToPlainText().Trim() is { Length: > 0 } countText
				&& (!int.TryParse(countText, out count) || count < 0))
		{
			return new CallState(ErrorMessages.Returns.UInteger);
		}

		if (name.Length == 0)
		{
			return new CallState(ErrorMessages.Returns.NotIndexed);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var cancellationToken = ExecutionBudget.CurrentToken;
		var visibility = await IndexedAttributeVisibility.ForAsync(executor, cancellationToken);

		if (opText == "count")
		{
			if (visibility.SeesEverything)
			{
				return await Mediator.Send(new CountIndexedAttributeQuery(name, value), cancellationToken) switch
				{
					long total => new CallState(total.ToString()),
					IndexBuilding => new CallState(ErrorMessages.Returns.IndexBuilding),
					_ => new CallState(ErrorMessages.Returns.NotIndexed)
				};
			}

			return await WalkIndexAsync(executor, visibility, name, IndexedAttributeOperation.Equal, value, null, 1, int.MaxValue,
					cancellationToken) switch
			{
				IndexWalk walk => new CallState(walk.Hits.Count.ToString()),
				Error<string> error => new CallState(error.Value)
			};
		}

		if (IndexOperation(opText) is not { } operation)
		{
			return new CallState(ErrorMessages.Returns.InvalidIndexOperation);
		}

		string? upper = null;
		if (operation == IndexedAttributeOperation.Between)
		{
			if (value.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [var low, var high])
			{
				return new CallState(ErrorMessages.Returns.BetweenTakesTwoValues);
			}

			(value, upper) = (low, high);
		}

		return await WalkIndexAsync(executor, visibility, name, operation, value, upper, start, count, cancellationToken) switch
		{
			IndexWalk walk => new CallState(string.Join(' ', walk.Hits.Select(hit => $"#{hit}"))),
			Error<string> error => new CallState(error.Value)
		};
	}

	private static IndexedAttributeOperation? IndexOperation(string op) => op switch
	{
		"=" or "eq" => IndexedAttributeOperation.Equal,
		"prefix" => IndexedAttributeOperation.Prefix,
		"<" or "lt" => IndexedAttributeOperation.Less,
		"<=" or "lte" => IndexedAttributeOperation.LessOrEqual,
		">" or "gt" => IndexedAttributeOperation.Greater,
		">=" or "gte" => IndexedAttributeOperation.GreaterOrEqual,
		"between" => IndexedAttributeOperation.Between,
		"first" => IndexedAttributeOperation.First,
		"last" => IndexedAttributeOperation.Last,
		_ => null
	};

	/// <summary>The dbref numbers an <c>indexed()</c> walk kept, in index order.</summary>
	private sealed record IndexWalk(List<int> Hits);

	/// <summary>
	/// Walks an index in pages, keeping each hit the executor may read, from the <paramref name="start"/>th such hit
	/// for at most <paramref name="count"/>. The provider says, for most hits, whether they can be read from what it
	/// already holds (<see cref="IndexedAttributeVisibility"/>); only the rest load the object and its copy for
	/// <c>CanViewAttribute</c>, with one <see cref="AttributeViewMemo"/> for the walk.
	/// </summary>
	private async ValueTask<Result<IndexWalk>> WalkIndexAsync(AnySharpObject executor, IndexedAttributeVisibility visibility,
		string name, IndexedAttributeOperation operation, string value, string? upper, int start, int count,
		CancellationToken cancellationToken)
	{
		var kept = new List<int>();
		if (count == 0)
		{
			return new IndexWalk(kept);
		}

		var memo = new AttributeViewMemo();
		var path = name.Split('`');
		var skip = start - 1;
		IndexedAttributeCursor? after = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			// A page asks for what is still wanted, so START/COUNT read no more of the index than they need; pages after
			// the first grow, since hidden hits mean more are needed than are kept.
			var wanted = (long)skip + (count - kept.Count);
			var limit = (int)Math.Clamp(after is null ? wanted : wanted * 2, 1, IndexedPageSize);
			var spec = new IndexedAttributeQuerySpec(name, operation, value, upper, after, limit, !visibility.SeesEverything);
			IndexedAttributePage page;
			switch (await Mediator.Send(new GetIndexedAttributeQuery(spec), cancellationToken))
			{
				case IndexedAttributePage found:
					page = found;
					break;
				case IndexBuilding:
					return new Error<string>(ErrorMessages.Returns.IndexBuilding);
				default:
					return new Error<string>(ErrorMessages.Returns.NotIndexed);
			}

			foreach (var hit in page.Hits)
			{
				var verdict = visibility.SeesEverything ? IndexedAttributeVerdict.Visible : visibility.Judge(hit);
				if (verdict == IndexedAttributeVerdict.Check && !await CanReadIndexedCopyAsync(executor, hit, path, memo))
				{
					continue;
				}

				if (verdict == IndexedAttributeVerdict.Hidden)
				{
					continue;
				}

				if (skip > 0)
				{
					skip--;
					continue;
				}

				kept.Add(hit.Number);
				if (kept.Count >= count)
				{
					return new IndexWalk(kept);
				}
			}

			if (page.Next is null)
			{
				return new IndexWalk(kept);
			}

			after = page.Next;
		}
	}

	/// <summary>The full <c>CanViewAttribute</c> test for a hit the facts could not settle.</summary>
	private async ValueTask<bool> CanReadIndexedCopyAsync(AnySharpObject executor, IndexedAttributeHit hit, string[] path,
		AttributeViewMemo memo)
	{
		if (await Mediator.Send(new GetObjectNodeQuery(hit.DBRef)) is not AnySharpObject target)
		{
			return false;
		}

		var levels = await Mediator.CreateStream(new GetAttributeQuery(target.Object().DBRef, path)).ToArrayAsync();
		return levels.Length == path.Length && await PermissionService.CanViewAttribute(executor, target, memo, levels);
	}
}
