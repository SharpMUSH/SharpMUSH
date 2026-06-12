using System.Text;

namespace SharpMUSH.Library.Utilities;

/// <summary>
/// Normalization for q-register name segments derived from untrusted external input — HTTP header
/// names (<c>%q&lt;hdr.*&gt;</c>) and form parameter names (<c>%q&lt;form.*&gt;</c>, see <c>formq()</c>).
/// One definition of "register-safe": register names must match <c>[A-Z0-9_.-]+</c>
/// (ParserState.AddRegister), the PennMUSH analog being <c>pi_regs_normalize_key</c>.
/// </summary>
public static class RegisterNames
{
	/// <summary>
	/// Normalizes a name segment into a q-register-acceptable key: uppercased, with anything
	/// outside [A-Z0-9_.-] replaced by an underscore.
	/// </summary>
	public static string NormalizeSegment(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var c in name.ToUpperInvariant())
		{
			builder.Append(c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '.' or '-' ? c : '_');
		}

		return builder.ToString();
	}
}
