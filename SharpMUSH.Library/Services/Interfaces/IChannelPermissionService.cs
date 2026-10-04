using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The channel half of the permission rules: PennMUSH's <c>Chan_*</c> macros (<c>hdrs/extchat.h</c>).
/// Channel commands, functions and services depend on this; nothing else needs it.
/// </summary>
/// <remarks>
/// It is not part of <see cref="IPermissionService"/>: a caller that needs both asks for both. The
/// engine registers one <c>PermissionService</c> under the two interfaces, so they share a single instance.
/// </remarks>
public interface IChannelPermissionService
{
	/// <summary>PennMUSH <c>Chan_Ok_Type</c> — <c>hdrs/extchat.h:196</c>.</summary>
	bool ChannelOkType(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can</c> — <c>hdrs/extchat.h:198</c>.</summary>
	ValueTask<bool> ChannelStandardCan(AnySharpObject target, string[] channelType);

	/// <summary>PennMUSH <c>Chan_Can_Priv</c> — <c>hdrs/extchat.h:203</c>; the type being set, not the current one.</summary>
	ValueTask<bool> ChannelCanPriv(AnySharpObject target, string[] channelType);

	/// <summary>PennMUSH <c>Chan_Can_Access</c>.</summary>
	ValueTask<bool> ChannelCanAccess(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Join</c>.</summary>
	ValueTask<bool> ChannelCanJoin(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Speak</c>.</summary>
	ValueTask<bool> ChannelCanSpeak(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Cemit</c>.</summary>
	ValueTask<bool> ChannelCanCemit(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Modify</c> — <c>hdrs/extchat.h:210</c>.</summary>
	ValueTask<bool> ChannelCanModifyAsync(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_See</c>.</summary>
	ValueTask<bool> ChannelCanSeeAsync(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Hide</c> — <c>hdrs/extchat.h:216</c>.</summary>
	ValueTask<bool> ChannelCanHide(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Nuke</c>.</summary>
	ValueTask<bool> ChannelCanNukeAsync(AnySharpObject target, SharpChannel channel);

	/// <summary>PennMUSH <c>Chan_Can_Decomp</c>.</summary>
	ValueTask<bool> ChannelCanDecomposeAsync(AnySharpObject target, SharpChannel channel);
}
