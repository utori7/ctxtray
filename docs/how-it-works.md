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

Sampling interval is 5 minutes most often, sometimes 15. **Sampling stops when you are
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

The judgement is exposed as `freshness` in `ctxtray --json`. Earlier builds also marked
`behind` in the panel (first `▲`, then `+`); users did not read the mark as intended, so
it was removed and the panel only greys out `reference` values.

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
  33k autocompact buffer on a 1M window agrees with the 0.967 compaction point.

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
  alive, `--json` shows the Claude Desktop process as the tab's `pid` and `entrypoint`
  (earlier builds showed whichever process file was read last).

Short-lived "ghost" sessions exist (they start and end within a second, with no
transcript). Requiring a transcript file to exist filters them out.

A tab without a live process is still shown in the panel, greyed out, because it can be
resumed with the same context. It is not used for the tray icon, the tooltip or the
notifications: its usage cannot grow while it is stopped, and earlier builds re-announced
such sessions every time ctxtray started. Once the tab runs again, it counts again.

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

1. `modelLimits` in the config (set by you, also from Settings → Models)
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
window. (Versions up to 0.1.7 did that, which is how `claude-opus-5-5` once showed a
percentage before anyone had checked its window.)

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

### Compaction point

Earlier builds estimated "turns until compaction" from the median token growth per turn.
It was removed before release: the estimate was never checked against an actual
compaction, and it rested on a placeholder compaction point.

The compaction point is **0.967 of the window**, taken from the Claude Code documentation
([Default auto-compact thresholds](https://code.claude.com/docs/en/model-config#default-auto-compact-thresholds)):
models running with a native 1M window compact "at about 967K tokens by default", which is
also what Claude Desktop shows (97%). The same page says other sessions (for example 200K
models) compact at the model's context limit, so for those ctxtray reaches 100% about
3 points early. One value is used for every model; a window changed with `/autocompact`,
`autoCompactWindow` or `CLAUDE_CODE_AUTO_COMPACT_WINDOW` is not picked up, because ctxtray
does not read Claude Code's settings — set `compactThreshold` in the config to match.

Versions up to 0.1.1 used 0.92 as a placeholder, because no compaction was observed during
development (one session grew monotonically from 37k to 564k tokens with no drop), and
wrote it to the config file on every save although the dialog could not change it. A
stored 0.92 is therefore read as "not set" and replaced with the documented value.

Detecting compactions (a drop of 30% or more) and recording the pre-drop peak is still
designed but not implemented; it would only matter for a changed window.

Because that point can move, context thresholds are expressed as progress toward it rather
than as a fixed percentage of the window. A hardcoded "85% is dangerous" would fire
*after* compaction if the point were moved to 80%.

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

## 6. Terms compliance

ctxtray reads plain-text files that Claude Desktop and Claude Code wrote themselves,
and does nothing else. Specifically it does not modify the application, does not
decompile or analyse its code, does not call any Anthropic API, and does not touch stored
credentials.

By default it makes no network connections. The one exception is opt-in
(`fetchModelLimits`, off by default): for a model whose context window it does not know,
it reads that model's public documentation page on `platform.claude.com` once, without
cookies, tokens, or any other credentials (see Denominators above).

## 7. When it breaks

All four sources are internal formats and can change without notice. The design rule is
that an unreadable or unrecognised source degrades to "unknown" and records a diagnostic;
it never throws, and never guesses.

If something reads wrong after an update, `ctxtray --json` shows the resolved paths and
a `diag` string, which is the fastest way to see which of the four sources stopped
working.

From Claude Code 2.1.229 through 2.1.271 the fields ctxtray uses were unchanged, although
the contents of `plan-usage-history.json` were reset around 2.1.247.
