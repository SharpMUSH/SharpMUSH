namespace SharpMUSH.Messaging.KafkaFlow;

/// <summary>
/// Marker class for type-based KafkaFlow producer registration.
/// This enables dependency injection of IMessageProducer&lt;SharpMushProducer&gt;
/// following KafkaFlow's recommended type-based producer pattern.
/// </summary>
public class SharpMushProducer
{
}
