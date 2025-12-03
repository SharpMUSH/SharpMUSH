using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface INotifyService
{
	enum NotificationType
	{
		Emit,
		Say,
		Pose,
		SemiPose,
		Announce,
		NSEmit,
		NSSay,
		NSPose,
		NSSemiPose,
		NSAnnounce
	}

	// TODO: Add a 'sender' for Noisy etc rules.
	ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	/// <summary>
	/// Enables output buffering for the specified handle.
	/// All Notify calls for this handle will be buffered until FlushBuffer is called.
	/// This reduces RabbitMQ message traffic and TCP overhead for batch operations.
	/// </summary>
	/// <param name="handle">The connection handle to enable buffering for</param>
	void EnableBuffering(long handle);

	/// <summary>
	/// Flushes all buffered output for the specified handle.
	/// Sends all accumulated messages as a single batch to reduce RabbitMQ and TCP overhead.
	/// </summary>
	/// <param name="handle">The connection handle to flush</param>
	ValueTask FlushBuffer(long handle);

	/// <summary>
	/// Disables buffering for the specified handle without flushing.
	/// Useful for cleanup in error cases.
	/// </summary>
	/// <param name="handle">The connection handle to disable buffering for</param>
	void DisableBuffering(long handle);
}