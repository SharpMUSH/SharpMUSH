<!-- help-article
{
  "corpus": "help",
  "id": "warnings-list",
  "lookup": "warnings list",
  "aliases": [],
  "sections": [
    {
      "id": "combined-warning-profiles",
      "heading": "Combined warning profiles",
      "lookup": "warnings list combined warning profiles"
    }
  ],
  "redirects": {
    "WARNINGS LIST2": "warnings list combined warning profiles"
  }
}
-->
# Warnings List

The building warning system supports the following types of warnings:

exit-unlinked         Warn on unlinked exits<br>
exit-oneway           Warn on exits with no return exit<br>
exit-multiple         Warn on multiple exits from A to B<br>
exit-msgs             Warn on missing succ/osucc/odrop/fail<br>
exit-desc             Warn on missing description<br>
room-desc             Warn on missing description<br>
thing-msgs            Warn on missing succ/osucc/odrop/fail<br>
thing-desc            Warn on missing description<br>
my-desc               Warn on missing player description<br>
lock-checks           Warn on `@lock` problems

## Combined warning profiles

These warnings combine the functionality of multiple warnings above:

serious      exit-unlinked, thing-desc, room-desc, my-desc, lock-checks<br>
normal       serious, exit-oneway, exit-multiple, exit-msgs<br>
extra        normal, thing-msgs<br>
all          all of the above

The warning "none" indicates no warnings.<br>
You can exclude warnings from a larger list by using !`<warning>` after the larger list. For example: `@warnings` me=all !exit-oneway
