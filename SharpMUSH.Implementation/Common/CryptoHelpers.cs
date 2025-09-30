using System.Security.Cryptography;
using System.Text;
using OneOf;
using OneOf.Types;

namespace SharpMUSH.Implementation.Common;

public static class CryptoHelpers
{
	public static readonly Dictionary<string, HashAlgorithm> hashAlgorithms = new(StringComparer.InvariantCultureIgnoreCase) 
	{
		{"MD5", MD5.Create()},
		{"SHA1", SHA1.Create()},
		{"SHA256", SHA256.Create()},
		{"SHA384", SHA384.Create()},
		{"SHA512", SHA512.Create()}
	};

	public static OneOf<string, None> Digest(string type, MString str)
	{
		if (!hashAlgorithms.TryGetValue(type, out var hashAlgorithm))
		{
			return new None();
		}
		
		hashAlgorithm.Initialize();
		
		var data = hashAlgorithm.ComputeHash(Encoding.UTF32.GetBytes(str.ToPlainText()));
		
		var sBuilder = new StringBuilder();

		// Loop through each byte of the hashed data and format each one as a hexadecimal string.
		foreach (var bt in data)
		{
			sBuilder.Append(bt.ToString("x2")); // "x2" formats as a two-digit hexadecimal number
		}

		// Return the hexadecimal string.
		return sBuilder.ToString();
	}
}