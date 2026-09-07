using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAttributeStore"/>: attributes, attribute flags, and the attribute-entry table. Not
/// ported yet — every member throws <see cref="NotImplementedException"/> until Task 9.
/// </summary>
public sealed partial class LightningDatabase
{
	public IAsyncEnumerable<SharpAttribute> GetAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttribute> GetAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttribute> GetAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazySharpAttribute> GetLazyAttributesByRegexAsync(DBRef dbref, string attributePattern, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<AttributeWithInheritance> GetAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<LazyAttributeWithInheritance> GetLazyAttributeWithInheritanceAsync(DBRef dbref, string[] attribute,
		bool checkParent = true, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttributeEntry> GetAllAttributeEntriesAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeEntry?> GetSharpAttributeEntry(string name, CancellationToken ct = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeEntry?> CreateOrUpdateAttributeEntryAsync(string name, string[] defaultFlags, string? limit = null, string[]? enumValues = null, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> DeleteAttributeEntryAsync(string name, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetAttributeAsync(DBRef dbref, string[] attribute, MString value, SharpPlayer owner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask ReassignAttributeOwnerAsync(SharpPlayer oldOwner, SharpPlayer newOwner, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> SetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask SetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> UnsetAttributeFlagAsync(SharpObject dbref, string[] attribute, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask UnsetAttributeFlagAsync(SharpAttribute attr, SharpAttributeFlag flag, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<SharpAttributeFlag?> GetAttributeFlagAsync(string flagName, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public IAsyncEnumerable<SharpAttributeFlag> GetAttributeFlagsAsync(CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> ClearAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();

	public ValueTask<bool> WipeAttributeAsync(DBRef dbref, string[] attribute, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException();
}
