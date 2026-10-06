using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Models;

// The comm-feed package's OOB payloads (`comm.channels`, `comm.message`, `comm.who`), typed as far as the feed needs
// beyond ICommFeed's own records. The wire contract is docs/softcode/comm-feed-handler.md; the design
// handoff's proposal is docs/design/d1/README.md §7.3. CommPayloadParser builds these.

/// <summary>Someone taking part in a channel line or page: a name, and the objid when it was sent.</summary>
public sealed record CommParticipant(string Name, string? ObjId);

/// <summary>A <c>comm.channels</c> push: who it was built for, and their channels.</summary>
/// <param name="Viewer">The player the list is for, or null when the payload does not say.</param>
/// <param name="Channels">The channels, in the order sent. <see cref="CommChannel.Unread"/> is what the
/// game sent, 0 when it sent none (the bundled package never does).</param>
/// <param name="ServerUnread">The counts the game did send, by channel name — so a 0 it sent can be told
/// apart from a row that carried none.</param>
public sealed record CommChannelList(
	CommParticipant? Viewer,
	IReadOnlyList<CommChannel> Channels,
	IReadOnlyDictionary<string, int> ServerUnread);

/// <summary>A <c>comm.message</c> push.</summary>
/// <param name="Message">The line, as the feed files it.</param>
/// <param name="Recipients">A page's recipients, each with its objid when the payload's <c>toObjids</c>
/// lines up with <c>to</c>; empty for a channel line.</param>
public sealed record CommEntry(CommMessage Message, IReadOnlyList<CommParticipant> Recipients);

/// <summary>A <c>comm.who</c> push: <paramref name="Member"/> came onto (<paramref name="Online"/>) or went off
/// <paramref name="Channel"/>'s member list, as the receiving player sees it.</summary>
public sealed record CommWhoChange(string Channel, CommParticipant Member, bool Online);
