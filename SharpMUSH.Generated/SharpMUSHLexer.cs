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
		ESCAPE=1, OBRACK=2, CBRACK=3, OBRACE=4, CBRACE=5, CPAREN=6, CCARET=7, 
		COMMAWS=8, EQUALS=9, PERCENT=10, DOLLAR=11, SEMICOLON=12, COLON=13, OANSI=14, 
		RSPACE=15, FUNCHAR=16, OTHER=17, ANY_AT_ALL=18, REG_STARTCARET=19, REG_NUM=20, 
		VWX=21, ARG_NUM=22, SPACE=23, BLANKLINE=24, TAB=25, DBREF=26, ENACTOR_NAME=27, 
		CAP_ENACTOR_NAME=28, ACCENT_NAME=29, MONIKER_NAME=30, SUB_PRONOUN=31, 
		OBJ_PRONOUN=32, POS_PRONOUN=33, ABS_POS_PRONOUN=34, CALLED_DBREF=35, EXECUTOR_DBREF=36, 
		LOCATION_DBREF=37, LASTCOMMAND_BEFORE_EVAL=38, LASTCOMMAND_AFTER_EVAL=39, 
		INVOCATION_DEPTH=40, CURRENT_ARG_COUNT=41, ITEXT_NUM=42, STEXT_NUM=43, 
		OTHER_SUB=44, ANY=45, SPACEREGEX=46, ANYREGEX=47, CANSI=48, ANSICHARACTER=49;
	public const int
		SUBSTITUTION=1, ESCAPING=2, REGEX=3, ANSI=4;
	public static string[] channelNames = {
		"DEFAULT_TOKEN_CHANNEL", "HIDDEN"
	};

	public static string[] modeNames = {
		"DEFAULT_MODE", "SUBSTITUTION", "ESCAPING", "REGEX", "ANSI"
	};

	public static readonly string[] ruleNames = {
		"WS", "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", "CCARET", 
		"COMMAWS", "EQUALS", "PERCENT", "DOLLAR", "SEMICOLON", "COLON", "OANSI", 
		"RSPACE", "FUNCHAR", "OTHER", "ANY_AT_ALL", "REG_STARTCARET", "REG_NUM", 
		"VWX", "ARG_NUM", "SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", 
		"CAP_ENACTOR_NAME", "ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", 
		"POS_PRONOUN", "ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "OTHER_SUB", "ANY", "SPACEREGEX", 
		"ANYREGEX", "CANSI", "ANSICHARACTER"
	};


	public SharpMUSHLexer(ICharStream input)
	: this(input, Console.Out, Console.Error) { }

	public SharpMUSHLexer(ICharStream input, TextWriter output, TextWriter errorOutput)
	: base(input, output, errorOutput)
	{
		Interpreter = new LexerATNSimulator(this, _ATN, decisionToDFA, sharedContextCache);
	}

	private static readonly string[] _LiteralNames = {
		null, "'\\'", "'['", "']'", "'{'", "'}'", "')'", "'>'", null, "'='", "'%'", 
		"'$'", "';'", "':'", "'\\u001B'", null, null, null, null, null, null, 
		null, null, null, null, null, "'#'", "'n'", "'N'", "'~'", null, null, 
		null, null, null, "'@'", "'!'", null, null, null, "'?'", "'+'", null, 
		null, null, null, null, null, "'m'"
	};
	private static readonly string[] _SymbolicNames = {
		null, "ESCAPE", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "CPAREN", "CCARET", 
		"COMMAWS", "EQUALS", "PERCENT", "DOLLAR", "SEMICOLON", "COLON", "OANSI", 
		"RSPACE", "FUNCHAR", "OTHER", "ANY_AT_ALL", "REG_STARTCARET", "REG_NUM", 
		"VWX", "ARG_NUM", "SPACE", "BLANKLINE", "TAB", "DBREF", "ENACTOR_NAME", 
		"CAP_ENACTOR_NAME", "ACCENT_NAME", "MONIKER_NAME", "SUB_PRONOUN", "OBJ_PRONOUN", 
		"POS_PRONOUN", "ABS_POS_PRONOUN", "CALLED_DBREF", "EXECUTOR_DBREF", "LOCATION_DBREF", 
		"LASTCOMMAND_BEFORE_EVAL", "LASTCOMMAND_AFTER_EVAL", "INVOCATION_DEPTH", 
		"CURRENT_ARG_COUNT", "ITEXT_NUM", "STEXT_NUM", "OTHER_SUB", "ANY", "SPACEREGEX", 
		"ANYREGEX", "CANSI", "ANSICHARACTER"
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
		4,0,49,302,6,-1,6,-1,6,-1,6,-1,6,-1,2,0,7,0,2,1,7,1,2,2,7,2,2,3,7,3,2,
		4,7,4,2,5,7,5,2,6,7,6,2,7,7,7,2,8,7,8,2,9,7,9,2,10,7,10,2,11,7,11,2,12,
		7,12,2,13,7,13,2,14,7,14,2,15,7,15,2,16,7,16,2,17,7,17,2,18,7,18,2,19,
		7,19,2,20,7,20,2,21,7,21,2,22,7,22,2,23,7,23,2,24,7,24,2,25,7,25,2,26,
		7,26,2,27,7,27,2,28,7,28,2,29,7,29,2,30,7,30,2,31,7,31,2,32,7,32,2,33,
		7,33,2,34,7,34,2,35,7,35,2,36,7,36,2,37,7,37,2,38,7,38,2,39,7,39,2,40,
		7,40,2,41,7,41,2,42,7,42,2,43,7,43,2,44,7,44,2,45,7,45,2,46,7,46,2,47,
		7,47,2,48,7,48,2,49,7,49,1,0,5,0,107,8,0,10,0,12,0,110,9,0,1,1,1,1,1,1,
		1,1,1,2,1,2,1,3,1,3,1,4,1,4,1,5,1,5,1,6,1,6,1,7,1,7,1,8,1,8,1,8,1,9,1,
		9,1,10,1,10,1,10,1,10,1,11,1,11,1,11,1,11,1,12,1,12,1,13,1,13,1,14,1,14,
		1,14,1,14,1,15,1,15,1,16,4,16,152,8,16,11,16,12,16,153,1,16,1,16,1,16,
		1,17,4,17,160,8,17,11,17,12,17,161,1,18,4,18,165,8,18,11,18,12,18,166,
		1,19,1,19,1,19,1,19,1,19,1,20,1,20,1,20,1,20,1,20,1,21,1,21,1,21,1,21,
		1,21,1,22,1,22,1,22,1,22,1,23,1,23,1,23,1,23,1,24,1,24,1,24,1,24,1,25,
		1,25,1,25,1,25,1,26,1,26,1,26,1,26,1,27,1,27,1,27,1,27,1,28,1,28,1,28,
		1,28,1,29,1,29,1,29,1,29,1,30,1,30,1,30,1,30,1,31,1,31,1,31,1,31,1,32,
		1,32,1,32,1,32,1,33,1,33,1,33,1,33,1,34,1,34,1,34,1,34,1,35,1,35,1,35,
		1,35,1,36,1,36,1,36,1,36,1,37,1,37,1,37,1,37,1,38,1,38,1,38,1,38,1,39,
		1,39,1,39,1,39,1,40,1,40,1,40,1,40,1,41,1,41,1,41,1,41,1,42,1,42,4,42,
		266,8,42,11,42,12,42,267,1,42,1,42,1,43,1,43,4,43,274,8,43,11,43,12,43,
		275,1,43,1,43,1,44,1,44,1,44,1,44,1,45,1,45,1,45,1,45,1,46,1,46,1,46,1,
		46,1,47,1,47,1,48,1,48,1,48,1,48,1,49,4,49,299,8,49,11,49,12,49,300,1,
		166,0,50,5,0,7,1,9,2,11,3,13,4,15,5,17,6,19,7,21,8,23,9,25,10,27,11,29,
		12,31,13,33,14,35,15,37,16,39,17,41,18,43,19,45,20,47,21,49,22,51,23,53,
		24,55,25,57,26,59,27,61,28,63,29,65,30,67,31,69,32,71,33,73,34,75,35,77,
		36,79,37,81,38,83,39,85,40,87,41,89,42,91,43,93,44,95,45,97,46,99,47,101,
		48,103,49,5,0,1,2,3,4,20,3,0,9,10,12,13,32,32,3,0,48,57,65,90,97,122,9,
		0,27,27,32,32,36,37,40,41,44,44,58,62,91,93,123,123,125,125,2,0,81,81,
		113,113,1,0,48,57,2,0,86,88,118,120,2,0,65,90,97,122,2,0,66,66,98,98,2,
		0,82,82,114,114,2,0,84,84,116,116,2,0,75,75,107,107,2,0,83,83,115,115,
		2,0,79,79,111,111,2,0,80,80,112,112,2,0,65,65,97,97,2,0,76,76,108,108,
		2,0,67,67,99,99,2,0,85,85,117,117,2,0,73,73,105,105,1,0,109,109,303,0,
		7,1,0,0,0,0,9,1,0,0,0,0,11,1,0,0,0,0,13,1,0,0,0,0,15,1,0,0,0,0,17,1,0,
		0,0,0,19,1,0,0,0,0,21,1,0,0,0,0,23,1,0,0,0,0,25,1,0,0,0,0,27,1,0,0,0,0,
		29,1,0,0,0,0,31,1,0,0,0,0,33,1,0,0,0,0,35,1,0,0,0,0,37,1,0,0,0,0,39,1,
		0,0,0,0,41,1,0,0,0,1,43,1,0,0,0,1,45,1,0,0,0,1,47,1,0,0,0,1,49,1,0,0,0,
		1,51,1,0,0,0,1,53,1,0,0,0,1,55,1,0,0,0,1,57,1,0,0,0,1,59,1,0,0,0,1,61,
		1,0,0,0,1,63,1,0,0,0,1,65,1,0,0,0,1,67,1,0,0,0,1,69,1,0,0,0,1,71,1,0,0,
		0,1,73,1,0,0,0,1,75,1,0,0,0,1,77,1,0,0,0,1,79,1,0,0,0,1,81,1,0,0,0,1,83,
		1,0,0,0,1,85,1,0,0,0,1,87,1,0,0,0,1,89,1,0,0,0,1,91,1,0,0,0,1,93,1,0,0,
		0,2,95,1,0,0,0,3,97,1,0,0,0,3,99,1,0,0,0,4,101,1,0,0,0,4,103,1,0,0,0,5,
		108,1,0,0,0,7,111,1,0,0,0,9,115,1,0,0,0,11,117,1,0,0,0,13,119,1,0,0,0,
		15,121,1,0,0,0,17,123,1,0,0,0,19,125,1,0,0,0,21,127,1,0,0,0,23,130,1,0,
		0,0,25,132,1,0,0,0,27,136,1,0,0,0,29,140,1,0,0,0,31,142,1,0,0,0,33,144,
		1,0,0,0,35,148,1,0,0,0,37,151,1,0,0,0,39,159,1,0,0,0,41,164,1,0,0,0,43,
		168,1,0,0,0,45,173,1,0,0,0,47,178,1,0,0,0,49,183,1,0,0,0,51,187,1,0,0,
		0,53,191,1,0,0,0,55,195,1,0,0,0,57,199,1,0,0,0,59,203,1,0,0,0,61,207,1,
		0,0,0,63,211,1,0,0,0,65,215,1,0,0,0,67,219,1,0,0,0,69,223,1,0,0,0,71,227,
		1,0,0,0,73,231,1,0,0,0,75,235,1,0,0,0,77,239,1,0,0,0,79,243,1,0,0,0,81,
		247,1,0,0,0,83,251,1,0,0,0,85,255,1,0,0,0,87,259,1,0,0,0,89,263,1,0,0,
		0,91,271,1,0,0,0,93,279,1,0,0,0,95,283,1,0,0,0,97,287,1,0,0,0,99,291,1,
		0,0,0,101,293,1,0,0,0,103,298,1,0,0,0,105,107,7,0,0,0,106,105,1,0,0,0,
		107,110,1,0,0,0,108,106,1,0,0,0,108,109,1,0,0,0,109,6,1,0,0,0,110,108,
		1,0,0,0,111,112,5,92,0,0,112,113,1,0,0,0,113,114,6,1,0,0,114,8,1,0,0,0,
		115,116,5,91,0,0,116,10,1,0,0,0,117,118,5,93,0,0,118,12,1,0,0,0,119,120,
		5,123,0,0,120,14,1,0,0,0,121,122,5,125,0,0,122,16,1,0,0,0,123,124,5,41,
		0,0,124,18,1,0,0,0,125,126,5,62,0,0,126,20,1,0,0,0,127,128,5,44,0,0,128,
		129,3,5,0,0,129,22,1,0,0,0,130,131,5,61,0,0,131,24,1,0,0,0,132,133,5,37,
		0,0,133,134,1,0,0,0,134,135,6,10,1,0,135,26,1,0,0,0,136,137,5,36,0,0,137,
		138,1,0,0,0,138,139,6,11,2,0,139,28,1,0,0,0,140,141,5,59,0,0,141,30,1,
		0,0,0,142,143,5,58,0,0,143,32,1,0,0,0,144,145,5,27,0,0,145,146,1,0,0,0,
		146,147,6,14,3,0,147,34,1,0,0,0,148,149,5,32,0,0,149,36,1,0,0,0,150,152,
		7,1,0,0,151,150,1,0,0,0,152,153,1,0,0,0,153,151,1,0,0,0,153,154,1,0,0,
		0,154,155,1,0,0,0,155,156,5,40,0,0,156,157,3,5,0,0,157,38,1,0,0,0,158,
		160,8,2,0,0,159,158,1,0,0,0,160,161,1,0,0,0,161,159,1,0,0,0,161,162,1,
		0,0,0,162,40,1,0,0,0,163,165,9,0,0,0,164,163,1,0,0,0,165,166,1,0,0,0,166,
		167,1,0,0,0,166,164,1,0,0,0,167,42,1,0,0,0,168,169,7,3,0,0,169,170,5,60,
		0,0,170,171,1,0,0,0,171,172,6,19,4,0,172,44,1,0,0,0,173,174,7,3,0,0,174,
		175,7,4,0,0,175,176,1,0,0,0,176,177,6,20,4,0,177,46,1,0,0,0,178,179,7,
		5,0,0,179,180,7,6,0,0,180,181,1,0,0,0,181,182,6,21,4,0,182,48,1,0,0,0,
		183,184,7,4,0,0,184,185,1,0,0,0,185,186,6,22,4,0,186,50,1,0,0,0,187,188,
		7,7,0,0,188,189,1,0,0,0,189,190,6,23,4,0,190,52,1,0,0,0,191,192,7,8,0,
		0,192,193,1,0,0,0,193,194,6,24,4,0,194,54,1,0,0,0,195,196,7,9,0,0,196,
		197,1,0,0,0,197,198,6,25,4,0,198,56,1,0,0,0,199,200,5,35,0,0,200,201,1,
		0,0,0,201,202,6,26,4,0,202,58,1,0,0,0,203,204,5,110,0,0,204,205,1,0,0,
		0,205,206,6,27,4,0,206,60,1,0,0,0,207,208,5,78,0,0,208,209,1,0,0,0,209,
		210,6,28,4,0,210,62,1,0,0,0,211,212,5,126,0,0,212,213,1,0,0,0,213,214,
		6,29,4,0,214,64,1,0,0,0,215,216,7,10,0,0,216,217,1,0,0,0,217,218,6,30,
		4,0,218,66,1,0,0,0,219,220,7,11,0,0,220,221,1,0,0,0,221,222,6,31,4,0,222,
		68,1,0,0,0,223,224,7,12,0,0,224,225,1,0,0,0,225,226,6,32,4,0,226,70,1,
		0,0,0,227,228,7,13,0,0,228,229,1,0,0,0,229,230,6,33,4,0,230,72,1,0,0,0,
		231,232,7,14,0,0,232,233,1,0,0,0,233,234,6,34,4,0,234,74,1,0,0,0,235,236,
		5,64,0,0,236,237,1,0,0,0,237,238,6,35,4,0,238,76,1,0,0,0,239,240,5,33,
		0,0,240,241,1,0,0,0,241,242,6,36,4,0,242,78,1,0,0,0,243,244,7,15,0,0,244,
		245,1,0,0,0,245,246,6,37,4,0,246,80,1,0,0,0,247,248,7,16,0,0,248,249,1,
		0,0,0,249,250,6,38,4,0,250,82,1,0,0,0,251,252,7,17,0,0,252,253,1,0,0,0,
		253,254,6,39,4,0,254,84,1,0,0,0,255,256,5,63,0,0,256,257,1,0,0,0,257,258,
		6,40,4,0,258,86,1,0,0,0,259,260,5,43,0,0,260,261,1,0,0,0,261,262,6,41,
		4,0,262,88,1,0,0,0,263,265,7,18,0,0,264,266,7,4,0,0,265,264,1,0,0,0,266,
		267,1,0,0,0,267,265,1,0,0,0,267,268,1,0,0,0,268,269,1,0,0,0,269,270,6,
		42,4,0,270,90,1,0,0,0,271,273,5,36,0,0,272,274,7,4,0,0,273,272,1,0,0,0,
		274,275,1,0,0,0,275,273,1,0,0,0,275,276,1,0,0,0,276,277,1,0,0,0,277,278,
		6,43,4,0,278,92,1,0,0,0,279,280,9,0,0,0,280,281,1,0,0,0,281,282,6,44,4,
		0,282,94,1,0,0,0,283,284,9,0,0,0,284,285,1,0,0,0,285,286,6,45,4,0,286,
		96,1,0,0,0,287,288,5,32,0,0,288,289,1,0,0,0,289,290,6,46,4,0,290,98,1,
		0,0,0,291,292,9,0,0,0,292,100,1,0,0,0,293,294,5,109,0,0,294,295,1,0,0,
		0,295,296,6,48,4,0,296,102,1,0,0,0,297,299,8,19,0,0,298,297,1,0,0,0,299,
		300,1,0,0,0,300,298,1,0,0,0,300,301,1,0,0,0,301,104,1,0,0,0,12,0,1,2,3,
		4,108,153,161,166,267,275,300,5,5,2,0,5,1,0,5,3,0,5,4,0,4,0,0
	};

	public static readonly ATN _ATN =
		new ATNDeserializer().Deserialize(_serializedATN);


}
