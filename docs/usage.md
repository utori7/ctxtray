# Using ctxtray

[Back to the README](../README.md) · [日本語](usage.ja.md)

- [The panel](#the-panel)
- [Terminal and VS Code sessions](#terminal-and-vs-code-sessions)
- [The tray icon](#the-tray-icon)
- [Reading the rate limits](#reading-the-rate-limits)
- [Reset times](#reset-times)
- [Thresholds and notifications](#thresholds-and-notifications)
- [Where auto-compaction happens](#where-auto-compaction-happens)
- [Model and effort](#model-and-effort)
- [Model context windows](#model-context-windows)
- [Contrast themes and screen readers](#contrast-themes-and-screen-readers)

## The panel

| Action | Result |
|---|---|
| `Ctrl+Alt+C` | show / hide the panel (configurable, or none) |
| a shortcut you choose | pass clicks through the panel, or stop (none by default; set one in Settings) |
| drag | move the panel |
| hover over a row | details: the full name, the model and effort, token counts, how much is left before auto-compaction, and when the value was recorded |
| click the tray icon | show / hide the panel |
| right-click the tray icon or the panel | show / hide panel, pass clicks through, start at sign-in, settings, refresh now, open config file, about, exit |

To change a shortcut, open Settings (**Panel controls** tab), click its box and press a key together with Ctrl, Alt, or Shift;
Delete sets it to none. If the box does not change when you press a key, another app has already taken that key.

**Pass clicks through** makes the panel ignore the mouse entirely: you can click what is
behind it, but you can no longer drag it, hover a row for details, or right-click it.
Turn it back off from the tray menu, or with the click-through shortcut if you set one.
The panel says how to undo it for a few seconds whenever you turn it on.

The dot to the left of a session name means the same as the dot in Claude Desktop's sidebar,
in the same colours.

| Dot | State |
|---|---|
| amber | waiting for your approval (to run a tool) or your answer (to a question from Claude), or an unread reply that needs action; the row details say which |
| blue | an unread reply: it finished while you were on another tab; opening the tab clears it |
| grey | working |
| ring | finished, or the process is not running |

Rows without a dot come from older versions of Claude Code that do not record their state.
"Needs action" is an unread reply that Claude Desktop has classified as needing you (for example,
one that ends with a question). Like blue, it clears when you open the tab.
Terminal and VS Code sessions have neither (they turn into a ring when they finish).
Unread is worked out from the time Claude Desktop records for opening a tab, so it takes a few
seconds to clear after you open the tab.

Turn on **Highlight sessions waiting for you** on the Settings **Panel content** tab to give amber rows a
coloured background and the word "Approval", "Answer" or "Action" at the right end of the session name column.

A session name in grey means its Claude Code process is not running right now
(for example, a tab that has been idle). Grey sessions are left out of the tray icon and
the notifications: their usage cannot grow until they run again. You can hide them
altogether in Settings. Sessions you have not used for a while (24 hours by default) are not
listed in the first place.

Click **show N more** at the right of the heading to see the rows that are currently
filtered out, and click it again to fold them away. Nothing is saved, and the tray icon
and notifications are unaffected.

In the panel, warn and danger are shown by diagonal stripes on the bar as well as by
colour — wider for warn, tighter for danger — so the level does not depend on telling red
from green. It can be turned off in Settings. The tray icon is too small for stripes
to read, so it uses colour alone.

The panel stays on screen: when it sits in the lower half of the screen it grows upward
as sessions come and go, and it is never pushed past the edge. While a video, a
presentation, or a game is full-screen, the panel hides itself and comes back afterwards
(it stays visible when Claude Desktop itself is in front). Both can be turned off in Settings.

The panel and the row details are kept out of screen sharing (Teams, Zoom and so on) and
screenshots; you still see them on your own screen. This keeps session names out of a shared
screen in a meeting. If you want the panel in a screenshot, turn this off in Settings. It relies
on a Windows feature, so it cannot promise that every way of capturing the screen leaves it out.
Before Windows 10 version 2004, the panel shows up as an empty box instead.

## Terminal and VS Code sessions

Claude Code sessions started from a terminal or VS Code are also listed, marked `>_`,
while their Claude Code process is running. The row disappears when that process exits
(a Claude Desktop tab stays, greyed out), and a conversation that has not been sent
anything yet is not listed.

The name is the one Claude Code gives the process, so it can change when you restart or resume
the session. A conversation open in both Claude Desktop and VS Code is listed once, as the
Claude Desktop tab.

## The tray icon

The tray icon shows amounts as bars — hover over it for the exact values.
Each value has its own colour (context blue, 5-hour green, weekly violet), and a value
that crosses a threshold turns amber, then red. The panel uses the same colours, and
both list the values in the same order: context, 5-hour, weekly.
Two arrangements, switchable in Settings:

- **One combined icon** (default) — one horizontal bar per value, top to bottom:
  context, 5-hour, weekly.
- **One icon per value** — a separate icon for each value, with a label above its bar:
  letters (`C`, `5h`, `W`) or symbols (speech bubble, clock, calendar).
  Instead of a label, an icon can also show the current percentage (the same number
  as the panel); you then tell the icons apart by their order and colour.
  The icons are added so that they line up left to right as context, 5-hour, weekly.
  If they land in the hidden overflow, drag them onto the taskbar once.

You choose which values to show, and the choice applies to both arrangements.

Where an icon shows context, it uses the **running** session with the **highest percentage**
(a share of that model's context window, the same number as the panel).
Hidden and grey sessions are not considered; if no session is running, the context bar is empty.

## Reading the rate limits

Claude Desktop samples your usage every 5–15 minutes and only while you are using it.
While you are working, the number shown can therefore trail your real usage: measured
growth reaches 2.8 points/min, so a 7-minute-old sample can understate the 5-hour limit by
~20 points. `ctxtray --json` reports this as `"freshness": "behind"`.

**Greyed out means "for reference only"** — Claude Desktop is not running, or nothing has
been sampled for over 24 hours. Only Claude Desktop records rate limits, so while you use
Claude Code in a terminal or VS Code with Claude Desktop closed, the rate limits stay at
Claude Desktop's last sample and are greyed out; those sessions' context keeps updating.

**Greyed out with "recorded 40 min ago"** means you have been using Claude Code for 30 minutes
but Claude Desktop has not recorded a new value. Claude Desktop normally records every
15 minutes while in use. Restarting Claude Desktop may fix it.

## Reset times

The 5-hour reset is derived from the sample history. Because samples are 5–15 minutes
apart, the estimate can be early by up to one sampling interval; when it was checked
against an actual reset (once), it was one minute off.
By default the time is shown only while the 5-hour limit is over its warn threshold (set on the
**Thresholds and notifications** tab); it can also be always shown or never shown.

The weekly reset is **not recorded anywhere**. ctxtray can narrow it down to a weekday
from the history (`ctxtray --verify-weekly` shows the derivation), but that estimate is
not precise enough to trust, so it is not shown in the panel or the tooltip.

## Thresholds and notifications

Thresholds are defined **once** (**Thresholds and notifications** tab in Settings) and drive the
panel colours, the tray icon colour, and the notifications together. There is no separate set of
numbers for each. The tab shows the actual warn and danger colours next to the numbers.

For each value you choose whether warn, danger, or both send a notification.
With context set to danger only, for example, the colour still changes at 75% but you are
notified only above 90%. Context notifications come per session, so this helps when you work
in several sessions at once. When a value crosses both levels at once (ctxtray starting while a
value is already in danger, for example), you get one notification, for the higher of the
levels you chose.

**Notify again** decides whether a value that drops and rises again is announced again.
With a context threshold of 75% and **Notify again** at 10%, a session that falls below 65% after
auto-compaction and then passes 75% again is announced again.

## Where auto-compaction happens

ctxtray shows "tokens until auto-compaction" in the row details and the notifications. It works
out where that happens without reading Claude Code's settings.

- By default (**Claude Code default**), as the
  [Claude Code docs](https://code.claude.com/docs/en/model-config#default-auto-compact-thresholds)
  describe, models with a 1M context window are taken to compact at about 967K tokens and other
  models (such as 200K ones) at their limit.
- If you changed it with `/autocompact`, choose **Custom** under **Auto-compaction** in
  **Settings > General** and enter the same value (`500k`, `1M`, `500000`, … — the same forms as
  `/autocompact`). As in Claude Code, the value is capped at each model's context window.

Changing this does not change Claude Code. The value applies to every session, so a project's own
setting, `--autocompact` at launch, or the `CLAUDE_CODE_AUTO_COMPACT_WINDOW` environment variable
is not picked up. It is used only for the "tokens until auto-compaction" figure and for the
warning in Settings; it does not affect the panel's percentages or colours.

A context threshold at or above the auto-compaction point is never reached, because the
conversation is compacted first. Settings warns about this in red.

## Model and effort

Each session's model and effort always appear in the details when you hover over a row
(for example `Opus 5.5 · high`). Turn on **Model and effort** under **On each row** on the
**Panel content** tab in Settings to show them on the row as well (off by default).
To show only one of the two, set `showModel` and `showEffort` separately in the config file.

The panel's width is the session-name column (**Session name width** under **Look**) plus whatever
you turn on — the bar, token counts, the model column. Turning any of them on never squeezes
the names; the panel gets wider instead.
To keep the panel narrow, set **Layout** under **On each row** to **Two lines**. The model and effort then appear
in small type under the session name and the token count under the percentage, so the rows get taller
instead of the panel getting wider.

- The values are what Claude Code recorded for the session's last response. If you switch
  mid-session, the new value appears from the next response
- Model names are shown only when checked against the official docs; any other model is shown
  by its `claude-…` ID (ctxtray does not build a name from the ID)
- Effort is shown exactly as recorded (`medium`, `high`, `xhigh`, …)

## Model context windows

No file records the denominator of the context %, so it comes from the model name:
your settings first, then the built-in table, then values fetched from the official docs.
A model found in none of them **shows `?%` instead of a percentage**; hover over the row
to see why and what you can do. A wrong denominator is worse than an honest blank, so ctxtray
does not guess from a similarly named model either.

The built-in table holds 14 models, each checked against its page in the official docs
(listed in [how-it-works.md](how-it-works.md#denominators)).
When you switch to a newer model, either of these brings the percentage back:

- Pick its context window in **Settings > Models** (200K or 1M). Other values go in `modelLimits`
  in the config file.
- Turn on **Look up unknown models in the official docs** (off by default). Only when
  ctxtray meets a model it does not know, it reads that model's public docs page on
  `platform.claude.com` (for example `…/docs/en/models/opus-5-5/overview.md`) and uses
  the context window only if the page's model ID matches. A model that is not listed is checked again
  after 24 hours. Only the page URL is requested; no conversation, account, or sign-in data is used.

The first time you use a model with an unknown context window, ctxtray tells you once with a
notification (not if context notifications are off for both warn and danger). The **Models** tab
lists every known context window and where it came from (built-in, official docs, or set by you).

## Contrast themes and screen readers

Under a Windows contrast theme, ctxtray uses the theme's own colours and stays opaque.
In the panel, values are then told apart by the row labels and by the stripes on warn
and danger rather than by colour; the tray icon has neither, so hover it for the values.

The panel cannot be read by a screen reader. It is drawn as a single surface on a
window that never takes focus, so there is nothing for Narrator to walk. The same values
are in the tray icon's tooltip and in `ctxtray --status` in a terminal.
