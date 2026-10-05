# ctxtray の設定ファイルとコマンドライン

[README に戻る](../README.ja.md) ・ [English](configuration.md)

- [設定ファイル](#設定ファイル)
- [コマンドライン](#コマンドライン)
- [書き込むファイル](#書き込むファイル)
- [通信](#通信)

## 設定ファイル

ふだんの設定は、トレイメニューの「設定…」で開く設定画面（パネルの操作／パネルの表示項目／トレイアイコン／しきい値と通知／モデル／全般の 6 つのタブ）で変更できます。
設定は `%LOCALAPPDATA%\ctxtray\config.json` に保存されます。
このファイルを直接編集した場合も、**保存するとすぐに反映**され、再起動は必要ありません。
実行中に設定ファイルが読み込めなくなったときは、修正されるまで前回の設定で動作します。

下の例のようにコメント（`//` や `/* */`）を書いても読み込めますが、コメントは残りません。
設定画面で OK を押したときやパネルを移動したときに、ctxtray がファイルを書き直すためです。

```jsonc
{
  "thresholds": {
    // コンテキストは 0〜1 の小数で指定します（0.75 = パネルの 75%）。
    // 自動圧縮が起きる位置（1M のモデルは既定で約 97%）以上にすると、その色になる前に自動圧縮されます。
    "context":  { "warn": 0.75, "danger": 0.90 },
    "fiveHour": { "warn": 80,   "danger": 95 },
    "weekly":   { "warn": 80,   "danger": 95 }
  },
  "notify": {
    // 値ごとに、注意と危険のどちらで（または両方で）通知するか。
    "enabled": {
      "context":  { "warn": true, "danger": true },
      "fiveHour": { "warn": true, "danger": true },
      "weekly":   { "warn": true, "danger": true }
    },
    // 通知したあとは、値がしきい値からこのポイント数だけ下がるまで、
    // 再びしきい値を超えても通知しません。
    "hysteresisPts": 10,
    // 同じ値の通知の最小間隔（分）。前回より上のレベル（注意 → 危険）に
    // 上がったときは、待たずに通知します。
    "minRepeatMinutes": 30
  },
  "tray": {
    "mode": "single",            // "single" = 1 個にまとめる / "multi" = 値ごとに分ける
    "label": "letters",          // multi のときのラベル: "letters"（文字） | "glyphs"（記号） | "percent"（現在の %）
    "values": ["context", "fiveHour", "weekly"]
  },
  "display": {
    "theme": "auto",             // "light" | "dark"
    "textSize": "normal",        // "xsmall" | "small" | "large" | "xlarge"
    "nameWidth": 180,            // セッション名の列の幅（表示倍率 100%・文字サイズ「標準」での px。実際は倍率と文字サイズに合わせて広がる）。パネルの幅はこれとオンにした列の合計。highlightWaiting がオンのときは強調の文字が入る幅より狭くしない
    "showRateLimits": true,
    "showSessions": true,
    "hideIdleSessions": true,
    "idleHours": 24,
    "hideStoppedSessions": false, // 実行中でないセッション（灰色の行）を非表示にする
    "showBar": true,
    "showTokens": false,         // 例: 284k/1M
    "showModel": false,          // 行にモデル名を表示（例: Opus 5.5）
    "showEffort": false,         // 行にエフォートを表示（例: high）
    "rowLayout": "oneLine",      // "oneLine"（トークン数とモデルを横に並べる。パネルの幅が広がる）| "twoLine"（2 段目に表示。行が高くなる）
    "highlightWaiting": false,   // 対応が必要な行（承認待ち・回答待ち・対応待ち）に色を付け、待っている内容を表示
    "showResets": "warn",        // "warn" | "always" | "never"（5時間枠のリセット時刻。warn は 5時間枠が注意の値を超えたときだけ）
    "opacity": 0.9,
    "clickThrough": false,
    "showAtStartup": true,
    "hideWhenFullscreen": true,  // 全画面表示のアプリを使用中は非表示にする
    "hideFromCapture": true      // 画面共有とスクリーンショットでは非表示にする
  },
  "language": "auto",            // "ja" | "en"
  "clickThroughHotkey": "",      // 例: "Ctrl+Alt+T" でクリックの透過をキーで切り替える。"" = なし
  // 自動圧縮が起きる位置（トークン数）。"auto" は Claude Code の既定値です。
  // /autocompact で変更した場合は、同じ値（500000、"500k" など）にしてください。
  "autoCompactWindow": "auto",
  // ctxtray に登録されていないモデルのコンテキストウィンドウ（トークン数）。
  // ここに書いた値は、組み込みの表より優先されます。モデル名は完全に一致させてください
  // （末尾の日付だけが違う名前、たとえば -20251001 付きは同じモデルとして扱います）。
  "modelLimits": { "claude-example-6": 1000000 },
  // 上限が不明なモデルを公式ドキュメントで調べます（既定はオフ。後述）。
  "fetchModelLimits": false,
  // 新しいバージョンが出ていないか、1 日 1 回 GitHub で確認します（既定はオフ。後述）。
  "checkUpdates": false
}
```

0.9.0 までの設定ファイルも、そのまま読み込めます。`compactThreshold`（割合）は `autoCompactWindow` に、
`notify.enabled` の `true` / `false` は注意・危険の両方オン／両方オフとして引き継ぎ、次に保存したときに新しい形で書き直します。

## コマンドライン

```
ctxtray                     引数なしで常駐（トレイ + パネル）
ctxtray --status            状態を表形式で 1 回表示
ctxtray --json              状態を JSON で出力
ctxtray --no-external       Claude Desktop のタブだけを対象にする
ctxtray --include-archived  終了済みのタブも含める
ctxtray --verify-weekly     週間枠リセットの推定過程を表示
ctxtray --watch-status      セッションの状態の変化を記録し続ける（診断用。Ctrl+C で終了）
ctxtray --icon-preview [dir] トレイアイコンのプレビューを PNG で出力
ctxtray --hud-preview [dir]  パネルのプレビュー（架空のデータ）を PNG で出力
ctxtray --version           バージョンを表示
```

`--status` と `--json` は、パネルで非表示にしているセッションも含め、すべてのセッションを出力します。
どちらも通信は行いません。

`ctxtray.exe` は GUI アプリなので、シェルは終了を待たず、`>` でリダイレクトしても出力を受け取れません。
スクリプトから使うときは、**パイプで受け取ってください**（PowerShell の例）。

```powershell
ctxtray --json | ConvertFrom-Json
ctxtray --json | Out-File state.json
```

パイプやファイルに出力するときは、JSON 内の日本語などを `\uXXXX` の形式で書き出します。
受け取る側がどの文字コードで読み込んでも文字化けしないようにするためです。

`--json` の出力には、セッション名、作業フォルダーのパス、ユーザー名を含むパスが入ります。
人に見せるときは、それらを消してください。

### `--watch-status`（診断用）

Claude Code が `~/.claude/sessions/<pid>.json` に記録しているセッションの状態（`status`）を 0.5 秒ごとに読み、
変化があるたびに 1 行出力します。承認待ちや完了のときに Claude Code が何を記録するかを調べるためのものです。
同じ行に、transcript の末尾（最後の発言の種類とツール名）と、Claude Desktop のタブの記録の項目も出力します。

```powershell
ctxtray --watch-status | Tee-Object ctxtray-status.log
```

画面に表示しながらファイルにも保存できます。Ctrl+C で終了します。
出力にはセッション名、フォルダーのパス、会話の本文は含めません。
値は英数字と `. _ : -` だけの短いものに限り、それ以外は文字数だけを出力するため、そのまま Issue に貼り付けられます。
例外は何を待っているかを表す `waitingFor` で、英字と空白だけの 40 文字以内の文なら、その文を出力します。
ID のような値は、実行ごとの番号（`id#1` など）に置き換えます。同じ値は同じ番号になるので、値が変わったことは分かります。
入れ子の項目は `postTurnSummary.status_category` のように「親.子」の形で出力します。

## 書き込むファイル

書き込むのは、ctxtray 自身のファイルだけです。

- `%LOCALAPPDATA%\ctxtray\config.json`：設定（読み込めなかったファイルを退避したときは `config.json.bak` も）
- `%LOCALAPPDATA%\ctxtray\model-limits.json`：公式ドキュメントから取得したモデルの上限と名前、
  上限が不明なモデルを通知したかどうかの記録
- `%LOCALAPPDATA%\ctxtray\update-check.json`：最後に更新を確認した時刻、最新のバージョン番号、
  通知したバージョンの記録（「新しいバージョンを確認する」をオンにした後だけ）
- スタートアップフォルダーの `ctxtray.lnk`：「サインイン時に起動」がオンの間だけ

## 通信

**既定では、ネットワーク通信を一切行いません。** 通信するのは、設定で次のどちらかをオンにした場合だけです。
どちらも Cookie・トークンなどの認証情報は使わず、会話やアカウントの情報は送信しません。

- 「上限が不明なモデルを公式ドキュメントで調べる」（設定 > モデル）:
  ctxtray に登録されていないモデルを見つけたときに、
  `https://platform.claude.com/docs/en/models/<モデル>/overview.md`（誰でも閲覧できる公開ドキュメント）を読み込みます。
  取得できた上限は記録して使い続けます。見つからなかったモデルは 24 時間後にもう一度確認します。
- 「新しいバージョンを確認する」（設定 > 全般）:
  `https://api.github.com/repos/utori7/ctxtray/releases/latest`（GitHub の公開 API）を 1 日 1 回読み込み、
  最新リリースのバージョン番号だけを取り出します。確認に失敗した場合は 1 時間後にもう一度確認します。
  ダウンロードや ctxtray.exe の入れ替えは行いません。

どちらも HTTPS だけを使い、他のサイトへの転送には従わず、10 秒で打ち切り、読み込むのは最大 256 KB です。
詳しくは [how-it-works.md](how-it-works.md#6-what-it-does-not-do-and-the-network)（英語）を参照してください。
