using Mediator;
using SharpMUSH.Library.Models;
namespace SharpMUSH.Library.Queries.Database;
/// <summary>Uncached: changes must invalidate in-flight authentication immediately.</summary>
public record GetExternalProviderSettingsQuery(string Provider) : IQuery<ExternalProviderSettings>;
/// <summary>Uncached: acknowledgement uses the current identity owners.</summary>
public record GetExternalProviderAccountCountQuery(string Registration) : IQuery<int>;
