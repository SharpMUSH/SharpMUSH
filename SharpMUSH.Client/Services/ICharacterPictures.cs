namespace SharpMUSH.Client.Services;

/// <summary>
/// The picture each character shows as its avatar: its <c>IMAGE</c> attribute, which the server keeps
/// as the gallery's avatar. <see cref="Components.Kit.CharacterAvatar"/> asks here when the place it is
/// drawn in knows the character but not the picture.
/// </summary>
public interface ICharacterPictures
{
	/// <summary>A picture may have changed (a gallery write): an avatar on screen asks again.</summary>
	event Action? Changed;

	/// <summary>
	/// The character's picture, or null when it has none, no character answers to
	/// <paramref name="character"/>, or the directory could not be read.
	/// </summary>
	/// <param name="character">An objid (<c>#42:1700000000000</c>), a dbref (<c>#42</c>) or a name.</param>
	Task<string?> PictureOfAsync(string character, CancellationToken cancellationToken = default);
}
