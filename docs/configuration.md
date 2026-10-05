# ctxtray config file and command line

[Back to the README](../README.md) · [日本語](configuration.ja.md)

- [Config file](#config-file)
- [Command line](#command-line)
- [Files it writes](#files-it-writes)
- [Network](#network)

## Config file

Day-to-day settings are in the dialog that **Settings…** on the tray menu opens, with six tabs
(**Panel controls**, **Panel content**, **Tray icon**, **Thresholds and notifications**, **Models**, **General**).
Everything is stored in `%LOCALAPPDATA%\ctxtray\config.json`. You can also edit that file
directly — **save it and changes apply immediately**, no restart.
If the file cannot be read while ctxtray is running, the previous settings stay in effect
until you fix it.

Comments (`//` and `/* */`) are accepted, as in the example below, but they are not kept:
ctxtray rewrites the file when you press OK in the dialog or move the panel.

```jsonc
{
  "thresholds": {
    // Context is a fraction of the window (0.75 = 75% on the panel).
    // A value at or above auto-compaction (about 97% on 1M models by default) is never reached.
    "context":  { "warn": 0.75, "danger": 0.90 },
    "fiveHour": { "warn": 80,   "danger": 95 },
    "weekly":   { "warn": 80,   "danger": 95 }
  },
  "notify": {
    // For each value, whether warn and danger send a notification.
    "enabled": {
      "context":  { "warn": true, "danger": true },
      "fiveHour": { "warn": true, "danger": true },
      "weekly":   { "warn": true, "danger": true }
    },
    // After a notification, the value has to fall this many points below the
    // threshold before crossing it again notifies you again.
    "hysteresisPts": 10,
    // Minimum time between notifications for one value. A rise to a higher level
    // than the last notification (warn -> danger) is reported straight away.
    "minRepeatMinutes": 30
  },
  "tray": {
    "mode": "single",            // "single" = one combined icon, "multi" = one per value
    "label": "letters",          // multi only: "letters" | "glyphs" | "percent"
    "values": ["context", "fiveHour", "weekly"]
  },
  "display": {
    "theme": "auto",             // "light" | "dark"
    "textSize": "normal",        // "xsmall" | "small" | "large" | "xlarge"
    "nameWidth": 180,            // width of the session-name column in px at 100% scaling and Normal text size (on screen it grows with both); the panel is this plus the columns you turn on; with highlightWaiting on, never narrower than the highlight words need
    "showRateLimits": true,
    "showSessions": true,
    "hideIdleSessions": true,
    "idleHours": 24,
    "hideStoppedSessions": false, // hide the grey rows (sessions that are not running)
    "showBar": true,
    "showTokens": false,         // e.g. 284k/1M
    "showModel": false,          // model name on the row (e.g. Opus 5.5)
    "showEffort": false,         // effort on the row (e.g. high)
    "rowLayout": "oneLine",      // "oneLine" (token counts and model side by side, wider panel) | "twoLine" (on a second line, taller rows)
    "highlightWaiting": false,   // colour the rows that need you (approval, answer, or an unread reply that needs action), and say which
    "showResets": "warn",        // "warn" | "always" | "never" (5-hour reset time; warn = only while the 5-hour limit is over its warn threshold)
    "opacity": 0.9,
    "clickThrough": false,
    "showAtStartup": true,
    "hideWhenFullscreen": true,  // hide while a full-screen app is in use
    "hideFromCapture": true      // hide from screen sharing and screenshots
  },
  "language": "auto",            // "ja" | "en"
  "clickThroughHotkey": "",      // e.g. "Ctrl+Alt+T" to toggle "clickThrough" from the keyboard; "" = none
  // Where auto-compaction happens, in tokens. "auto" is Claude Code's default.
  // If you changed it with /autocompact, use the same value (500000, "500k", ...).
  "autoCompactWindow": "auto",
  // Context window for models ctxtray does not know yet (tokens).
  // Entries here take precedence over the built-in table. Use the exact model name
  // (a name that differs only by a date, such as -20251001, counts as the same model).
  "modelLimits": { "claude-example-6": 1000000 },
  // Look up unknown models in the official docs (off by default; see below).
  "fetchModelLimits": false,
  // Check GitHub once a day for a new version (off by default; see below).
  "checkUpdates": false
}
```

Config files from 0.9.0 and earlier are read as they are: `compactThreshold` (a share of the window)
is carried over into `autoCompactWindow`, and `true` / `false` in `notify.enabled` means both warn and
danger on / off. The file is written in the new form the next time it is saved.

## Command line

```
ctxtray                     run resident (tray + panel)
ctxtray --status            print the current state as a table
ctxtray --json              print the current state as JSON
ctxtray --no-external       only Claude Desktop tabs
ctxtray --include-archived  include closed tabs
ctxtray --verify-weekly     show how the weekly reset was derived
ctxtray --watch-status      log each change in session state (diagnostics; Ctrl+C to stop)
ctxtray --icon-preview [dir] write a PNG sheet of the tray icons
ctxtray --hud-preview [dir]  write PNGs of the panel with made-up data
ctxtray --version           print the version
```

`--status` and `--json` list every session, including the ones the panel hides. Neither makes
a network connection.

`ctxtray.exe` is a GUI program, so a shell does not wait for it and `>` redirection does
not capture its output. **Use a pipe** when scripting (PowerShell):

```powershell
ctxtray --json | ConvertFrom-Json
ctxtray --json | Out-File state.json
```

When the output goes to a pipe or a file, non-ASCII characters in the JSON are written as
`\uXXXX` escapes, so it reads correctly whatever code page the receiving side uses.

The output of `--json` contains session names, working-directory paths, and paths that include
your user name. Remove them before showing it to anyone.

### `--watch-status` (diagnostics)

Reads the session state (`status`) that Claude Code records in `~/.claude/sessions/<pid>.json`
every 0.5 seconds and prints one line for each change. It is for finding out what Claude Code
records while it waits for approval or after it finishes. Each line also shows the end of the
transcript (the kind of the last message and the tool name) and the fields of the Claude Desktop
tab record.

```powershell
ctxtray --watch-status | Tee-Object ctxtray-status.log
```

This shows the lines and saves them to a file at the same time. Press Ctrl+C to stop.
The output contains no session names, folder paths or conversation text. Values are printed only
when they are short and use letters, digits and `. _ : -`; anything else is printed as its length,
so the output can be pasted into an issue as is. The one exception is `waitingFor`, which says
what the session is waiting for: it is printed when it is a phrase of up to 40 letters and spaces.
Values that look like IDs are replaced with a number for that run (`id#1` and so on); the same
value always gets the same number, so you can still see when it changes. Nested fields are
printed as `parent.child`, for example `postTurnSummary.status_category`.

## Files it writes

It writes only its own files:

- `%LOCALAPPDATA%\ctxtray\config.json` — your settings (plus `config.json.bak` if an
  unreadable file had to be set aside)
- `%LOCALAPPDATA%\ctxtray\model-limits.json` — context windows and model names fetched from
  the official docs, and which unknown models you have already been told about
- `%LOCALAPPDATA%\ctxtray\update-check.json` — when it last checked for updates, the latest
  version number, and which version you have been told about (only once **Check for new
  versions** is on)
- `ctxtray.lnk` in your Startup folder — only while **Start at sign-in** is on

## Network

**By default it makes no network connections at all.** It connects only if you turn on one of
these two settings. Neither sends cookies, tokens, or other credentials, and nothing about your
conversations or account is sent.

- **Look up unknown models in the official docs** (Settings > Models): when ctxtray meets a model
  it does not know, it reads `https://platform.claude.com/docs/en/models/<model>/overview.md`
  (public documentation). A context window it found is recorded and kept; a model that is not
  found is checked again after 24 hours.
- **Check for new versions** (Settings > General): reads
  `https://api.github.com/repos/utori7/ctxtray/releases/latest` (GitHub's public API) once a
  day and takes only the latest release's version number. After a failed check it tries again
  an hour later. It does not download anything or replace ctxtray.exe.

Both use HTTPS only, do not follow redirects to another host, give up after 10 seconds, and read
at most 256 KB. See [how-it-works.md](how-it-works.md#6-what-it-does-not-do-and-the-network) for details.
