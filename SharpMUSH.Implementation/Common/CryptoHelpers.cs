using OneOf;
using OneOf.Types;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Implementation.Common;

public static class CryptoHelpers
{
	// The one-shot static hashers, not shared HashAlgorithm instances: an instance is stateful, so two
	// digest() calls hashing on it at once corrupt each other's result.
	public static readonly Dictionary<string, Func<byte[], byte[]>> hashAlgorithms = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{"MD5", MD5.HashData},
		{"SHA1", SHA1.HashData},
		{"SHA256", SHA256.HashData},
		{"SHA384", SHA384.HashData},
		{"SHA512", SHA512.HashData}
	};

	public static OneOf<string, None> Digest(string type, MString str)
	{
		if (!hashAlgorithms.TryGetValue(type, out var hash))
		{
			return new None();
		}

		return Convert.ToHexStringLower(hash(Encoding.UTF8.GetBytes(str.ToPlainText())));
	}
}