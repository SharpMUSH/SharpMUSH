using MoreLinq;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class SortService(ILocateService locate) : ISortService
{
	private readonly NaturalSort.Extension.NaturalSortComparer _naturalSortComparer =
		new(StringComparison.CurrentCultureIgnoreCase);

	public async ValueTask<IEnumerable<MString>> Sort(IEnumerable<MString> items, ISortService.SortType sortType,
		bool ascending = false)
	{
		await ValueTask.CompletedTask;
		var direction = ascending ? OrderByDirection.Ascending : OrderByDirection.Descending;
		return sortType switch
		{
			ISortService.SortType.CasedLexicographically
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCulture, direction),
			ISortService.SortType.UncasedLexicographically
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCultureIgnoreCase, direction),
			ISortService.SortType.DbRef
				=> items.OrderBy(i => i.ToPlainText(), StringComparer.CurrentCulture, direction),
			ISortService.SortType.IntegerSort
				=> items
					.Select(mString => (i: int.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderBy(val => val.i, Comparer<int>.Default, direction)
					.Select(val => val.mString),
			ISortService.SortType.DecimalSort
				=> items
					.Select(mString => (i: decimal.TryParse(mString.ToPlainText(), out var number) ? number : -1, mString))
					.OrderBy(val => val.i, Comparer<decimal>.Default, direction)
					.Select(val => val.mString),
			ISortService.SortType.NaturalSort 
				=> items.OrderBy(x => x.ToPlainText(), _naturalSortComparer, direction),
			ISortService.SortType.CasedName => throw new NotImplementedException(),
			ISortService.SortType.UncasedName => throw new NotImplementedException(),
			ISortService.SortType.Conn => throw new NotImplementedException(),
			ISortService.SortType.Idle => throw new NotImplementedException(),
			ISortService.SortType.Owner => throw new NotImplementedException(),
			ISortService.SortType.Location => throw new NotImplementedException(),
			ISortService.SortType.CreatedTime => throw new NotImplementedException(),
			ISortService.SortType.ModifiedTime => throw new NotImplementedException(),
			ISortService.SortType.AttributeName => throw new NotImplementedException(),
			_ => throw new NotImplementedException(),
		};
	}
}