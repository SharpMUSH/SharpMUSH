namespace SharpMUSH.Library.Definitions;

/// <summary>
/// The shipped text-file corpora, by the category directory name they live under in
/// <c>TextFiles/</c>. These are the game's help files, not wiki content: the wiki holds the
/// game's editable world content, and these hold the engine documentation the server ships.
/// </summary>
public static class HelpCorpora
{
	/// <summary>General help. Readable by anyone, in game and in the portal.</summary>
	public const string Help = "help";

	/// <summary>
	/// Administrator help. The in-game <c>ahelp</c> command refuses non-wizards, so every other
	/// surface that reads this corpus has to refuse them too.
	/// </summary>
	public const string Admin = "ahelp";

	/// <summary>Game news. Readable by anyone.</summary>
	public const string News = "news";
}
