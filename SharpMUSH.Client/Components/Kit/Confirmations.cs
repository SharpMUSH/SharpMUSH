using Microsoft.Extensions.Localization;
using MudBlazor;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// The yes/cancel message box an action asks before it does something hard to undo, and the questions
/// more than one page asks in the same words.
/// </summary>
public static class Confirmations
{
	/// <summary>True only when the viewer pressed <paramref name="confirm"/>; Cancel or dismissing the box is false.</summary>
	public static async Task<bool> ConfirmAsync(this IDialogService dialogs, IStringLocalizer<SharedResource> loc,
		string title, string message, string confirm)
		=> await dialogs.ShowMessageBoxAsync(title, message, yesText: confirm, cancelText: loc["Cancel"]) == true;

	/// <summary>Disconnecting a character, from its detail page or the moderation page.</summary>
	public static Task<bool> ConfirmBootAsync(this IDialogService dialogs, IStringLocalizer<SharedResource> loc, string name)
		=> dialogs.ConfirmAsync(loc, loc["AdmCharacterBootConfirmTitle", name], loc["AdmCharacterBootConfirm", name],
			loc["AdmCharacterBoot"]);

	/// <summary>Taking a character off its account, from its detail page or the moderation page.</summary>
	public static Task<bool> ConfirmUnlinkAsync(this IDialogService dialogs, IStringLocalizer<SharedResource> loc,
		string name, string accountName)
		=> dialogs.ConfirmAsync(loc, loc["AdmCharacterUnlinkConfirmTitle", name], loc["AdmCharacterUnlinkConfirm", name, accountName],
			loc["AdmCharacterUnlink"]);
}
