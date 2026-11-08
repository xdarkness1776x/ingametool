# InGameTool

A custom Dalamud diagnostics plugin for PlayerSync and Penumbra, intended to run **inside Final Fantasy XIV**.

**Install after the GitHub Actions build has succeeded:** Open Dalamud Settings > Experimental > Custom Plugin Repositories and add:

`https://raw.githubusercontent.com/xdarkness1776x/ingametool/main/pluginmaster.json`

Enable the repository, Save, open `/xlplugins`, search for **InGameTool**, and click Install. Dalamud handles installation and updates. There is no Windows script.

In the game, enter `/ingametool` or open it from Plugin Installer. Click **CHECK CONNECTIONS** to check live Penumbra IPC availability and installed/loaded status of PlayerSync, Glamourer, Customize+, and Penumbra. Click **SCAN MY CHARACTER + SAVE**, or select a cooperating player, confirm permission, and click **SCAN TARGET + SAVE**. Reports save to `Desktop/PlayerSync_Live_Investigator/reports`. Click **OPEN REPORT FOLDER** to see them.

**Limits:** PlayerSync is checked for loaded status only, not through an undocumented API. Penumbra resource resolution is queried through public IPC on a character in the local game scene. Reports contain no player names, IDs, mod file contents, or remote server credentials. Other players' full original mod manifests are not exposed through normal synchronization. This version does not copy, export, or download mods.

**Status:** source and CI configured; never assume installation works until the Actions build passes and `pluginmaster.json` exists. If CI fails, fix code and rerun before attempting installation. This is an unofficial third-party plugin.
