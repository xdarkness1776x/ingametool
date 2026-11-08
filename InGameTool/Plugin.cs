using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace InGameTool;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface Interface { get; set; } = null!;
    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static ITargetManager Targets { get; set; } = null!;
    [PluginService] private static IObjectTable Objects { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly ICallGateSubscriber<(int, int)> version;
    private readonly ICallGateSubscriber<Dictionary<string, string>> mods;
    private readonly ICallGateSubscriber<string> modFolder;
    private readonly ICallGateSubscriber<ushort[], Dictionary<string, HashSet<string>>?[]> paths;
    private readonly ICallGateSubscriber<List<nint>> playerSyncHandledAddresses;
    private bool open;
    private bool targetConsent;
    private bool includeGamePaths;
    private bool includeOwnModNames;
    private string status = "Ready. Click CHECK CONNECTIONS to test Penumbra IPC.";
    private string details = "";
    private readonly List<PreviewGroup> previewGroups = [];
    private readonly HashSet<string> selectedPreviewGroups = new(StringComparer.OrdinalIgnoreCase);
    private string previewStatus = "No resource groups loaded. Target a consenting PlayerSync-synced character.";
    private sealed record PreviewGroup(string Family, int ExternalFiles, int PenumbraFiles,
        int GameResources, int GamePaths);


    public Plugin()
    {
        version = Interface.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
        mods = Interface.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        modFolder = Interface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
        paths = Interface.GetIpcSubscriber<ushort[], Dictionary<string, HashSet<string>>?[]>(
            "Penumbra.GetGameObjectResourcePaths.V5");
        playerSyncHandledAddresses = Interface.GetIpcSubscriber<List<nint>>("PlayerSync.GetHandledAddresses");
        Commands.AddHandler("/ingametool", new CommandInfo((_, _) => open = !open)
        { HelpMessage = "Open InGameTool live diagnostics." });
        Interface.UiBuilder.Draw += Draw;
        Interface.UiBuilder.OpenMainUi += Open;
    }

    private void Open() => open = true;

    public void Dispose()
    {
        Interface.UiBuilder.Draw -= Draw;
        Interface.UiBuilder.OpenMainUi -= Open;
        Commands.RemoveHandler("/ingametool");
    }

    private static List<object> RelatedPlugins()
    {
        var names = new[] { "Penumbra", "PlayerSync", "Mare", "Glamourer", "Customize" };
        return Interface.InstalledPlugins
            .Where(p => names.Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)
                                    || p.InternalName.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Select(p => (object)new { p.Name, Version = p.Version.ToString(), p.IsLoaded })
            .ToList();
    }

    private void CheckConnections()
    {
        var penumbra = "unavailable";
        try { var v = version.InvokeFunc(); penumbra = v.Item1 + "." + v.Item2; }
        catch (Exception e) { penumbra = "IPC failed: " + e.GetType().Name; }
        var names = string.Join(", ", Interface.InstalledPlugins
            .Where(p => new[] { "Penumbra", "PlayerSync", "Mare", "Glamourer", "Customize" }
                .Any(n => p.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name + " " + p.Version + (p.IsLoaded ? " (loaded)" : " (not loaded)")));
        var playerSyncIpc = "unavailable";
        try
        {
            var addresses = playerSyncHandledAddresses.InvokeFunc();
            playerSyncIpc = "active (" + addresses.Count + " handled game objects)";
        }
        catch (Exception ex)
        {
            playerSyncIpc = "unavailable: " + ex.GetType().Name;
        }
        details = "PlayerSync handled-object IPC: " + playerSyncIpc + "; Penumbra public IPC version: " + penumbra + "; installed: " + names
            + ". PlayerSync presence is detected, but its private sync internals are not accessed.";
        status = "Connection check completed.";
    }

    private void Draw()
    {
        if (!open) return;
        if (!ImGui.Begin("InGameTool - Live Diagnostics##ingametool", ref open))
        { ImGui.End(); return; }
        try
        {
            ImGui.TextWrapped("Read-only PlayerSync / Penumbra investigation. No transfers or mod copies.");
            ImGui.TextWrapped("Reports are saved to your Desktop; nothing is uploaded.");
            ImGui.TextWrapped("v0.2.3: Browse and select appearance resource groups from PlayerSync-handled targets.");
            if (ImGui.Button("CHECK CONNECTIONS")) CheckConnections();
            ImGui.TextWrapped(details);
            ImGui.Separator();
            ImGui.Checkbox("Include game resource paths", ref includeGamePaths);
            ImGui.Checkbox("Include MY installed mod names (self scans only)", ref includeOwnModNames);
            ImGui.Checkbox("The targeted player agreed to the scan", ref targetConsent);
            if (ImGui.Button("SCAN MY CHARACTER + SAVE")) Capture(true);
            ImGui.SameLine();
            // Capture/Browse may clear targetConsent in this frame. Match the ImGui
            // BeginDisabled/EndDisabled calls using the pre-click state.
            var targetActionsDisabled = !targetConsent;
            if (targetActionsDisabled) ImGui.BeginDisabled();
            if (ImGui.Button("SCAN TARGET + SAVE")) Capture(false);
            if (ImGui.Button("BROWSE TARGET RESOURCES")) BrowseTarget();
            if (targetActionsDisabled) ImGui.EndDisabled();
            if (previewGroups.Count > 0)
            {
                ImGui.Separator();
                ImGui.TextWrapped("SELECT RESOURCE GROUPS (not original mod packages)");
                ImGui.TextWrapped("These groups come from game paths. Selection creates a local plan only; it does not copy or download files.");
                if (ImGui.Button("SELECT EXTERNAL GROUPS"))
                {
                    selectedPreviewGroups.Clear();
                    foreach (var g in previewGroups.Where(g => g.ExternalFiles > 0))
                        selectedPreviewGroups.Add(g.Family);
                }
                ImGui.SameLine();
                if (ImGui.Button("CLEAR SELECTION")) selectedPreviewGroups.Clear();
                ImGui.TextWrapped(previewStatus);
                if (ImGui.CollapsingHeader("Appearance groups (click to expand)"))
                {
                    foreach (var g in previewGroups)
                    {
                        var picked = selectedPreviewGroups.Contains(g.Family);
                        var title = g.Family + " | external " + g.ExternalFiles
                            + " | Penumbra " + g.PenumbraFiles
                            + " | game/swap " + g.GameResources
                            + " | paths " + g.GamePaths;
                        if (ImGui.Checkbox(title + "##group_" + g.Family, ref picked))
                        {
                            if (picked) selectedPreviewGroups.Add(g.Family);
                            else selectedPreviewGroups.Remove(g.Family);
                        }
                    }
                }
                ImGui.TextWrapped(selectedPreviewGroups.Count + " appearance group(s) selected.");
                if (selectedPreviewGroups.Count == 0) ImGui.BeginDisabled();
                if (ImGui.Button("SAVE SELECTED GROUPS PLAN")) SaveSelectionPlan();
                if (selectedPreviewGroups.Count == 0) ImGui.EndDisabled();
            }
            if (ImGui.Button("OPEN REPORT FOLDER"))
            {
                try {
                    var folder = Folder();
                    Directory.CreateDirectory(folder);
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                } catch (Exception e) { status = "Cannot open report folder: " + e.GetType().Name; }
            }
            ImGui.Separator();
            ImGui.TextWrapped(status);
        }
        catch (Exception e) { status = "UI error: " + e.GetType().Name; Log.Error(e, "UI"); }
        finally { ImGui.End(); }
    }

    private static string Folder()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(desktop)) throw new IOException("Desktop is unavailable");
        return Path.Combine(desktop, "PlayerSync_Live_Investigator", "reports");
    }

    private static string ClassifyOrigin(string resolvedPath, string penumbraDirectory)
    {
        if (!Path.IsPathFullyQualified(resolvedPath)) return "game-resource-or-swap";
        var root = penumbraDirectory.TrimEnd('\\', '/');
        if (root.Length > 0 && resolvedPath.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return "installed-penumbra";
        // A path outside Penumbra is NOT proof that PlayerSync owns it.
        return "external-local-file";
    }

    private static string ClassifyResourceFamily(string gamePath)
    {
        var parts = gamePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return "other";
        if (!parts[0].Equals("chara", StringComparison.OrdinalIgnoreCase))
            return parts[0].ToLowerInvariant();
        if (parts.Length > 2 && (parts[1] == "equipment" || parts[1] == "accessory"))
            return parts[1] + "/" + parts[2];
        if (parts.Length > 5 && parts[1] == "human" && parts[3] == "obj")
            return "human/" + parts[4] + "/" + parts[5];
        return "chara/" + parts[1];
    }

    private void BrowseTarget()
    {
        previewGroups.Clear();
        selectedPreviewGroups.Clear();
        if (!targetConsent)
        {
            previewStatus = "Confirm the targeted player's permission first.";
            return;
        }
        try
        {
            if (Targets.Target is not IPlayerCharacter player)
            {
                previewStatus = "Target a player character first.";
                return;
            }
            // This public endpoint confirms PlayerSync is handling the game object.
            // It is not a permission grant, and exposes no original mod catalog.
            var handled = playerSyncHandledAddresses.InvokeFunc();
            if (!handled.Contains(player.Address))
            {
                previewStatus = "PlayerSync is not handling this target right now. No preview created.";
                return;
            }

            var penumbraDirectory = modFolder.InvokeFunc() ?? string.Empty;
            var resourceMap = paths.InvokeFunc(new ushort[] { player.ObjectIndex })?.FirstOrDefault();
            if (resourceMap == null)
            {
                previewStatus = "Penumbra did not return this character's resource map.";
                return;
            }
            // Never retain full local file paths in the preview model.
            var grouped = resourceMap.SelectMany(entry => (entry.Value ?? new HashSet<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(path => new
                {
                    Family = ClassifyResourceFamily(path),
                    Origin = ClassifyOrigin(entry.Key, penumbraDirectory),
                    Resource = entry.Key,
                    GamePath = path
                }))
                .GroupBy(x => x.Family)
                .Select(g => new PreviewGroup(
                    g.Key,
                    g.Where(x => x.Origin == "external-local-file").Select(x => x.Resource)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    g.Where(x => x.Origin == "installed-penumbra").Select(x => x.Resource)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    g.Where(x => x.Origin == "game-resource-or-swap").Select(x => x.Resource)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    g.Select(x => x.GamePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
                .OrderByDescending(x => x.ExternalFiles)
                .ThenBy(x => x.Family, StringComparer.OrdinalIgnoreCase)
                .ToList();
            previewGroups.AddRange(grouped);
            previewStatus = "Loaded " + previewGroups.Count +
                " resource groups from a currently PlayerSync-handled character. No mod files were accessed.";
            status = previewStatus;
        }
        catch (Exception ex)
        {
            previewStatus = "Could not browse character: " + ex.GetType().Name;
            status = previewStatus;
            Log.Error(ex, "BrowseTarget");
        }
        finally
        {
            // Confirm permission for each fresh target browse.
            targetConsent = false;
        }
    }

    private void SaveSelectionPlan()
    {
        if (selectedPreviewGroups.Count == 0)
        {
            status = "Select at least one group first.";
            return;
        }
        try
        {
            Directory.CreateDirectory(Folder());
            var selection = previewGroups.Where(g => selectedPreviewGroups.Contains(g.Family)).ToArray();
            var plan = new
            {
                Schema = 1,
                ToolVersion = "0.2.3.1",
                Utc = DateTimeOffset.UtcNow,
                Type = "resource-group-selection",
                Note = "NOT original Penumbra mods, transfer authorization, or downloadable files.",
                Groups = selection
            };
            var file = Path.Combine(Folder(), "ingametool_selection_" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(file,
                JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true }));
            status = "Saved " + selection.Length + " resource-group selections to Desktop: " + Path.GetFileName(file);
        }
        catch (Exception ex)
        {
            status = "Could not save selection: " + ex.GetType().Name;
            Log.Error(ex, "SaveSelectionPlan");
        }
    }

    private void Capture(bool self)
    {
        if (!self && !targetConsent) { status = "Confirm consent first."; return; }
        var player = self ? Objects.LocalPlayer : Targets.Target as IPlayerCharacter;
        if (player == null) { status = "No player character available; target a player."; return; }
        CheckConnections();
        var errors = new List<string>();
        var report = new Dictionary<string, object?>
        {
            ["schema"] = 4, ["toolVersion"] = "0.2.3.1", ["utc"] = DateTimeOffset.UtcNow,
            ["subject"] = self ? "self" : "consenting-target",
            ["consentConfirmed"] = self || targetConsent,
            ["connectionStatus"] = details,
            ["detectedPlugins"] = RelatedPlugins(),
            ["dalamudVersion"] = Interface.GetDalamudVersion().Version.ToString()
        };
        // Public PlayerSync IPC returns runtime game-object addresses, but no mod data.
        // Never serialize raw addresses. Store only whether this player is handled.
        try
        {
            var handled = playerSyncHandledAddresses.InvokeFunc();
            report["playerSyncHandledActor"] = new
            {
                QuerySucceeded = true,
                VisibleHandledObjectCount = handled.Count,
                SelectedCharacterHandled = handled.Contains(player.Address)
            };
        }
        catch (Exception ex)
        {
            report["playerSyncHandledActor"] = new
            {
                QuerySucceeded = false,
                Diagnostic = ex.GetType().Name
            };
        }
        string root = "";
        try { root = modFolder.InvokeFunc() ?? ""; }
        catch (Exception e) { errors.Add("Penumbra.GetModDirectory: " + e.GetType().Name); }
        try
        {
            var list = mods.InvokeFunc();
            report["myInstalledModCount"] = list.Count;
            if (self && includeOwnModNames)
                report["myInstalledModNames"] = list.Values.OrderBy(x => x).Take(500).ToArray();
        }
        catch (Exception e) { errors.Add("Penumbra.GetModList: " + e.GetType().Name); }
        try
        {
            var data = paths.InvokeFunc(new ushort[] { player.ObjectIndex });
            var entries = data?.FirstOrDefault();
            if (entries == null) errors.Add("Penumbra resource IPC returned no data");
            else
            {
                var allPaths = entries.Values.Where(v => v != null).SelectMany(v => v)
                    .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var extensions = allPaths.GroupBy(x => Path.GetExtension(x) ?? string.Empty)
                    .ToDictionary(g => g.Key, g => g.Count());
                var local = entries.Keys.Count(Path.IsPathFullyQualified);
                var inside = entries.Keys.Count(p => root.Length > 0 &&
                    Path.IsPathFullyQualified(p) &&
                    p.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                report["resourceSummary"] = new {
                    ResolvedResources = entries.Count, GamePaths = allPaths.Length,
                    LocalFiles = local, InsidePenumbra = inside, OutsidePenumbra = local - inside,
                    Extensions = extensions
                };
                // Pair each resolved resource with the game path(s) to which it maps.
                // This is strictly metadata: do not export local absolute file paths,
                // file contents, sync keys, character names, or memory addresses.
                var mapped = entries
                    .SelectMany(entry => (entry.Value ?? new HashSet<string>())
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Select(gamePath => new
                        {
                            Origin = ClassifyOrigin(entry.Key, root),
                            Family = ClassifyResourceFamily(gamePath),
                            Resource = entry.Key,
                            GamePath = gamePath
                        }))
                    .ToArray();
                report["resourceOrigins"] = entries
                    .GroupBy(entry => ClassifyOrigin(entry.Key, root))
                    .OrderBy(group => group.Key)
                    .Select(group => new { Origin = group.Key, ResolvedFiles = group.Count() })
                    .ToArray();
                report["originByAppearanceGroup"] = mapped
                    .GroupBy(x => new { x.Origin, x.Family })
                    .OrderBy(g => g.Key.Origin).ThenBy(g => g.Key.Family)
                    .Select(g => new
                    {
                        g.Key.Origin,
                        AppearanceGroup = g.Key.Family,
                        DistinctResolvedFiles = g.Select(x => x.Resource).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        ReferencedGamePaths = g.Select(x => x.GamePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    })
                    .ToArray();
                if (includeGamePaths) report["optionalGamePaths"] = allPaths.OrderBy(x => x).Take(20000).ToArray();
            }
        }
        catch (Exception e) { errors.Add("Penumbra.GetGameObjectResourcePaths.V5: " + e.GetType().Name); }
        report["errors"] = errors;
        try
        {
            Directory.CreateDirectory(Folder());
            var file = Path.Combine(Folder(), "ingametool_" + (self ? "self" : "target") + "_"
                + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            status = "Saved to Desktop: " + Path.GetFileName(file) + ". Diagnostics: " + errors.Count + " note(s).";
        }
        catch (Exception e) { status = "Saving failed: " + e.Message; Log.Error(e, "Save"); }
        finally { if (!self) targetConsent = false; }
    }
}
