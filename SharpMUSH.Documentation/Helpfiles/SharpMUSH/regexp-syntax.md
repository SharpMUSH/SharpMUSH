<!-- help-article
{
  "corpus": "help",
  "id": "regexp-syntax",
  "lookup": "regexp syntax",
  "aliases": [],
  "sections": [
    {
      "id": "pattern-characters",
      "heading": "Pattern characters",
      "lookup": "regexp syntax pattern characters"
    },
    {
      "id": "character-classes",
      "heading": "Character classes",
      "lookup": "regexp syntax character classes"
    },
    {
      "id": "boundary-assertions",
      "heading": "Boundary assertions",
      "lookup": "regexp syntax boundary assertions"
    },
    {
      "id": "quantifiers",
      "heading": "Quantifiers",
      "lookup": "regexp syntax quantifiers"
    },
    {
      "id": "back-references",
      "heading": "Back references",
      "lookup": "regexp syntax back references"
    },
    {
      "id": "lookaround-assertions",
      "heading": "Lookaround assertions",
      "lookup": "regexp syntax lookaround assertions"
    },
    {
      "id": "advanced-syntax-resources",
      "heading": "Advanced syntax resources",
      "lookup": "regexp syntax advanced syntax resources"
    }
  ],
  "redirects": {
    "regexp syntax2": "regexp syntax pattern characters",
    "regexp syntax3": "regexp syntax character classes",
    "regexp syntax4": "regexp syntax boundary assertions",
    "regexp syntax5": "regexp syntax quantifiers",
    "regexp syntax6": "regexp syntax back references",
    "regexp syntax7": "regexp syntax lookaround assertions",
    "regexp syntax8": "regexp syntax advanced syntax resources"
  }
}
-->
# regexp syntax

SharpMUSH uses PCRE for its regular expression engine. PCRE is an open source library of functions to support regular expressions whose syntax and semantics are as close as possible to those of the Perl 5 language. The text below is excerpted from its man page. PCRE was written by Philip Hazel <ph10@cam.ac.uk>, and is Copyright (c) 1997-1999 University of Cambridge, England. You can find it at ftp://ftp.csx.cam.ac.uk/pub/software/programming/pcre/

(Note that in SharpMUSH, if the regular expression is in an eval'd context (like an argument to regmatch), you'll have to do a lot of escaping to make things work right. One way to escape an argument like %0 is: regeditall(%0,\\W,\\$0) or similar).

Regular expression matching in SharpMUSH can be used on user-defined command or listen patterns. In this usage, regular expressions are matched case-insensitively unless the attribute has the CASE flag set. Regular expressions can also be matched in MUSHcode using `regmatch()`, `regrab()`, regedit, etc. function families, which usually come in case-sensitive and case-insensitive versions.

## Pattern characters

A regular expression is a pattern that is matched against a subject string from left to right. Most characters stand for themselves in a pattern, and match the corresponding characters in the subject.

There are two different sets of meta-characters: those that are recognized anywhere in the pattern except within square brackets, and those that are recognized in square brackets.  Outside square brackets, the meta-characters are as follows:

       \      general escape character with several uses<br>
       ^      assert start of subject<br>
       $      assert end of subject<br>
       .      match any character except newline<br>
       [      start character class definition
       |      start of alternative branch ("or")
       (      start subpattern<br>
       )      end subpattern<br>
       ?      0 or 1 quantifier (after a unit to quantify) or, minimal match (after a quantifier) or, extends the meaning of ( after a (
       *      0 or more quantifier
       +      1 or more quantifier

## Character classes

Part of a pattern that is in square brackets is called a "character class". It matches any character listed in the class. In a character class, the only metacharacters are:

       \      general escape character<br>
       ^      negate the class, if the first character in the class
       -      indicates character range (e.g. A-Z, 0-4)
`[:NAME:]`   A symbol for a group of characters that can vary according to the language the mush is using. See [regexp classes] for more information.<br>
       ]      terminates the character class

A backslash will escape most metacharacters, and can turn some normal characters into generic character types:

       \d     any decimal digit<br>
       \D     any character that is not a decimal digit<br>
       \s     any whitespace character<br>
       \S     any character that is not a whitespace character<br>
       \w     any "word" character (letter, digit, or underscore)<br>
       \W     any "non-word" character

## Boundary assertions

A backlash can also be used for two useful assertions -- conditions that must be met at a particular point in a match:

       \b     word boundary<br>
       \B     not a word boundary

A word boundary is a position in the subject string where the current character and the previous character do not both match \w or \W (i.e. one matches \w and  the  other  matches \W), or the start<br>
or end of the string if the first or last character matches \w, respectively.

## Quantifiers

Quantifiers specify repetition of characters. Four are available:
       *    match 0 or more of whatever came before
       +    match 1 or more of whatever came before<br>
       ?    match 0 or 1 of whatever came before<br>
	 {m,n}  match between 'm' and 'n' of whatever came before. if 'm' is omitted, it matches between 0 and 'n'. if 'n' is omitted, matches at least 'm'. Note the MUSH parser often requires escaping the braces and the comma.

Quantifiers are usually greedy -- they match as much as possible. Adding a ? after a quantifier causes it to match as little as possible instead.

## Back references

Outside a character class, a backslash followed by a digit greater than 0 (and possibly further digits) is a back reference to a capturing subpattern earlier (i.e. to its left) in the pattern, provided there have been that many previous capturing left parentheses. A back reference matches whatever actually matched the capturing subpattern in the current subject string, rather than anything matching the subpattern itself. So the pattern

    (sens|respons)e and \1ibility

matches "sense and sensibility" and "response and responsibility", but not "sense and responsibility".

You can give names to subpatterns and refer to them that way instead of using numbers.

(?P`<NAME>`subexpr) (Note: Literal <>'s) is a named capture, and (?P=NAME) refers back to it. The above pattern might be written:

(?P`<word>`sens|respons)e and (?P=word)ibility

In a `$-command`, the value of the named pattern can be accessed via the r(`<name>`, args). Softcode functions which work with regexps allow you to access the named subpatterns via $`<NAME>` (the <> are literal here).

## Lookaround assertions

An assertion is a test on the characters following or preceding the current matching point that does not actually consume any characters. There are two kinds: those that look ahead of the current position in the subject string, and those that look behind it.

An assertion subpattern is matched in the normal way, except that it does not cause the current matching position to be changed. Lookahead assertions start with (?= for positive assertions and (?! for negative assertions. For example, Lookbehind assertions start with (?<= for positive assertions and (?<! for negative assertions.

Assertion subpatterns are not capturing subpatterns, and may not be repeated, because it makes no sense to assert the same thing several times. If an assertion contains capturing subpatterns within it, these are always counted for the purposes of numbering the capturing subpatterns in the whole pattern.

## Advanced syntax resources

PCRE's engine can also do conditional subpattern matching, embedded comments in regexps, and a bunch of other things. See a regexp book for details.
