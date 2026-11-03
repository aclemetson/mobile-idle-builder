using System;
using System.Collections.Generic;
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
        "(cell_x,cell_y -> to_cell_x,to_cell_y; e.g. painting conveyors) | collect (target = field id such as " +
        "quark_field or electron_field, count = taps: pans to the field if off screen, then taps its tiles in turn as " +
        "each comes off cooldown, waiting out cooldowns; returns items gained and missed taps). Coordinates are " +
        "screen pixels, top-left origin. The returned 'after' lists only fields that changed since the last call.")]
    public static class PlaytestInputTool
    {
        public class Parameters
        {
            [ToolParameter("tap | tap_element | tap_cell | drag | drag_cells | collect")]
            public string action { get; set; }
            [ToolParameter("collect: number of taps (max 40).", Required = false, DefaultValue = "10")]
            public int count { get; set; }
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
            if (action == "collect") return await Collect(@params, driver);
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
            PlaytestBenchmark.Active?.CountAction();

            // Turn-based: each action costs the player a fixed amount of game time; frozen otherwise.
            if (driver.TurnBased) await PlaytestToolUtil.RunGame(driver, driver.ActionSeconds);
            else if (settleMs > 0) await Task.Delay(Math.Min(settleMs, 10000));

            return new SuccessResponse($"{action} done.", new { input = detail, after = PlaytestToolUtil.BriefDelta() });
        }

        // Collecting from a field is the most repeated thing a player does; one call here replaces
        // a pan plus a tap per item. Taps are real input; only the pan is a camera move (the same
        // place a swipe would leave it), counted as one action.
        private static async Task<object> Collect(JObject @params, PlaytestDriver driver)
        {
            string target = @params?["target"]?.ToString();
            int count     = Mathf.Clamp(@params?["count"]?.Value<int>() ?? 10, 1, 40);
            var fields    = PlaytestState.Fields().FindAll(f => f.Field.id == target || f.Field.fieldType == target);
            if (fields.Count == 0)
                return new ErrorResponse($"No field '{target}' on this site.",
                    new { available = PlaytestState.Fields().Select(f => f.Field.id).Distinct().ToArray() });

            var grid = UnityEngine.Object.FindAnyObjectByType<GridRenderer>();
            var cam  = UnityEngine.Object.FindAnyObjectByType<CameraController>();
            // Tappable = on screen and not under HUD chrome. A cell under the drawer handle or an open panel
            // counted as on screen, so collect skipped the pan and tapped the UI instead (#261: it opened Worlds).
            bool OnScreen(FieldInstance f) =>
                PlaytestToolUtil.CellToScreen(CellOf(f, grid), out var sp, out _) && !PlaytestToolUtil.OverUi(sp);

            // Fast-forward the macro's waits; player time is the same at any time scale.
            float scaleBefore = driver.TimeScale;
            driver.SetTimeScale(Mathf.Max(scaleBefore, 10f));
            var start  = Time.realtimeSinceStartup;
            var before = Inventory();
            int taps = 0, hits = 0, missed = 0, cooldownWaits = 0;
            bool panned = false, blocked = false;

            try
            {
                if (!fields.TrueForAll(OnScreen) && cam != null)
                {
                    var centre = fields.Aggregate(Vector3.zero, (sum, f) => sum + f.transform.position) / fields.Count;
                    cam.PanTo(centre);
                    await PlaytestToolUtil.WaitFor(() => fields.TrueForAll(OnScreen), 3f);
                    cam.ResumeFollow();
                    panned = true;
                    PlaytestBenchmark.Active?.CountAction();
                    if (driver.TurnBased) await PlaytestToolUtil.RunGame(driver, driver.ActionSeconds);
                }

                int next = 0;
                while (taps < count && Time.realtimeSinceStartup - start < PlaytestToolUtil.MaxWaitSeconds - 3f)
                {
                    if (!fields.Exists(OnScreen)) { blocked = true; break; }   // camera moved away, or UI covers it
                    var ready = fields.Where(f => PlaytestState.CooldownLeft(f) <= 0f && OnScreen(f)).ToList();
                    if (ready.Count == 0)
                    {
                        float wait = fields.Min(f => PlaytestState.CooldownLeft(f)) + 0.05f;
                        cooldownWaits++;
                        if (driver.TurnBased) await PlaytestToolUtil.RunGame(driver, wait);
                        else await Task.Delay(TimeSpan.FromSeconds(wait / driver.TimeScale));
                        continue;
                    }

                    var field = ready[next++ % ready.Count];
                    if (!PlaytestToolUtil.CellToScreen(CellOf(field, grid), out var p, out _)) break;
                    bool tapped = false;
                    driver.Tap(p, () => tapped = true);
                    await PlaytestToolUtil.WaitFor(() => tapped, 5f);
                    await PlaytestToolUtil.WaitFrames(2);
                    taps++;
                    if (PlaytestState.CooldownLeft(field) > 0f) hits++; else missed++;
                    PlaytestBenchmark.Active?.CountAction();
                    if (driver.TurnBased) await PlaytestToolUtil.RunGame(driver, driver.ActionSeconds);
                }
            }
            finally
            {
                driver.SetTimeScale(scaleBefore);
            }

            var after  = Inventory();
            var gained = after.Where(kv => kv.Value > (before.TryGetValue(kv.Key, out int b) ? b : 0))
                              .ToDictionary(kv => kv.Key, kv => kv.Value - (before.TryGetValue(kv.Key, out int b) ? b : 0));
            string message = blocked
                ? $"Stopped after {taps} taps: no '{target}' tile is on screen clear of UI. Close open panels or the " +
                  "drawer, or drag the map, then call again. Not a game bug by itself."
                : taps < count
                    ? $"Collected with {taps} of {count} taps before the call limit; call again for the rest."
                    : $"Collected with {taps} taps.";
            return new SuccessResponse(message,
                new { taps, hits, missed, cooldownWaits, panned, blocked, gained, after = PlaytestToolUtil.BriefDelta() });
        }

        private static Vector2Int CellOf(FieldInstance f, GridRenderer grid) =>
            grid != null ? GridRenderer.WorldToCell(f.transform.position, grid.CellSize) : Vector2Int.zero;

        private static Dictionary<string, int> Inventory() =>
            (PlaytestState.Capture().inventory ?? new List<PlaytestState.InventoryEntry>())
                .GroupBy(i => i.id).ToDictionary(g => g.Key, g => g.Sum(i => i.quantity));

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
            [ToolParameter("Longest edge of the saved image in pixels.", Required = false, DefaultValue = "800")]
            public int max_resolution { get; set; }
            [ToolParameter("Optional file name prefix, e.g. a finding id.", Required = false)]
            public string label { get; set; }
        }

        public static async Task<object> HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            if (EditorApplication.isPaused) EditorApplication.isPaused = false;
            int maxRes = Mathf.Clamp(@params?["max_resolution"]?.Value<int>() ?? 800, 256, 4096);

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
        "Play Mode only. Session control for AI playtests. action=status | time_scale (value 0.1-50; game time " +
        "and research/cooldown timers run that much faster) | clock (mode turn_based or realtime; action_seconds = " +
        "player time each input costs in turn_based) | wait (seconds of PLAYER time with the app open; in realtime " +
        "mode capped at 25 s real, in turn_based the game runs that long at time_scale, up to 25 s real per call) | " +
        "away (seconds: the player closes the app that long; offline earnings, timers, welcome-back modal) | " +
        "console (command: a dev-console command, e.g. 'tutorial skip place_sfc' or 'iap grant 4'; cheats taint " +
        "the run, note them) | list_fixtures | load_fixture (name; reloads the scene) | save_fixture (name) | " +
        "fresh_start (scope: local = local save only; cloud = local + UGS cloud save; all = cloud + settings + " +
        "recipe knowledge + all prefs, a true first launch). Loading or wiping first backs up the current save " +
        "once per day as dev snapshot 'pre_playtest_<yyyymmdd>'.")]
    public static class PlaytestControlTool
    {
        // Cloud-deleting commands only run through fresh_start, which backs up the save first.
        private static readonly string[] BlockedCommands = { "clear cloud save", "clear all save" };

        public class Parameters
        {
            [ToolParameter("status | time_scale | clock | wait | away | console | list_fixtures | load_fixture | save_fixture | fresh_start")]
            public string action { get; set; }
            [ToolParameter("time_scale: speed multiplier.", Required = false)]
            public float value { get; set; }
            [ToolParameter("wait/away: player seconds.", Required = false)]
            public float seconds { get; set; }
            [ToolParameter("clock: turn_based | realtime.", Required = false)]
            public string mode { get; set; }
            [ToolParameter("clock: player seconds each input costs in turn_based mode.", Required = false, DefaultValue = "2")]
            public float action_seconds { get; set; }
            [ToolParameter("fresh_start: local | cloud | all.", Required = false, DefaultValue = "local")]
            public string scope { get; set; }
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
                    return new SuccessResponse("Status.", PlaytestToolUtil.BriefFull());

                case "time_scale":
                {
                    float value = @params?["value"]?.Value<float>() ?? 1f;
                    driver.SetTimeScale(value);
                    return new SuccessResponse($"Playtest time scale {driver.TimeScale}x.",
                        new { timeScale = driver.TimeScale, note = "Above 10x, fast crafts can lose output per frame; keep benchmark runs at 10x or less." });
                }

                case "clock":
                {
                    string mode = @params?["mode"]?.ToString() ?? "turn_based";
                    if (mode != "turn_based" && mode != "realtime") return new ErrorResponse("mode must be turn_based or realtime.");
                    driver.SetTurnBased(mode == "turn_based");
                    if (@params?["action_seconds"] != null)
                        driver.ActionSeconds = Mathf.Clamp(@params["action_seconds"].Value<float>(), 0f, 60f);
                    return new SuccessResponse($"Clock {mode}.", new { mode, driver.ActionSeconds, after = PlaytestToolUtil.BriefDelta() });
                }

                case "wait":
                {
                    float seconds = Mathf.Max(0f, @params?["seconds"]?.Value<float>() ?? 1f);
                    if (!driver.TurnBased)
                    {
                        float real = Mathf.Min(seconds, PlaytestToolUtil.MaxWaitSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(real));
                        return new SuccessResponse($"Waited {real}s.", PlaytestToolUtil.BriefDelta());
                    }
                    double before = driver.PlayerSeconds;
                    bool finished = await PlaytestToolUtil.RunGame(driver, seconds);
                    double ran = driver.PlayerSeconds - before;
                    return new SuccessResponse(finished
                            ? $"Ran {ran:0}s of player time."
                            : $"Ran {ran:0}s of {seconds:0}s before the 25 s call limit; call wait again for the rest (or raise time_scale).",
                        new { playerSecondsRan = Math.Round(ran, 1), finished, after = PlaytestToolUtil.BriefDelta() });
                }

                case "away":
                {
                    float seconds = @params?["seconds"]?.Value<float>() ?? 0f;
                    if (seconds < 60f) return new ErrorResponse("away needs seconds >= 60 (offline earnings ignore shorter absences).");
                    return await RunConsole($"away {seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
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
                {
                    string command = (@params?["scope"]?.ToString() ?? "local") switch
                    {
                        "local" => "clear save",
                        "cloud" => "clear cloud save",
                        "all"   => "clear all save",
                        _       => null,
                    };
                    if (command == null) return new ErrorResponse("scope must be local, cloud or all.");
                    PlaytestToolUtil.BackupOnce();
                    // A new game starts on the wall clock; earlier freezes or absences no longer apply.
                    GameClock.DevReset();
                    return await RunConsole(command, expectReload: true);
                }

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
            return new SuccessResponse(result, new { after = PlaytestToolUtil.BriefDelta() });
        }
    }

    [McpForUnityTool("playtest_benchmark", Description =
        "Play Mode only. Times a scenario run in PLAYER time (game time let through + absences, not agent time). " +
        "action=start (scenario: brief name; notes) right after setup | mark (note: a moment worth timing) | note (note, severity S1-S3, screenshot path: a finding for the review pass) | status (totals + recent milestones) | end (outcome: reached_goal | time_box | " +
        "stuck | crashed; writes the summary). Auto-detected: tutorial steps, research started/done, first " +
        "building of each type, building totals, entropy and net worth by power of ten, tier, prestige, sites, " +
        "worlds, crystals spent, purchases, absences. Files: Playtests/benchmarks/<scenario>/<run>.jsonl + .json.")]
    public static class PlaytestBenchmarkTool
    {
        public class Parameters
        {
            [ToolParameter("start | mark | note | status | end")]
            public string action { get; set; }
            [ToolParameter("start: scenario name (the brief's file name).", Required = false)]
            public string scenario { get; set; }
            [ToolParameter("start: free-text notes; mark: what happened.", Required = false)]
            public string notes { get; set; }
            [ToolParameter("mark: short label for the moment; note: the finding.", Required = false)]
            public string note { get; set; }
            [ToolParameter("note: S1 (blocks progress) | S2 (confusing, player may quit) | S3 (polish).", Required = false, DefaultValue = "S3")]
            public string severity { get; set; }
            [ToolParameter("note: path of the playtest_screenshot that shows it.", Required = false)]
            public string screenshot { get; set; }
            [ToolParameter("end: reached_goal | time_box | stuck | crashed.", Required = false)]
            public string outcome { get; set; }
        }

        private static JObject s_Meta;

        public static object HandleCommand(JObject @params)
        {
            if (!PlaytestToolUtil.RequirePlayMode(out var error)) return error;
            var driver = PlaytestDriver.Ensure();
            var bench  = PlaytestBenchmark.Active;
            string action = @params?["action"]?.ToString();

            switch (action)
            {
                case "start":
                {
                    string scenario = @params?["scenario"]?.ToString()?.ToLowerInvariant();
                    if (!PlaytestToolUtil.IsValidName(scenario)) return new ErrorResponse("scenario must be letters, digits, '-' or '_'.");
                    string runId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    string dir   = Path.Combine(PlaytestToolUtil.ProjectRoot, "Playtests", "benchmarks", scenario);
                    bench = PlaytestBenchmark.Start(scenario, runId, dir);
                    s_Meta = new JObject
                    {
                        ["scenario"]      = scenario,
                        ["runId"]         = runId,
                        ["startedLocal"]  = DateTime.Now.ToString("s"),
                        ["branch"]        = PlaytestToolUtil.Git("rev-parse --abbrev-ref HEAD"),
                        ["commit"]        = PlaytestToolUtil.Git("rev-parse --short HEAD"),
                        ["appVersion"]    = Application.version,
                        ["screen"]        = $"{Screen.width}x{Screen.height}",
                        ["clock"]         = driver.TurnBased ? "turn_based" : "realtime",
                        ["actionSeconds"] = driver.ActionSeconds,
                        ["timeScale"]     = driver.TimeScale,
                        ["notes"]         = @params?["notes"]?.ToString(),
                    };
                    if (!driver.TurnBased)
                        return new SuccessResponse($"Benchmark '{scenario}' started, but the clock is realtime: agent thinking time will count as player time. Use playtest_control clock turn_based for timings.", s_Meta);
                    return new SuccessResponse($"Benchmark '{scenario}' started.", s_Meta);
                }

                case "mark":
                {
                    if (bench == null) return new ErrorResponse("No benchmark running; start one first.");
                    string note = @params?["note"]?.ToString() ?? @params?["notes"]?.ToString();
                    if (string.IsNullOrWhiteSpace(note)) return new ErrorResponse("mark needs 'note'.");
                    bench.Sample();
                    bench.Record("mark", note);
                    return new SuccessResponse($"Marked at {PlaytestBenchmark.FormatDuration(bench.PlayerSeconds)} player time.");
                }

                case "note":
                {
                    // A finding for the review pass: what was seen, how bad, and the screenshot that shows it.
                    if (bench == null) return new ErrorResponse("No benchmark running; start one first.");
                    string text = @params?["note"]?.ToString() ?? @params?["notes"]?.ToString();
                    if (string.IsNullOrWhiteSpace(text)) return new ErrorResponse("note needs 'note'.");
                    string severity   = @params?["severity"]?.ToString() ?? "S3";
                    string screenshot = @params?["screenshot"]?.ToString();
                    bench.Sample();
                    bench.Record("finding", $"[{severity}] {text}" + (string.IsNullOrEmpty(screenshot) ? "" : $" | {screenshot}"));
                    return new SuccessResponse("Noted.");
                }

                case "status":
                {
                    if (bench == null) return new ErrorResponse("No benchmark running.");
                    bench.Sample();
                    var recent = bench.Milestones.Skip(Math.Max(0, bench.Milestones.Count - 12))
                        .Select(m => new { m.type, m.detail, at = PlaytestBenchmark.FormatDuration(m.playerSeconds) });
                    return new SuccessResponse("Benchmark status.", new
                    {
                        bench.Scenario, bench.RunId,
                        playerTime = PlaytestBenchmark.FormatDuration(bench.PlayerSeconds),
                        bench.Actions, bench.DollarsSpent, bench.CrystalsBought,
                        milestones = bench.Milestones.Count, recent,
                    });
                }

                case "end":
                {
                    if (bench == null) return new ErrorResponse("No benchmark running.");
                    bench.Sample();
                    string outcome = @params?["outcome"]?.ToString() ?? "unspecified";
                    bench.Record("run_end", outcome);

                    var summary = s_Meta != null ? (JObject)s_Meta.DeepClone() : new JObject { ["scenario"] = bench.Scenario, ["runId"] = bench.RunId };
                    summary["outcome"]        = outcome;
                    summary["playerSeconds"]  = Math.Round(bench.PlayerSeconds, 1);
                    summary["actions"]        = bench.Actions;
                    summary["dollarsSpent"]   = bench.DollarsSpent;
                    summary["crystalsBought"] = bench.CrystalsBought;
                    summary["milestones"]     = JArray.FromObject(bench.Milestones);
                    string path = Path.ChangeExtension(bench.EventsPath, ".json");
                    File.WriteAllText(path, summary.ToString(Newtonsoft.Json.Formatting.Indented));
                    PlaytestBenchmark.Stop();

                    var table = bench.Milestones.Select(m => new { at = PlaytestBenchmark.FormatDuration(m.playerSeconds), m.type, m.detail, m.dollars });
                    return new SuccessResponse($"Benchmark ended ({outcome}) after {PlaytestBenchmark.FormatDuration(bench.PlayerSeconds)} player time.",
                        new { summaryPath = path, eventsPath = bench.EventsPath, milestones = table });
                }

                default:
                    return new ErrorResponse("action must be start, mark, status or end.");
            }
        }
    }

    internal static class PlaytestToolUtil
    {
        /// <summary>Runs a git command in the project and returns its first output line, or null.</summary>
        public static string Git(string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", args)
                {
                    WorkingDirectory       = ProjectRoot,
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                string line = p?.StandardOutput.ReadLine();
                p?.WaitForExit(3000);
                return line?.Trim();
            }
            catch { return null; }
        }

        // MCP for Unity cancels a command after 30s by default; stay under it.
        public const float MaxWaitSeconds = 25f;

        /// <summary>
        /// Lets the game run for <paramref name="playerSeconds"/> (turn-based clock) and waits for
        /// it, at most MaxWaitSeconds of real time. False if the call limit cut it short; the
        /// rest of the run is cancelled so the game freezes again.
        /// </summary>
        public static async Task<bool> RunGame(PlaytestDriver driver, double playerSeconds)
        {
            if (playerSeconds <= 0) return true;
            bool done = false;
            driver.RunFor(playerSeconds, () => done = true);
            bool finished = await WaitFor(() => done, MaxWaitSeconds);
            if (!finished) driver.CancelRun();   // re-freeze: drops the unfinished remainder
            return finished;
        }

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

        private static JObject s_LastBrief;

        /// <summary>
        /// The Brief fields that changed since the previous Brief/BriefDelta call, so a run of
        /// actions doesn't repeat the same summary every turn. {"unchanged": true} when nothing did.
        /// </summary>
        public static JObject BriefDelta()
        {
            var full  = JObject.FromObject(Brief());
            var delta = new JObject();
            foreach (var prop in full.Properties())
                if (s_LastBrief == null || !JToken.DeepEquals(prop.Value, s_LastBrief[prop.Name]))
                    delta[prop.Name] = prop.Value;
            s_LastBrief = full;
            return delta.HasValues ? delta : new JObject { ["unchanged"] = true };
        }

        /// <summary>The full summary; also the new baseline for BriefDelta.</summary>
        public static object BriefFull()
        {
            var full = JObject.FromObject(Brief());
            s_LastBrief = full;
            return full;
        }

        public static object Brief()
        {
            var s = PlaytestState.Capture();
            var driver = PlaytestDriver.Instance;
            var bench  = PlaytestBenchmark.Active;
            return new
            {
                editorPaused = EditorApplication.isPaused,
                clock = driver == null ? null : new
                {
                    mode = driver.TurnBased ? "turn_based" : "realtime",
                    driver.ActionSeconds,
                    playerTime = PlaytestBenchmark.FormatDuration(bench?.PlayerSeconds ?? driver.PlayerSeconds),
                },
                benchmark = bench == null ? null : new { bench.Scenario, bench.RunId, bench.Actions, bench.DollarsSpent },
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
