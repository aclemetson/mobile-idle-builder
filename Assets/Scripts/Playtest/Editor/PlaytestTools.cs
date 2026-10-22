using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using MobileIdleBuilder.Dev;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Playtest.Editor
{
    // MCP tools for the AI playtest harness (Playtests/README.md). They only act in Play Mode;
    // enter it with manage_editor. Use playtest_screenshot, not the built-in screenshot: in Play
    // Mode with the Editor in the background, that falls back to a camera render without the HUD
    // and steps the player loop. Coordinates are Game View screen pixels (top-left origin).

    [McpForUnityTool("playtest_state", Description =
        "Play Mode only. JSON snapshot of the running game: load status, entropy/net worth/prestige, " +
        "tutorial step id + hint, open HUD panels, dialogue, placement/conveyor/deconstruct modes, " +
        "research (unlocked, purchasable now, in progress), buildings on the active grid with crafting " +
        "and power status, power supply/draw, inventory. Read it every turn alongside a screenshot.")]
    public static class PlaytestStateTool
    {
        public class Parameters
        {
            [ToolParameter("Also list the visible UI elements (same as playtest_ui list).", Required = false, DefaultValue = "false")]
            public bool include_ui { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            var snapshot = PlaytestState.Capture();
            if (@params?["include_ui"]?.Value<bool>() == true)
                return new SuccessResponse("Game state captured.", new { state = snapshot, ui = PlaytestUi.List() });
            return new SuccessResponse("Game state captured.", snapshot);
        }
    }

    [McpForUnityTool("playtest_ui", Description =
        "Play Mode only. Lists what is visible on the HUD: buttons, toggles, fields and text, each with " +
        "name, text, enabled flag, owning panel and screen rect/center in screenshot pixels (top-left origin). " +
        "Hidden panels and rows scrolled out of view are excluded. action=list (optional filter, " +
        "interactive_only) or action=find (target = element name or visible text, index for duplicates).")]
    public static class PlaytestUiTool
    {
        public class Parameters
        {
            [ToolParameter("list | find", Required = false, DefaultValue = "list")]
            public string action { get; set; }
            [ToolParameter("list: only elements whose name, text or panel contains this.", Required = false)]
            public string filter { get; set; }
            [ToolParameter("list: drop plain text, keep buttons/toggles/fields/sliders.", Required = false, DefaultValue = "false")]
            public bool interactive_only { get; set; }
            [ToolParameter("find: element name, or its visible text (exact, then contains).", Required = false)]
            public string target { get; set; }
            [ToolParameter("find: which match to return when several share the text.", Required = false, DefaultValue = "0")]
            public int index { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            string action = @params?["action"]?.ToString() ?? "list";
            if (action == "find")
            {
                string target = @params?["target"]?.ToString();
                if (string.IsNullOrEmpty(target)) return new ErrorResponse("find needs 'target'.");
                var found = PlaytestUi.Find(target, @params?["index"]?.Value<int>() ?? 0, out var findError);
                return found == null ? new ErrorResponse(findError) : new SuccessResponse($"Found '{target}'.", found);
            }
            var listing = PlaytestUi.List(@params?["filter"]?.ToString(),
                                          @params?["interactive_only"]?.Value<bool>() ?? false);
            return new SuccessResponse($"{listing.elements.Count} visible element(s).", listing);
        }
    }

    [McpForUnityTool("playtest_input", Description =
        "Play Mode only. Real player input through a virtual Input System mouse, so HUD and world react exactly " +
        "as to a finger. Waits until the gesture has finished plus settle_ms, then returns a short state summary. " +
        "action=tap (x,y) | tap_element (target = name or visible text, index) | tap_cell (cell_x, cell_y: grid " +
        "cell on the active site) | drag (x,y -> to_x,to_y; pans the camera, scrolls lists) | drag_cells " +
        "(cell_x,cell_y -> to_cell_x,to_cell_y; e.g. painting conveyors). Coordinates are screenshot pixels, top-left origin.")]
    public static class PlaytestInputTool
    {
        public class Parameters
        {
            [ToolParameter("tap | tap_element | tap_cell | drag | drag_cells")]
            public string action { get; set; }
            [ToolParameter("Screen x in screenshot pixels.", Required = false)]
            public float x { get; set; }
            [ToolParameter("Screen y in screenshot pixels (top-left origin).", Required = false)]
            public float y { get; set; }
            [ToolParameter("drag: end x.", Required = false)]
            public float to_x { get; set; }
            [ToolParameter("drag: end y.", Required = false)]
            public float to_y { get; set; }
            [ToolParameter("tap_element: element name or visible text.", Required = false)]
            public string target { get; set; }
            [ToolParameter("tap_element: which match when several share the text.", Required = false, DefaultValue = "0")]
            public int index { get; set; }
            [ToolParameter("tap_cell/drag_cells: grid column.", Required = false)]
            public int cell_x { get; set; }
            [ToolParameter("tap_cell/drag_cells: grid row.", Required = false)]
            public int cell_y { get; set; }
            [ToolParameter("drag_cells: end column.", Required = false)]
            public int to_cell_x { get; set; }
            [ToolParameter("drag_cells: end row.", Required = false)]
            public int to_cell_y { get; set; }
            [ToolParameter("drag: frames spent moving (more = slower drag).", Required = false, DefaultValue = "20")]
            public int frames { get; set; }
            [ToolParameter("Milliseconds to wait after the gesture before reporting (animations, panel slides).", Required = false, DefaultValue = "400")]
            public int settle_ms { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            var driver = PlaytestDriver.Ensure();
            if (driver.IsBusy) return new ErrorResponse("A previous gesture is still running; retry shortly.");

            string action = @params?["action"]?.ToString();
            int frames    = Math.Max(2, @params?["frames"]?.Value<int>() ?? 20);
            int settleMs  = Mathf.Clamp(@params?["settle_ms"]?.Value<int>() ?? 400, 0, 10000);
            var done      = new TaskCompletionSource<bool>();
            object detail;

            switch (action)
            {
                case "tap":
                {
                    var p = ReadPoint(@params, "x", "y");
                    driver.Tap(p, () => done.TrySetResult(true));
                    detail = new { tapped = new { p.x, p.y }, overUi = PlaytestToolUtil.OverUi(p) };
                    break;
                }
                case "tap_element":
                {
                    string target = @params?["target"]?.ToString();
                    if (string.IsNullOrEmpty(target)) return new ErrorResponse("tap_element needs 'target'.");
                    var el = PlaytestUi.Find(target, @params?["index"]?.Value<int>() ?? 0, out var findError);
                    if (el == null) return new ErrorResponse(findError);
                    if (!el.enabled) return new ErrorResponse($"'{target}' is visible but disabled.", el);
                    driver.Tap(new Vector2(el.centerX, el.centerY), () => done.TrySetResult(true));
                    detail = new { tapped = el };
                    break;
                }
                case "tap_cell":
                {
                    if (!PlaytestToolUtil.CellToScreen(ReadCell(@params, "cell_x", "cell_y"), out var p, out var cellError))
                        return new ErrorResponse(cellError);
                    driver.Tap(p, () => done.TrySetResult(true));
                    detail = new { cell = new { x = @params["cell_x"], y = @params["cell_y"] }, screen = new { p.x, p.y },
                                   overUi = PlaytestToolUtil.OverUi(p) };
                    break;
                }
                case "drag":
                {
                    var a = ReadPoint(@params, "x", "y");
                    var b = ReadPoint(@params, "to_x", "to_y");
                    driver.Drag(a, b, frames, () => done.TrySetResult(true));
                    detail = new { from = new { a.x, a.y }, to = new { b.x, b.y }, startOverUi = PlaytestToolUtil.OverUi(a) };
                    break;
                }
                case "drag_cells":
                {
                    if (!PlaytestToolUtil.CellToScreen(ReadCell(@params, "cell_x", "cell_y"), out var a, out var e1))
                        return new ErrorResponse(e1);
                    if (!PlaytestToolUtil.CellToScreen(ReadCell(@params, "to_cell_x", "to_cell_y"), out var b, out var e2))
                        return new ErrorResponse(e2);
                    driver.Drag(a, b, frames, () => done.TrySetResult(true));
                    detail = new { from = new { a.x, a.y }, to = new { b.x, b.y } };
                    break;
                }
                default:
                    return new ErrorResponse("action must be tap, tap_element, tap_cell, drag or drag_cells.");
            }

            // The last state event is applied on the frame after it is queued.
            if (!await PlaytestToolUtil.WaitFor(() => done.Task.IsCompleted, 10f))
                return new ErrorResponse("Gesture did not finish within 10s (is the game paused?).", detail);
            await PlaytestToolUtil.WaitFrames(2);
            if (settleMs > 0) await Task.Delay(Math.Min(settleMs, 10000));

            return new SuccessResponse($"{action} done.", new { input = detail, after = PlaytestToolUtil.Brief() });
        }

        private static Vector2 ReadPoint(JObject p, string xKey, string yKey) =>
            new(p?[xKey]?.Value<float>() ?? 0f, p?[yKey]?.Value<float>() ?? 0f);

        private static Vector2Int ReadCell(JObject p, string xKey, string yKey) =>
            new(p?[xKey]?.Value<int>() ?? 0, p?[yKey]?.Value<int>() ?? 0);
    }

    [McpForUnityTool("playtest_screenshot", Description =
        "Play Mode only. Captures exactly what the player sees (3D scene + HUD) and saves a PNG under " +
        "Playtests/reports/screenshots. Open it with the Read tool. The image is downscaled to max_resolution; " +
        "multiply image pixel coordinates by 'scale' to get the screen pixels that playtest_input and playtest_ui use.")]
    public static class PlaytestScreenshotTool
    {
        public class Parameters
        {
            [ToolParameter("Longest edge of the saved image in pixels.", Required = false, DefaultValue = "1280")]
            public int max_resolution { get; set; }
            [ToolParameter("Optional file name prefix, e.g. a finding id.", Required = false)]
            public string label { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            int maxRes = Mathf.Clamp(@params?["max_resolution"]?.Value<int>() ?? 1280, 256, 4096);

            Texture2D full = null;
            bool done = false;
            PlaytestDriver.Ensure().CaptureScreen(tex => { full = tex; done = true; });
            if (!await PlaytestToolUtil.WaitFor(() => done, 10f) || full == null)
                return new ErrorResponse("Screen capture did not complete. Is the Game View visible (not minimized or behind another Editor tab)?");

            var image = PlaytestToolUtil.Downscale(full, maxRes);
            float scale = (float)full.width / image.width;
            string label = PlaytestToolUtil.IsValidName(@params?["label"]?.ToString()?.ToLowerInvariant())
                ? @params["label"].ToString().ToLowerInvariant() + "-" : "";
            string dir  = Path.Combine(PlaytestToolUtil.ProjectRoot, "Playtests", "reports", "screenshots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{label}{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            File.WriteAllBytes(path, image.EncodeToPNG());

            var result = new { path, imageWidth = image.width, imageHeight = image.height,
                               screenWidth = full.width, screenHeight = full.height, scale };
            if (image != full) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(full);
            return new SuccessResponse("Screenshot saved; open it with the Read tool.", result);
        }
    }

    [McpForUnityTool("playtest_control", Description =
        "Play Mode only. Session control for AI playtests. action=status | time_scale (value 0.1-50; real-time " +
        "timers such as research do NOT speed up) | wait (seconds of real time, max 25, then a state summary) | " +
        "console (command: a dev-console command, e.g. 'tutorial skip place_sfc'; cheats taint the run, note them " +
        "in the report) | list_fixtures | load_fixture (name: Playtests/fixtures/<name>.json; reloads the scene) | " +
        "save_fixture (name: captures the current state into Playtests/fixtures) | fresh_start (wipes the LOCAL " +
        "save for a first-time-player run). Loading or wiping first backs up the current save once per day as " +
        "dev snapshot 'pre_playtest_<yyyymmdd>'.")]
    public static class PlaytestControlTool
    {
        // Commands that reach UGS Cloud Save; a playtest must never delete the developer's cloud data.
        private static readonly string[] BlockedCommands = { "clear cloud save", "clear all save" };

        public class Parameters
        {
            [ToolParameter("status | time_scale | wait | console | list_fixtures | load_fixture | save_fixture | fresh_start")]
            public string action { get; set; }
            [ToolParameter("time_scale: speed multiplier.", Required = false)]
            public float value { get; set; }
            [ToolParameter("wait: real-time seconds.", Required = false)]
            public float seconds { get; set; }
            [ToolParameter("console: the dev-console command line.", Required = false)]
            public string command { get; set; }
            [ToolParameter("load_fixture/save_fixture: fixture name (letters, digits, - or _).", Required = false)]
            public string name { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            string action = @params?["action"]?.ToString();
            if (action == "list_fixtures")
                return new SuccessResponse("Fixtures.", new { directory = PlaytestToolUtil.FixtureDir, fixtures = PlaytestToolUtil.ListFixtures() });

            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            var driver = PlaytestDriver.Ensure();

            switch (action)
            {
                case "status":
                    return new SuccessResponse("Status.", PlaytestToolUtil.Brief());

                case "time_scale":
                {
                    float value = @params?["value"]?.Value<float>() ?? 1f;
                    driver.SetTimeScale(value);
                    return new SuccessResponse($"Playtest time scale {driver.TimeScale}x.",
                        new { timeScale = driver.TimeScale, note = "Research and other wall-clock timers are unaffected." });
                }

                case "wait":
                {
                    float seconds = Mathf.Clamp(@params?["seconds"]?.Value<float>() ?? 1f, 0f, PlaytestToolUtil.MaxWaitSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    return new SuccessResponse($"Waited {seconds}s.", PlaytestToolUtil.Brief());
                }

                case "console":
                {
                    string command = @params?["command"]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(command)) return new ErrorResponse("console needs 'command'.");
                    if (BlockedCommands.Any(b => command.StartsWith(b, StringComparison.OrdinalIgnoreCase)))
                        return new ErrorResponse($"'{command}' touches cloud save and is blocked for playtests. Use fresh_start.");
                    return await RunConsole(command);
                }

                case "save_fixture":
                {
                    string name = @params?["name"]?.ToString()?.ToLowerInvariant();
                    if (!PlaytestToolUtil.IsValidName(name)) return new ErrorResponse("name must be letters, digits, '-' or '_'.");
                    string result = DevConsoleController.ExecuteCommand($"snapshot save {name}");
                    if (result == null || result.StartsWith("Error")) return new ErrorResponse(result ?? "Dev console not ready.");
                    Directory.CreateDirectory(PlaytestToolUtil.FixtureDir);
                    string dest = PlaytestToolUtil.FixturePath(name);
                    File.Copy(PlaytestToolUtil.SnapshotPath(name), dest, overwrite: true);
                    return new SuccessResponse($"Saved fixture '{name}'.", new { path = dest, console = result });
                }

                case "load_fixture":
                {
                    string name = @params?["name"]?.ToString()?.ToLowerInvariant();
                    if (!PlaytestToolUtil.IsValidName(name)) return new ErrorResponse("name must be letters, digits, '-' or '_'.");
                    string src = PlaytestToolUtil.FixturePath(name);
                    if (!File.Exists(src))
                        return new ErrorResponse($"No fixture '{name}'.", new { available = PlaytestToolUtil.ListFixtures() });
                    PlaytestToolUtil.BackupOnce();
                    Directory.CreateDirectory(Path.GetDirectoryName(PlaytestToolUtil.SnapshotPath(name)));
                    File.Copy(src, PlaytestToolUtil.SnapshotPath(name), overwrite: true);
                    return await RunConsole($"snapshot load {name}", expectReload: true);
                }

                case "fresh_start":
                    PlaytestToolUtil.BackupOnce();
                    return await RunConsole("clear save", expectReload: true);

                default:
                    return new ErrorResponse("Unknown action. See the tool description.");
            }
        }

        private static async Task<object> RunConsole(string command, bool expectReload = false)
        {
            var previousBridge = ECSLoadBridge.Instance;
            string result = DevConsoleController.ExecuteCommand(command);
            if (result == null) return new ErrorResponse("Dev console is not ready yet (it starts with the scene).");
            if (result.StartsWith("Error") || result.StartsWith("Unknown")) return new ErrorResponse(result);

            expectReload |= SceneLoader.IsTransitioning;
            if (expectReload)
            {
                bool reloaded = await PlaytestToolUtil.WaitFor(() =>
                    !SceneLoader.IsTransitioning &&
                    ECSLoadBridge.Instance != null && ECSLoadBridge.Instance != previousBridge &&
                    ECSLoadBridge.Instance.IsLoaded, PlaytestToolUtil.MaxWaitSeconds);
                if (!reloaded)
                    return new SuccessResponse($"{result} Still reloading; poll playtest_control status until loaded is true.",
                        new { reloading = true });
            }
            return new SuccessResponse(result, new { after = PlaytestToolUtil.Brief() });
        }
    }

    internal static class PlaytestToolUtil
    {
        // MCP for Unity cancels a command after 30s by default; stay under it.
        public const float MaxWaitSeconds = 25f;

        public static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        public static string FixtureDir  => Path.Combine(ProjectRoot, "Playtests", "fixtures");
        public static string FixturePath(string name) => Path.Combine(FixtureDir, name + ".json");
        // Must match DevConsoleController.SnapshotDir.
        public static string SnapshotPath(string name) =>
            Path.Combine(Application.persistentDataPath, "snapshots", name + ".json");

        public static bool IsValidName(string name) =>
            !string.IsNullOrEmpty(name) && System.Text.RegularExpressions.Regex.IsMatch(name, "^[a-z0-9_-]+$");

        public static string[] ListFixtures() =>
            Directory.Exists(FixtureDir)
                ? Directory.GetFiles(FixtureDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToArray()
                : Array.Empty<string>();

        public static bool RequirePlayMode(out object error)
        {
            error = EditorApplication.isPlaying
                ? null
                : new ErrorResponse("Not in Play Mode. Start it with manage_editor (action play) first.");
            return error == null;
        }

        /// <summary>Saves the developer's current state once per day before a playtest overwrites it.</summary>
        public static void BackupOnce()
        {
            string name = $"pre_playtest_{DateTime.Now:yyyyMMdd}";
            if (!File.Exists(SnapshotPath(name)))
                DevConsoleController.ExecuteCommand($"snapshot save {name}");
        }

        /// <summary>Screen point (top-left origin) of a grid cell's centre on the active site.</summary>
        public static bool CellToScreen(Vector2Int cell, out Vector2 screen, out string error)
        {
            screen = default;
            var grid = UnityEngine.Object.FindAnyObjectByType<GridRenderer>();
            var cam  = Camera.main;
            if (grid == null || cam == null) { error = "No GridRenderer or main camera in the scene."; return false; }
            if (cell.x < 0 || cell.y < 0 || cell.x >= grid.Width || cell.y >= grid.Height)
            {
                error = $"Cell ({cell.x},{cell.y}) is outside the {grid.Width}x{grid.Height} grid.";
                return false;
            }

            // Tiles are centred at cell * cellSize on the Y=0 plane (GridRenderer.WorldToCell).
            var world = new Vector3(cell.x * grid.CellSize, 0f, cell.y * grid.CellSize);
            var sp    = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || sp.x < 0f || sp.y < 0f || sp.x > Screen.width || sp.y > Screen.height)
            {
                error = $"Cell ({cell.x},{cell.y}) is off screen; drag to pan the camera first.";
                return false;
            }
            screen = PlaytestDriver.ToTopLeft(new Vector2(sp.x, sp.y));
            error  = null;
            return true;
        }

        public static bool OverUi(Vector2 screenTopLeft) =>
            UIInputBlocker.IsPointerOverUI(PlaytestDriver.ToInputSpace(screenTopLeft));

        /// <summary>Returns <paramref name="source"/> itself when it already fits.</summary>
        public static Texture2D Downscale(Texture2D source, int maxEdge)
        {
            int longest = Mathf.Max(source.width, source.height);
            if (longest <= maxEdge) return source;
            float f = (float)maxEdge / longest;
            int w = Mathf.Max(1, Mathf.RoundToInt(source.width * f));
            int h = Mathf.Max(1, Mathf.RoundToInt(source.height * f));

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            small.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return small;
        }

        public static object Brief()
        {
            var s = PlaytestState.Capture();
            return new
            {
                editorPaused = EditorApplication.isPaused,
                s.loaded,
                s.timeScale,
                entropy       = s.economy?.entropy,
                tutorialStep  = s.tutorial?.stepId,
                tutorialHint  = s.tutorial?.hintText,
                openPanels    = s.ui?.openPanels,
                dialogue      = s.ui?.dialoguePlaying,
                placing       = s.ui?.placingBuilding,
                candidateValid = s.ui?.placementCandidateValid,
                conveyorMode  = s.ui?.conveyorMode,
                buildings     = s.grid?.buildingCount,
                activeResearch = s.research?.activeId,
            };
        }

        public static Task WaitFrames(int frames)
        {
            var tcs = new TaskCompletionSource<bool>();
            int remaining = frames;
            void Tick()
            {
                if (--remaining > 0) return;
                EditorApplication.update -= Tick;
                tcs.TrySetResult(true);
            }
            EditorApplication.update += Tick;
            return tcs.Task;
        }

        public static Task<bool> WaitFor(Func<bool> condition, float timeoutSeconds)
        {
            var tcs = new TaskCompletionSource<bool>();
            double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            void Tick()
            {
                bool ok;
                try { ok = condition(); } catch { ok = false; }
                if (!ok && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= Tick;
                tcs.TrySetResult(ok);
            }
            EditorApplication.update += Tick;
            return tcs.Task;
        }
    }

    /// <summary>
    /// Keeps Play Mode running while the Editor is in the background (the agent's terminal holds
    /// focus during a playtest). Editor-only; the player setting stays off for device builds.
    /// </summary>
    [InitializeOnLoad]
    internal static class PlaytestPlayModeHook
    {
        static PlaytestPlayModeHook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Application.runInBackground = true;
            };
        }
    }
}
