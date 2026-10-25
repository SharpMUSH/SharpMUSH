using System.IO.Compression;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>What an import makes of the files it is given, before any of them is read as a database.</summary>
public static class ImportInputs
{
	/// <summary>The most an input may hold once decompressed: the worker's limit on any input.</summary>
	public const long MaxBytes = 512L << 20;

	/// <summary>
	/// Whether the file is gzip, as PennMUSH writes its dumps by default (<c>outdb.gz</c>): told by its first two
	/// bytes, not its name, since an upload keeps no name.
	/// </summary>
	public static bool IsGzip(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		Span<byte> magic = stackalloc byte[2];
		return stream.ReadAtLeast(magic, 2, throwOnEndOfStream: false) == 2 && magic[0] == 0x1f && magic[1] == 0x8b;
	}

	/// <summary>Writes the decompressed <paramref name="input"/> to <paramref name="output"/>, refusing one past <see cref="MaxBytes"/>.</summary>
	public static async Task DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken)
	{
		await using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
		var buffer = new byte[81920];
		long written = 0;
		int read;
		while ((read = await gzip.ReadAsync(buffer, cancellationToken)) > 0)
		{
			written += read;
			if (written > MaxBytes) throw new IOException("An import input decompresses to more than the 512 MiB worker limit.");
			await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
		}
	}

	/// <summary>
	/// How long an import of these files may take: ten minutes, and a minute more for every 8 MiB they hold. A
	/// 94 MB PennMUSH dump of 45,000 objects converts in a little over three minutes.
	/// </summary>
	public static TimeSpan Deadline(DatabaseImportFiles files)
	{
		var bytes = new[] { files.Database, files.Mail, files.Chat, files.MailFolders }
			.OfType<string>().Where(File.Exists).Sum(path => new FileInfo(path).Length);
		return TimeSpan.FromMinutes(10 + bytes / (8L << 20));
	}
}
