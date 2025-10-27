namespace SharpMUSH.Library.Services.Interfaces;

public interface ISortService
{
	enum SortType
	{
		CasedLexicographically,
		UncasedLexicographically,
		DbRef,
		IntegerSort,
		DecimalSort,
		NaturalSort,
		CasedName,
		UncasedName,
		Conn,
		Idle,
		Owner,
		Location,
		CreatedTime,
		ModifiedTime,
		AttributeName
	}

	/// <summary>
	/// Sorts a list of MStrings according to the specified sort type and order.
	/// </summary>
	/// <param name="items">List of MStrings</param>
	/// <param name="sortType">Sort Type</param>
	/// <param name="ascending">Descending is the default</param>
	/// <returns>Sorted List</returns>
	ValueTask<IEnumerable<MString>> Sort(IEnumerable<MString> items, SortType sortType, bool ascending = false);
}