using MoreLinq;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface ISortService
{
	record SortInformation(SortType Type, OrderByDirection Direction, string? AttributeName);
	
	[Flags]
	enum SortType
	{
		Invalid,
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

	SortInformation StringToSortType(string sortType);

	/// <summary>
	/// Sorts a list of MStrings according to the specified sort type and order.
	/// </summary>
	/// <param name="items">List of MStrings</param>
	/// <param name="parser">MUSHCode Parser with the current state</param>
	/// <param name="sortData">Sort Type</param>
	/// <param name="keySelector">Transform from MString to string</param>
	/// <returns>Sorted List</returns>
	ValueTask<IEnumerable<MString>> Sort(IEnumerable<MString> items, Func<MString, string> keySelector, IMUSHCodeParser parser, SortInformation sortData);
}