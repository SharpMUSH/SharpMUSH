using System.Text;
using System.Text.Json;
using MarkupString;
using SharpMUSH.Library.Utilities;
using SharpMUSH.SocketServer.Models;
using TelnetNegotiationCore.Gmcp;

namespace SharpMUSH.SocketServer.Services;

/// <summary>A GMCP message to send ahead of a render's text: its package and its JSON body.</summary>
public readonly record struct GmcpFrame(string Package, string Json);

/// <summary>
/// The GMCP <c>Client.Media</c> messages a render's sounds become, for a telnet client that listed
/// <c>Client.Media</c> (or <c>Client</c>) in Core.Supports. Mudlet plays sound this way: it has no MSP
/// trigger in the text and does not play MXP <c>&lt;SOUND&gt;</c>.
/// </summary>
public static class ClientMediaFrames
{
	/// <summary>The GMCP module a client lists to be sent these.</summary>
	public const string Module = "Client.Media";

	/// <summary>The key music is played under, so a new piece replaces the one playing.</summary>
	public const string MusicKey = "music";

	/// <summary>
	/// Whether a connection is sent <c>Client.Media</c>: a telnet connection that agreed to GMCP and listed
	/// the module. A WebSocket plays sound in the page.
	/// </summary>
	public static bool Wanted(string connectionType, ProtocolCapabilities capabilities) =>
		connectionType != "websocket" && capabilities.GmcpModules is { } listed && GmcpSupports.Lists(listed, Module);

	/// <summary>
	/// A <c>Client.Media.Play</c> for each sound in <paramref name="text"/> and a <c>Client.Media.Stop</c> for
	/// each <c>stopsound()</c>, in the order they stand in it; null when it has none.
	/// </summary>
	public static IReadOnlyList<GmcpFrame>? From(MarkupText text)
	{
		if (text.Runs.IsDefaultOrEmpty) return null;

		List<GmcpFrame>? frames = null;
		foreach (var run in text.Runs)
		{
			foreach (var markup in run.Markups)
			{
				var frame = markup switch
				{
					SoundMarkup sound => Play(sound),
					SoundStopMarkup stop => Stop(stop),
					_ => (GmcpFrame?)null
				};
				if (frame is not { } found) continue;

				// Two of the same sound side by side are one run over two carriers; each is played.
				var point = (IPointMarkup)markup;
				var times = Math.Max(1, run.Length / Math.Max(1, point.Carrier.Length));
				frames ??= [];
				for (var i = 0; i < times; i++) frames.Add(found);
			}
		}

		return frames;
	}

	/// <summary>
	/// <c>Client.Media.Play</c> for <paramref name="sound"/>. An http or https address is split into the
	/// directory Mudlet fetches from (<c>url</c>) and the file (<c>name</c>), since Mudlet joins the two;
	/// anything else is a file under <c>Client.Media.Default</c>. Null for an address that names no file.
	/// </summary>
	public static GmcpFrame? Play(SoundMarkup sound)
	{
		var (url, name) = Locate(sound.Source);
		if (string.IsNullOrEmpty(name)) return null;

		var music = sound.Channel == SoundChannel.Music;
		var play = new MediaPlay(name)
		{
			Url = url,
			Type = music ? MediaType.Music : MediaType.Sound,
			Volume = sound.Volume,
			Loops = sound.Repeats,
			// Mudlet carries on with a piece already playing unless told not to, so music always says which.
			Continue = music ? sound.Continues : null,
			Key = music ? MusicKey : null
		};
		return new GmcpFrame(play.Package, play.ToJson());
	}

	/// <summary><c>Client.Media.Stop</c> for <paramref name="stop"/>: one type, or everything.</summary>
	public static GmcpFrame Stop(SoundStopMarkup stop)
	{
		var message = new MediaStop
		{
			Type = stop.Channel switch
			{
				SoundChannel.Music => MediaType.Music,
				SoundChannel.Effects => MediaType.Sound,
				_ => null
			}
		};
		return new GmcpFrame(message.Package, message.ToJson());
	}

	/// <summary>
	/// <c>Client.Media.Default</c> for the game's <c>media_base_url</c>, with the closing slash Mudlet needs;
	/// null when it is unset.
	/// </summary>
	public static GmcpFrame? Default(string? mediaBaseUrl)
	{
		if (string.IsNullOrWhiteSpace(mediaBaseUrl)) return null;
		var trimmed = mediaBaseUrl.Trim();
		var message = new MediaDefault(trimmed.EndsWith('/') ? trimmed : trimmed + "/");
		return new GmcpFrame(message.Package, message.ToJson());
	}

	private static (string? Url, string Name) Locate(string source)
	{
		var trimmed = source.Trim();
		if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
			return (null, trimmed);

		var path = uri.GetLeftPart(UriPartial.Path);
		var cut = path.LastIndexOf('/') + 1;
		return (path[..cut], path[cut..] + uri.Query);
	}

	/// <summary>
	/// The response header the rendering worker carries a render's frames in, so the body stays the text alone
	/// and a socket server that does not read it loses only the sound.
	/// </summary>
	public const string Header = "X-SharpMUSH-Gmcp";

	/// <summary>
	/// The most <see cref="Header"/> may hold. The socket server's HTTP client refuses a response whose headers pass
	/// 64 KB, and would take that for a worker outage and ask again for ever, so a render's frames stop well short.
	/// </summary>
	public const int MaxHeaderLength = 16 * 1024;

	/// <summary>
	/// The frames as <see cref="Header"/> carries them: a JSON array, base64 encoded, of as many of them, in order, as
	/// fit in <see cref="MaxHeaderLength"/>; null when none does. <paramref name="dropped"/> is how many were left out.
	/// The line is sent either way.
	/// </summary>
	public static string? Encode(IReadOnlyList<GmcpFrame> frames, out int dropped)
	{
		// The array's brackets, then each pair and the comma before it.
		var length = 2;
		var kept = 0;
		foreach (var frame in frames)
		{
			var pair = JsonSerializer.SerializeToUtf8Bytes(new[] { frame.Package, frame.Json }).Length + (kept > 0 ? 1 : 0);
			if (Base64Length(length + pair) > MaxHeaderLength) break;
			length += pair;
			kept++;
		}

		dropped = frames.Count - kept;
		return kept == 0
			? null
			: Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(frames.Take(kept).Select(frame => new[] { frame.Package, frame.Json })));
	}

	private static int Base64Length(int bytes) => (bytes + 2) / 3 * 4;

	/// <summary>The frames in a <see cref="Header"/> value; null for none, or for one that does not read.</summary>
	public static IReadOnlyList<GmcpFrame>? Decode(string? header)
	{
		if (string.IsNullOrEmpty(header)) return null;
		try
		{
			var pairs = JsonSerializer.Deserialize<string[][]>(Encoding.UTF8.GetString(Convert.FromBase64String(header)));
			var frames = pairs?.Where(pair => pair is [_, _]).Select(pair => new GmcpFrame(pair[0], pair[1])).ToArray();
			return frames is { Length: > 0 } ? frames : null;
		}
		catch (Exception ex) when (ex is FormatException or JsonException)
		{
			return null;
		}
	}
}
