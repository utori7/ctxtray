# How ctxtray works

This document exists so you can verify the claims in the README without reading the
source, and so that whoever has to fix this after a Claude Desktop update knows where
the bodies are buried.

Everything here was established by observing the files on disk, not by reading
Claude Desktop's code.

## 1. Finding the data folder

Claude Desktop is distributed as an MSIX package, so its writes to `%APPDATA%\Claude`
are redirected to a package-private location:

| | Path | Visible to |
|---|---|---|
| **Real** | `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\` | any process |
| Virtual view | `%APPDATA%\Claude\` | only processes inside the package container |

**This is the single easiest thing to get wrong.** Claude Code runs *inside* the
container, so a PowerShell session started from Claude Code can see `%APPDATA%\Claude`
and any prototype written there appears to work. A tray application runs outside the
container and sees nothing.

ctxtray resolves the real path first (wildcard on the package name, never hardcoded)
and falls back to `%APPDATA%\Claude` in case Claude Desktop is ever shipped
non-packaged. `ctxtray --json` reports what it resolved under `paths`, so a failure is
diagnosable instead of silently producing empty data.

`~/.claude` is written by Claude Code, not by the packaged app, and is not redirected.
`CLAUDE_CONFIG_DIR` is honoured if set.

## 2. Rate limits

`<data folder>/plan-usage-history.json`

```json
{"version":2,"samples":[{"t":1786841646425,"org":"…","u":{"fh":47,"sd":13}}]}
```

- `t` — sample time, epoch milliseconds
- `u.fh` — five-hour window, percent
- `u.sd` — seven-day window, percent
- `org` — the organization the sample belongs to

Samples are recorded per organization. ctxtray keeps only the samples of the
organization in the newest sample, so switching accounts does not mix two sets of
numbers. A sample without `t`, `fh` or `sd` is skipped rather than read as 0%.
Other fields (a newer `u.xu` has appeared) are ignored.

Sampling interval was 5 minutes most often, sometimes 15, in July and August 2026; since
mid-September it has been 15 minutes almost every time. **Sampling stops when you are
not using the app**, even while Claude Desktop is running.

### Freshness

Over 80 hours of logging, the age of the newest sample was:

| | median | max | over 20 min |
|---|---:|---:|---:|
| all the time | 13.8 min | 26 h | 44.0 % |
| when a response had arrived within the last 15 min | 7.7 min | **19.5 min** | **0.0 %** |

So staleness is an artifact of *idle time*, not a defect of the source: while you are
working the number is never more than about 20 minutes old.

That is not the same as accurate, though. The five-hour figure was measured climbing at
up to **2.8 points per minute** (median 0.53), so a 7-minute-old sample can understate it
by roughly 20 points.

ctxtray therefore judges freshness by **whether anything ran since the sample**, not by
elapsed time:

| Condition | Display |
|---|---|
| sample time ≥ newest transcript write | `current` — shown as is |
| sample time < newest transcript write | `behind` — the value is a lower bound; shown as is |
| Desktop not running, or sample older than 24 h | `reference` — greyed out |
| `behind` for 30 minutes with no new sample | `stalled` — greyed out, heading says "recorded 40 min ago" |

"Desktop running" means a process named `Claude` whose executable is not Claude Code.
Claude Code's own binary is also called `claude.exe` (Claude Desktop's bundled copy under
`…\Claude\claude-code\<version>\`, or a standalone install), so ctxtray excludes
executables in a `claude-code` folder and executables whose product name is
"Claude Code". Counting them would keep stale numbers looking current while only a
terminal session is running. This was checked by quitting Claude Desktop while a terminal
session kept running: `desktop_running` became `false`, the rate limits turned grey, and
the terminal session's context kept updating. The Claude Code binary bundled with the
VS Code extension also reports the product name "Claude Code", so a VS Code session is
excluded the same way.

The judgement is exposed as `freshness` in `ctxtray --json`. The panel greys out
`reference` and `stalled` values.

`stalled` covers Claude Desktop that stops sampling while you keep working. On 2026-10-02
Desktop wrote one sample right after it started and nothing for the next 40 minutes of use;
its own usage indicator showed 26% while the file still said 0%. Restarting Desktop brought
the samples back. Samples normally arrive every 15 minutes in use, so ctxtray waits for
30 minutes of `behind` on the same sample. It counts from the moment it first sees
`behind`, not from the sample time, because after a long break the sample is old but Desktop
records a new one soon after you start again. The count lives in the running tray app, so
a one-off `ctxtray --json` never reports `stalled`.

### Five-hour reset

The window starts when you first use it, so the sample where `fh` goes from `0` to a
positive value marks the start; reset is start + 5 hours. ctxtray takes the last
zero-valued sample, so the estimate errs early — by up to one sampling interval
(5–15 minutes).

Do **not** implement this as "find when `fh` dropped to 0" — sampling stops while Desktop
is closed, so that search walks across the gap and returns a timestamp from the previous
day. That bug was hit during development.

The estimate was checked once during development: Claude Desktop's local HTTP cache
happened to hold a server response with the actual five-hour reset time, and the
estimate was within one minute of it. That cache is *not* read at runtime — Chromium
evicts it at will, it only ever held the five-hour window, and it is a server response
rather than application state.

### Weekly reset — learned, not read

No local file records it. What is observable is that `sd` decreased between two samples,
which brackets a reset to that interval. ctxtray extracts each such interval, folds
them over a 7-day period, and intersects them (as 5-minute buckets on a ring, which is
less error-prone than circular interval arithmetic).

A decrease is not always a reset. On 2026-09-16 `sd` went from 24% to 17% in the middle
of the week. An interval that does not overlap the range narrowed so far is ignored, but
if the first observation is of this kind, the whole estimate is wrong.

The intervals are recomputed each time from the history Claude Desktop currently keeps in
`plan-usage-history.json`. **ctxtray does not persist them**, and Claude Desktop trims or
resets that file (386 samples dropped to 102 after one update; 76 remained on
2026-09-16), which discards the observations.

An interval spanning 7 days or more constrains nothing and is discarded.

Until the intersection is within an hour, only a weekday can be given. On the development
machine in September 2026, the intersection stayed about 15 hours wide — enough for
"around Friday", not enough for a time.

**The estimate is not shown in the panel or the tooltip.** A weekday guess sitting next to
verified numbers is easy to mistake for one of them. Run `ctxtray --verify-weekly` to see
the derivation on your own data.

## 3. Sessions

Two independent sources, which mean different things:

**`<data folder>/claude-code-sessions/<account>/<org>/local_*.json`** — one file per open
Claude Desktop tab. Provides `cliSessionId` (which matches the transcript filename),
`cwd`, `model`, `title`, `lastActivityAt`. Closing a tab deletes the entry rather than
archiving it, so this list is always "tabs open right now". Terminal and VS Code sessions
never appear here.

**`~/.claude/sessions/<pid>.json`** — one file per running Claude Code process, with
`pid`, `sessionId`, `cwd`, `entrypoint`, `procStart`. Files of exited processes remain,
so ctxtray checks that the PID is alive and compares `procStart` against the process
start time to rule out PID reuse. The same folder also holds other files
(`<pid>.<hash>.key` in Claude Code 2.1.271); ctxtray opens only `*.json`.

The two are complementary — on the development machine, 6 open tabs versus 3 running
processes. Sessions whose `entrypoint` is not `claude-desktop` are shown with a `>_` mark.

A terminal session was checked with Claude Code 2.1.274 in Windows Terminal:

- The process file has `"entrypoint": "cli"`, `"kind": "interactive"` and the same
  `pid` / `sessionId` / `cwd` / `procStart` fields as Claude Desktop's.
- Its `name` is generated by Claude Code (`"nameSource": "derived"`: the folder name plus
  a short suffix) and changes each time the process starts. ctxtray shows it as is and
  falls back to the folder name when it is missing.
- Claude Code deletes the process file on a normal exit, so the row disappears at the
  next refresh. Unlike a Claude Desktop tab there is nothing left to show greyed out.
- `claude --continue` keeps the `sessionId` and appends to the same transcript, so the
  resumed row continues from the previous token count.
- The token count matched Claude Code's own `/context` total (51,270 vs "51.3k"), and its
  33k autocompact buffer on a 1M window agrees with compacting at about 967K tokens.

A VS Code session was checked with the Claude Code extension 2.1.274:

- The process file has `"entrypoint": "claude-vscode"` and otherwise the same fields.
  Closing VS Code ended its Claude Code processes and removed their files; the row
  disappeared.
- When the extension started, it launched three Claude Code processes: one resuming the
  folder's most recent conversation (which was open in Claude Desktop at the time), one
  for an empty conversation, and one for the conversation actually used. The empty one has
  no transcript and is not listed.
- The resumed one shares its `sessionId` with the Claude Desktop tab, so it is not listed
  separately; the tab counts as running while either process is alive. When both are
  alive, `--json` shows the Claude Desktop process as the tab's `pid` and `entrypoint`.

Short-lived "ghost" sessions exist (they start and end within a second, with no
transcript). Requiring a transcript file to exist filters them out.

A tab without a live process is still shown in the panel, greyed out, because it can be
resumed with the same context. It is not used for the tray icon, the tooltip or the
notifications: its usage cannot grow while it is stopped, and counting it would announce a
high value again every time ctxtray started. Once the tab runs again, it counts again.

## 4. Context

`~/.claude/projects/<cwd slug>/<cliSessionId>.jsonl`, read from the end.

The slug is the working directory with `\`, `/`, `:` and `_` replaced by `-`, but that
rule is internal, so ctxtray falls back to searching for `<sessionId>.jsonl` if the
guess misses. A miss is remembered for 30 seconds, so sessions without a transcript do
not trigger a full search on every refresh.

From the last `assistant` line that carries a `usage` field:

```
tokens = input_tokens + cache_creation_input_tokens + cache_read_input_tokens
```

**`output_tokens` is deliberately excluded.** When Claude Desktop's own indicator read
169k, this formula gave 169,046; including `output_tokens` gave 170,534. What the
indicator reports is the prompt length actually sent on the most recent request.

Lines whose model is `<synthetic>` (interrupted or errored turns, all-zero usage) are
skipped — taking one would display 0%. Subagent turns are written to separate files
(`<sessionId>/subagents/…`) and do not affect the main session's number.

The same line also gives the model (`message.model`) and the effort (a top-level `effort`
field, for example `"high"`). Both are taken from the transcript first and from the tab
record (`local_*.json`) only when the line has none, so a switch mid-session shows up from
the next response, and terminal and VS Code sessions — which have no tab record — get an
effort too. Checked on 2026-09-26: across ten Desktop tabs the tab record and the last
response agreed (including one tab switched from `medium` to `high`), and terminal and
VS Code transcripts carry `effort` on every real response; only `<synthetic>` lines lack it.

### Model names

Shown in the row details, and on the row if **Model** is turned on. The name is the page
`title` from the official docs with the leading "Claude " removed (`Claude Opus 5.5` →
`Opus 5.5`). The 14 built-in models carry their titles in the same table as their windows
(checked on 2026-09-26); for a model fetched from the docs, the title from that page's
front matter is kept alongside the window. Matching follows the same rules as the
denominators. **A name is never built from the ID** — any other model is shown as its
`claude-…` ID.

### Denominators

Not present in any file, so they come from the model name. ctxtray looks in this order:

1. `modelLimits` in the config file (set by you, also from **Settings > Models**)
2. the built-in table below
3. values fetched from the official docs (only if `fetchModelLimits` is on)

A model found in none of them shows no percentage (`?%`).

The built-in table. Every row was checked on 2026-09-25 against the model's page in the
official docs (`https://platform.claude.com/docs/en/models/<name>/overview.md`, the
`Model ID` and `Context window` lines); two were also measured against Claude's own readout.

| Model | Window | Also measured |
|---|---:|---|
| `claude-fable-5-1`, `claude-mythos-5-1` | 1,000,000 | |
| `claude-fable-5`, `claude-mythos-5` | 1,000,000 | |
| `claude-opus-5-5` | 1,000,000 | |
| `claude-opus-5` | 1,000,000 | Desktop's indicator: 169k / 1M against a computed 169,046 |
| `claude-opus-4-8`, `-4-7`, `-4-6` | 1,000,000 | |
| `claude-opus-4-5` | 200,000 | |
| `claude-sonnet-5` | 1,000,000 | the terminal's `/context`: 51.3k/1m |
| `claude-sonnet-4-6` | 1,000,000 | |
| `claude-sonnet-4-5`, `claude-haiku-4-5` | 200,000 | |

Names match exactly, or when they differ only by a trailing 8-digit date
(`claude-haiku-4-5-20251001` is `claude-haiku-4-5`). There is no prefix matching: a new
point release such as `claude-opus-5-9` is **not** assumed to share `claude-opus-5`'s
window.

**Fetching from the docs** (opt-in). When a model is unknown, ctxtray drops `claude-` and
any date to form the page name (`claude-opus-5-5` → `opus-5-5`), refuses anything but
lowercase letters, digits, and hyphens, and requests
`https://platform.claude.com/docs/en/models/opus-5-5/overview.md` — HTTPS only, 10-second
timeout, no redirects to other hosts, at most 256 KB, no cookies or credentials. The value
is used only if the page's `Model ID` (ignoring any date) equals the model name and the
`Context window` reads between 1K and 10M tokens; the page `title` in the front matter is
kept as the model name (up to 40 characters). Results are kept in
`%LOCALAPPDATA%\ctxtray\model-limits.json`: a fetched window is never fetched again; a
model that could not be found is tried again after 24 hours (or at once with **Check now**).
Records written by 0.2.0 have no name, so for those models the page is read once more to
fill it in — the window already recorded is kept; a failed read waits 24 hours.
The panel and `--json` (`limit_source`) say where each denominator came from.
`--status` and `--json` never connect; they only read that file.

### Auto-compaction point

ctxtray follows the Claude Code documentation
([Set the auto-compact window](https://code.claude.com/docs/en/model-config#set-the-auto-compact-window),
checked on 2026-10-01):

- Without a set window, models running with a native 1M window compact "at about 967K tokens
  by default", and other sessions (for example 200K models) compact at the model's context limit.
- `/autocompact`, `--autocompact`, the `autoCompactWindow` setting and
  `CLAUDE_CODE_AUTO_COMPACT_WINDOW` set the window as a **token count** from 100K to 1M, and
  "Claude Code caps the window at the model's context window".

So ctxtray keeps one token count, `autoCompactWindow` in its own config file (`"auto"` by
default, which stands for 967,000), and takes the point for each session as the smaller of that
and the model's context window. With the default this gives 967K on a 1M model and 200K on a
200K model, both as documented. The settings dialog accepts the same forms as `/autocompact`
(`200000`, `500k`, `1M`, a bare `100`–`1000` meaning thousands, `auto`).

ctxtray does not read Claude Code's settings, so the user enters the value they gave
`/autocompact`. One value applies to every session; a project-level setting, `--autocompact`
for one launch, or the environment variable is not picked up. When a window is set, ctxtray
takes the point to be the window itself; whether Claude Code compacts exactly there or slightly
before it has not been checked. The point is used only for the "tokens until auto-compaction"
figure (row details and notifications) and for the warning in the settings dialog.

A config file from 0.9.0 or earlier holds `compactThreshold`, a share of the window, instead.
It is carried over once: 0.967 (the old default) and 0.92 (an old placeholder value) become
`"auto"`; any other value is read as a point on a 1M model (0.5 becomes 500,000).

Context thresholds use the same percentage as the panel: a share of the window. The settings
dialog warns in red when a context threshold is at or above the point on a 1M model (the
earliest point as a share of the window), because such a colour would never appear there.

### Notifications for warn and danger

Each value (context, 5-hour, weekly) has two switches, warn and danger, under
`notify.enabled` (for example `"context": { "warn": false, "danger": true }`). The thresholds
are still the single set above; the switches only choose which levels are announced. A file
from 0.9.0 or earlier holds `true` or `false` per value; `true` is read as both on and `false`
as both off. When a value crosses more than one level at once (ctxtray starting while a value
is already in danger, for example), it announces the highest level that is switched on among
those crossed, so "warn only" still reports the warn crossing.

## 5. Getting out of the way of full-screen apps

The panel is a topmost window, so without help it would sit on top of a full-screen video,
a presentation, or a game. Windows already tracks whether this is a good moment to show a
notification, and ctxtray borrows that answer: `SHQueryUserNotificationState` reporting
`QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN`, or `QUNS_PRESENTATION_MODE` is taken to mean
"something is full-screen". A merely maximised window is not one of those states.

One exception: if the window in front belongs to Claude Desktop, the panel stays. Claude
Desktop in full screen is exactly when its context readout is worth seeing. The owner of
the foreground window is resolved with `GetForegroundWindow` and `GetWindowThreadProcessId`,
then checked against the same test used for "is Claude Desktop running" (section 2) —
the executable's name and product name, nothing more.

The panel only re-appears if ctxtray was the one that hid it. Hiding it yourself, or while
a full-screen app is running, is respected. The whole behaviour is one setting
(`hideWhenFullscreen`, on by default).

Separately, the panel and the row-details card are left out of screen sharing and
screenshots, because they show session names. Each window is given
`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`, so it stays on the monitor but does not
appear in captures (Windows 10 version 2004 and later; earlier versions treat the value as
`WDA_MONITOR`, and the window appears as an empty box). The affinity is set again whenever
the window is recreated, as it is when click-through is switched. Windows does not offer a
way to leave a window out of screen sharing but not out of screenshots, so both go
together. Microsoft does not present this as a guarantee against every capture method. One
setting (`hideFromCapture`, on by default) turns it off.

## 6. What it does not do, and the network

The data ctxtray shows comes from plain-text files that Claude Desktop and Claude Code wrote
themselves (sections 2–4). Besides reading them, it asks Windows which processes are running
and what their executables are called (section 2), whether something is full-screen and who
owns the window in front (section 5), and whether Windows uses a light or dark theme. It does
not modify the application, does not decompile or analyse its code, does not call any
Anthropic API, and does not touch stored credentials.

By default it makes no network connections. There are two exceptions, both opt-in and
both off by default, and neither sends cookies, tokens, or any other credentials:

- `fetchModelLimits`: for a model whose context window it does not know, it reads that
  model's public documentation page on `platform.claude.com` (see Denominators above for
  when it reads a page again).
- `checkUpdates`: once a day it reads
  `https://api.github.com/repos/utori7/ctxtray/releases/latest` (GitHub's public REST API,
  unauthenticated) and keeps only `tag_name`, accepted only in the form `vX.Y.Z`. If that is
  newer than the running version it shows one notification per version; clicking it opens
  `https://github.com/utori7/ctxtray/releases/tag/vX.Y.Z`, a URL built from the checked version
  rather than taken from the response. A failed check is retried after an hour. The last check
  time, the latest version, and the version already notified are kept in
  `%LOCALAPPDATA%\ctxtray\update-check.json` so that a restart does not check again. It never
  downloads or replaces the exe.

Both use the same request rules: HTTPS only, no redirects to another host, a 10-second
timeout, and at most 256 KB read.

## 7. When it breaks

All four sources are internal formats and can change without notice. The design rule is
that an unreadable or unrecognised source degrades to "unknown" and records a diagnostic;
it never throws, and never guesses.

If something reads wrong after an update, `ctxtray --json` shows the resolved paths and
a `diag` string, which is the fastest way to see which of the four sources stopped
working.

From Claude Code 2.1.229 through 2.1.284 (the copy bundled with Claude Desktop, last checked on
2026-10-02) ctxtray read all four sources without a diagnostic, although the contents of
`plan-usage-history.json` were reset around 2.1.247.
