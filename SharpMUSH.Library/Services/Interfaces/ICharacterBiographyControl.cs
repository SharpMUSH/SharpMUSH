namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Who may write a character's biography (its page in the Character namespace): the character's own player,
/// from any character on the same account, and whoever controls the character (staff). A biography naming
/// no character is written by nobody this way; <c>wiki.admin</c> holders still may, as with every page.
/// </summary>
public interface ICharacterBiographyControl
{
	/// <summary>
	/// True when <paramref name="writerDbref"/> (the acting character, as wiki authors are stored) may write the
	/// biography titled <paramref name="title"/> whose slug is <paramref name="slug"/>. Its character is the player
	/// whose name slugifies to <paramref name="slug"/>.
	/// </summary>
	ValueTask<bool> MayWriteAsync(string? writerDbref, string title, string slug, CancellationToken ct = default);
}
