using SharpMUSH.Library.DiscriminatedUnions;
using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Implementation.Common;

public static class CryptoHelpers
{
	public static readonly Dictionary<string, HashAlgorithm> hashAlgorithms = new(StringComparer.InvariantCultureIgnoreCase)
	{
		{"MD5",    MD5.Create()},
		{"SHA1",   SHA1.Create()},
		{"SHA256", SHA256.Create()},
		{"SHA384", SHA384.Create()},
		{"SHA512", SHA512.Create()}
	};

	public static Option<string> Digest(string type, MString str)
	{
		if (!hashAlgorithms.TryGetValue(type, out var hashAlgorithm))
			return new None();

		hashAlgorithm.Initialize();
		var data = hashAlgorithm.ComputeHash(Encoding.UTF8.GetBytes(str.ToPlainText()));

		var sb = new StringBuilder();
		foreach (var bt in data)
			sb.Append(bt.ToString("x2"));

		return sb.ToString();
	}
}
