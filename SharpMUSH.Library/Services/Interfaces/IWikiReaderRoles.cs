namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The roles a wiki reader holds, looked up from the dbref the reader was built with, for a reader that came
/// with permissions alone (a portal session's claims). A private wiki area may let in every holder of a role.
/// </summary>
public interface IWikiReaderRoles
{
	/// <summary>The slugs of the roles the object at <paramref name="dbref"/> holds; empty when there is no such object.</summary>
	ValueTask<IReadOnlySet<string>> RolesOfAsync(string dbref, CancellationToken ct = default);
}
