using System.Security.Cryptography;
using System.Text;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// The one content-hash the engine uses to fingerprint a serialized value: SHA-256 of the UTF-8
/// bytes, rendered as uppercase hex.
/// </summary>
/// <remarks>
/// The casing is load-bearing, not cosmetic. Object snapshots store this digest and re-verify it on
/// read (<see cref="Services.Snapshots.ObjectSnapshotService"/> rejects a snapshot whose digest does
/// not match), so a snapshot written by an earlier build has to keep matching. Every other caller
/// only writes the value, which is why they all use this uppercase form.
/// </remarks>
public static class ContentHash
{
	/// <summary>SHA-256 of <paramref name="value"/>'s UTF-8 bytes as uppercase hex.</summary>
	public static string Sha256Hex(string value)
		=> Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
