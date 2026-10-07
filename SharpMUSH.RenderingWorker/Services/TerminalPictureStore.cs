using MarkupString.Ansi;
using StbImageSharp;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace SharpMUSH.RenderingWorker.Services;

/// <summary>
/// The pictures a terminal is sent: fetched, decoded and shrunk once, and kept for every connection that
/// shows them. Rendering never waits on the network here — <see cref="TryGet"/> answers from what is
/// already held and starts a fetch for what is not.
/// </summary>
/// <remarks>
/// <para>
/// The engine only lets a picture into markup when <c>image_hosts</c> allows its address, but whatever is
/// allowed is fetched from this process, so the fetch refuses any address that resolves to this machine or
/// a private network: a player's picture must not be a way to reach the services next to the game. The
/// game's own pictures, at relative addresses, are fetched from <c>Rendering:ImageBaseAddress</c> when it
/// is set, and are text art when it is not.
/// </para>
/// <para>
/// Pictures are kept by address, each with a key that is a hash of its pixels, so the same picture at two
/// addresses is one Kitty image to a terminal. What is held is bounded by <see cref="MaxCacheBytes"/>;
/// past it the least recently used go first. A failed fetch is remembered for a while, so a broken
/// address is not fetched again for every line that names it.
/// </para>
/// </remarks>
public sealed class TerminalPictureStore : IDisposable
{
	/// <summary>The largest file fetched.</summary>
	public const int MaxDownloadBytes = 8 * 1024 * 1024;

	/// <summary>The largest picture decoded, in pixels each way; anything larger is refused unread.</summary>
	public const int MaxDecodedSide = 8192;

	/// <summary>The longest side a picture is kept at. Ample for the 297 cells a Kitty picture may cover.</summary>
	public const int MaxStoredSide = 512;

	/// <summary>How long a failed fetch is remembered before the address is tried again.</summary>
	private static readonly TimeSpan FailureMemory = TimeSpan.FromMinutes(10);

	private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
	private readonly HttpClient _remote;
	private readonly HttpClient? _local;
	private readonly Uri? _baseAddress;
	private readonly ILogger<TerminalPictureStore> _logger;
	private long _bytes;

	public TerminalPictureStore(IConfiguration configuration, ILogger<TerminalPictureStore> logger)
		: this(configuration["Rendering:ImageBaseAddress"], ParseLong(configuration["Rendering:PictureCacheBytes"]) ?? 64L * 1024 * 1024,
			logger, CreateGuardedHandler())
	{
	}

	/// <param name="baseAddress">Where the game's own pictures are, for relative addresses; null for none.</param>
	/// <param name="maxCacheBytes">How many bytes of pixels to hold.</param>
	/// <param name="logger">Where failed fetches are logged.</param>
	/// <param name="remoteHandler">What fetches pictures from elsewhere; the guarded handler outside tests.</param>
	public TerminalPictureStore(string? baseAddress, long maxCacheBytes, ILogger<TerminalPictureStore> logger,
		HttpMessageHandler remoteHandler)
	{
		_logger = logger;
		MaxCacheBytes = maxCacheBytes;
		_remote = new HttpClient(remoteHandler) { Timeout = TimeSpan.FromSeconds(10) };
		if (Uri.TryCreate(baseAddress, UriKind.Absolute, out var root) && root.Scheme is "http" or "https")
		{
			_baseAddress = root;
			_local = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		}
	}

	/// <summary>How many bytes of pixels are held at most.</summary>
	public long MaxCacheBytes { get; }

	/// <summary>
	/// The picture at <paramref name="source"/>, when it is held. When it is not, a fetch is started and
	/// <paramref name="pending"/> is what completes when it ends, or null for an address that is never fetched.
	/// </summary>
	public bool TryGet(string source, out TerminalPicture? picture, out Task? pending)
	{
		pending = null;
		picture = null;
		if (Resolve(source) is not { } address) return false;

		var entry = _entries.GetOrAdd(address.AbsoluteUri, _ => new Entry());
		entry.LastUsed = Environment.TickCount64;
		if (entry.Picture is { } held)
		{
			picture = held;
			return true;
		}

		lock (entry)
		{
			if (entry.Fetch is null || entry.Fetch.IsCompleted && Environment.TickCount64 - entry.FailedAt > FailureMemory.TotalMilliseconds)
			{
				entry.Fetch = FetchAsync(address, entry);
			}

			pending = entry.Fetch.IsCompleted ? null : entry.Fetch;
		}

		// A fetch can finish before it is first awaited, such as from a cache in front of this one.
		picture = entry.Picture;
		return picture is not null;
	}

	/// <summary>The address <paramref name="source"/> names, or null for one that is never fetched.</summary>
	private Uri? Resolve(string source)
	{
		if (string.IsNullOrWhiteSpace(source)) return null;
		if (Uri.TryCreate(source, UriKind.Absolute, out var absolute))
			return absolute.Scheme is "http" or "https" ? absolute : null;
		// Relative: one of the game's own pictures, which is fetched only from where the game says they are.
		if (_baseAddress is null || source.StartsWith("//", StringComparison.Ordinal)) return null;
		return Uri.TryCreate(_baseAddress, source, out var local) && local.Host == _baseAddress.Host ? local : null;
	}

	private async Task FetchAsync(Uri address, Entry entry)
	{
		try
		{
			var client = _baseAddress is not null && address.Host == _baseAddress.Host && _local is not null ? _local : _remote;
			using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > MaxDownloadBytes)
				throw new InvalidDataException($"{address} is over {MaxDownloadBytes} bytes.");

			await using var stream = await response.Content.ReadAsStreamAsync();
			var bytes = await ReadBoundedAsync(stream);
			var picture = Decode(bytes);
			entry.Picture = picture;
			Interlocked.Add(ref _bytes, picture.Rgba.Length);
			Trim();
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException
			or IOException or InvalidOperationException or ArgumentException)
		{
			entry.FailedAt = Environment.TickCount64;
			_logger.LogDebug(ex, "Could not fetch the picture at {Address}", address);
		}
	}

	private static async Task<byte[]> ReadBoundedAsync(Stream stream)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		int read;
		while ((read = await stream.ReadAsync(chunk)) > 0)
		{
			if (buffer.Length + read > MaxDownloadBytes) throw new InvalidDataException($"Over {MaxDownloadBytes} bytes.");
			buffer.Write(chunk, 0, read);
		}

		return buffer.ToArray();
	}

	/// <summary>
	/// <paramref name="bytes"/> decoded to RGBA and shrunk to <see cref="MaxStoredSide"/>, keyed by a hash of
	/// the file. PNG, JPEG, GIF (its first frame), BMP, TGA and PSD read; anything else throws.
	/// </summary>
	public static TerminalPicture Decode(byte[] bytes)
	{
		var info = ImageInfo.FromStream(new MemoryStream(bytes, writable: false))
			?? throw new InvalidDataException("Not a picture this server reads.");
		if (info.Width is <= 0 or > MaxDecodedSide || info.Height is <= 0 or > MaxDecodedSide)
			throw new InvalidDataException($"A {info.Width}x{info.Height} picture is over {MaxDecodedSide} pixels a side.");

		var image = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
		var (width, height, rgba) = Shrink(image.Width, image.Height, image.Data, MaxStoredSide);
		var key = Convert.ToHexString(SHA256.HashData(bytes), 0, 16);
		return new TerminalPicture(key, width, height, rgba);
	}

	/// <summary>
	/// <paramref name="rgba"/> at most <paramref name="maxSide"/> pixels a side, keeping its shape: each new
	/// pixel the average of the ones it covers, weighted by their opacity so a transparent edge does not darken.
	/// </summary>
	public static (int Width, int Height, byte[] Rgba) Shrink(int width, int height, byte[] rgba, int maxSide)
	{
		if (width <= maxSide && height <= maxSide) return (width, height, rgba);

		var scale = (double)maxSide / Math.Max(width, height);
		var newWidth = Math.Max(1, (int)Math.Round(width * scale));
		var newHeight = Math.Max(1, (int)Math.Round(height * scale));
		var result = new byte[newWidth * newHeight * 4];

		for (var y = 0; y < newHeight; y++)
		{
			var top = y * height / newHeight;
			var bottom = Math.Max(top + 1, (y + 1) * height / newHeight);
			for (var x = 0; x < newWidth; x++)
			{
				var left = x * width / newWidth;
				var right = Math.Max(left + 1, (x + 1) * width / newWidth);
				long r = 0, g = 0, b = 0, a = 0, count = 0;
				for (var sy = top; sy < bottom; sy++)
				{
					for (var sx = left; sx < right; sx++)
					{
						var i = (sy * width + sx) * 4;
						var alpha = rgba[i + 3];
						r += rgba[i] * alpha;
						g += rgba[i + 1] * alpha;
						b += rgba[i + 2] * alpha;
						a += alpha;
						count++;
					}
				}

				var o = (y * newWidth + x) * 4;
				if (a > 0)
				{
					result[o] = (byte)(r / a);
					result[o + 1] = (byte)(g / a);
					result[o + 2] = (byte)(b / a);
				}

				result[o + 3] = (byte)(a / count);
			}
		}

		return (newWidth, newHeight, result);
	}

	/// <summary>Drops the least recently used pictures until what is held fits.</summary>
	private void Trim()
	{
		if (Interlocked.Read(ref _bytes) <= MaxCacheBytes) return;

		foreach (var (address, entry) in _entries.Where(pair => pair.Value.Picture is not null)
			.OrderBy(pair => pair.Value.LastUsed).ToArray())
		{
			if (Interlocked.Read(ref _bytes) <= MaxCacheBytes) break;
			if (_entries.TryRemove(address, out var removed) && removed.Picture is { } dropped)
				Interlocked.Add(ref _bytes, -dropped.Rgba.Length);
		}

		// Failures are small but unbounded in number; keep only the recent ones.
		if (_entries.Count > 4096)
		{
			foreach (var (address, entry) in _entries.Where(pair => pair.Value.Picture is null && pair.Value.Fetch?.IsCompleted == true))
				if (Environment.TickCount64 - entry.FailedAt > FailureMemory.TotalMilliseconds) _entries.TryRemove(address, out _);
		}
	}

	/// <summary>
	/// A handler that connects only to public addresses: each address the name resolves to is checked
	/// as the connection is made, so a name that resolves somewhere else the second time is still caught.
	/// </summary>
	public static SocketsHttpHandler CreateGuardedHandler() => new()
	{
		AllowAutoRedirect = true,
		MaxAutomaticRedirections = 3,
		ConnectCallback = async (context, ct) =>
		{
			var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
			var allowed = addresses.Where(IsPublic).ToArray();
			if (allowed.Length == 0)
				throw new HttpRequestException($"{context.DnsEndPoint.Host} is not a public address.");

			var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
			try
			{
				await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
				return new NetworkStream(socket, ownsSocket: true);
			}
			catch
			{
				socket.Dispose();
				throw;
			}
		}
	};

	/// <summary>Whether <paramref name="address"/> is on the public internet: not this machine, a private network, link-local or multicast.</summary>
	public static bool IsPublic(IPAddress address)
	{
		if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
		if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
			|| address.Equals(IPAddress.Broadcast))
			return false;

		if (address.AddressFamily == AddressFamily.InterNetworkV6)
			return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal);

		var b = address.GetAddressBytes();
		return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
			|| (b[0] == 100 && b[1] is >= 64 and <= 127)
			|| (b[0] == 169 && b[1] == 254)
			|| (b[0] == 172 && b[1] is >= 16 and <= 31)
			|| (b[0] == 192 && b[1] == 168)
			|| (b[0] == 192 && b[1] == 0 && b[2] == 0)
			|| (b[0] == 198 && b[1] is 18 or 19));
	}

	private static long? ParseLong(string? value) => long.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

	public void Dispose()
	{
		_remote.Dispose();
		_local?.Dispose();
	}

	private sealed class Entry
	{
		public volatile TerminalPicture? Picture;
		public Task? Fetch;
		public long LastUsed;
		public long FailedAt;
	}
}
