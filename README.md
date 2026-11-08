# InGameTool

A custom Dalamud diagnostics plugin for PlayerSync and Penumbra, intended to run **inside Final Fantasy XIV**.

**Install after the GitHub Actions build has succeeded:** Open Dalamud Settings > Experimental > Custom Plugin Repositories and add:

`https://raw.githubusercontent.com/xdarkness1776x/ingametool/main/pluginmaster.json`

Enable the repository, Save, open `/xlplugins`, search for **InGameTool**, and click Install. Dalamud handles installation and updates. There is no Windows script.

In the game, enter `/ingametool` or open it from Plugin Installer. Click **CHECK CONNECTIONS** to check live Penumbra IPC availability and installed/loaded status of PlayerSync, Glamourer, Customize+, and Penumbra. Click **SCAN MY CHARACTER + SAVE**, or select a cooperating player, confirm permission, and click **SCAN TARGET + SAVE**. Reports save to `Desktop/PlayerSync_Live_Investigator/reports`. Click **OPEN REPORT FOLDER** to see them.

**Limits:** PlayerSync is checked for loaded status only, not through an undocumented API. Penumbra resource resolution is queried through public IPC on a character in the local game scene. Reports contain no player names, IDs, mod file contents, or remote server credentials. Other players' full original mod manifests are not exposed through normal synchronization. This version does not copy, export, or download mods.

**Status:** source and CI configured; never assume installation works until the Actions build passes and `pluginmaster.json` exists. If CI fails, fix code and rerun before attempting installation. This is an unofficial third-party plugin.

## Direct-download updates

Starting with version 0.2.1.1, release automation publishes a version-specific ZIP at `distribution/v<version>/InGameTool.zip` through raw.githubusercontent.com and verifies its download checksum. This mirrors the GitHub Release ZIP but avoids GitHub's release-assets download redirect. Dalamud installs and updates using this ZIP; no Windows launcher is required.

## 0.2.2.0 — source attribution diagnostics

The reports now include a **read-only PlayerSync handled-object IPC check**, using the public `PlayerSync.GetHandledAddresses` endpoint. Game-object pointers are never saved; the report records only whether the selected character is handled and how many game objects are handled. Reports also include `resourceOrigins` and `originByAppearanceGroup` (equipment/accessory/body part). These summaries do not expose any local absolute filesystem paths or transfer mod files. **External local file** does not necessarily mean the file originated in PlayerSync. Original mod identities are not available from resource mapping alone.

## 0.2.3.0 — selectable preview of appearance resources

With a consenting, PlayerSync-handled player targeted, select the consent checkbox and click **BROWSE TARGET RESOURCES**. The plugin reads Penumbra's public resource-map IPC and presents a checklist of **appearance resource groups** (equipment, hair, face, etc.), with a count of external local resources, Penumbra-installed resources, and game/swap resources. Click **SAVE SELECTED GROUPS PLAN** to export a JSON summary to the Desktop reports folder; no original local file paths, player identities, memory addresses, or mod files are included. The plan is *not* a list of original mods, and no download is performed. The preview is cleared on each new browse and requires a new permission confirmation.

### 0.2.3.1 UI state fix

Ensures in-game target-consent controls remain balanced when permission is reset after a capture or preview. This avoids an ImGui disabled-stack error when scanning/browsing a target.
