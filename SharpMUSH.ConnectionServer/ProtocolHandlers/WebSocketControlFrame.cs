using System.Text.Json;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Discriminates browser-sent JSON control frames from ordinary command text on the WebSocket.
/// Plain text and any JSON that is not a recognized control frame are treated as commands.
/// </summary>
public static class WebSocketControlFrame
{
	private static int Clamp(int v) => v < 1 ? 1 : v > 1000 ? 1000 : v;

	public static bool TryParseNaws(string message, out int cols, out int rows)
	{
		cols = 0;
		rows = 0;

		var trimmed = message.AsSpan().TrimStart();
		if (trimmed.Length == 0 || trimmed[0] != '{')
			return false;

		try
		{
			using var doc = JsonDocument.Parse(message);
			var root = doc.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("type", out var typeEl)
				|| typeEl.ValueKind != JsonValueKind.String
				|| typeEl.GetString() != "naws"
				|| !root.TryGetProperty("cols", out var colsEl) || colsEl.ValueKind != JsonValueKind.Number
				|| !root.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Number)
				return false;

			if (!colsEl.TryGetInt32(out var rawCols) || !rowsEl.TryGetInt32(out var rawRows))
				return false;

			cols = Clamp(rawCols);
			rows = Clamp(rawRows);
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}
}
