using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Interface for MessageQueueNotifyService to enable mocking in tests.
/// This extends INotifyService with no additional methods, but provides
/// a concrete type for dependency injection and testing.
/// </summary>
public interface IMessageQueueNotifyService : INotifyService
{
	// No additional methods - this interface exists solely for mocking purposes
	// and to provide a distinct type for the message queue implementation
}
