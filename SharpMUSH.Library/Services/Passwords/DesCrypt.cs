using System.Security.Cryptography;

namespace SharpMUSH.Library.Services.Passwords;

/// <summary>
/// The traditional DES-based <c>crypt(3)</c> of Unix V7 — crypt(5)'s "descrypt": the first eight
/// characters of the password, seven bits each, as a DES key; a two-character, 12-bit salt that
/// swaps pairs of bits in DES's expansion; 25 encryptions of an all-zero block, printed as the salt
/// plus eleven characters of <c>./0-9A-Za-z</c>.
/// </summary>
/// <remarks>
/// .NET has no <c>crypt(3)</c>, and its <see cref="System.Security.Cryptography.DES"/> cannot run a
/// salt-perturbed expansion, so this is DES written out bit by bit from the FIPS 46 tables. It exists
/// only to verify passwords very old PennMUSH databases stored as <c>crypt(password, "XX")</c>, and
/// it runs once per legacy login, so it favours legibility over speed.
/// </remarks>
internal static class DesCrypt
{
	private static readonly byte[] InitialPermutation =
	[
		58, 50, 42, 34, 26, 18, 10, 2, 60, 52, 44, 36, 28, 20, 12, 4,
		62, 54, 46, 38, 30, 22, 14, 6, 64, 56, 48, 40, 32, 24, 16, 8,
		57, 49, 41, 33, 25, 17, 9, 1, 59, 51, 43, 35, 27, 19, 11, 3,
		61, 53, 45, 37, 29, 21, 13, 5, 63, 55, 47, 39, 31, 23, 15, 7
	];

	private static readonly byte[] FinalPermutation =
	[
		40, 8, 48, 16, 56, 24, 64, 32, 39, 7, 47, 15, 55, 23, 63, 31,
		38, 6, 46, 14, 54, 22, 62, 30, 37, 5, 45, 13, 53, 21, 61, 29,
		36, 4, 44, 12, 52, 20, 60, 28, 35, 3, 43, 11, 51, 19, 59, 27,
		34, 2, 42, 10, 50, 18, 58, 26, 33, 1, 41, 9, 49, 17, 57, 25
	];

	private static readonly byte[] KeyPermutationC =
		[57, 49, 41, 33, 25, 17, 9, 1, 58, 50, 42, 34, 26, 18, 10, 2, 59, 51, 43, 35, 27, 19, 11, 3, 60, 52, 44, 36];

	private static readonly byte[] KeyPermutationD =
		[63, 55, 47, 39, 31, 23, 15, 7, 62, 54, 46, 38, 30, 22, 14, 6, 61, 53, 45, 37, 29, 21, 13, 5, 28, 20, 12, 4];

	private static readonly byte[] KeyShifts = [1, 1, 2, 2, 2, 2, 2, 2, 1, 2, 2, 2, 2, 2, 2, 1];

	private static readonly byte[] SubkeyPermutationC =
		[14, 17, 11, 24, 1, 5, 3, 28, 15, 6, 21, 10, 23, 19, 12, 4, 26, 8, 16, 7, 27, 20, 13, 2];

	private static readonly byte[] SubkeyPermutationD =
		[41, 52, 31, 37, 47, 55, 30, 40, 51, 45, 33, 48, 44, 49, 39, 56, 34, 53, 46, 42, 50, 36, 29, 32];

	private static readonly byte[] Expansion =
	[
		32, 1, 2, 3, 4, 5, 4, 5, 6, 7, 8, 9, 8, 9, 10, 11, 12, 13, 12, 13, 14, 15, 16, 17,
		16, 17, 18, 19, 20, 21, 20, 21, 22, 23, 24, 25, 24, 25, 26, 27, 28, 29, 28, 29, 30, 31, 32, 1
	];

	private static readonly byte[] RoundPermutation =
		[16, 7, 20, 21, 29, 12, 28, 17, 1, 15, 23, 26, 5, 18, 31, 10, 2, 8, 24, 14, 32, 27, 3, 9, 19, 13, 30, 6, 22, 11, 4, 25];

	private static readonly byte[][] SBoxes =
	[
		[
			14, 4, 13, 1, 2, 15, 11, 8, 3, 10, 6, 12, 5, 9, 0, 7, 0, 15, 7, 4, 14, 2, 13, 1, 10, 6, 12, 11, 9, 5, 3, 8,
			4, 1, 14, 8, 13, 6, 2, 11, 15, 12, 9, 7, 3, 10, 5, 0, 15, 12, 8, 2, 4, 9, 1, 7, 5, 11, 3, 14, 10, 0, 6, 13
		],
		[
			15, 1, 8, 14, 6, 11, 3, 4, 9, 7, 2, 13, 12, 0, 5, 10, 3, 13, 4, 7, 15, 2, 8, 14, 12, 0, 1, 10, 6, 9, 11, 5,
			0, 14, 7, 11, 10, 4, 13, 1, 5, 8, 12, 6, 9, 3, 2, 15, 13, 8, 10, 1, 3, 15, 4, 2, 11, 6, 7, 12, 0, 5, 14, 9
		],
		[
			10, 0, 9, 14, 6, 3, 15, 5, 1, 13, 12, 7, 11, 4, 2, 8, 13, 7, 0, 9, 3, 4, 6, 10, 2, 8, 5, 14, 12, 11, 15, 1,
			13, 6, 4, 9, 8, 15, 3, 0, 11, 1, 2, 12, 5, 10, 14, 7, 1, 10, 13, 0, 6, 9, 8, 7, 4, 15, 14, 3, 11, 5, 2, 12
		],
		[
			7, 13, 14, 3, 0, 6, 9, 10, 1, 2, 8, 5, 11, 12, 4, 15, 13, 8, 11, 5, 6, 15, 0, 3, 4, 7, 2, 12, 1, 10, 14, 9,
			10, 6, 9, 0, 12, 11, 7, 13, 15, 1, 3, 14, 5, 2, 8, 4, 3, 15, 0, 6, 10, 1, 13, 8, 9, 4, 5, 11, 12, 7, 2, 14
		],
		[
			2, 12, 4, 1, 7, 10, 11, 6, 8, 5, 3, 15, 13, 0, 14, 9, 14, 11, 2, 12, 4, 7, 13, 1, 5, 0, 15, 10, 3, 9, 8, 6,
			4, 2, 1, 11, 10, 13, 7, 8, 15, 9, 12, 5, 6, 3, 0, 14, 11, 8, 12, 7, 1, 14, 2, 13, 6, 15, 0, 9, 10, 4, 5, 3
		],
		[
			12, 1, 10, 15, 9, 2, 6, 8, 0, 13, 3, 4, 14, 7, 5, 11, 10, 15, 4, 2, 7, 12, 9, 5, 6, 1, 13, 14, 0, 11, 3, 8,
			9, 14, 15, 5, 2, 8, 12, 3, 7, 0, 4, 10, 1, 13, 11, 6, 4, 3, 2, 12, 9, 5, 15, 10, 11, 14, 1, 7, 6, 0, 8, 13
		],
		[
			4, 11, 2, 14, 15, 0, 8, 13, 3, 12, 9, 7, 5, 10, 6, 1, 13, 0, 11, 7, 4, 9, 1, 10, 14, 3, 5, 12, 2, 15, 8, 6,
			1, 4, 11, 13, 12, 3, 7, 14, 10, 15, 6, 8, 0, 5, 9, 2, 6, 11, 13, 8, 1, 4, 10, 7, 9, 5, 0, 15, 14, 2, 3, 12
		],
		[
			13, 2, 8, 4, 6, 15, 11, 1, 10, 9, 3, 14, 5, 0, 12, 7, 1, 15, 13, 8, 10, 3, 7, 4, 12, 5, 6, 11, 0, 14, 9, 2,
			7, 11, 4, 1, 9, 12, 14, 2, 0, 6, 10, 13, 15, 3, 5, 8, 2, 1, 14, 7, 4, 10, 8, 13, 15, 12, 9, 0, 3, 5, 6, 11
		]
	];

	/// <summary>
	/// <c>crypt(key, salt)</c>. The key is the password's bytes; only the low seven bits of the first
	/// eight matter. The salt is two characters of <c>./0-9A-Za-z</c>.
	/// </summary>
	public static string Crypt(ReadOnlySpan<byte> key, string salt)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(salt.Length, 2, nameof(salt));

		Span<byte> keyBits = stackalloc byte[64];
		keyBits.Clear();
		for (var i = 0; i < Math.Min(key.Length, 8); i++)
		{
			for (var bit = 0; bit < 7; bit++)
			{
				keyBits[i * 8 + bit] = (byte)((key[i] >> (6 - bit)) & 1);
			}
		}

		var subkeys = Subkeys(keyBits);

		// Each set salt bit swaps an expansion output with the one 24 places on.
		Span<byte> expansion = stackalloc byte[48];
		for (var i = 0; i < 48; i++)
		{
			expansion[i] = (byte)(Expansion[i] - 1);
		}

		for (var i = 0; i < 2; i++)
		{
			var value = FromCryptAlphabet(salt[i]);
			for (var bit = 0; bit < 6; bit++)
			{
				if (((value >> bit) & 1) != 0)
				{
					(expansion[6 * i + bit], expansion[6 * i + bit + 24]) = (expansion[6 * i + bit + 24], expansion[6 * i + bit]);
				}
			}
		}

		Span<byte> block = stackalloc byte[66];
		block.Clear();
		for (var i = 0; i < 25; i++)
		{
			Encrypt(block[..64], subkeys, expansion);
		}

		CryptographicOperations.ZeroMemory(keyBits);
		foreach (var subkey in subkeys)
		{
			CryptographicOperations.ZeroMemory(subkey);
		}

		Span<char> output = stackalloc char[13];
		output[0] = salt[0];
		output[1] = salt[1];
		for (var i = 0; i < 11; i++)
		{
			var value = 0;
			for (var bit = 0; bit < 6; bit++)
			{
				value = (value << 1) | block[6 * i + bit];
			}

			output[i + 2] = ToCryptAlphabet(value);
		}

		return new string(output);
	}

	private static byte[][] Subkeys(ReadOnlySpan<byte> keyBits)
	{
		Span<byte> c = stackalloc byte[28];
		Span<byte> d = stackalloc byte[28];
		for (var i = 0; i < 28; i++)
		{
			c[i] = keyBits[KeyPermutationC[i] - 1];
			d[i] = keyBits[KeyPermutationD[i] - 1];
		}

		var subkeys = new byte[16][];
		for (var round = 0; round < 16; round++)
		{
			for (var shift = 0; shift < KeyShifts[round]; shift++)
			{
				RotateLeft(c);
				RotateLeft(d);
			}

			var subkey = subkeys[round] = new byte[48];
			for (var i = 0; i < 24; i++)
			{
				subkey[i] = c[SubkeyPermutationC[i] - 1];
				subkey[i + 24] = d[SubkeyPermutationD[i] - 28 - 1];
			}
		}

		return subkeys;
	}

	private static void RotateLeft(Span<byte> half)
	{
		var first = half[0];
		half[1..].CopyTo(half);
		half[^1] = first;
	}

	private static void Encrypt(Span<byte> block, byte[][] subkeys, ReadOnlySpan<byte> expansion)
	{
		Span<byte> permuted = stackalloc byte[64];
		for (var i = 0; i < 64; i++)
		{
			permuted[i] = block[InitialPermutation[i] - 1];
		}

		var left = permuted[..32];
		var right = permuted[32..];
		Span<byte> previousRight = stackalloc byte[32];
		Span<byte> preS = stackalloc byte[48];
		Span<byte> f = stackalloc byte[32];

		for (var round = 0; round < 16; round++)
		{
			right.CopyTo(previousRight);
			for (var i = 0; i < 48; i++)
			{
				preS[i] = (byte)(right[expansion[i]] ^ subkeys[round][i]);
			}

			for (var box = 0; box < 8; box++)
			{
				var t = 6 * box;
				var row = (preS[t] << 1) | preS[t + 5];
				var column = (preS[t + 1] << 3) | (preS[t + 2] << 2) | (preS[t + 3] << 1) | preS[t + 4];
				var value = SBoxes[box][row * 16 + column];
				for (var bit = 0; bit < 4; bit++)
				{
					f[4 * box + bit] = (byte)((value >> (3 - bit)) & 1);
				}
			}

			for (var i = 0; i < 32; i++)
			{
				right[i] = (byte)(left[i] ^ f[RoundPermutation[i] - 1]);
			}

			previousRight.CopyTo(left);
		}

		// The last round's halves are not swapped back before the final permutation.
		Span<byte> preOutput = stackalloc byte[64];
		right.CopyTo(preOutput);
		left.CopyTo(preOutput[32..]);
		for (var i = 0; i < 64; i++)
		{
			block[i] = preOutput[FinalPermutation[i] - 1];
		}
	}

	private static int FromCryptAlphabet(char c) => c switch
	{
		> 'Z' => c - 6 - 7 - '.',
		> '9' => c - 7 - '.',
		_ => c - '.'
	};

	private static char ToCryptAlphabet(int value) => value switch
	{
		< 12 => (char)('.' + value),
		< 38 => (char)('.' + value + 7),
		_ => (char)('.' + value + 7 + 6)
	};
}
