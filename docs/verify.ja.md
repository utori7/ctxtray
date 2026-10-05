# ダウンロードしたファイルの確認

[README に戻る](../README.ja.md) ・ [English](verify.md)

Release ページの `.sha256` ファイルで確かめられるのは、ダウンロード中にファイルが壊れていないかだけです。
`.sha256` は zip と同じ Release に置かれているため、Release ごと差し替えられた場合は見分けられません。

v0.9.0 以降のバージョンは、[GitHub CLI](https://cli.github.com/)（`gh`）で次の 2 点を確かめられます。

```powershell
# このリポジトリのリリース用の GitHub Actions がビルドしたものか
gh attestation verify ctxtray-<バージョン>-win-x64.zip --repo utori7/ctxtray --signer-workflow utori7/ctxtray/.github/workflows/release.yml

# 公開したあとに差し替えられていないか
gh release verify-asset v<バージョン> ctxtray-<バージョン>-win-x64.zip --repo utori7/ctxtray
```

1 つ目のコマンドは、展開した `ctxtray.exe` にもそのまま使えます。

ビルド済みのファイルを使いたくない場合は、ソースから[自分でビルド](../README.ja.md#ビルド)することもできます。
