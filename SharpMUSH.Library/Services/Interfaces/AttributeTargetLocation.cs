using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// How a command or function finds the object whose attributes it reads or writes: any object the executor can
/// locate, or an account's holder by its <c>#acct:&lt;key&gt;</c>. Every attribute command and function locates its
/// target through these, so an account's attributes reach exactly the same set of them; a search for anything else
/// (code to run, a place to go, a player to page) never finds a holder.
/// </summary>
public static class AttributeTargetLocation
{
	/// <summary>The search an attribute target is located with.</summary>
	public const LocateFlags Flags = LocateFlags.All | LocateFlags.MatchAccountHolders;

	extension(ILocateService locate)
	{
		/// <summary>The object, or account, whose attributes <paramref name="name"/> names; tells the executor when there is none.</summary>
		public ValueTask<AnyOptionalSharpObjectOrError> LocateAttributeTargetAndNotifyIfInvalid(IMUSHCodeParser parser,
			AnySharpObject executor, string name)
			=> locate.LocateAndNotifyIfInvalid(parser, executor, executor, name, Flags);

		/// <inheritdoc cref="LocateAttributeTargetAndNotifyIfInvalid"/>
		public ValueTask<AnySharpObjectOrErrorCallState> LocateAttributeTargetAndNotifyIfInvalidWithCallState(
			IMUSHCodeParser parser, AnySharpObject executor, string name)
			=> locate.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, name, Flags);

		/// <summary>Runs <paramref name="found"/> on the object, or account, whose attributes <paramref name="name"/> names.</summary>
		public ValueTask<CallState> LocateAttributeTargetAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser parser,
			AnySharpObject executor, string name, Func<AnySharpObject, ValueTask<CallState>> found)
			=> locate.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, Flags, found);

		/// <inheritdoc cref="LocateAttributeTargetAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser, AnySharpObject, string, Func{AnySharpObject, ValueTask{CallState}})"/>
		public ValueTask<CallState> LocateAttributeTargetAndNotifyIfInvalidWithCallStateFunction(IMUSHCodeParser parser,
			AnySharpObject executor, string name, Func<AnySharpObject, CallState> found)
			=> locate.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, name, Flags, found);
	}
}
