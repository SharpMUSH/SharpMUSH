using SharpMUSH.Library.DiscriminatedUnions;
using System.Globalization;

namespace SharpMUSH.Library.ExpandedObjectData;

/// <summary>
/// Expanded Mail Data for a Player.
/// </summary>
/// <remarks>
/// A message is stored under its folder's name, and a folder also has PennMUSH's folder number
/// (<c>0</c>-<c>MAX_FOLDERS</c>, <c>hdrs/extmail.h</c>), which is what <c>maillist()</c> writes and what a
/// <c>&lt;folder&gt;:&lt;message&gt;</c> specification names. Folder 0 is <see cref="Inbox"/>. A numbered folder
/// nobody has named is stored under its number's digits ("3"), as the PennMUSH importer has always
/// stored one, and shows as <c>unnamed</c>, as PennMUSH's <c>get_folder_name</c> does.
/// </remarks>
/// <param name="Folders">All of a Player's Folders.</param>
/// <param name="ActiveFolder">The active Folder.</param>
/// <param name="FolderNumbers">Each folder's number (1 to <see cref="MaxFolder"/>), by the name its messages are
/// stored under. Folder 0 is <see cref="Inbox"/> and is not listed.</param>
public record ExpandedMailData(
	string[]? Folders = null,
	string? ActiveFolder = null,
	Dictionary<string, int>? FolderNumbers = null) : AbstractExpandedData
{
	/// <summary>The name folder 0 is stored under.</summary>
	public const string Inbox = "INBOX";

	/// <summary>PennMUSH's <c>MAX_FOLDERS</c>: the highest folder number.</summary>
	public const int MaxFolder = 15;

	/// <summary>The number of the folder stored as <paramref name="folder"/>.</summary>
	public Found<int> NumberOf(string folder)
	{
		if (folder.Equals(Inbox, StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}

		foreach (var (name, number) in FolderNumbers ?? [])
		{
			if (name.Equals(folder, StringComparison.OrdinalIgnoreCase))
			{
				return number;
			}
		}

		return UnnamedNumber(folder) is { } unnamed ? unnamed : new NotFound();
	}

	/// <summary>The name folder <paramref name="number"/>'s messages are stored under.</summary>
	public string FolderFor(int number)
	{
		if (number == 0)
		{
			return Inbox;
		}

		foreach (var (name, assigned) in FolderNumbers ?? [])
		{
			if (assigned == number)
			{
				return name;
			}
		}

		return number.ToString(CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// PennMUSH's <c>get_folder_name</c>: the folder's name in upper case, as <c>add_folder_name</c> stores it, or
	/// <c>unnamed</c>. Folder 0 is <see cref="Inbox"/>: PennMUSH names it <c>inbox</c> when it creates a player
	/// (<c>create_player</c>, <c>src/player.c</c>), and it cannot be renamed here.
	/// </summary>
	public string DisplayName(int number)
	{
		var folder = FolderFor(number);
		return number != 0 && UnnamedNumber(folder) == number ? "unnamed" : folder.ToUpperInvariant();
	}

	/// <summary>
	/// PennMUSH's <c>parse_folder</c>: a folder number from 0 to <see cref="MaxFolder"/>, or the name of one of the
	/// player's folders. Answers the folder's number.
	/// </summary>
	public Found<int> Resolve(string spec)
	{
		spec = spec.Trim();
		if (spec.Length == 0)
		{
			return new NotFound();
		}

		if (char.IsAsciiDigit(spec[0]))
		{
			return int.TryParse(spec, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
						 && number is >= 0 and <= MaxFolder
				? number
				: new NotFound();
		}

		return Listed(spec) || spec.Equals(Inbox, StringComparison.OrdinalIgnoreCase)
			? NumberOf(spec)
			: new NotFound();
	}

	/// <summary>
	/// This data with <paramref name="folder"/> numbered: unchanged when it already has a number, otherwise given
	/// the lowest one no other folder holds. Not found when all of 1 to <see cref="MaxFolder"/> are taken.
	/// </summary>
	public Found<ExpandedMailData> WithFolder(string folder)
	{
		if (folder.Equals(Inbox, StringComparison.OrdinalIgnoreCase) || Listed(folder))
		{
			return this;
		}

		var own = UnnamedNumber(folder);
		return (own is { } digits && !(FolderNumbers ?? []).ContainsValue(digits) ? own : FreeNumber()) is { } assigned
			? this with { FolderNumbers = new Dictionary<string, int>(FolderNumbers ?? []) { [folder] = assigned } }
			: new NotFound();
	}

	/// <summary>This data with folder <paramref name="number"/> stored as <paramref name="folder"/> instead.</summary>
	public ExpandedMailData WithFolderNamed(int number, string folder)
	{
		var numbers = (FolderNumbers ?? [])
			.Where(entry => entry.Value != number)
			.ToDictionary(entry => entry.Key, entry => entry.Value);
		numbers[folder] = number;
		return this with { FolderNumbers = numbers };
	}

	/// <summary>
	/// Numbers every folder in <paramref name="folders"/> that has no number yet: a folder stored under its own
	/// number's digits keeps that number, and the others take the lowest free ones in name order. A folder
	/// left over once all of 1 to <see cref="MaxFolder"/> are taken stays unnumbered.
	/// </summary>
	public ExpandedMailData WithFolders(IEnumerable<string> folders)
	{
		var data = this;
		var unnumbered = folders
			.Where(folder => !folder.Equals(Inbox, StringComparison.OrdinalIgnoreCase) && !Listed(folder))
			.Distinct(StringComparer.Ordinal)
			.OrderBy(folder => UnnamedNumber(folder) is null)
			.ThenBy(folder => folder, StringComparer.Ordinal)
			.ToList();

		foreach (var folder in unnumbered)
		{
			if (data.WithFolder(folder) is ExpandedMailData numbered)
			{
				data = numbered;
			}
		}

		return data;
	}

	private bool Listed(string folder)
		=> (FolderNumbers ?? []).Keys.Any(name => name.Equals(folder, StringComparison.OrdinalIgnoreCase));

	private int? FreeNumber()
	{
		var taken = (FolderNumbers ?? []).Values.ToHashSet();
		for (var number = 1; number <= MaxFolder; number++)
		{
			if (!taken.Contains(number))
			{
				return number;
			}
		}

		return null;
	}

	/// <summary>The number a folder stored under a number's own digits has.</summary>
	private static int? UnnamedNumber(string folder)
		=> int.TryParse(folder, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
			 && number is >= 1 and <= MaxFolder
			 && folder == number.ToString(CultureInfo.InvariantCulture)
			? number
			: null;
}
