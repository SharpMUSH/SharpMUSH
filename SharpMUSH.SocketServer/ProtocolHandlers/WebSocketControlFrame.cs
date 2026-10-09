using System.Text.Json;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>
/// Discriminates browser-sent JSON control frames from ordinary command text on the WebSocket.
/// Plain text and any JSON that is not a recognized control frame are treated as commands.
/// </summary>
public static class WebSocketControlFrame
{
	private static int Clamp(int v) => v < 1 ? 1 : v > 1000 ? 1000 : v;

	/// <summary>The most terminal types a frame may report, and the longest each may be.</summary>
	public const int MaxTerminalTypes = 16;

	public const int MaxTerminalTypeLength = 64;

	/// <summary>
	/// A browser terminal's terminal types, <c>{"type":"ttype","types":["NAME","ANSI",...]}</c>: what a telnet
	/// client reports with TTYPE and MTTS, already written as names, so the game reads both the same way. A
	/// frame with no names, more than <see cref="MaxTerminalTypes"/>, or one that is not a short string is not one.
	/// </summary>
	public static bool TryParseTerminalTypes(string message, out IReadOnlyList<string> types)
	{
		types = [];

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
				|| typeEl.GetString() != "ttype"
				|| !root.TryGetProperty("types", out var typesEl)
				|| typesEl.ValueKind != JsonValueKind.Array)
				return false;

			var count = typesEl.GetArrayLength();
			if (count is 0 or > MaxTerminalTypes)
				return false;

			var names = new List<string>(count);
			foreach (var entry in typesEl.EnumerateArray())
			{
				if (entry.ValueKind != JsonValueKind.String
					|| entry.GetString()?.Trim() is not { Length: > 0 and <= MaxTerminalTypeLength } name)
					return false;
				names.Add(name);
			}

			types = names;
			return true;
		}
		catch (JsonException)
		{
			return false;
		}
	}

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
