//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     ANTLR Version: 4.13.1
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from c:/Users/admin/OneDrive/Documents/Repos/MUParser/AntlrCSharp.Generated/PennMUSHLexer.g4 by ANTLR 4.13.1

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
public partial class PennMUSHLexer : Lexer {
	protected static DFA[] decisionToDFA;
	protected static PredictionContextCache sharedContextCache = new PredictionContextCache();
	public const int
		OPAREN=1, ESCAPE=2, OBRACK=3, CBRACK=4, OBRACE=5, CBRACE=6, CPAREN=7, 
		OCARET=8, CCARET=9, COMMA=10, EQUALS=11, DOLLAR=12, PERCENT=13, SEMICOLON=14, 
		COLON=15, OANSI=16, FUNCHAR=17, OTHER=18, REG_STARTCARET=19, REG_NUM=20, 
		VWX=21, ARG_NUM=22, SPACE=23, BLANKLINE=24, TAB=25, DBREF=26, ENACTOR_NAME=27, 
		CAP_ENACTOR_NAME=28, ACCENT_NAME=29, MONIKER_NAME=30, SUB_PRONOUN=31, 
		OBJ_PRONOUN=32, POS_PRONOUN=33, ABS_POS_PRONOUN=34, CALLED_DBREF=35, EXECUTOR_DBREF=36, 
		LOCATION_DBREF=37, LASTCOMMAND_BEFORE_EVAL=38, LASTCOMMAND_AFTER_EVAL=39, 
		INVOCATION_DEPTH=40, CURRENT_ARG_COUNT=41, ITEXT_NUM=42, STEXT_NUM=43, 
		UNESCAPE=44, ESCAPING_OTHER=45;
	public const int
		SUBSTITUTION=1, ESCAPING=2;
	public static string[] channelNames = {
		"DEFAULT_TOKEN_CHANNEL", "HIDDEN"
	};

	public static string[] modeNames = {
		"DEFAULT_MODE", "SUBSTITUTION", "ESCAPING"
	};

	public static readonly string[] ruleNames = {
		"OPAREN", "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", 
		"OCARET", "CCARET", "COMMA", "EQUALS", "DOLLAR", "PERCENT", "SEMICOLON", 
		"COLON", "OANSI", "FUNCHAR", "OTHER", "REG_STARTCARET", "REG_NUM", "VWX", 
		"ARG_NUM", "SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", "CAP_ENACTOR_NAME", 
		"ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", "POS_PRONOUN", 
		"ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "UNESCAPE", "ESCAPING_OTHER"
	};


	public PennMUSHLexer(ICharStream input)
	: this(input, Console.Out, Console.Error) { }

	public PennMUSHLexer(ICharStream input, TextWriter output, TextWriter errorOutput)
	: base(input, output, errorOutput)
	{
		Interpreter = new LexerATNSimulator(this, _ATN, decisionToDFA, sharedContextCache);
	}

	private static readonly string[] _LiteralNames = {
		null, "'('", null, "'['", "']'", "'{'", "'}'", "')'", "'<'", "'>'", "','", 
		"'='", "'$'", "'%'", "';'", "':'", "'\\u001B'", null, null, null, null, 
		null, null, null, null, null, "'#'", "'n'", "'N'", "'~'", null, null, 
		null, null, null, "'@'", "'!'", null, null, null, "'?'", "'+'"
	};
	private static readonly string[] _SymbolicNames = {
		null, "OPAREN", "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", 
		"OCARET", "CCARET", "COMMA", "EQUALS", "DOLLAR", "PERCENT", "SEMICOLON", 
		"COLON", "OANSI", "FUNCHAR", "OTHER", "REG_STARTCARET", "REG_NUM", "VWX", 
		"ARG_NUM", "SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", "CAP_ENACTOR_NAME", 
		"ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", "POS_PRONOUN", 
		"ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "UNESCAPE", "ESCAPING_OTHER"
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

	public override string GrammarFileName { get { return "PennMUSHLexer.g4"; } }

	public override string[] RuleNames { get { return ruleNames; } }

	public override string[] ChannelNames { get { return channelNames; } }

	public override string[] ModeNames { get { return modeNames; } }

	public override int[] SerializedAtn { get { return _serializedATN; } }

	static PennMUSHLexer() {
		decisionToDFA = new DFA[_ATN.NumberOfDecisions];
		for (int i = 0; i < _ATN.NumberOfDecisions; i++) {
			decisionToDFA[i] = new DFA(_ATN.GetDecisionState(i), i);
		}
	}
	private static int[] _serializedATN = {
		4,0,45,263,6,-1,6,-1,6,-1,2,0,7,0,2,1,7,1,2,2,7,2,2,3,7,3,2,4,7,4,2,5,
		7,5,2,6,7,6,2,7,7,7,2,8,7,8,2,9,7,9,2,10,7,10,2,11,7,11,2,12,7,12,2,13,
		7,13,2,14,7,14,2,15,7,15,2,16,7,16,2,17,7,17,2,18,7,18,2,19,7,19,2,20,
		7,20,2,21,7,21,2,22,7,22,2,23,7,23,2,24,7,24,2,25,7,25,2,26,7,26,2,27,
		7,27,2,28,7,28,2,29,7,29,2,30,7,30,2,31,7,31,2,32,7,32,2,33,7,33,2,34,
		7,34,2,35,7,35,2,36,7,36,2,37,7,37,2,38,7,38,2,39,7,39,2,40,7,40,2,41,
		7,41,2,42,7,42,2,43,7,43,2,44,7,44,1,0,1,0,1,1,1,1,1,1,1,1,1,2,1,2,1,3,
		1,3,1,4,1,4,1,4,1,4,1,5,1,5,1,5,1,5,1,6,1,6,1,7,1,7,1,8,1,8,1,9,1,9,1,
		10,1,10,1,11,1,11,1,12,1,12,1,12,1,12,1,13,1,13,1,14,1,14,1,15,1,15,1,
		16,4,16,135,8,16,11,16,12,16,136,1,17,4,17,140,8,17,11,17,12,17,141,1,
		18,1,18,1,18,1,18,1,18,1,19,1,19,1,19,1,19,1,19,1,20,1,20,1,20,1,20,1,
		20,1,21,1,21,1,21,1,21,1,22,1,22,1,22,1,22,1,23,1,23,1,23,1,23,1,24,1,
		24,1,24,1,24,1,25,1,25,1,25,1,25,1,26,1,26,1,26,1,26,1,27,1,27,1,27,1,
		27,1,28,1,28,1,28,1,28,1,29,1,29,1,29,1,29,1,30,1,30,1,30,1,30,1,31,1,
		31,1,31,1,31,1,32,1,32,1,32,1,32,1,33,1,33,1,33,1,33,1,34,1,34,1,34,1,
		34,1,35,1,35,1,35,1,35,1,36,1,36,1,36,1,36,1,37,1,37,1,37,1,37,1,38,1,
		38,1,38,1,38,1,39,1,39,1,39,1,39,1,40,1,40,1,40,1,40,1,41,1,41,4,41,241,
		8,41,11,41,12,41,242,1,41,1,41,1,42,1,42,4,42,249,8,42,11,42,12,42,250,
		1,42,1,42,1,43,1,43,1,43,1,43,1,43,1,44,1,44,1,44,1,44,0,0,45,3,1,5,2,
		7,3,9,4,11,5,13,6,15,7,17,8,19,9,21,10,23,11,25,12,27,13,29,14,31,15,33,
		16,35,17,37,18,39,19,41,20,43,21,45,22,47,23,49,24,51,25,53,26,55,27,57,
		28,59,29,61,30,63,31,65,32,67,33,69,34,71,35,73,36,75,37,77,38,79,39,81,
		40,83,41,85,42,87,43,89,44,91,45,3,0,1,2,19,3,0,48,57,65,90,97,122,8,0,
		27,27,36,37,40,41,44,44,58,62,91,93,123,123,125,125,2,0,81,81,113,113,
		1,0,48,57,2,0,86,88,118,120,2,0,65,90,97,122,2,0,66,66,98,98,2,0,82,82,
		114,114,2,0,84,84,116,116,2,0,75,75,107,107,2,0,83,83,115,115,2,0,79,79,
		111,111,2,0,80,80,112,112,2,0,65,65,97,97,2,0,76,76,108,108,2,0,67,67,
		99,99,2,0,85,85,117,117,2,0,73,73,105,105,1,0,92,92,264,0,3,1,0,0,0,0,
		5,1,0,0,0,0,7,1,0,0,0,0,9,1,0,0,0,0,11,1,0,0,0,0,13,1,0,0,0,0,15,1,0,0,
		0,0,17,1,0,0,0,0,19,1,0,0,0,0,21,1,0,0,0,0,23,1,0,0,0,0,25,1,0,0,0,0,27,
		1,0,0,0,0,29,1,0,0,0,0,31,1,0,0,0,0,33,1,0,0,0,0,35,1,0,0,0,0,37,1,0,0,
		0,1,39,1,0,0,0,1,41,1,0,0,0,1,43,1,0,0,0,1,45,1,0,0,0,1,47,1,0,0,0,1,49,
		1,0,0,0,1,51,1,0,0,0,1,53,1,0,0,0,1,55,1,0,0,0,1,57,1,0,0,0,1,59,1,0,0,
		0,1,61,1,0,0,0,1,63,1,0,0,0,1,65,1,0,0,0,1,67,1,0,0,0,1,69,1,0,0,0,1,71,
		1,0,0,0,1,73,1,0,0,0,1,75,1,0,0,0,1,77,1,0,0,0,1,79,1,0,0,0,1,81,1,0,0,
		0,1,83,1,0,0,0,1,85,1,0,0,0,1,87,1,0,0,0,2,89,1,0,0,0,2,91,1,0,0,0,3,93,
		1,0,0,0,5,95,1,0,0,0,7,99,1,0,0,0,9,101,1,0,0,0,11,103,1,0,0,0,13,107,
		1,0,0,0,15,111,1,0,0,0,17,113,1,0,0,0,19,115,1,0,0,0,21,117,1,0,0,0,23,
		119,1,0,0,0,25,121,1,0,0,0,27,123,1,0,0,0,29,127,1,0,0,0,31,129,1,0,0,
		0,33,131,1,0,0,0,35,134,1,0,0,0,37,139,1,0,0,0,39,143,1,0,0,0,41,148,1,
		0,0,0,43,153,1,0,0,0,45,158,1,0,0,0,47,162,1,0,0,0,49,166,1,0,0,0,51,170,
		1,0,0,0,53,174,1,0,0,0,55,178,1,0,0,0,57,182,1,0,0,0,59,186,1,0,0,0,61,
		190,1,0,0,0,63,194,1,0,0,0,65,198,1,0,0,0,67,202,1,0,0,0,69,206,1,0,0,
		0,71,210,1,0,0,0,73,214,1,0,0,0,75,218,1,0,0,0,77,222,1,0,0,0,79,226,1,
		0,0,0,81,230,1,0,0,0,83,234,1,0,0,0,85,238,1,0,0,0,87,246,1,0,0,0,89,254,
		1,0,0,0,91,259,1,0,0,0,93,94,5,40,0,0,94,4,1,0,0,0,95,96,5,92,0,0,96,97,
		1,0,0,0,97,98,6,1,0,0,98,6,1,0,0,0,99,100,5,91,0,0,100,8,1,0,0,0,101,102,
		5,93,0,0,102,10,1,0,0,0,103,104,5,123,0,0,104,105,1,0,0,0,105,106,6,4,
		1,0,106,12,1,0,0,0,107,108,5,125,0,0,108,109,1,0,0,0,109,110,6,5,1,0,110,
		14,1,0,0,0,111,112,5,41,0,0,112,16,1,0,0,0,113,114,5,60,0,0,114,18,1,0,
		0,0,115,116,5,62,0,0,116,20,1,0,0,0,117,118,5,44,0,0,118,22,1,0,0,0,119,
		120,5,61,0,0,120,24,1,0,0,0,121,122,5,36,0,0,122,26,1,0,0,0,123,124,5,
		37,0,0,124,125,1,0,0,0,125,126,6,12,2,0,126,28,1,0,0,0,127,128,5,59,0,
		0,128,30,1,0,0,0,129,130,5,58,0,0,130,32,1,0,0,0,131,132,5,27,0,0,132,
		34,1,0,0,0,133,135,7,0,0,0,134,133,1,0,0,0,135,136,1,0,0,0,136,134,1,0,
		0,0,136,137,1,0,0,0,137,36,1,0,0,0,138,140,8,1,0,0,139,138,1,0,0,0,140,
		141,1,0,0,0,141,139,1,0,0,0,141,142,1,0,0,0,142,38,1,0,0,0,143,144,7,2,
		0,0,144,145,5,60,0,0,145,146,1,0,0,0,146,147,6,18,3,0,147,40,1,0,0,0,148,
		149,7,2,0,0,149,150,7,3,0,0,150,151,1,0,0,0,151,152,6,19,3,0,152,42,1,
		0,0,0,153,154,7,4,0,0,154,155,7,5,0,0,155,156,1,0,0,0,156,157,6,20,3,0,
		157,44,1,0,0,0,158,159,7,3,0,0,159,160,1,0,0,0,160,161,6,21,3,0,161,46,
		1,0,0,0,162,163,7,6,0,0,163,164,1,0,0,0,164,165,6,22,3,0,165,48,1,0,0,
		0,166,167,7,7,0,0,167,168,1,0,0,0,168,169,6,23,3,0,169,50,1,0,0,0,170,
		171,7,8,0,0,171,172,1,0,0,0,172,173,6,24,3,0,173,52,1,0,0,0,174,175,5,
		35,0,0,175,176,1,0,0,0,176,177,6,25,3,0,177,54,1,0,0,0,178,179,5,110,0,
		0,179,180,1,0,0,0,180,181,6,26,3,0,181,56,1,0,0,0,182,183,5,78,0,0,183,
		184,1,0,0,0,184,185,6,27,3,0,185,58,1,0,0,0,186,187,5,126,0,0,187,188,
		1,0,0,0,188,189,6,28,3,0,189,60,1,0,0,0,190,191,7,9,0,0,191,192,1,0,0,
		0,192,193,6,29,3,0,193,62,1,0,0,0,194,195,7,10,0,0,195,196,1,0,0,0,196,
		197,6,30,3,0,197,64,1,0,0,0,198,199,7,11,0,0,199,200,1,0,0,0,200,201,6,
		31,3,0,201,66,1,0,0,0,202,203,7,12,0,0,203,204,1,0,0,0,204,205,6,32,3,
		0,205,68,1,0,0,0,206,207,7,13,0,0,207,208,1,0,0,0,208,209,6,33,3,0,209,
		70,1,0,0,0,210,211,5,64,0,0,211,212,1,0,0,0,212,213,6,34,3,0,213,72,1,
		0,0,0,214,215,5,33,0,0,215,216,1,0,0,0,216,217,6,35,3,0,217,74,1,0,0,0,
		218,219,7,14,0,0,219,220,1,0,0,0,220,221,6,36,3,0,221,76,1,0,0,0,222,223,
		7,15,0,0,223,224,1,0,0,0,224,225,6,37,3,0,225,78,1,0,0,0,226,227,7,16,
		0,0,227,228,1,0,0,0,228,229,6,38,3,0,229,80,1,0,0,0,230,231,5,63,0,0,231,
		232,1,0,0,0,232,233,6,39,3,0,233,82,1,0,0,0,234,235,5,43,0,0,235,236,1,
		0,0,0,236,237,6,40,3,0,237,84,1,0,0,0,238,240,7,17,0,0,239,241,7,3,0,0,
		240,239,1,0,0,0,241,242,1,0,0,0,242,240,1,0,0,0,242,243,1,0,0,0,243,244,
		1,0,0,0,244,245,6,41,3,0,245,86,1,0,0,0,246,248,5,36,0,0,247,249,7,3,0,
		0,248,247,1,0,0,0,249,250,1,0,0,0,250,248,1,0,0,0,250,251,1,0,0,0,251,
		252,1,0,0,0,252,253,6,42,3,0,253,88,1,0,0,0,254,255,5,92,0,0,255,256,1,
		0,0,0,256,257,6,43,1,0,257,258,6,43,3,0,258,90,1,0,0,0,259,260,8,18,0,
		0,260,261,1,0,0,0,261,262,6,44,3,0,262,92,1,0,0,0,7,0,1,2,136,141,242,
		250,4,5,2,0,6,0,0,5,1,0,4,0,0
	};

	public static readonly ATN _ATN =
		new ATNDeserializer().Deserialize(_serializedATN);


}
