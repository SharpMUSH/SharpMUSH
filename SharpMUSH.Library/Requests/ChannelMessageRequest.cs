using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Library.Requests;

public record ChannelMessageRequest(
	SharpChannel Channel, 
	AnyOptionalSharpObject Source,
	INotifyService.NotificationType MessageType, 
	MString Message,
	MString Title,
	MString PlayerName,
	MString Says,
	string[] Options // silent or noisy
	) : INotification;