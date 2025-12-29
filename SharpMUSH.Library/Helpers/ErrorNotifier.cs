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
/// Callers choose which error and notification to use via ErrorMessages constants.
/// </summary>
public static class ErrorNotifier
{
	/// <summary>
	/// Core error pattern: optionally notify user, then return error.
	/// 
	/// Example usage:
	/// return await ErrorNotifier.NotifyAndReturn(
	///     NotifyService,
	///     executor.DBRef,
	///     errorReturn: ErrorMessages.Returns.PermissionDenied,
	///     notifyMessage: ErrorMessages.Notifications.PermissionDenied,
	///     shouldNotify: true);
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
}
