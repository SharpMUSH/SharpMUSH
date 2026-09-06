using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Attributes on objects, attribute flags, and the attribute-entry table that governs attribute defaults.
/// </summary>
public interface IAttributeStore
{
	/// <summary>
	/// Get the attribute value of an object's attribute.
	/// </summary>
	/// <param name="dbref">DBRef of an object to get the attributes for</param>
	/// <param name="attribute">Attribute Path - uses attribute leaves</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The <see cref="SharpAttribute"/> hierarchy, with the last attribute being the final leaf.</returns>
	IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	// TODO: Return type for attribute pattern queries needs reconsideration.
	// Attribute patterns return multiple attribute paths, so return type should ideally be
	// IEnumerable<IEnumerable<SharpAttribute>> to represent full paths for each match.
	IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	/// <remarks>
	/// <b>Contract:</b> results must be ordered parent-before-child by <c>LongName</c> (a
	/// branch attribute before any of its own leaves). <c>@CLONE</c>'s attribute-tree copy
	/// (<c>BuildingCommands.cs</c>) depends on this ordering to replicate PennMUSH's
	/// <c>no_clone</c> skip-propagation (<c>atr_cpy</c>/<c>atr_new_add(makeroots: false)</c>,
	/// <c>attrib.c:1692-1710, 756-820</c>): it walks results in order and treats a LongName as
	/// skipped if its immediate parent was already skipped, which only works if the parent was
	/// actually visited first.
	/// <para>
	/// Every provider satisfies this today, but not the same way: ArangoDB's implementation
	/// sorts explicitly (<c>SORT v.LongName ASC</c>), Memgraph's Cypher traversal orders by
	/// path depth, and SurrealDB's does neither - it satisfies the invariant only because its
	/// traversal happens to be a manual preorder DFS (<c>SurrealDatabase.cs</c>,
	/// <c>GetAllAttributesForIdAsync</c>: yield an attribute, then recurse into its children,
	/// before moving to the next sibling). Production runs SurrealDB. A future change to that
	/// traversal (or a new provider) that preserves "returns every attribute" while dropping
	/// this ordering would silently break <c>@CLONE</c>'s no_clone handling without breaking
	/// this method's own contract as documented anywhere else - which is why it's documented
	/// here, on the interface, rather than left as an accident of one provider's implementation.
	/// </para>
	/// </remarks>
	IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Get an attribute with full inheritance chain resolution in a single database call.
	/// Follows the inheritance order: object → parent chain (parent, grandparent, etc.) → object's zones → parent's zones → grandparent's zones, etc.
	/// This means PARENTS TAKE PRECEDENCE OVER ZONES at all levels.
	/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
	/// Streams each attribute in the path with inherited flags merged from deeper inheritance levels.
	/// </summary>
	/// <param name="dbref">DBRef of the object to start the search from</param>
	/// <param name="attribute">Attribute path to search for (e.g., ["FOO", "BAR", "BAZ"])</param>
	/// <param name="checkParent">Whether to check parent and zone inheritance chains</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Stream of AttributeWithInheritance for each segment in the attribute path, or empty if not found</returns>
	IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lazy version of GetAttributeWithInheritanceAsync for efficient retrieval.
	/// Returns the complete attribute path (FOO → BAR → BAZ) from the first object in the inheritance chain where the attribute is found.
	/// </summary>
	IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default);

	/// <summary>
	/// Get all attribute entries from the attribute table.
	/// </summary>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Async enumerable of all attribute entries</returns>
	IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Get a specific attribute entry by name.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="ct">Cancellation Token</param>
	/// <returns>The attribute entry if found, null otherwise</returns>
	ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default);

	/// <summary>
	/// Create or update an attribute entry in the attribute table.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="defaultFlags">Default flags for this attribute</param>
	/// <param name="limit">Optional regex pattern to limit values</param>
	/// <param name="enumValues">Optional enumeration of allowed values</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>The created or updated attribute entry</returns>
	ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags, string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Delete an attribute entry from the attribute table.
	/// </summary>
	/// <param name="name">Attribute name</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="value">The value to place into the attribute</param>
	/// <param name="owner">Attribute Owner</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Bulk-reassigns all attributes owned by <paramref name="oldOwner"/> to <paramref name="newOwner"/>.
	/// Used when a player is deleted so that surviving attributes are transferred to the probate player.
	/// This operates at the edge/relationship level for efficiency.
	/// </summary>
	/// <param name="oldOwner">Player whose attribute ownership is being transferred</param>
	/// <param name="newOwner">Player who will become the new owner of those attributes</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask ReassignAttributeOwnerAsync(SharpPlayer oldOwner, SharpPlayer newOwner, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr">Attribute</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute flag. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="attr"></param>
	/// <param name="flag">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <param name="flagName">Flag</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Set an attribute. This does not do any checks, as that is up to the functionality itself.
	/// </summary>
	/// <returns>Success or Failure</returns>
	IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets an attribute to string.Empty, or if it has no children, removes it entirely.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);

	/// <summary>
	/// Wipe an attribute and all of its children.
	/// This does not do any checks regarding permissions, as that is up to the functionality itself.
	/// </summary>
	/// <param name="dbref">Database Reference Number</param>
	/// <param name="attribute">Attribute Path.</param>
	/// <param name="cancellationToken">Cancellation Token</param>
	/// <returns>Success or Failure</returns>
	ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default);
}
