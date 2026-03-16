namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a semantic token with its type, modifiers, and range.
/// This provides semantic information beyond syntax highlighting.
/// </summary>
public record SemanticToken
{
	/// <summary>
	/// The range of the token in the document.
	/// </summary>
	public required Range Range { get; init; }

	/// <summary>
	/// The semantic type of the token.
	/// </summary>
	public SemanticTokenType TokenType { get; init; }

	/// <summary>
	/// The modifiers applied to the token.
	/// </summary>
	public SemanticTokenModifier Modifiers { get; init; } = SemanticTokenModifier.None;

	/// <summary>
	/// The text content of the token.
	/// </summary>
	public required string Text { get; init; }

	/// <summary>
	/// Additional data specific to the token type.
	/// For example, for ObjectReference tokens, this might contain the resolved object name.
	/// For Function tokens, this might contain function metadata.
	/// </summary>
	public object? Data { get; init; }

	/// <summary>
	/// Gets the length of the token in characters.
	/// </summary>
	public int Length => Text.Length;

	public override string ToString()
	{
		var modifiers = Modifiers != SemanticTokenModifier.None
			? $" [{Modifiers}]"
			: "";
		return $"{TokenType}{modifiers}: '{Text}' at {Range}";
	}
}

/// <summary>
/// Represents a collection of semantic tokens encoded in the LSP delta format.
/// This format is more efficient for transmission over the network in LSP scenarios.
/// </summary>
public record SemanticTokensData
{
	/// <summary>
	/// The semantic token types legend (ordered array of token type names).
	/// </summary>
	public required string[] TokenTypes { get; init; }

	/// <summary>
	/// The semantic token modifiers legend (ordered array of modifier names).
	/// </summary>
	public required string[] TokenModifiers { get; init; }

	/// <summary>
	/// The actual semantic tokens data in delta-encoded format.
	/// Each token is represented by 5 integers:
	/// - deltaLine: line delta from previous token (or absolute line for first token)
	/// - deltaChar: character delta from previous token (or absolute char for first token on new line)
	/// - length: token length
	/// - tokenType: index into tokenTypes array
	/// - tokenModifiers: bit flags representing indices in tokenModifiers array
	/// </summary>
	public required int[] Data { get; init; }

	/// <summary>
	/// Creates LSP-compatible semantic tokens data from a list of semantic tokens.
	/// </summary>
	/// <param name="tokens">The semantic tokens to encode.</param>
	/// <returns>The encoded semantic tokens data.</returns>
	public static SemanticTokensData FromTokens(IReadOnlyList<SemanticToken> tokens)
	{
		// Define the token types and modifiers legends
		var tokenTypes = Enum.GetNames(typeof(SemanticTokenType));
		var tokenModifiers = Enum.GetNames(typeof(SemanticTokenModifier))
			.Where(name => name != nameof(SemanticTokenModifier.None))
			.ToArray();

		// Encode tokens in delta format
		var data = new List<int>();
		int prevLine = 0;
		int prevChar = 0;

		foreach (var token in tokens.OrderBy(t => t.Range.Start.Line).ThenBy(t => t.Range.Start.Character))
		{
			var deltaLine = token.Range.Start.Line - prevLine;
			var deltaChar = deltaLine == 0
				? token.Range.Start.Character - prevChar
				: token.Range.Start.Character;

			var tokenTypeIndex = (int)token.TokenType;
			var tokenModifiersBits = EncodeModifiers(token.Modifiers);

			data.Add(deltaLine);
			data.Add(deltaChar);
			data.Add(token.Length);
			data.Add(tokenTypeIndex);
			data.Add(tokenModifiersBits);

			prevLine = token.Range.Start.Line;
			prevChar = token.Range.Start.Character;
		}

		return new SemanticTokensData
		{
			TokenTypes = tokenTypes,
			TokenModifiers = tokenModifiers,
			Data = [.. data]
		};
	}

	private static int EncodeModifiers(SemanticTokenModifier modifiers)
	{
		if (modifiers == SemanticTokenModifier.None)
			return 0;

		int result = 0;
		int bitPosition = 0;

		foreach (SemanticTokenModifier modifier in Enum.GetValues(typeof(SemanticTokenModifier)))
		{
			if (modifier == SemanticTokenModifier.None)
				continue;

			if (modifiers.HasFlag(modifier))
			{
				result |= (1 << bitPosition);
			}

			bitPosition++;
		}

		return result;
	}
}
