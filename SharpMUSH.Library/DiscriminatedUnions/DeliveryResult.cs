using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// The object a message was delivered to, or why it was not delivered.
/// </summary>
public partial union DeliveryResult(AnySharpObject, DeliveryFailure);
