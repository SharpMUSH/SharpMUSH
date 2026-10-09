using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

// Feed writes (help @feed). Nothing reads feeds through the cache, so nothing is invalidated: a feed's
// members and lines change with every line sent, and the kind and tap rows are one key read each.

/// <summary>Writes a kind, replacing one of the same name. See <see cref="IFeedStore.SetFeedKindAsync"/>.</summary>
public record SetFeedKindCommand(SharpFeedKind Kind) : ICommand;

/// <summary>Deletes a kind and everything of it. See <see cref="IFeedStore.DeleteFeedKindAsync"/>.</summary>
public record DeleteFeedKindCommand(string Kind) : ICommand<bool>;

/// <summary>Writes a feed's settings and locks. See <see cref="IFeedStore.SetFeedAsync"/>.</summary>
public record SetFeedCommand(SharpFeed Feed) : ICommand;

/// <summary>Deletes a feed with its members and lines. See <see cref="IFeedStore.DeleteFeedAsync"/>.</summary>
public record DeleteFeedCommand(string Kind, string Key) : ICommand<bool>;

/// <summary>Moves a feed's lines and members to another key of its kind. See <see cref="IFeedStore.RenameFeedAsync"/>.</summary>
public record RenameFeedCommand(string Kind, string From, string To) : ICommand<bool>;

/// <summary>Writes a membership. See <see cref="IFeedStore.SetFeedMemberAsync"/>.</summary>
public record SetFeedMemberCommand(string Kind, string Key, SharpFeedMember Member) : ICommand;

/// <summary>Ends a membership. See <see cref="IFeedStore.RemoveFeedMemberAsync"/>.</summary>
public record RemoveFeedMemberCommand(string Kind, string Key, DBRef Member) : ICommand<bool>;

/// <summary>Stores a line within <paramref name="Limits"/>. See <see cref="IFeedStore.AppendFeedMessageAsync"/>.</summary>
public record AppendFeedMessageCommand(SharpFeedMessage Message, FeedSettings Limits) : ICommand;

/// <summary>Drops a feed's lines sent before <paramref name="Before"/>, or all. See <see cref="IFeedStore.PurgeFeedAsync"/>.</summary>
public record PurgeFeedCommand(string Kind, string Key, DateTimeOffset? Before) : ICommand<int>;

/// <summary>Adds a tap. See <see cref="IFeedStore.AddFeedTapAsync"/>.</summary>
public record AddFeedTapCommand(SharpFeedTap Tap) : ICommand;

/// <summary>Removes a tap. See <see cref="IFeedStore.RemoveFeedTapAsync"/>.</summary>
public record RemoveFeedTapCommand(SharpFeedTap Tap) : ICommand<bool>;
