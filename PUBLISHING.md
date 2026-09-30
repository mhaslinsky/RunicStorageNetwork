# Publishing releases

The [Thunderstore release workflow](https://github.com/rerit33/RunicStorageNetwork/actions/workflows/thunderstore.yml) uploads a prepared release ZIP to **Rerit/RunicStorageNetwork** in the **Valheim** community. It uses the official Thunderstore CLI.

The Unity asset and plugin build remains local. GitHub validates and uploads the finished package; it does not rebuild the Unity models or download game assemblies. Ordinary pushes and pull requests do not publish anything.

## One-time setup

In Thunderstore, open **Settings → Teams → Rerit → Service Accounts** and create a publishing service account. Save its token as the GitHub repository Actions secret **TCLI_AUTH_TOKEN**. Do not put the token in source files, release notes or command arguments.

## Release procedure

1. Build and test the mod locally. Keep the source version, compiled DLL version and `manifest.json` version aligned. Update `CHANGELOG_EN.md` and `CHANGELOG.md`.
2. Commit and push the matching source. Create a tag such as `v0.5.3` on that commit and push the tag.
3. Create a **draft GitHub release** for that tag and attach `RunicStorageNetwork-0.5.3.zip`. Attach the ZIP before publishing the release.
4. In Actions, run **Thunderstore release → Run workflow**, entering the tag. This manual run validates the archive and CLI configuration but **never uploads to Thunderstore**, even when the secret is configured.
5. Once the checks and your in-game testing are complete, publish the GitHub release as a normal release. This triggers the upload automatically. Drafts and prereleases do not upload.

The archive must contain exactly:

```text
manifest.json
README.md
CHANGELOG.md
icon.png
plugins/RunicStorageNetwork/RunicStorageNetwork.dll
plugins/RunicStorageNetwork/Assets/rsn_core_windows
```

The validator checks the tag, source and compiled assembly versions, metadata, dependencies, documentation, icon dimensions and asset bundle header. Archive entries and duplicates are checked before reading files. These checks do not replace in-game tests.

For a local validation run:

```powershell
.\tools\ValidateRelease.ps1 -Package '.\dist\RunicStorageNetwork-0.5.3.zip' -Tag 'v0.5.3'
```

## Failed or repeated runs

Inspect the Actions log. A missing ZIP, mismatched version or stale documentation stops the upload. An already published Thunderstore version is skipped on retries. If an upload reports a connection error, check Thunderstore before retrying: it may have accepted the package before the response was lost.

Fix release assets while the GitHub release is still a draft, then repeat the manual validation. After publication, use a new version for package changes; do not move a published version tag. The workflow never overwrites or deletes an existing Thunderstore version.

## Hexium

The separate [Hexium release workflow](https://github.com/rerit33/RunicStorageNetwork/actions/workflows/hexium.yml) publishes the same prepared ZIP to **Rerit/RunicStorageNetwork** at **valheim.hexium.gg**. A failure on one platform does not prevent the other workflow from running. The Hexium categories are configured in `hexium.toml`.

### One-time setup

1. Sign in to Hexium and create or join the **Rerit** team. In team settings, create an API token with publishing access. If Hexium requests verification of the team name, complete that on the site first.
2. Add the token as the repository Actions secret **HEXIUM_AUTH_TOKEN**, separately from Thunderstore's **TCLI_AUTH_TOKEN**. Never commit the token or paste it into an issue or workflow input.
3. Run **Hexium release → Run workflow** on `main`, set `tag` to `v0.5.6` (or another existing release), and leave **publish** unchecked. This validates the release ZIP, checks dependencies on Hexium and parses the CLI configuration without credentials or an upload.
4. For the first upload, run it again with **publish** checked. Only an already published, stable GitHub release can be uploaded this way. This also allows backfilling older releases without changing their tags.

After setup, publishing a new stable GitHub release automatically starts both platform workflows. Ordinary pushes, pull requests and draft releases do not upload packages. Hexium's manual run uploads **only** when **publish** is explicitly checked; Thunderstore's manual run remains validation-only.

Hexium accepts Thunderstore-compatible archives and assumes BepInExPack_Valheim, removing that dependency from its manifest metadata on upload. Jötunn remains a required dependency and is checked before publication. The original release ZIP is not modified by the workflow.

After upload, the workflow sets Hexium's install location to **both client and server** and verifies the published version. A retry skips an existing version but still applies the install-location setting. If publication reports a connection error or succeeds before a later metadata step fails, inspect the package page before retrying; an accepted version must not be uploaded again.

## API builds and GitHub Wiki

For the 0.8.8 code-only release, `tools/BuildApiPreview.ps1 -Release` builds and validates `dist/RunicStorageNetwork-0.8.8.zip`, runs the isolated tests, and checks the installed game packet format. Without `-Release`, it creates a local preview archive instead. The script reuses the verified 0.8.1 visual assets and also builds a separate single-player test consumer. Do not include that test plugin in the main release ZIP.

Wiki sources are versioned in this repository: `API.md` becomes Wiki page `API.md`; files in `docs/wiki/` retain their names. After updating them, clone/pull `git@github.com:rerit33/RunicStorageNetwork.wiki.git`, copy those files, review its diff, and commit/push its default branch. The Wiki must have an initial page created through GitHub before it can be cloned. Never force-push Wiki history or remove unrelated pages.

Keep configuration details and the API guide on GitHub. Only the short experimental notice and documentation links belong in the packaged README; neither the Wiki files nor the API example are included in the six-file mod package.

## References

- [Official Thunderstore CLI](https://github.com/thunderstore-io/thunderstore-cli)
- [Thunderstore CLI authentication and prebuilt package publishing](https://github.com/thunderstore-io/thunderstore-cli/wiki)
- [GitHub release workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release)
- [Hexium packaging and dependencies](https://hexium.gg/packaging)
- [Hexium API reference](https://hexium.gg/api/docs/)
- [Hexium team API tokens](https://hexium.gg/faq)
