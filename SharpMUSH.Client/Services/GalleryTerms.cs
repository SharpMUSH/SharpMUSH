using Microsoft.Extensions.Localization;
using MudBlazor;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>The terms a game asks an uploader to accept before a gallery upload (<c>gallery_consent_text</c>).</summary>
public static class GalleryTerms
{
	/// <summary>
	/// Shows the game's terms, read fresh, and answers with the version of the text the uploader accepted, or an empty
	/// string when the game asks for none; not found when they turned the terms down, and nothing is uploaded. When the
	/// rules cannot be read the upload goes ahead without terms, and the server refuses it if it needed them.
	/// </summary>
	public static async Task<Found<string>> AcceptGalleryTermsAsync(this IDialogService dialogs,
		IStringLocalizer<SharedResource> loc, GalleryService gallery)
	{
		if (await gallery.PolicyAsync() is not GalleryPolicy { ConsentText: { } text, ConsentVersion: { } version })
			return string.Empty;
		return await dialogs.ConfirmAsync(loc, loc["NavGalleryTermsTitle"], text, loc["NavGalleryTermsAccept"])
			? version
			: new NotFound();
	}
}
