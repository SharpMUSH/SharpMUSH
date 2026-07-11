namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Classifies an event that occurred in a room and needs to be broadcast to all observers.
/// </summary>
public enum RoomEventType
{
	Arrive,
	Depart,
	Say,
	Pose,
}
