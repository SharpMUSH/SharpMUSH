using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Tools.ContractAudit;

internal static class SourceProvenance
{
	private const string Census = "docs/audits/softcode-contracts.json";

	internal static Snapshot Capture(string root, IEnumerable<string> trackedPaths)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var paths = trackedPaths.Where(path => path != Census).Order(StringComparer.Ordinal).ToArray();
		foreach (var path in paths)
		{
			Append(Encoding.UTF8.GetBytes(path));
			Append(File.ReadAllBytes(Path.Join(root, path)));
		}
		return new Snapshot("sha256-length-prefixed-tracked-paths-and-bytes-excluding-census", Convert.ToHexStringLower(hash.GetHashAndReset()), paths.Length);

		void Append(byte[] bytes)
		{
			Span<byte> length = stackalloc byte[sizeof(long)];
			BinaryPrimitives.WriteInt64LittleEndian(length, bytes.LongLength);
			hash.AppendData(length);
			hash.AppendData(bytes);
		}
	}

	internal sealed record Snapshot(string Algorithm, string ContentSha256, int Files);
}
