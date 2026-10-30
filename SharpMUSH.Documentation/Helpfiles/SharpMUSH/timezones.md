<!-- help-article
{
  "corpus": "help",
  "id": "timezones",
  "lookup": "timezones",
  "aliases": [
    "@tz"
  ],
  "sections": [],
  "redirects": {
    "TIMEZONES2": "timezones",
    "TIMEZONES3": "timezones",
    "TIMEZONES4": "timezones",
    "TIMEZONES5": "timezones",
    "TIMEZONES6": "timezones",
    "TIMEZONES7": "timezones",
    "TIMEZONES8": "timezones",
    "TIMEZONES9": "timezones",
    "TIMEZONES10": "timezones",
    "TIMEZONES11": "timezones",
    "TIMEZONES12": "timezones",
    "TIMEZONES13": "timezones"
  }
}
-->
# Timezones

The time(), timefmt() and convsecs() functions have an optional time zone argument that's used for formatting the time. Without this time zone specified, the one the game's server is running under is used.

If the time zone argument is a dbref, the contents of that object's @TZ attribute is used as the zone. The attribute is not evaluated. If the object doesn't have this attribute, the server's time zone will be used.

If it's the string 'UTC', that time zone is used instead of the local one. If it's a number between -24 and +24, it adds that many hours from UTC/GMT. Fractional times are supported, e.g., -1.5 hours.

Symbolic time zone names can also be used.

valid(timezone, `<tz>`) tests if `<tz>` can be used as a timezone.

Zone names are those of the IANA time zone database installed on the server, such as `America/New_York` or `Europe/Paris`; the list is at https://www.iana.org/time-zones.
