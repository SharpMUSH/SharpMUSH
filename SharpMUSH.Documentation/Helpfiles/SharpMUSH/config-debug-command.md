<!-- help-article
{
  "corpus": "help",
  "id": "config-debug-command",
  "lookup": "@config debug",
  "aliases": [],
  "sections": []
}
-->
# @config debug

Options for diagnosing the softcode parser. Leave them at their defaults on a running game.

- `debug_sharpparser=<boolean>`: Write the parser's trace of every expression it reads to the server's console output, and stop reusing parsed expressions? Off by default. It makes every command slower.
- `parser_prediction_mode=<string>`: How the parser decides between ways to read an expression: `TwoStage` (the default), `SLL` or `LL`. TwoStage reads quickly with SLL and reads again with LL only when SLL reports a syntax error, so it gives LL's result at SLL's speed. `SLL` and `LL` force one mode, to check a parse.
