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
            var now = Now(settings, debugNow);
            string week = PveRules.WeekId(now);

            if (!PveRules.CanJoin(now)) return Ser(new PveStatusResponse { error = PveErrors.OutsideWindow });

            var profile = (await store.GetProfile(ctx.PlayerId)).Value ?? new PveProfile();
            if (profile.lastWeek == week) return Ser(new PveStatusResponse { error = PveErrors.AlreadyJoined });

            string name = await store.GetPlayerName(ctx.PlayerId);
            if (string.IsNullOrEmpty(name)) return Ser(new PveStatusResponse { error = PveErrors.NameRequired });
            if (PlayerNameRules.Validate(PlayerNameRules.WithoutTag(name), PveGameConfig.Embedded.NamePatterns) != NameCheck.Ok)
                return Ser(new PveStatusResponse { error = PveErrors.NameBlocked });

            var leagueDef = PveRules.League(cfg, profile.league) ?? cfg.leagues[0];
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
        public async Task<string> Snapshot(IExecutionContext ctx, double netWorth, double rate, int cap, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveSnapshotResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
            var now = Now(settings, debugNow);

            var profile = (await store.GetProfile(ctx.PlayerId)).Value;
            if (profile == null || string.IsNullOrEmpty(profile.bracketId) || profile.lastWeek != PveRules.WeekId(now))
                return Ser(new PveSnapshotResponse { error = PveErrors.NotJoined });

            int run = PveRules.RunSeconds(cfg);
            var check = SnapCheck.Accepted;
            int t = 0;
            string key = PveRules.MemberKey(ctx.PlayerId);
            var written = await PveStore.WithRetry(
                () => store.GetCustom<PveMember>(profile.bracketId, key),
                m =>
                {
                    if (m == null) { check = SnapCheck.Stale; return null; }
                    int rawT = (int)Math.Floor((now - PveRules.ParseIso(m.startUtc)).TotalSeconds);
                    check = PveRules.AddSnap(m.snaps, rawT, netWorth, rate, cap, run, cfg.snapshot_bucket_minutes * 60);
                    t = Math.Min(Math.Max(0, rawT), run);
                    return check == SnapCheck.Accepted ? m : null;
                },
                (m, lk) => store.SetCustom(profile.bracketId, key, m, lk));

            if (check == SnapCheck.RunOver) return Ser(new PveSnapshotResponse { error = PveErrors.RunOver, t = t });
            if (check != SnapCheck.Accepted)
                return Ser(new PveSnapshotResponse { error = $"{PveErrors.Rejected}:{check}", t = t });
            return Ser(new PveSnapshotResponse { accepted = written != null, t = t });
        }

        // ── Board ─────────────────────────────────────────────────────────

        [CloudCodeFunction("Board")]
        public async Task<string> Board(IExecutionContext ctx, string debugNow = null)
        {
            var cfg = Config();
            if (cfg?.leagues == null) return Ser(new PveBoardResponse { error = PveErrors.Config });
            var store = new PveStore(_api, ctx);
            var settings = await Settings(store);
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
            foreach (var r in PveRules.Rank(members, viewerT))
                resp.rows.Add(new PveBoardRow
                {
                    rank = r.rank,
                    name = PlayerNameRules.WithoutTag(r.member.name),
                    value = r.value,
                    projected = r.projected,
                    you = r.member.playerId == ctx.PlayerId,
                    elapsed = PveRules.Elapsed(PveRules.ParseIso(r.member.startUtc), now, run),
                });
            return Ser(resp);
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
                        if (band != null && (band.crystals > 0 || band.stars > 0))
                            p.pendingRewards.Add(new PveReward
                            {
                                week = meta.week, league = meta.league, rank = r.rank, size = size,
                                crystals = band.crystals, stars = band.stars,
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
