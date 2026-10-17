using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Services;

/// <summary>A non-secret session reference for short-lived authority records; never store a source bearer.</summary>
public static class AccountSessionDigest
{
	public static string Compute(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
