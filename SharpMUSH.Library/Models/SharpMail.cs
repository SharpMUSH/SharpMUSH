namespace SharpMUSH.Library.Models;

public class SharpMail
{
	public required DateTimeOffset DateSent { get; set; }
	public required bool Fresh { get; set; }
	public required bool Read { get; set; }
	public required bool Tagged { get; set; }
	public required bool Urgent { get; set; }
	public required bool Cleared { get; set; }
	public required string Folder { get; set; }
	public required MString Content { get; set; }
	public required MString Subject { get; set; }
	public required Lazy<SharpObject> From { get; set; }
}