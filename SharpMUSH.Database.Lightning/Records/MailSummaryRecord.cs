namespace SharpMUSH.Database.Lightning.Records;

/// <summary>
/// The fields of a <see cref="MailRecord"/> a mailbox listing shows. Decoding a stored mail row into this
/// type skips the body: the serializer steps over <c>Content</c> without materialising it.
/// </summary>
public sealed record MailSummaryRecord
{
	public long Sender { get; init; }
	public long DateSent { get; init; }
	public bool Read { get; init; }
	public bool Urgent { get; init; }
	public string Folder { get; init; } = "";
	public string Subject { get; init; } = "";
}
