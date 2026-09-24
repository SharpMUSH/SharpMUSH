using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateMailAliasCommand(SharpMailAlias Alias) : ICommand<Result<SharpMailAlias>>;

/// <summary>Replaces the alias named <paramref name="Name"/>; a different <c>Alias.Name</c> renames it.</summary>
public record UpdateMailAliasCommand(string Name, SharpMailAlias Alias) : ICommand<Result<SharpMailAlias>>;

public record DeleteMailAliasCommand(string Name) : ICommand<Found<None>>;

public record DeleteAllMailAliasesCommand : ICommand;

/// <summary>PennMUSH <c>malias_cleanup</c> for a destroyed player.</summary>
public record ReleaseMailAliasesCommand(int Player, int NewOwner) : ICommand;
