using System.Text.Json;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// Who a client says it is, from whichever protocol it says it in: GMCP's <c>Core.Hello</c>, MSDP's
/// <c>CLIENT_NAME</c>/<c>CLIENT_VERSION</c>, or MNES. <c>@sockset</c> shows it.
/// </summary>
public static class ConnectionClient
{
	public const string NameKey = "ClientName";
	public const string VersionKey = "ClientVersion";

	/// <summary>Records <c>{"client": …, "version": …}</c>; anything else in the message is left alone.</summary>
	public static void RecordGmcpHello(IConnectionService connections, long handle, string json)
	{
		try
		{
			using var hello = JsonDocument.Parse(json);
			if (hello.RootElement.ValueKind != JsonValueKind.Object) return;
			Record(connections, handle, Text(hello.RootElement, "client"), Text(hello.RootElement, "version"));
		}
		catch (JsonException)
		{
			// Not JSON: nothing to record.
		}

		static string? Text(JsonElement hello, string property) =>
			hello.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	/// <summary>
	/// Records a name and version, either of which may be missing. The client chose them, so they are
	/// kept short and free of control characters before anyone sees them.
	/// </summary>
	public static void Record(IConnectionService connections, long handle, string? name, string? version)
	{
		if (Clean(name) is { } cleanName) connections.Update(handle, NameKey, cleanName);
		if (Clean(version) is { } cleanVersion) connections.Update(handle, VersionKey, cleanVersion);
	}

	private const int MaxLength = 64;

	private static string? Clean(string? value)
	{
		var printable = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
		return printable.Length == 0 ? null : printable[..Math.Min(printable.Length, MaxLength)];
	}
}
