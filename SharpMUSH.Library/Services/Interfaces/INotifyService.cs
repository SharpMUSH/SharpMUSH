using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

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

	// Sender parameter added for Noisy rules support
	ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	/// <summary>
	/// [DEPRECATED] Begin a batching scope for the specified connection handle.
	/// This is now a no-op as batching is always active with automatic 10ms timeout.
	/// Kept for backward compatibility.
	/// </summary>
	void BeginBatchingScope(long handle);

	/// <summary>
	/// End a batching scope for the specified connection handle.
	/// Flushes any accumulated messages immediately instead of waiting for the 10ms timeout.
	/// </summary>
	ValueTask EndBatchingScope(long handle);

	/// <summary>
	/// Begin a context-based batching scope that batches notifications to ANY target.
	/// Returns an IDisposable that should be disposed to end the scope and flush messages.
	/// Supports ref-counting for nested scopes.
	/// </summary>
	IDisposable BeginBatchingContext();

	/// <summary>
	/// Unified error handling: optionally notify user, then return error.
	/// The notify message and error return are SEPARATE and can be different strings.
	/// 
	/// Example usage:
	/// return await NotifyService.NotifyAndReturn(
	///     executor.DBRef,
	///     errorReturn: ErrorMessages.Returns.PermissionDenied,
	///     notifyMessage: ErrorMessages.Notifications.PermissionDenied,
	///     shouldNotify: true);
	/// </summary>
	/// <param name="target">Object to notify (DBRef)</param>
	/// <param name="errorReturn">Error string for return value (e.g., "#-1 PERMISSION DENIED")</param>
	/// <param name="notifyMessage">Message to show user (e.g., "You don't have permission to do that.")</param>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	/// <returns>CallState with error return string</returns>
	ValueTask<CallState> NotifyAndReturn(
		DBRef target,
		string errorReturn,
		string notifyMessage,
		bool shouldNotify);
}