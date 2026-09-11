using SharpMUSH.Library.Models.Wiki;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A translation upsert: the translation as written, the conflict that refused it, or the message of
/// a storage failure.
/// </summary>
public partial union TranslationWriteResult(WikiTranslation, WikiWriteConflict, Error<string>);
