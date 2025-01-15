using Newtonsoft.Json;
using SharpMUSH.Library.DiscriminatedUnions;

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
	[JsonIgnore] public required Lazy<AnyOptionalSharpObject> From { get; set; }
}