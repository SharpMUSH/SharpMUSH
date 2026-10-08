using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit;

/// <summary>A game where no character has a picture: every avatar is initials.</summary>
public sealed class NoCharacterPictures : ICharacterPictures
{
	public event Action? Changed
	{
		add { }
		remove { }
	}

	public Task<string?> PictureOfAsync(string character, CancellationToken cancellationToken = default) =>
		Task.FromResult<string?>(null);
}
