using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Notifications;

public record ChannelMessageNotification(
	SharpChannel Channel, 
	AnyOptionalSharpObject Source,
	INotifyService.NotificationType MessageType, 
	MString Message,
	MString Title,
	MString PlayerName,
	MString Says,
	string[] Options // silent or noisy
	) : INotification;