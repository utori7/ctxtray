# Verifying the download

[Back to the README](../README.md) · [日本語](verify.ja.md)

The `.sha256` file on the release page only tells you the file was not damaged while downloading.
It sits in the same release as the zip, so it cannot tell you if the whole release was replaced.

For v0.9.0 and later, the [GitHub CLI](https://cli.github.com/) (`gh`) can check two things:

```powershell
# Was it built by this repository's release workflow on GitHub Actions?
gh attestation verify ctxtray-<version>-win-x64.zip --repo utori7/ctxtray --signer-workflow utori7/ctxtray/.github/workflows/release.yml

# Has it been replaced since it was published?
gh release verify-asset v<version> ctxtray-<version>-win-x64.zip --repo utori7/ctxtray
```

The first command works on the extracted `ctxtray.exe` as well.

If you would rather not trust a prebuilt binary, [build it yourself](../README.md#building).
