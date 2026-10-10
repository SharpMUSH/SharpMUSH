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
		NSAnnounce,
		PrivateEmit,
		NSPrivateEmit
	}

	// Sender parameter added for Noisy rules support
	ValueTask Notify(DBRef who, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(AnySharpObject who, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long handle, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Notify(long[] handles, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(DBRef who, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(AnySharpObject who, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	/// <summary>
	/// Publishes a prompt only to the captured transport incarnation. <paramref name="inputSession"/> is
	/// the <c>@input</c> session it belongs to, which a WebSocket client keeps it under until
	/// <see cref="ClearPromptToSession"/> names that session.
	/// </summary>
	ValueTask PromptToSession(long handle, string sessionId, SharpMessage what, Guid? inputSession = null)
		=> throw new NotSupportedException("This notifier does not support incarnation-bound prompts.");

	/// <summary>
	/// Tells a WebSocket client that <c>@input</c> session <paramref name="inputSession"/> ended, so the
	/// prompt it shows for it comes down; null takes down whatever prompt it shows. A terminal is sent nothing.
	/// </summary>
	ValueTask ClearPromptToSession(long handle, string sessionId, Guid? inputSession)
		=> ValueTask.CompletedTask;

	ValueTask Prompt(long handle, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

	ValueTask Prompt(long[] handles, SharpMessage what, AnySharpObject? sender = null, NotificationType type = NotificationType.Announce);

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

	/// <summary>Publishes a localized status only to the captured transport incarnation.</summary>
	ValueTask NotifyLocalizedToSession(long handle, string sessionId, string key, params object[] args)
		=> throw new NotSupportedException("This notifier does not support incarnation-bound status output.");

	/// <summary>
	/// Sends a locale-aware notification to all connections for a DBRef, recording the sender.
	/// The message is looked up from the resource file by <paramref name="key"/>,
	/// formatted with <paramref name="args"/>, and translated per each connection's locale.
	/// The shorter forms (no sender, or an object instead of a DBRef) are in
	/// <see cref="NotifyServiceLocalizedExtensions"/>.
	/// </summary>
	ValueTask NotifyLocalized(DBRef who, string key, AnySharpObject? sender, params object[] args);

	/// <summary>
	/// Sends a locale-aware notification to a single connection handle, recording the sender.
	/// </summary>
	ValueTask NotifyLocalized(long handle, string key, AnySharpObject? sender, params object[] args);

	/// <summary>
	/// Sends a locale-aware notification whose placeholders are markup-aware <see cref="MString"/> values.
	/// This preserves HTML/MXP markup while still translating the surrounding text per connection locale.
	/// </summary>
	ValueTask NotifyLocalizedMarkup(DBRef who, string key, AnySharpObject? sender, params MString[] args);

	/// <summary>
	/// Sends a locale-aware markup notification to a single connection handle.
	/// </summary>
	ValueTask NotifyLocalizedMarkup(long handle, string key, AnySharpObject? sender, params MString[] args);
}
