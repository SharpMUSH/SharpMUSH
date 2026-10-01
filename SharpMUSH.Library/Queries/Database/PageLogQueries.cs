using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The last <paramref name="Lines"/> pages of <paramref name="Character"/>'s own conversation with
/// <paramref name="With"/>, oldest first. Not cached: the log grows with every page, and only the portal
/// reads it, once per conversation opened.
/// </summary>
public record GetPageLogQuery(DBRef Character, IReadOnlyList<DBRef> With, int Lines) : IQuery<IReadOnlyList<SharpPage>>;

/// <summary>
/// <paramref name="Character"/>'s own logged page conversations, the latest first, at most
/// <paramref name="Limit"/> (0 for all). Not cached, as <see cref="GetPageLogQuery"/>.
/// </summary>
public record GetPageConversationsQuery(DBRef Character, int Limit = 0) : IQuery<IReadOnlyList<SharpPageConversation>>;
