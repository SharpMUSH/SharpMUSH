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

What the web portal's guide for new players, at `/welcome`, shows. An empty option is left out of the guide; all but onboarding_art_style are empty by default. The identity step of Prepare for players in the portal sets them too.

- `onboarding_introduction=<string>`: Plain text introducing the game.
- `onboarding_welcome_page=<string>`: A wiki page to link as the welcome page, written `<namespace>/<slug>`, such as `main/welcome`.
- `onboarding_rules_page=<string>`: A wiki page to link as the rules, written the same way.
- `onboarding_character_guide=<string>`: A wiki page to link as the character guide, written the same way.
- `onboarding_art_style=<string>`: The style of the guide's pictures: `minimalist` (the default), `chibi`, `anime`, `kemono`, `pixel` or `comicbook`.
- `onboarding_art_override=<string>`: One picture of that style to use, by its id, in place of the one picked for the game's genre.
- `onboarding_guide_name=<string>`: The guide's name, at most 40 characters. The guide introduces itself by it, and the portal's guide labels use it. Empty is Sharpie.
- `onboarding_guide_art=<string>`: The guide's picture: a media upload (`/api/wiki-assets/...`) or an http or https address, shown square everywhere the guide appears. Empty shows Sharpie in onboarding_art_style.

A page is linked only while the wiki is on and a visitor who is not signed in can read it.
