# Natural date expressions (#2000)

A pure, bounded English parser in `Library.Time` serves `dateparse(expression[, zone[, reference[, output]]])` and scene scheduling. `convtime()` remains unchanged. No new dependency or runtime network is needed. Input is limited to 512 characters and fully consumed; unresolved inputs return typed incomplete, ambiguous, unsupported or invalid diagnostics. A written numeric offset and named zone must agree; invalid named zones are never ignored. A resolved value retains the instant, zone, explicit components and normalized local date/time/offset. Numeric inputs and references are epoch seconds (fractional allowed); numeric output precision follows existing time functions. The `normalized` output mode preserves the resolved local date/time, numeric offset and selected zone for scene confirmations; all parsing uses the same captured reference.

## Support matrix

| Family | Examples | Policy |
| --- | --- | --- |
| Relative dates | today at 19:00; tomorrow 7pm; the day after tomorrow at noon; yesterday midnight | Clock required; calendar dates in chosen zone |
| Weekdays | Tuesday 18:30; Tue 6:30pm; this Friday 8pm; next Tuesday 18:30; next week Tuesday 18:30 | Bare: next occurrence, today only before clock; this: current Monday week; next: strictly later occurrence; next week: following Monday week |
| Offsets | in 90 minutes; 2 hours from now; +1h30m; in a fortnight | Seconds/minutes/hours elapsed; days/weeks calendar; reference clock retained without explicit clock |
| Offset with clock | in 7 days at 18:30; +7d 18:30; two weeks from now at 7pm | Calendar adjustment then explicit local clock; elapsed-only offset plus clock rejected as contradictory |
| Absolute | October 23 at 7pm; 23 Oct 2026 19:00; 2026-10-23 19:00; ISO offset; epoch | Missing year means reference year; no hidden future-year rollover |
| Clocks | 18:30; 6:30pm; noon; midnight | Today; past stays past |
| Zones | tomorrow 18:30 America/Chicago; UTC; Z; ISO offset | Expression zone overrides function zone; function defaults to executor TZ, then UTC; fixed offsets accepted, abbreviations refused |
| Extended | a week from Tuesday at 7pm; first Saturday of next month at noon; last Friday of November at 18:00 | Bare-weekday anchor plus seven days; exact ordinal month; nonexistent fifth weekday and contradictory relative-month/year refused |
| Clarification | next Tuesday; tonight; tomorrow evening; next weekend; 03/04; CST | No guessed clock, daypart, numeric order or abbreviation |
| Unsupported | Tuesday 6-8pm; every other Tuesday at 7pm | Ranges and recurrence need separate scheduling semantics |

General parsing permits past instants. Scene verbs require future instants, report the resolved date/clock/zone, and do not write on refusal. DST gaps and folds require a different wall clock or explicit numeric offset; calendar arithmetic never silently normalizes them. Legacy scene single h/d/w offsets retain their established elapsed-seconds semantics. New multiunit/calendar phrases use the shared parser. Calendar quantities apply before elapsed quantities. Years/months are outside the initial offset grammar (named dates and ordinal months are supported).

Scene schedule scans at most eight slash boundaries for the first completely parsed date prefix, retaining the entire remaining title and pitch verbatim. A prefix containing trailing junk never qualifies; IANA slash names and slash-containing titles/pitches have regression coverage. Reschedule and timed pause parse the whole date argument directly. Each operation captures one reference.

## Sources and choice

Microsoft Recognizers Text is an MIT-licensed .NET entity extraction/resolution library with multilingual date/time specifications, including unsupported corpus entries. Its extraction model would still need whole-input checks and our explicit missing-time, weekday and DST policies. A focused grammar avoids a second resolution policy and provides stable diagnostics for the intentionally bounded English surface. This is a policy/maintenance choice, not a measured performance claim. Sources: [Microsoft Recognizers Text](https://github.com/microsoft/Recognizers-Text) and its [English specs](https://github.com/microsoft/Recognizers-Text/tree/master/Specs/DateTime/English). [Chrono](https://github.com/wanasit/chrono), [dateparser](https://dateparser.readthedocs.io/en/stable/settings.html) and [Todoist's documented date input](https://www.todoist.com/help/todoist/features/schedule-a-date-and-time-for-your-todoist-tasks-q7VobO) inform the phrase families; their runtimes and implicit defaults are not imported. All examples are interpreted by this document's policies.

NuGet inspection: [Microsoft.Recognizers.Text.DateTime 1.8.13](https://www.nuget.org/packages/Microsoft.Recognizers.Text.DateTime) provides .NET Standard 2.0/2.1 and net6.0 assets and depends on five Recognizers packages (definitions, text, TIMEX, numbers and numbers with units). It is a viable .NET candidate, not rejected for framework incompatibility. No dependency was installed because the focused grammar already fits the approved bounded policies. Chrono documents an explicit reference/timezone and `forwardDate`; dateparser documents `RELATIVE_BASE`, date ordering, required parts and strict parsing. These are evidence for making our choices explicit, not reasons to copy their defaults.
