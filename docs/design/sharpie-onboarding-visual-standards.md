# Make Sharpie Guidance Clear and Optional

**Use WCAG 2.2 AA as the accessibility target, and established design-system patterns to shape the experience.** There is no formal web standard that prescribes an onboarding tour, mascot, or number of lessons. The strongest direction for SharpMUSH is contextual, resumable help beside the task, with a direct route to independent play. Sharpie should reinforce the lesson through a relevant action while the text and controls remain sufficient on their own. The immediate priorities are clearer icon meanings, real keyboard-accessible tooltips, compact progress, and consistent command/audience formatting. This report synthesizes research; it does not establish accessibility conformance or independently audit the current implementation. ([WCAG 2.2](https://www.w3.org/TR/WCAG22/), [NN/G contextual help](https://www.nngroup.com/articles/onboarding-tutorials/))

## Standards define access; conventions shape the journey

WCAG requirements cover keyboard operation, meaningful focus order, visible focus, accessible names and states, and relationships conveyed programmatically. **Validate the complete welcome → login → character → Play process**, not only the guide. Reflow at 320 CSS pixels and text enlargement to 200% are separate checks. The AA target-size minimum is 24×24 CSS pixels with specified exceptions; 44-pixel hit areas are a sensible SharpMUSH usability choice above that baseline. Ordinary text needs 4.5:1 contrast; necessary component/state graphics generally need 3:1. Color cannot carry meaning alone. ([WCAG conformance](https://www.w3.org/TR/WCAG22/#conformance), [reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [target size](https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum.html), [contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), [non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html))

ARIA Authoring Practices and design systems provide implementation guidance rather than additional WCAG mandates. A truly modal guide should move focus inside, contain keyboard navigation, close with Escape, and restore focus appropriately. A companion panel that permits simultaneous play needs nonmodal behavior. Its visual resemblance to a drawer does not justify `aria-modal`. Returning focus to the command composer after insertion fits the next-workflow exception. ([APG modal dialog](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/))

## Make every toolbar action predictable

Keep the requested icon-only toolbar, but replace reliance on native `title` with a **tested tooltip visible on keyboard focus and hover**, paired with an accessible name. Carbon supports this pattern; NN/G cautions that many icons remain ambiguous, especially on touch. Tooltips should name an action, never conceal essential instructions. ([Carbon tooltip accessibility](https://www.carbondesignsystem.com/building-blocks/core/components/tooltip/accessibility), [tooltip guidelines](https://www.carbondesignsystem.com/building-blocks/core/components/tooltip/guidelines), [icon usability](https://www.nngroup.com/articles/icon-usability/))

Use “Back to game,” “Full guide,” and “Stop guide.” A back arrow, book/list, and stop square are proposed mappings to test with newcomers. An X commonly suggests temporary closure, making it a poor candidate for persistent stopping. Escape should dismiss without disabling future guidance; stopping should offer a clear recovery route. These are product recommendations, not standardized icon meanings.

## Establish one reusable visual vocabulary

| Element | Proposed SharpMUSH treatment | Purpose |
|---|---|---|
| Navigation | Stable icon order, focus/hover labels, comfortable hit areas; one primary next action | Separate return, navigation, and stopping |
| Player progress | “3 of 8” with a short current title; explicit current/completed states; fuller step list on demand | Preserve space on mobile without losing position |
| Admin progress | Resumable task checklist with actual configuration evidence | Support tasks completed in different sessions or orders |
| Commands | Selectable monospace command, one short explanation, consistent padding; distinct Insert and Send actions | Make executable text unmistakable |
| Audience | Explicit “This room” or “Channel members” text, optionally paired with a stable icon | Explain message reach without relying on color or implying unsupported privacy |
| Sharpie | One contextual interaction, consistent image area, smaller footprint on narrow screens | Reinforce the current lesson without competing with it |
| Empty states | Brief explanation plus the next available action; illustration optional | Help users recover without inventing unavailable features |

Linear step indicators should supplement navigation; flexible task lists suit more complex, revisitable work. Apply those patterns selectively: optional player learning should not become a mandatory transaction. ([USWDS step indicator](https://designsystem.digital.gov/components/step-indicator/), [GOV.UK task list](https://design-system.service.gov.uk/components/task-list/))

## Give Sharpie a teaching role, not extra chrome

Use the hierarchy **lesson heading → one-sentence purpose → command/action → next step**. Keep essential channel commands directly visible; disclose advanced details separately. A whole-lesson accordion hides the task instead of simplifying it. ([NN/G progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/))

Sharpie could route speech bubbles toward a group for channels, inspect a doorway for navigation, or point at the command composer for practice. Genre should change costume and props while preserving that teaching action. Favor single-concept illustrations over busy scenes, and reduce imagery when space is scarce. If nearby text already communicates everything, use empty alternative text for decorative art. Keep lesson wording outside images and validate artwork on both light and dark surfaces. ([Atlassian illustrations](https://atlassian.design/foundations/illustrations/), [Carbon empty states](https://www.carbondesignsystem.com/building-blocks/core/patterns/empty-states), [WAI decorative images](https://www.w3.org/WAI/tutorials/images/decorative/))

The next evaluation should ask newcomers to predict the toolbar actions, select a character, and find help on a channel without prompting. Success on those tasks would provide evidence for this particular design; the presence of a mascot or a familiar component alone cannot establish usability or improved retention.
