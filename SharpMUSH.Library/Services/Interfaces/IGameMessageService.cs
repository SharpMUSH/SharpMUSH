using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The connect screen, the MOTDs and the other <see cref="GameMessage"/>s: what a connection is shown, and the
/// stored text and source the Messages page edits.
/// </summary>
/// <remarks>
/// Each message has a stored text in the database, which starts as the text SharpMUSH ships. A game can read its
/// messages from the Messages object instead (<see cref="GameMessageSource.Object"/>): each one is then its
/// attribute there, evaluated as softcode, and the stored text stands in only where the object has no such
/// attribute. Until an administrator chooses, the object is the source exactly while the Messages package is
/// installed.
/// </remarks>
public interface IGameMessageService
{
	/// <summary>
	/// What <paramref name="message"/> shows now, or <see cref="None"/> when it shows nothing. An attribute is evaluated as the
	/// Messages object, with <paramref name="viewer"/> (or the object, at the connect screen) as the enactor and the
	/// connection's descriptor as <c>%0</c>.
	/// </summary>
	ValueTask<Option<MString>> RenderAsync(GameMessage message, long handle, AnySharpObject? viewer = null);

	/// <summary>The stored text of <paramref name="message"/>, ANSI escapes and all.</summary>
	ValueTask<string> GetTextAsync(GameMessage message);

	/// <summary>Whether <paramref name="message"/>'s stored text is still the shipped one.</summary>
	ValueTask<bool> IsDefaultAsync(GameMessage message);

	/// <summary>Stores <paramref name="text"/> as <paramref name="message"/>'s text; null puts the shipped text back.</summary>
	ValueTask SetTextAsync(GameMessage message, string? text);

	/// <summary>Where messages are read from now, and whether an administrator chose it.</summary>
	ValueTask<(GameMessageSource Source, bool Chosen)> GetSourceAsync();

	/// <summary>Sets where messages are read from; null follows the Messages package again.</summary>
	ValueTask SetSourceAsync(GameMessageSource? source);

	/// <summary>The Messages object, or <see cref="None"/> when the Messages package has not created one.</summary>
	ValueTask<AnyOptionalSharpObject> MessagesObjectAsync();

	/// <summary>The text SharpMUSH ships for <paramref name="message"/>.</summary>
	string ShippedText(GameMessage message);
}
