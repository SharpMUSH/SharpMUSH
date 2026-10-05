using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The shorthand forms of <see cref="INotifyService.NotifyLocalized(DBRef,string,AnySharpObject?,object[])"/>
/// and <see cref="INotifyService.NotifyLocalizedMarkup(DBRef,string,AnySharpObject?,MString[])"/>: no
/// sender, or an object in place of its DBRef. Each is exactly the full form with that argument filled
/// in, so an implementation of <see cref="INotifyService"/> writes only the full forms.
/// </summary>
public static class NotifyServiceLocalizedExtensions
{
	/// <summary>Sends a locale-aware notification to all connections for a DBRef, with no sender.</summary>
	public static ValueTask NotifyLocalized(this INotifyService notifyService, DBRef who, string key,
		params object[] args)
		=> notifyService.NotifyLocalized(who, key, sender: null, args);

	/// <summary>Sends a locale-aware notification to all connections for an object, with no sender.</summary>
	public static ValueTask NotifyLocalized(this INotifyService notifyService, AnySharpObject who, string key,
		params object[] args)
		=> notifyService.NotifyLocalized(who.Object().DBRef, key, sender: null, args);

	/// <summary>Sends a locale-aware notification to all connections for an object, recording the sender.</summary>
	public static ValueTask NotifyLocalized(this INotifyService notifyService, AnySharpObject who, string key,
		AnySharpObject? sender, params object[] args)
		=> notifyService.NotifyLocalized(who.Object().DBRef, key, sender, args);

	/// <summary>Sends a locale-aware notification to a single connection handle, with no sender.</summary>
	public static ValueTask NotifyLocalized(this INotifyService notifyService, long handle, string key,
		params object[] args)
		=> notifyService.NotifyLocalized(handle, key, sender: null, args);

	/// <summary>Sends a locale-aware markup notification to all connections for an object.</summary>
	public static ValueTask NotifyLocalizedMarkup(this INotifyService notifyService, AnySharpObject who, string key,
		AnySharpObject? sender, params MString[] args)
		=> notifyService.NotifyLocalizedMarkup(who.Object().DBRef, key, sender, args);
}
