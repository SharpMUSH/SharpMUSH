using DotNext.Threading;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public record SharpAttribute(
	string Id,
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<SharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry)
{
	[JsonIgnore]
	public StoredMarkup? StoredValue { get; init; }

	/// <summary>The metadata exists, but its stored body cannot be decoded. Never treat this as an absent attribute.</summary>
	[JsonIgnore]
	public bool IsUnreadable { get; init; }

	/// <summary>Discovery-only marker: the row exists but its access metadata is unavailable; mask its entire subtree.</summary>
	[JsonIgnore]
	public bool IsMetadataUnreadable { get; init; }

	public MString Value { get; set; } = MarkupText.Empty;

	/// <summary>
	/// When the value was last written, in Unix milliseconds (UTC): what <c>mtime(&lt;object&gt;/&lt;attribute&gt;)</c>
	/// reads. Null when the store does not know, as for an attribute written before attributes carried the time.
	/// </summary>
	[JsonIgnore]
	public long? ModifiedTime { get; init; }

	/// <summary>
	/// The attribute's own read key (<c>@atrlock/read</c>), or null: whoever passes it may read the attribute although the
	/// usual rules refuse them. A key in the attribute table applies where this is null.
	/// </summary>
	public string? ReadLock { get; init; }

	/// <summary>The attribute's own write key (<c>@atrlock/write</c>), or null; see <see cref="ReadLock"/>.</summary>
	public string? WriteLock { get; init; }
}

public record LazySharpAttribute(
	string Id,
	string Key,
	string Name,
	IEnumerable<SharpAttributeFlag> Flags,
	int? CommandListIndex,
	[property: JsonIgnore] string LongName,
	[property: JsonIgnore] AsyncLazy<IAsyncEnumerable<LazySharpAttribute>> Leaves,
	[property: JsonIgnore] AsyncLazy<SharpPlayer?> Owner,
	[property: JsonIgnore] AsyncLazy<SharpAttributeEntry?> SharpAttributeEntry,
	[property: JsonIgnore] AsyncLazy<MString> Value)
{
	/// <inheritdoc cref="SharpAttribute.ReadLock"/>
	public string? ReadLock { get; init; }

	/// <inheritdoc cref="SharpAttribute.WriteLock"/>
	public string? WriteLock { get; init; }
}