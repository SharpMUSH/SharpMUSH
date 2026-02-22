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
		AttributeName,
		UncasedAttributeContent,
		CasedAttributeContent
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
	IAsyncEnumerable<MString> Sort(IEnumerable<MString> items, Func<MString, CancellationToken, ValueTask<string>> keySelector, IMUSHCodeParser parser, SortInformation sortData);

	/// <summary>
	/// Sorts a list of MStrings according to the specified sort type and order.
	/// </summary>
	/// <param name="items">List of MStrings</param>
	/// <param name="parser">MUSHCode Parser with the current state</param>
	/// <param name="sortData">Sort Type</param>
	/// <param name="keySelector">Transform from MString to string</param>
	/// <returns>Sorted List</returns>
	IAsyncEnumerable<MString> Sort(IAsyncEnumerable<MString> items, Func<MString, CancellationToken, ValueTask<string>> keySelector, IMUSHCodeParser parser, SortInformation sortData);
}