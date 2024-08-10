//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.13.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from D:/SharpMUSH/SharpMUSH.Generated/SharpMUSHLexer.g4 by ANTLR 4.13.1

// Unreachable code detected
#pragma warning disable 0162
// The variable '...' is assigned but its value is never used
#pragma warning disable 0219
// Missing XML comment for publicly visible type or member '...'
#pragma warning disable 1591
// Ambiguous reference in cref attribute
#pragma warning disable 419

using System;
using System.IO;
using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using DFA = Antlr4.Runtime.Dfa.DFA;

[System.CodeDom.Compiler.GeneratedCode("ANTLR", "4.13.1")]
[System.CLSCompliant(false)]
public partial class SharpMUSHLexer : Lexer {
	protected static DFA[] decisionToDFA;
	protected static PredictionContextCache sharedContextCache = new PredictionContextCache();
	public const int
		OPARENWS=1, ESCAPE=2, OBRACK=3, CBRACK=4, OBRACE=5, CBRACE=6, CPAREN=7, 
		CCARET=8, COMMAWS=9, EQUALS=10, PERCENT=11, SEMICOLON=12, COLON=13, OANSI=14, 
		RSPACE=15, FUNCHAR=16, OTHER=17, REG_STARTCARET=18, REG_NUM=19, VWX=20, 
		ARG_NUM=21, SPACE=22, BLANKLINE=23, TAB=24, DBREF=25, ENACTOR_NAME=26, 
		CAP_ENACTOR_NAME=27, ACCENT_NAME=28, MONIKER_NAME=29, SUB_PRONOUN=30, 
		OBJ_PRONOUN=31, POS_PRONOUN=32, ABS_POS_PRONOUN=33, CALLED_DBREF=34, EXECUTOR_DBREF=35, 
		LOCATION_DBREF=36, LASTCOMMAND_BEFORE_EVAL=37, LASTCOMMAND_AFTER_EVAL=38, 
		INVOCATION_DEPTH=39, CURRENT_ARG_COUNT=40, ITEXT_NUM=41, STEXT_NUM=42, 
		OTHER_SUB=43, UNESCAPE=44, ESCAPING_OTHER=45, CANSI=46, ANSICHARACTER=47;
	public const int
		SUBSTITUTION=1, ESCAPING=2, ANSI=3;
	public static string[] channelNames = {
		"DEFAULT_TOKEN_CHANNEL", "HIDDEN"
	};

	public static string[] modeNames = {
		"DEFAULT_MODE", "SUBSTITUTION", "ESCAPING", "ANSI"
	};

	public static readonly string[] ruleNames = {
		"OPARENWS", "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", 
		"CCARET", "COMMAWS", "EQUALS", "PERCENT", "SEMICOLON", "COLON", "OANSI", 
		"RSPACE", "FUNCHAR", "OTHER", "REG_STARTCARET", "REG_NUM", "VWX", "ARG_NUM", 
		"SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", "CAP_ENACTOR_NAME", 
		"ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", "POS_PRONOUN", 
		"ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "OTHER_SUB", "UNESCAPE", 
		"ESCAPING_OTHER", "CANSI", "ANSICHARACTER"
	};


	public SharpMUSHLexer(ICharStream input)
	: this(input, Console.Out, Console.Error) { }

	public SharpMUSHLexer(ICharStream input, TextWriter output, TextWriter errorOutput)
	: base(input, output, errorOutput)
	{
		Interpreter = new LexerATNSimulator(this, _ATN, decisionToDFA, sharedContextCache);
	}

	private static readonly string[] _LiteralNames = {
		null, null, null, "'['", "']'", "'{'", "'}'", "')'", "'>'", null, "'='", 
		"'%'", "';'", "':'", "'\\u001B'", "' '", null, null, null, null, null, 
		null, null, null, null, "'#'", "'n'", "'N'", "'~'", null, null, null, 
		null, null, "'@'", "'!'", null, null, null, "'?'", "'+'", null, null, 
		null, null, null, "'m'"
	};
	private static readonly string[] _SymbolicNames = {
		null, "OPARENWS", "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", 
		"CCARET", "COMMAWS", "EQUALS", "PERCENT", "SEMICOLON", "COLON", "OANSI", 
		"RSPACE", "FUNCHAR", "OTHER", "REG_STARTCARET", "REG_NUM", "VWX", "ARG_NUM", 
		"SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", "CAP_ENACTOR_NAME", 
		"ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", "POS_PRONOUN", 
		"ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "OTHER_SUB", "UNESCAPE", 
		"ESCAPING_OTHER", "CANSI", "ANSICHARACTER"
	};
	public static readonly IVocabulary DefaultVocabulary = new Vocabulary(_LiteralNames, _SymbolicNames);

	[NotNull]
	public override IVocabulary Vocabulary
	{
		get
		{
			return DefaultVocabulary;
		}
	}

	public override string GrammarFileName { get { return "SharpMUSHLexer.g4"; } }

	public override string[] RuleNames { get { return ruleNames; } }

	public override string[] ChannelNames { get { return channelNames; } }

	public override string[] ModeNames { get { return modeNames; } }

	public override int[] SerializedAtn { get { return _serializedATN; } }

	static SharpMUSHLexer() {
		decisionToDFA = new DFA[_ATN.NumberOfDecisions];
		for (int i = 0; i < _ATN.NumberOfDecisions; i++) {
			decisionToDFA[i] = new DFA(_ATN.GetDecisionState(i), i);
		}
	}
	private static int[] _serializedATN = {
		4,0,47,291,6,-1,6,-1,6,-1,6,-1,2,0,7,0,2,1,7,1,2,2,7,2,2,3,7,3,2,4,7,4,
		2,5,7,5,2,6,7,6,2,7,7,7,2,8,7,8,2,9,7,9,2,10,7,10,2,11,7,11,2,12,7,12,
		2,13,7,13,2,14,7,14,2,15,7,15,2,16,7,16,2,17,7,17,2,18,7,18,2,19,7,19,
		2,20,7,20,2,21,7,21,2,22,7,22,2,23,7,23,2,24,7,24,2,25,7,25,2,26,7,26,
		2,27,7,27,2,28,7,28,2,29,7,29,2,30,7,30,2,31,7,31,2,32,7,32,2,33,7,33,
		2,34,7,34,2,35,7,35,2,36,7,36,2,37,7,37,2,38,7,38,2,39,7,39,2,40,7,40,
		2,41,7,41,2,42,7,42,2,43,7,43,2,44,7,44,2,45,7,45,2,46,7,46,1,0,1,0,5,
		0,101,8,0,10,0,12,0,104,9,0,1,1,1,1,1,1,1,1,1,2,1,2,1,3,1,3,1,4,1,4,1,
		4,1,4,1,5,1,5,1,5,1,5,1,6,1,6,1,7,1,7,1,8,1,8,5,8,128,8,8,10,8,12,8,131,
		9,8,1,9,1,9,1,10,1,10,1,10,1,10,1,11,1,11,1,12,1,12,1,13,1,13,1,13,1,13,
		1,14,1,14,1,15,4,15,150,8,15,11,15,12,15,151,1,16,4,16,155,8,16,11,16,
		12,16,156,1,17,1,17,1,17,1,17,1,17,1,18,1,18,1,18,1,18,1,18,1,19,1,19,
		1,19,1,19,1,19,1,20,1,20,1,20,1,20,1,21,1,21,1,21,1,21,1,22,1,22,1,22,
		1,22,1,23,1,23,1,23,1,23,1,24,1,24,1,24,1,24,1,25,1,25,1,25,1,25,1,26,
		1,26,1,26,1,26,1,27,1,27,1,27,1,27,1,28,1,28,1,28,1,28,1,29,1,29,1,29,
		1,29,1,30,1,30,1,30,1,30,1,31,1,31,1,31,1,31,1,32,1,32,1,32,1,32,1,33,
		1,33,1,33,1,33,1,34,1,34,1,34,1,34,1,35,1,35,1,35,1,35,1,36,1,36,1,36,
		1,36,1,37,1,37,1,37,1,37,1,38,1,38,1,38,1,38,1,39,1,39,1,39,1,39,1,40,
		1,40,4,40,256,8,40,11,40,12,40,257,1,40,1,40,1,41,1,41,4,41,264,8,41,11,
		41,12,41,265,1,41,1,41,1,42,1,42,1,42,1,42,1,43,1,43,1,43,1,43,1,43,1,
		44,1,44,1,44,1,44,1,45,1,45,1,45,1,45,1,46,4,46,288,8,46,11,46,12,46,289,
		0,0,47,4,1,6,2,8,3,10,4,12,5,14,6,16,7,18,8,20,9,22,10,24,11,26,12,28,
		13,30,14,32,15,34,16,36,17,38,18,40,19,42,20,44,21,46,22,48,23,50,24,52,
		25,54,26,56,27,58,28,60,29,62,30,64,31,66,32,68,33,70,34,72,35,74,36,76,
		37,78,38,80,39,82,40,84,41,86,42,88,43,90,44,92,45,94,46,96,47,4,0,1,2,
		3,21,3,0,9,10,12,13,32,32,3,0,48,57,65,90,97,122,9,0,27,27,32,32,36,37,
		40,41,44,44,58,62,91,93,123,123,125,125,2,0,81,81,113,113,1,0,48,57,2,
		0,86,88,118,120,2,0,65,90,97,122,2,0,66,66,98,98,2,0,82,82,114,114,2,0,
		84,84,116,116,2,0,75,75,107,107,2,0,83,83,115,115,2,0,79,79,111,111,2,
		0,80,80,112,112,2,0,65,65,97,97,2,0,76,76,108,108,2,0,67,67,99,99,2,0,
		85,85,117,117,2,0,73,73,105,105,1,0,92,92,1,0,109,109,294,0,4,1,0,0,0,
		0,6,1,0,0,0,0,8,1,0,0,0,0,10,1,0,0,0,0,12,1,0,0,0,0,14,1,0,0,0,0,16,1,
		0,0,0,0,18,1,0,0,0,0,20,1,0,0,0,0,22,1,0,0,0,0,24,1,0,0,0,0,26,1,0,0,0,
		0,28,1,0,0,0,0,30,1,0,0,0,0,32,1,0,0,0,0,34,1,0,0,0,0,36,1,0,0,0,1,38,
		1,0,0,0,1,40,1,0,0,0,1,42,1,0,0,0,1,44,1,0,0,0,1,46,1,0,0,0,1,48,1,0,0,
		0,1,50,1,0,0,0,1,52,1,0,0,0,1,54,1,0,0,0,1,56,1,0,0,0,1,58,1,0,0,0,1,60,
		1,0,0,0,1,62,1,0,0,0,1,64,1,0,0,0,1,66,1,0,0,0,1,68,1,0,0,0,1,70,1,0,0,
		0,1,72,1,0,0,0,1,74,1,0,0,0,1,76,1,0,0,0,1,78,1,0,0,0,1,80,1,0,0,0,1,82,
		1,0,0,0,1,84,1,0,0,0,1,86,1,0,0,0,1,88,1,0,0,0,2,90,1,0,0,0,2,92,1,0,0,
		0,3,94,1,0,0,0,3,96,1,0,0,0,4,98,1,0,0,0,6,105,1,0,0,0,8,109,1,0,0,0,10,
		111,1,0,0,0,12,113,1,0,0,0,14,117,1,0,0,0,16,121,1,0,0,0,18,123,1,0,0,
		0,20,125,1,0,0,0,22,132,1,0,0,0,24,134,1,0,0,0,26,138,1,0,0,0,28,140,1,
		0,0,0,30,142,1,0,0,0,32,146,1,0,0,0,34,149,1,0,0,0,36,154,1,0,0,0,38,158,
		1,0,0,0,40,163,1,0,0,0,42,168,1,0,0,0,44,173,1,0,0,0,46,177,1,0,0,0,48,
		181,1,0,0,0,50,185,1,0,0,0,52,189,1,0,0,0,54,193,1,0,0,0,56,197,1,0,0,
		0,58,201,1,0,0,0,60,205,1,0,0,0,62,209,1,0,0,0,64,213,1,0,0,0,66,217,1,
		0,0,0,68,221,1,0,0,0,70,225,1,0,0,0,72,229,1,0,0,0,74,233,1,0,0,0,76,237,
		1,0,0,0,78,241,1,0,0,0,80,245,1,0,0,0,82,249,1,0,0,0,84,253,1,0,0,0,86,
		261,1,0,0,0,88,269,1,0,0,0,90,273,1,0,0,0,92,278,1,0,0,0,94,282,1,0,0,
		0,96,287,1,0,0,0,98,102,5,40,0,0,99,101,7,0,0,0,100,99,1,0,0,0,101,104,
		1,0,0,0,102,100,1,0,0,0,102,103,1,0,0,0,103,5,1,0,0,0,104,102,1,0,0,0,
		105,106,5,92,0,0,106,107,1,0,0,0,107,108,6,1,0,0,108,7,1,0,0,0,109,110,
		5,91,0,0,110,9,1,0,0,0,111,112,5,93,0,0,112,11,1,0,0,0,113,114,5,123,0,
		0,114,115,1,0,0,0,115,116,6,4,1,0,116,13,1,0,0,0,117,118,5,125,0,0,118,
		119,1,0,0,0,119,120,6,5,1,0,120,15,1,0,0,0,121,122,5,41,0,0,122,17,1,0,
		0,0,123,124,5,62,0,0,124,19,1,0,0,0,125,129,5,44,0,0,126,128,7,0,0,0,127,
		126,1,0,0,0,128,131,1,0,0,0,129,127,1,0,0,0,129,130,1,0,0,0,130,21,1,0,
		0,0,131,129,1,0,0,0,132,133,5,61,0,0,133,23,1,0,0,0,134,135,5,37,0,0,135,
		136,1,0,0,0,136,137,6,10,2,0,137,25,1,0,0,0,138,139,5,59,0,0,139,27,1,
		0,0,0,140,141,5,58,0,0,141,29,1,0,0,0,142,143,5,27,0,0,143,144,1,0,0,0,
		144,145,6,13,3,0,145,31,1,0,0,0,146,147,5,32,0,0,147,33,1,0,0,0,148,150,
		7,1,0,0,149,148,1,0,0,0,150,151,1,0,0,0,151,149,1,0,0,0,151,152,1,0,0,
		0,152,35,1,0,0,0,153,155,8,2,0,0,154,153,1,0,0,0,155,156,1,0,0,0,156,154,
		1,0,0,0,156,157,1,0,0,0,157,37,1,0,0,0,158,159,7,3,0,0,159,160,5,60,0,
		0,160,161,1,0,0,0,161,162,6,17,4,0,162,39,1,0,0,0,163,164,7,3,0,0,164,
		165,7,4,0,0,165,166,1,0,0,0,166,167,6,18,4,0,167,41,1,0,0,0,168,169,7,
		5,0,0,169,170,7,6,0,0,170,171,1,0,0,0,171,172,6,19,4,0,172,43,1,0,0,0,
		173,174,7,4,0,0,174,175,1,0,0,0,175,176,6,20,4,0,176,45,1,0,0,0,177,178,
		7,7,0,0,178,179,1,0,0,0,179,180,6,21,4,0,180,47,1,0,0,0,181,182,7,8,0,
		0,182,183,1,0,0,0,183,184,6,22,4,0,184,49,1,0,0,0,185,186,7,9,0,0,186,
		187,1,0,0,0,187,188,6,23,4,0,188,51,1,0,0,0,189,190,5,35,0,0,190,191,1,
		0,0,0,191,192,6,24,4,0,192,53,1,0,0,0,193,194,5,110,0,0,194,195,1,0,0,
		0,195,196,6,25,4,0,196,55,1,0,0,0,197,198,5,78,0,0,198,199,1,0,0,0,199,
		200,6,26,4,0,200,57,1,0,0,0,201,202,5,126,0,0,202,203,1,0,0,0,203,204,
		6,27,4,0,204,59,1,0,0,0,205,206,7,10,0,0,206,207,1,0,0,0,207,208,6,28,
		4,0,208,61,1,0,0,0,209,210,7,11,0,0,210,211,1,0,0,0,211,212,6,29,4,0,212,
		63,1,0,0,0,213,214,7,12,0,0,214,215,1,0,0,0,215,216,6,30,4,0,216,65,1,
		0,0,0,217,218,7,13,0,0,218,219,1,0,0,0,219,220,6,31,4,0,220,67,1,0,0,0,
		221,222,7,14,0,0,222,223,1,0,0,0,223,224,6,32,4,0,224,69,1,0,0,0,225,226,
		5,64,0,0,226,227,1,0,0,0,227,228,6,33,4,0,228,71,1,0,0,0,229,230,5,33,
		0,0,230,231,1,0,0,0,231,232,6,34,4,0,232,73,1,0,0,0,233,234,7,15,0,0,234,
		235,1,0,0,0,235,236,6,35,4,0,236,75,1,0,0,0,237,238,7,16,0,0,238,239,1,
		0,0,0,239,240,6,36,4,0,240,77,1,0,0,0,241,242,7,17,0,0,242,243,1,0,0,0,
		243,244,6,37,4,0,244,79,1,0,0,0,245,246,5,63,0,0,246,247,1,0,0,0,247,248,
		6,38,4,0,248,81,1,0,0,0,249,250,5,43,0,0,250,251,1,0,0,0,251,252,6,39,
		4,0,252,83,1,0,0,0,253,255,7,18,0,0,254,256,7,4,0,0,255,254,1,0,0,0,256,
		257,1,0,0,0,257,255,1,0,0,0,257,258,1,0,0,0,258,259,1,0,0,0,259,260,6,
		40,4,0,260,85,1,0,0,0,261,263,5,36,0,0,262,264,7,4,0,0,263,262,1,0,0,0,
		264,265,1,0,0,0,265,263,1,0,0,0,265,266,1,0,0,0,266,267,1,0,0,0,267,268,
		6,41,4,0,268,87,1,0,0,0,269,270,9,0,0,0,270,271,1,0,0,0,271,272,6,42,4,
		0,272,89,1,0,0,0,273,274,5,92,0,0,274,275,1,0,0,0,275,276,6,43,1,0,276,
		277,6,43,4,0,277,91,1,0,0,0,278,279,8,19,0,0,279,280,1,0,0,0,280,281,6,
		44,4,0,281,93,1,0,0,0,282,283,5,109,0,0,283,284,1,0,0,0,284,285,6,45,4,
		0,285,95,1,0,0,0,286,288,8,20,0,0,287,286,1,0,0,0,288,289,1,0,0,0,289,
		287,1,0,0,0,289,290,1,0,0,0,290,97,1,0,0,0,11,0,1,2,3,102,129,151,156,
		257,265,289,5,5,2,0,6,0,0,5,1,0,5,3,0,4,0,0
	};

	public static readonly ATN _ATN =
		new ATNDeserializer().Deserialize(_serializedATN);


}
