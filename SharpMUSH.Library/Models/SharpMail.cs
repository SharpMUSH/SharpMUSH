using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpMail
{
	[JsonIgnore] public string? Id { get; set; }
	public required DateTimeOffset DateSent { get; set; }
	public required bool Fresh { get; set; }
	public required bool Read { get; set; }
	public required bool Tagged { get; set; }
	public required bool Urgent { get; set; }
	public required bool Forwarded { get; set; }
	public required bool Cleared { get; set; }
	public required string Folder { get; set; }
	public required MString Content { get; set; }
	public required MString Subject { get; set; }
	[JsonIgnore] public required AsyncLazy<AnyOptionalSharpObject> From { get; set; }
}