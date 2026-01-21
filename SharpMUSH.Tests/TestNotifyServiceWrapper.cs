using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// Wrapper for INotifyService that delegates to a per-test instance.
/// This allows each test to have its own NotifyService mock while keeping
/// the DI container structure intact.
/// </summary>
public class TestNotifyServiceWrapper : INotifyService
{
	private static readonly AsyncLocal<INotifyService?> _currentNotifyService = new();
	
	/// <summary>
	/// Sets the NotifyService instance for the current test.
	/// This should be called in test setup before any commands are executed.
	/// </summary>
	public static void SetCurrentNotifyService(INotifyService notifyService)
	{
		_currentNotifyService.Value = notifyService;
	}
	
	/// <summary>
	/// Clears the current NotifyService instance.
	/// This should be called in test cleanup.
	/// </summary>
	public static void ClearCurrentNotifyService()
	{
		_currentNotifyService.Value = null;
	}
	
	/// <summary>
	/// Gets the current NotifyService instance for this test.
	/// Throws if no instance has been set.
	/// </summary>
	private INotifyService Current => _currentNotifyService.Value 
		?? throw new InvalidOperationException("No NotifyService has been set for the current test. Ensure SetupAsync() is called before accessing NotifyService.");
	
	public ValueTask Notify(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Notify(who, what, sender, type);
	
	public ValueTask Notify(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Notify(who, what, sender, type);
	
	public ValueTask Notify(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Notify(handle, what, sender, type);
	
	public ValueTask Notify(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Notify(handles, what, sender, type);
	
	public ValueTask Prompt(DBRef who, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Prompt(who, what, sender, type);
	
	public ValueTask Prompt(AnySharpObject who, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Prompt(who, what, sender, type);
	
	public ValueTask Prompt(long handle, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Prompt(handle, what, sender, type);
	
	public ValueTask Prompt(long[] handles, OneOf<MString, string> what, AnySharpObject? sender = null, 
		INotifyService.NotificationType type = INotifyService.NotificationType.Announce)
		=> Current.Prompt(handles, what, sender, type);
	
	public ValueTask<CallState> NotifyAndReturn(DBRef target, string errorReturn, string notifyMessage, bool shouldNotify)
		=> Current.NotifyAndReturn(target, errorReturn, notifyMessage, shouldNotify);
}
