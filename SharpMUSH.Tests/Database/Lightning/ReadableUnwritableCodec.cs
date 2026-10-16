namespace SharpMUSH.Tests.Fixtures;

internal sealed record ReadableUnwritableMarkup : IMarkup;

internal sealed class ReadableUnwritableCodec : IMarkupCodec
{
	public static ReadableUnwritableCodec Instance { get; } = new();
	private int _writes;
	public int Writes => Volatile.Read(ref _writes);
	public string Kind => "readable-unwritable-regression";
	public Type MarkupType => typeof(ReadableUnwritableMarkup);
	public IMarkup Read(System.Text.Json.JsonElement element) => new ReadableUnwritableMarkup();
	public void Write(System.Text.Json.Utf8JsonWriter writer, IMarkup markup)
	{
		Interlocked.Increment(ref _writes);
		throw new InvalidOperationException("writer must not run for raw copy");
	}
	public static string Payload => MarkupTextSerializer.Serialize(MarkupText.Wrap(
		new UnknownMarkup(Instance.Kind, "{\"k\":\"readable-unwritable-regression\",\"data\":42}"), MarkupText.Plain("readable")));
}
