using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The translation as saved, or why the save was refused.
/// </summary>
public partial union TranslationSaveResult(WikiTranslationInfo, WikiTranslationSaveError);
