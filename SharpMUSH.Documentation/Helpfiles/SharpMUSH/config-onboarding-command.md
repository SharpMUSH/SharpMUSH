<!-- help-article
{
  "corpus": "help",
  "id": "config-onboarding-command",
  "lookup": "@config onboarding",
  "aliases": [],
  "sections": []
}
-->
# @config onboarding

What the web portal's guide for new players, at `/welcome`, shows. An empty option is left out of the guide; all but onboarding_art_style are empty by default.

- `onboarding_introduction=<string>`: Plain text introducing the game.
- `onboarding_welcome_page=<string>`: A wiki page to link as the welcome page, written `<namespace>/<slug>`, such as `main/welcome`.
- `onboarding_rules_page=<string>`: A wiki page to link as the rules, written the same way.
- `onboarding_character_guide=<string>`: A wiki page to link as the character guide, written the same way.
- `onboarding_art_style=<string>`: The style of the guide's pictures: `minimalist` (the default), `chibi`, `anime`, `kemono`, `pixel` or `comicbook`.
- `onboarding_art_override=<string>`: One picture of that style to use, by its id, in place of the one picked for the game's genre.

A page is linked only while the wiki is on and a visitor who is not signed in can read it.
