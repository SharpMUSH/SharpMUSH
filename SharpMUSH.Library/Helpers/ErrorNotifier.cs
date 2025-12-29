using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Helpers;

/// <summary>
/// Unified error handling helper following the pattern:
/// await NotifyService.Notify(executor, notifyMessage); 
/// return errorReturn;
/// 
/// The notify message and error return are SEPARATE and can be different strings.
/// </summary>
public static class ErrorNotifier
{
	/// <summary>
	/// Core error pattern: optionally notify user, then return error.
	/// </summary>
	/// <param name="notifyService">Notification service</param>
	/// <param name="target">Object to notify (DBRef)</param>
	/// <param name="errorReturn">Error string for return value (e.g., "#-1 PERMISSION DENIED")</param>
	/// <param name="notifyMessage">Message to show user (e.g., "You don't have permission to do that.")</param>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	/// <returns>CallState with error return string</returns>
	public static async ValueTask<CallState> NotifyAndReturn(
		INotifyService notifyService,
		DBRef target,
		string errorReturn,
		string notifyMessage,
		bool shouldNotify)
	{
		if (shouldNotify)
		{
			await notifyService.Notify(target, notifyMessage);
		}
		
		return new CallState(errorReturn);
	}

	/// <summary>
	/// Permission denied error.
	/// Return: "#-1 PERMISSION DENIED"
	/// Notify: "You don't have permission to do that."
	/// </summary>
	public static ValueTask<CallState> PermissionDenied(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.PermissionDenied,
			ErrorMessages.Notifications.PermissionDenied,
			shouldNotify);

	/// <summary>
	/// No such object error.
	/// Return: "#-1 NO SUCH OBJECT"
	/// Notify: "I can't find that." or custom message
	/// </summary>
	public static ValueTask<CallState> NoSuchObject(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? customNotifyMessage = null)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.NoSuchObject,
			customNotifyMessage ?? ErrorMessages.Notifications.NoSuchObject,
			shouldNotify);

	/// <summary>
	/// Invalid argument error.
	/// Return: "#-1 INVALID ARGUMENT"
	/// Notify: "Invalid argument." or custom message
	/// </summary>
	public static ValueTask<CallState> InvalidArgument(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? customNotifyMessage = null)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.InvalidArgument,
			customNotifyMessage ?? ErrorMessages.Notifications.InvalidArgument,
			shouldNotify);

	/// <summary>
	/// Bad object name error.
	/// Return: "#-1 BAD OBJECT NAME"
	/// Notify: "I don't understand that object name." or custom message
	/// </summary>
	public static ValueTask<CallState> BadObjectName(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? customNotifyMessage = null)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.BadObjectName,
			customNotifyMessage ?? ErrorMessages.Notifications.BadObjectName,
			shouldNotify);

	/// <summary>
	/// No match error.
	/// Return: "#-1 NO MATCH"
	/// Notify: "I don't see that here."
	/// </summary>
	public static ValueTask<CallState> NoMatch(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.NoMatch,
			ErrorMessages.Notifications.NoMatch,
			shouldNotify);

	/// <summary>
	/// Not a room error.
	/// Return: "#-1 NOT A ROOM"
	/// Notify: "That's not a room." or custom message
	/// </summary>
	public static ValueTask<CallState> NotARoom(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? customNotifyMessage = null)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.NotARoom,
			customNotifyMessage ?? ErrorMessages.Notifications.NotARoom,
			shouldNotify);

	/// <summary>
	/// Ambiguous match error.
	/// Return: "#-2 I DON'T KNOW WHICH ONE YOU MEAN"
	/// Notify: "I don't know which one you mean." or custom message
	/// </summary>
	public static ValueTask<CallState> AmbiguousMatch(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? customNotifyMessage = null)
		=> NotifyAndReturn(
			notifyService,
			target,
			ErrorMessages.Returns.AmbiguousMatch,
			customNotifyMessage ?? ErrorMessages.Notifications.AmbiguousMatch,
			shouldNotify);
}
