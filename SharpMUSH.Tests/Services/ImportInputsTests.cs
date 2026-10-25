using System.IO.Compression;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Services;

public class ImportInputsTests
{
	private readonly string _root = Path.Join(Path.GetTempPath(), "import-inputs-" + Guid.NewGuid().ToString("N"));

	[After(Test)]
	public void Cleanup()
	{
		if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
	}

	private string Sized(string name, long length)
	{
		Directory.CreateDirectory(_root);
		var path = Path.Join(_root, name);
		using var file = File.Create(path);
		file.SetLength(length);
		return path;
	}

	[Test]
	public async Task GzipIsToldByItsFirstBytesNotItsName()
	{
		Directory.CreateDirectory(_root);
		var compressed = Path.Join(_root, "outdb");
		await using (var gzip = new GZipStream(File.Create(compressed), CompressionLevel.Fastest))
			await gzip.WriteAsync("+V-1\n"u8.ToArray());
		var plain = Path.Join(_root, "outdb.gz");
		await File.WriteAllTextAsync(plain, "+V-1\n");

		await Assert.That(ImportInputs.IsGzip(compressed)).IsTrue();
		await Assert.That(ImportInputs.IsGzip(plain)).IsFalse();
		await Assert.That(ImportInputs.IsGzip(Sized("empty", 0))).IsFalse();
	}

	/// <summary>A small file that unpacks past the worker's limit is refused as it unpacks, not after.</summary>
	[Test]
	public async Task DecompressingPastTheLimitIsRefused()
	{
		using var compressed = new MemoryStream();
		await using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
		{
			var zeros = new byte[1 << 20];
			for (var i = 0; i <= ImportInputs.MaxBytes >> 20; i++) await gzip.WriteAsync(zeros);
		}

		compressed.Position = 0;
		var written = new CountingStream();
		await Assert.That(async () => await ImportInputs.DecompressAsync(compressed, written, CancellationToken.None))
			.Throws<IOException>().WithMessageContaining("512 MiB");
		await Assert.That(written.Length).IsLessThanOrEqualTo(ImportInputs.MaxBytes);
	}

	[Test]
	public async Task TheDeadlineGrowsWithTheInputs()
	{
		var small = new DatabaseImportFiles(Sized("small", 1 << 20));
		var large = new DatabaseImportFiles(Sized("large", 64L << 20), Mail: Sized("mail", 16L << 20), Chat: Path.Join(_root, "missing"));

		await Assert.That(ImportInputs.Deadline(small)).IsEqualTo(TimeSpan.FromMinutes(10));
		await Assert.That(ImportInputs.Deadline(large)).IsEqualTo(TimeSpan.FromMinutes(20));
	}

	/// <summary>Takes what is written and keeps only its length.</summary>
	private sealed class CountingStream : Stream
	{
		private long _length;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => _length;
		public override long Position { get => _length; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => _length += count;
	}
}
