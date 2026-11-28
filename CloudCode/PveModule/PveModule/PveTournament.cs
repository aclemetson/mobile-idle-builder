using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace MobileIdleBuilder.PveModule
{
    /// <summary>Registers the game API client for the functions below.</summary>
    public class ModuleSetup : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.AddGameApiClient();
        }
    }

    /// <summary>
    /// PvE tournament endpoints (module "PveModule"). Every function returns a JSON string of a shared
    /// response model (Assets/Scripts/PvE/PveModels.cs); the client parses it with JsonUtility. Times are
    /// always the server's; <c>debugNow</c> is honoured only while pve_settings.allowDebugTime is on.
    /// Rules live in the shared PveRules; this class is storage and orchestration only.
    /// </summary>
    public class PveTournament
    {
        readonly IGameApiClient _api;

        public PveTournament(IGameApiClient api) => _api = api;

        static string Ser<T>(T v) => PveStore.Serialize(v);

        static PveConfig Config() => PveGameConfig.Embedded.Pve;

        static async Task<PveSettings> Settings(PveStore store) =>
            await store.GetCustomAnyClass<PveSettings>(PveRules.SettingsId, PveRules.SettingsKey) ?? new PveSettings();

        static DateTime Now(PveSettings settings, string debugNow) =>
            settings.allowDebugTime && !string.IsNullOrEmpty(debugNow) ? PveRules.ParseIso(debugNow) : DateTime.UtcNow;

        static bool Settled(PveProfile p, string week) => p.history.Exists(h => h.week == week);

        // ── Status ────────────────────────────────────────────────────────

        [CloudCodeFunction("Status")]
        public async Task<string> Status(IExecutionContext ctx, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveStatusResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);

            var profile = (await store.GetProfile(ctx.PlayerId)).Value ?? new PveProfile();
            // The scheduled settle may not have run yet (or failed): settle this player's finished bracket now.
            if (!string.IsNullOrEmpty(profile.bracketId) && !string.IsNullOrEmpty(profile.lastWeek)
                && now >= PveRules.SettleAfter(profile.lastWeek) && !Settled(profile, profile.lastWeek))
            {
                await SettleBracket(store, cfg, profile.bracketId, now);
                profile = (await store.GetProfile(ctx.PlayerId)).Value ?? profile;
            }
            return Ser(await BuildStatus(store, cfg, ctx.PlayerId, profile, now));
        }

        async Task<PveStatusResponse> BuildStatus(PveStore store, PveConfig cfg, string playerId, PveProfile profile, DateTime now)
        {
            string week = PveRules.WeekId(now);
            // Show the league the player will actually join (a profile above the league cap plays at the cap).
            // Display only; Join writes it to the profile.
            profile.league = PveRules.CappedLeague(cfg, profile.league);
            var r = new PveStatusResponse
            {
                serverNowUtc = PveRules.Iso(now),
                week         = week,
                phase        = PveRules.Phase(now).ToString(),
                phaseEndsUtc = PveRules.Iso(PveRules.PhaseEnds(now)),
                nextEntryUtc = PveRules.Iso(PveRules.NextEntry(now)),
                profile      = profile,
                joinedThisWeek = profile.lastWeek == week,
                bracketId    = profile.bracketId,
            };
            if (!string.IsNullOrEmpty(profile.bracketId))
            {
                var member = (await store.GetCustom<PveMember>(profile.bracketId, PveRules.MemberKey(playerId))).Value;
                var meta = (await store.GetCustom<PveBracketMeta>(profile.bracketId, PveRules.MetaKey)).Value;
                r.bracketSettled = meta?.settled ?? false;
                if (member != null)
                {
                    r.retired    = member.retired;
                    var start = PveRules.ParseIso(member.startUtc);
                    r.startUtc   = member.startUtc;
                    r.runEndsUtc = PveRules.Iso(start.AddSeconds(PveRules.RunSeconds(cfg)));
                }
            }
            return r;
        }

        // ── Join ──────────────────────────────────────────────────────────

        [CloudCodeFunction("Join")]
        public async Task<string> Join(IExecutionContext ctx, bool dev = false, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveStatusResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);
            string week = PveRules.WeekId(now);

            if (!PveRules.CanJoin(now)) return Ser(new PveStatusResponse { error = PveErrors.OutsideWindow });

            var profile = (await store.GetProfile(ctx.PlayerId)).Value ?? new PveProfile();
            if (profile.lastWeek == week) return Ser(new PveStatusResponse { error = PveErrors.AlreadyJoined });

            string name = await store.GetPlayerName(ctx.PlayerId);
            if (string.IsNullOrEmpty(name)) return Ser(new PveStatusResponse { error = PveErrors.NameRequired });
            if (PlayerNameRules.Validate(PlayerNameRules.WithoutTag(name), PveGameConfig.Embedded.NamePatterns) != NameCheck.Ok)
                return Ser(new PveStatusResponse { error = PveErrors.NameBlocked });

            // A league above the cap (pve_settings maxLeague) joins at the cap, and the profile is moved there below.
            var leagueDef = PveRules.League(cfg, PveRules.CappedLeague(cfg, profile.league)) ?? cfg.leagues[0];
            int capacity = PveRules.Capacity(cfg, leagueDef, settings.smallBrackets);
            string cursorKey = PveRules.CursorKey(leagueDef.id, dev);
            string indexId = PveRules.WeekIndexId(week);

            // 1. Take a seat in the league's open bracket (a full one opens the next).
            int seat = 0;
            await PveStore.WithRetry(
                () => store.GetCustom<PveWeekIndex>(indexId, PveRules.IndexKey),
                idx =>
                {
                    idx ??= new PveWeekIndex { week = week };
                    var c = idx.cursors.Find(x => x.key == cursorKey);
                    if (c == null) { c = new PveLeagueCursor { key = cursorKey, brackets = 1, openCapacity = capacity }; idx.cursors.Add(c); }
                    if (c.openCount >= c.openCapacity) { c.brackets++; c.openCount = 0; c.openCapacity = capacity; }
                    c.openCount++;
                    seat = c.brackets;
                    return idx;
                },
                (idx, lk) => store.SetCustom(indexId, PveRules.IndexKey, idx, lk));

            string bracketId = PveRules.BracketId(week, leagueDef.id, dev, seat);

            // 2. Add the player to that bracket's member list.
            await PveStore.WithRetry(
                () => store.GetCustom<PveBracketMeta>(bracketId, PveRules.MetaKey),
                meta =>
                {
                    meta ??= new PveBracketMeta { id = bracketId, week = week, league = leagueDef.id, dev = dev, capacity = capacity };
                    if (!meta.playerIds.Contains(ctx.PlayerId)) meta.playerIds.Add(ctx.PlayerId);
                    return meta;
                },
                (meta, lk) => store.SetCustom(bracketId, PveRules.MetaKey, meta, lk));

            // 3. The member record, with the t = 0 snapshot (the run starts from a fresh prestige).
            var member = new PveMember
            {
                playerId = ctx.PlayerId, name = name, startUtc = PveRules.Iso(now), dev = dev,
                nameColor = profile.nameColor, title = profile.title,   // the player's leaderboard look (#500)
                snaps = new List<PveSnap> { new PveSnap { t = 0, nw = 0, rate = 0, cap = 0 } },
            };
            await store.SetCustom(bracketId, PveRules.MemberKey(ctx.PlayerId), member, null);

            // 4. The profile remembers the entry.
            profile = await PveStore.WithRetry(
                () => store.GetProfile(ctx.PlayerId),
                p =>
                {
                    p ??= new PveProfile();
                    p.league = leagueDef.id;
                    p.lastWeek = week;
                    p.bracketId = bracketId;
                    return p;
                },
                (p, lk) => store.SetProfile(ctx.PlayerId, p, lk));

            return Ser(await BuildStatus(store, cfg, ctx.PlayerId, profile, now));
        }

        // ── Snapshot ──────────────────────────────────────────────────────

        [CloudCodeFunction("Snapshot")]
        /// <param name="atT">-1 = now; otherwise the elapsed time (s) the device took this snapshot at, for one it
        /// couldn't deliver earlier (see PveRules.SnapshotTime).</param>
        public async Task<string> Snapshot(IExecutionContext ctx, double netWorth, double rate, int cap, int atT = -1, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveSnapshotResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);

            var profile = (await store.GetProfile(ctx.PlayerId)).Value;
            if (profile == null || string.IsNullOrEmpty(profile.bracketId) || string.IsNullOrEmpty(profile.lastWeek))
                return Ser(new PveSnapshotResponse { error = PveErrors.NotJoined });
            // Once the week is settled the scores are written: nothing more counts (late deliveries included).
            if (now >= PveRules.SettleAfter(profile.lastWeek))
                return Ser(new PveSnapshotResponse { error = PveErrors.RunOver });

            int run = PveRules.RunSeconds(cfg);
            var check = SnapCheck.Accepted;
            bool retired = false;
            int t = 0;
            string key = PveRules.MemberKey(ctx.PlayerId);
            var written = await PveStore.WithRetry(
                () => store.GetCustom<PveMember>(profile.bracketId, key),
                m =>
                {
                    if (m == null) { check = SnapCheck.Stale; return null; }
                    if (m.retired) { retired = true; return null; }
                    int serverT = (int)Math.Floor((now - PveRules.ParseIso(m.startUtc)).TotalSeconds);
                    check = PveRules.SnapshotTime(serverT, atT, out int rawT);
                    if (check != SnapCheck.Accepted) { t = Math.Max(0, Math.Min(serverT, run)); return null; }
                    check = PveRules.AddSnap(m.snaps, rawT, netWorth, rate, cap, run, cfg.snapshot_bucket_minutes * 60);
                    t = Math.Min(Math.Max(0, rawT), run);
                    return check == SnapCheck.Accepted ? m : null;
                },
                (m, lk) => store.SetCustom(profile.bracketId, key, m, lk));

            if (retired) return Ser(new PveSnapshotResponse { error = PveErrors.Retired });
            if (check == SnapCheck.RunOver) return Ser(new PveSnapshotResponse { error = PveErrors.RunOver, t = t });
            if (check != SnapCheck.Accepted)
                return Ser(new PveSnapshotResponse { error = $"{PveErrors.Rejected}:{check}", t = t });
            return Ser(new PveSnapshotResponse { accepted = written != null, t = t });
        }

        // ── Retire (#505) ─────────────────────────────────────────────────

        /// <summary>
        /// Leaves this week's run early: records a last snapshot at the current net worth with idle rate 0, so
        /// the score stays at that value for the rest of the 24h (ValueAt projects nothing from it), and marks
        /// the member retired so no later snapshot counts. Used by the Retire button and by prestiging mid-run.
        /// </summary>
        [CloudCodeFunction("Retire")]
        public async Task<string> Retire(IExecutionContext ctx, double netWorth, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveSnapshotResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);

            var profile = (await store.GetProfile(ctx.PlayerId)).Value;
            if (profile == null || string.IsNullOrEmpty(profile.bracketId) || string.IsNullOrEmpty(profile.lastWeek))
                return Ser(new PveSnapshotResponse { error = PveErrors.NotJoined });
            if (now >= PveRules.SettleAfter(profile.lastWeek))
                return Ser(new PveSnapshotResponse { error = PveErrors.RunOver });

            int run = PveRules.RunSeconds(cfg);
            var check = SnapCheck.Accepted;
            bool already = false;
            int t = 0;
            string key = PveRules.MemberKey(ctx.PlayerId);
            await PveStore.WithRetry(
                () => store.GetCustom<PveMember>(profile.bracketId, key),
                m =>
                {
                    if (m == null) { check = SnapCheck.Stale; return null; }
                    if (m.retired) { already = true; return null; }
                    int serverT = (int)Math.Floor((now - PveRules.ParseIso(m.startUtc)).TotalSeconds);
                    if (serverT >= run) { check = SnapCheck.RunOver; return null; }   // the run is over anyway
                    t = Math.Max(0, serverT);
                    // The final value: never below what is already recorded (a retire can't lower a score).
                    double last = m.snaps.Count > 0 ? m.snaps[m.snaps.Count - 1].nw : 0;
                    check = PveRules.AddSnap(m.snaps, serverT, Math.Max(netWorth, last), 0, 0, run, cfg.snapshot_bucket_minutes * 60);
                    if (check != SnapCheck.Accepted) return null;
                    m.retired = true;
                    return m;
                },
                (m, lk) => store.SetCustom(profile.bracketId, key, m, lk));

            if (already) return Ser(new PveSnapshotResponse { error = PveErrors.Retired });
            if (check == SnapCheck.RunOver) return Ser(new PveSnapshotResponse { error = PveErrors.RunOver });
            if (check != SnapCheck.Accepted) return Ser(new PveSnapshotResponse { error = $"{PveErrors.Rejected}:{check}", t = t });
            return Ser(new PveSnapshotResponse { accepted = true, t = t });
        }

        // ── DeleteMyData (account deletion) ───────────────────────────────

        /// <summary>
        /// The first step of the Settings "Delete account" flow (docs/agents/tasks/feature-account-deletion.md):
        /// takes the caller off their bracket (member list and member record, so their name leaves the board and
        /// settlement skips them), then deletes every Cloud Save key they own, the game save included. Acts only on
        /// the caller; there is deliberately no player id argument. The client deletes the UGS account afterwards.
        /// Only the current bracket is cleared: the profile does not record older ones (they are settled history).
        /// Failures throw, so the client stops before deleting the account and the player can retry.
        /// </summary>
        [CloudCodeFunction("DeleteMyData")]
        public async Task<string> DeleteMyData(IExecutionContext ctx)
        {
            var store = new PveStore(_api, ctx);
            var profile = (await store.GetProfile(ctx.PlayerId)).Value;
            string bracketId = profile?.bracketId;
            if (!string.IsNullOrEmpty(bracketId))
            {
                await PveStore.WithRetry(
                    () => store.GetCustom<PveBracketMeta>(bracketId, PveRules.MetaKey),
                    meta => PveRules.WithoutPlayer(meta, ctx.PlayerId),
                    (meta, lk) => store.SetCustom(bracketId, PveRules.MetaKey, meta, lk));
                await store.DeleteCustom(bracketId, PveRules.MemberKey(ctx.PlayerId));
            }
            await store.DeleteAllPlayerData(ctx.PlayerId);
            return Ser(new PveDeleteResponse { deleted = true });
        }

        // ── Board ─────────────────────────────────────────────────────────

        [CloudCodeFunction("Board")]
        public async Task<string> Board(IExecutionContext ctx, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveBoardResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);

            var profile = (await store.GetProfile(ctx.PlayerId)).Value;
            if (profile == null || string.IsNullOrEmpty(profile.bracketId))
                return Ser(new PveBoardResponse { error = PveErrors.NotJoined });

            var (meta, members) = await ReadBracket(store, profile.bracketId);
            int run = PveRules.RunSeconds(cfg);
            var me = members.Find(m => m.playerId == ctx.PlayerId);
            bool over = (meta?.settled ?? false) || now >= PveRules.SettleAfter(profile.lastWeek);
            int viewerT = over || me == null ? run : PveRules.Elapsed(PveRules.ParseIso(me.startUtc), now, run);

            var resp = new PveBoardResponse { week = profile.lastWeek, league = meta?.league, viewerT = viewerT, settled = meta?.settled ?? false };
            var (up, down) = PveRules.Moves(cfg, PveRules.League(cfg, meta?.league), members.Count, meta?.capacity ?? members.Count);
            resp.promote = up;
            resp.demote = down;
            foreach (var r in PveRules.Rank(members, viewerT))
                resp.rows.Add(new PveBoardRow
                {
                    rank = r.rank,
                    name = PlayerNameRules.WithoutTag(r.member.name),
                    value = r.value,
                    projected = r.projected,
                    you = r.member.playerId == ctx.PlayerId,
                    retired = r.member.retired,
                    elapsed = PveRules.Elapsed(PveRules.ParseIso(r.member.startUtc), now, run),
                    nameColor = PveCosmetics.IsValid(r.member.nameColor, CosmeticKind.NameColor) ? r.member.nameColor : null,
                    title = PveCosmetics.IsValid(r.member.title, CosmeticKind.Title) ? r.member.title : null,
                });
            return Ser(resp);
        }

        // ── Look (#500) ───────────────────────────────────────────────────

        /// <summary>
        /// Sets the player's leaderboard look (name colour + title, PveCosmetics ids; "" = none). Stored on the
        /// profile so every later tournament carries it, and on this week's member record while it is still open.
        /// Unknown ids are refused, so no free text ever reaches the board. Ownership is the client's (Strings are
        /// a client-side balance); the worst a modified client gets is a colour.
        /// </summary>
        [CloudCodeFunction("SetLook")]
        public async Task<string> SetLook(IExecutionContext ctx, string nameColor = "", string title = "", string debugNow = null)
        {
            nameColor ??= ""; title ??= "";
            if (!PveCosmetics.IsValid(nameColor, CosmeticKind.NameColor) || !PveCosmetics.IsValid(title, CosmeticKind.Title))
                return Ser(new PveStatusResponse { error = PveErrors.BadLook });
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveStatusResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            cfg = PveRules.WithCap(cfg, settings.maxLeague);   // this environment's league cap (dev: bronze)
            var now = Now(settings, debugNow);

            var profile = await PveStore.WithRetry(
                () => store.GetProfile(ctx.PlayerId),
                p =>
                {
                    p ??= new PveProfile();
                    p.nameColor = nameColor;
                    p.title = title;
                    return p;
                },
                (p, lk) => store.SetProfile(ctx.PlayerId, p, lk));

            if (!string.IsNullOrEmpty(profile?.bracketId) && !string.IsNullOrEmpty(profile.lastWeek)
                && now < PveRules.SettleAfter(profile.lastWeek))
            {
                string key = PveRules.MemberKey(ctx.PlayerId);
                await PveStore.WithRetry(
                    () => store.GetCustom<PveMember>(profile.bracketId, key),
                    m =>
                    {
                        if (m == null) return null;
                        m.nameColor = nameColor;
                        m.title = title;
                        return m;
                    },
                    (m, lk) => store.SetCustom(profile.bracketId, key, m, lk));
            }
            return Ser(await BuildStatus(store, cfg, ctx.PlayerId, profile, now));
        }

        // ── Claim ─────────────────────────────────────────────────────────

        [CloudCodeFunction("Claim")]
        public async Task<string> Claim(IExecutionContext ctx, string week)
        {
            var store = new PveStore(_api, ctx);
            PveReward taken = null;
            await PveStore.WithRetry(
                () => store.GetProfile(ctx.PlayerId),
                p =>
                {
                    taken = null;
                    var r = p?.pendingRewards.Find(x => x.week == week);
                    if (r == null) return null;
                    p.pendingRewards.Remove(r);
                    taken = r;
                    return p;
                },
                (p, lk) => store.SetProfile(ctx.PlayerId, p, lk));
            return Ser(taken == null ? new PveClaimResponse { error = PveErrors.NothingToClaim } : new PveClaimResponse { reward = taken });
        }

        // ── Settlement (scheduled, Sunday 00:00 UTC) ──────────────────────

        /// <summary>
        /// Settles every bracket of <paramref name="week"/> (default: the most recent week whose runs have all
        /// ended). Called by the pve-settle schedule trigger with a service token; refused when a player calls it.
        /// </summary>
        [CloudCodeFunction("SettleWeek")]
        public async Task<string> SettleWeek(IExecutionContext ctx, string week = null)
        {
            if (!string.IsNullOrEmpty(ctx.PlayerId)) return Ser(new PveStatusResponse { error = "service_only" });
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveStatusResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            cfg = PveRules.WithCap(cfg, (await Settings(store)).maxLeague);
            var now = DateTime.UtcNow;
            week ??= PveRules.Phase(now) == PvePhase.Results ? PveRules.WeekId(now) : PveRules.WeekId(now.AddDays(-7));
            if (now < PveRules.SettleAfter(week)) return Ser(new PveStatusResponse { error = "too_early", week = week });

            string indexId = PveRules.WeekIndexId(week);
            var idx = (await store.GetCustom<PveWeekIndex>(indexId, PveRules.IndexKey)).Value;
            int settled = 0;
            if (idx != null)
            {
                foreach (var c in idx.cursors)
                {
                    string league = c.key.EndsWith("_dev") ? c.key.Substring(0, c.key.Length - 4) : c.key;
                    bool dev = c.key.EndsWith("_dev");
                    for (int n = 1; n <= c.brackets; n++)
                        if (await SettleBracket(store, cfg, PveRules.BracketId(week, league, dev, n), now)) settled++;
                }
                await PveStore.WithRetry(
                    () => store.GetCustom<PveWeekIndex>(indexId, PveRules.IndexKey),
                    i => { if (i == null || i.settled) return null; i.settled = true; return i; },
                    (i, lk) => store.SetCustom(indexId, PveRules.IndexKey, i, lk));
            }
            return Ser(new PveStatusResponse { week = week, phase = $"settled {settled} bracket(s)" });
        }

        async Task<(PveBracketMeta meta, List<PveMember> members)> ReadBracket(PveStore store, string bracketId)
        {
            PveBracketMeta meta = null;
            var members = new List<PveMember>();
            foreach (var item in await store.GetAllCustom(bracketId))
            {
                if (item.Key == PveRules.MetaKey) meta = PveStore.Deserialize<PveBracketMeta>(item.Value);
                else if (item.Key.StartsWith("m_")) { var m = PveStore.Deserialize<PveMember>(item.Value); if (m != null) members.Add(m); }
            }
            return (meta, members);
        }

        /// <summary>
        /// Ranks a finished bracket at the 24h mark and writes each member's result, league move and reward
        /// into their profile. Safe to repeat: a member whose history already has the week is skipped, and a
        /// settled bracket is left alone. Returns true when this call settled it.
        /// </summary>
        async Task<bool> SettleBracket(PveStore store, PveConfig cfg, string bracketId, DateTime now)
        {
            var (meta, members) = await ReadBracket(store, bracketId);
            if (meta == null || meta.settled || now < PveRules.SettleAfter(meta.week)) return false;

            var league = PveRules.League(cfg, meta.league);
            int size = members.Count;
            var (up, down) = PveRules.Moves(cfg, league, size, meta.capacity);
            foreach (var r in PveRules.Rank(members, PveRules.RunSeconds(cfg)))
            {
                var (next, result) = PveRules.NextLeague(cfg, meta.league, r.rank, size, up, down);
                var band = PveRules.RewardFor(league, r.rank, size);
                string pid = r.member.playerId;
                await PveStore.WithRetry(
                    () => store.GetProfile(pid),
                    p =>
                    {
                        p ??= new PveProfile();
                        if (Settled(p, meta.week)) return null;
                        p.history.Add(new PveHistoryEntry
                        {
                            week = meta.week, league = meta.league, rank = r.rank, size = size,
                            score = r.value, result = result, newLeague = next,
                        });
                        if (p.history.Count > 20) p.history.RemoveRange(0, p.history.Count - 20);
                        p.league = next;
                        if (band != null && (band.crystals > 0 || band.stars > 0 || band.strings > 0))
                            p.pendingRewards.Add(new PveReward
                            {
                                week = meta.week, league = meta.league, rank = r.rank, size = size,
                                crystals = band.crystals, stars = band.stars, strings = band.strings,
                            });
                        return p;
                    },
                    (p, lk) => store.SetProfile(pid, p, lk));
            }

            await PveStore.WithRetry(
                () => store.GetCustom<PveBracketMeta>(bracketId, PveRules.MetaKey),
                m => { if (m == null || m.settled) return null; m.settled = true; return m; },
                (m, lk) => store.SetCustom(bracketId, PveRules.MetaKey, m, lk));
            return true;
        }
    }
}
