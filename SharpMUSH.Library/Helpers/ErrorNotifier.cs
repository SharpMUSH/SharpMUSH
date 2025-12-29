using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Helpers;

/// <summary>
/// Unified error handling helper that both returns CallState errors and sends user notifications.
/// Provides consistent error handling patterns across commands and functions.
/// </summary>
public static class ErrorNotifier
{
	/// <summary>
	/// Returns error CallState and optionally notifies the user.
	/// </summary>
	/// <param name="notifyService">Notification service</param>
	/// <param name="target">Object to notify (DBRef)</param>
	/// <param name="errorCode">Error code (e.g., "#-1")</param>
	/// <param name="errorMessage">User-facing error message</param>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	/// <returns>CallState with error message</returns>
	public static async ValueTask<CallState> ReturnError(
		INotifyService notifyService,
		DBRef target,
		string errorCode,
		string errorMessage,
		bool shouldNotify)
	{
		var fullError = $"{errorCode} {errorMessage}";
		
		if (shouldNotify)
		{
			await notifyService.Notify(target, fullError);
		}
		
		return new CallState(fullError);
	}

	/// <summary>
	/// Permission denied error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> PermissionDenied(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify)
		=> ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.PermissionDenied,
			ErrorMessages.English.PermissionDenied,
			shouldNotify);

	/// <summary>
	/// No such object error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> NoSuchObject(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? objectName = null)
	{
		var message = objectName != null
			? $"{ErrorMessages.English.NoSuchObject}: {objectName}"
			: ErrorMessages.English.NoSuchObject;

		return ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.NoSuchObject,
			message,
			shouldNotify);
	}

	/// <summary>
	/// Invalid argument error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> InvalidArgument(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? argumentName = null)
	{
		var message = argumentName != null
			? $"{ErrorMessages.English.InvalidArgument}: {argumentName}"
			: ErrorMessages.English.InvalidArgument;

		return ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.InvalidArgument,
			message,
			shouldNotify);
	}

	/// <summary>
	/// Bad object name error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> BadObjectName(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? objectName = null)
	{
		var message = objectName != null
			? $"{ErrorMessages.English.BadObjectName}: {objectName}"
			: ErrorMessages.English.BadObjectName;

		return ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.BadObjectName,
			message,
			shouldNotify);
	}

	/// <summary>
	/// No match error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> NoMatch(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify)
		=> ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.NoMatch,
			ErrorMessages.English.NoMatch,
			shouldNotify);

	/// <summary>
	/// Not a room error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> NotARoom(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? objectName = null)
	{
		var message = objectName != null
			? $"{ErrorMessages.English.NotARoom}: {objectName}"
			: ErrorMessages.English.NotARoom;

		return ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.NotARoom,
			message,
			shouldNotify);
	}

	/// <summary>
	/// Ambiguous match error - returns error and optionally notifies.
	/// </summary>
	/// <param name="shouldNotify">Whether to send notification to user (required parameter)</param>
	public static ValueTask<CallState> AmbiguousMatch(
		INotifyService notifyService,
		DBRef target,
		bool shouldNotify,
		string? pattern = null)
	{
		var message = pattern != null
			? $"{ErrorMessages.English.AmbiguousMatch}: {pattern}"
			: ErrorMessages.English.AmbiguousMatch;

		return ReturnError(
			notifyService,
			target,
			ErrorMessages.Codes.AmbiguousMatch,
			message,
			shouldNotify);
	}
}
