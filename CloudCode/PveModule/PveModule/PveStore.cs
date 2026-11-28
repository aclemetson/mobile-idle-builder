using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudCode.Shared;
using Unity.Services.CloudSave.Model;

namespace MobileIdleBuilder.PveModule
{
    /// <summary>A stored value with the write lock it was read under (null value = key missing).</summary>
    public sealed class Locked<T> where T : class
    {
        public T Value;
        public string WriteLock;
    }

    /// <summary>
    /// Cloud Save access for the PvE module. Values are stored as JSON strings written by
    /// <see cref="Json"/> (public fields included), so the dashboard shows exactly what the shared
    /// models hold. Brackets and the week index are private custom items (clients cannot read them);
    /// the player's profile is protected player data (the client may read it, only the server writes).
    /// </summary>
    public sealed class PveStore
    {
        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        readonly IGameApiClient _api;
        readonly IExecutionContext _ctx;

        public PveStore(IGameApiClient api, IExecutionContext ctx)
        {
            _api = api;
            _ctx = ctx;
        }

        public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

        public static T Deserialize<T>(object stored) where T : class
        {
            if (stored == null) return null;
            string json = stored is string s ? s : stored.ToString();
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Json);
        }

        /// <summary>True when Cloud Save reports a custom ID or player with no data.</summary>
        public static bool IsNotFound(Exception e) =>
            e is ApiException api && api.Response != null && api.Response.StatusCode == HttpStatusCode.NotFound;

        /// <summary>True for the conflict Cloud Save returns when a write lock no longer matches.</summary>
        public static bool IsLockConflict(Exception e) =>
            e is ApiException api && api.Response != null && api.Response.StatusCode == HttpStatusCode.Conflict;

        // ── Private custom items (brackets, week index, settings) ──────────

        public async Task<Locked<T>> GetCustom<T>(string customId, string key) where T : class
        {
            ApiResponse<GetItemsResponse> res;
            try
            {
                res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId,
                    customId, new List<string> { key });
            }
            catch (ApiException e) when (IsNotFound(e)) { return new Locked<T>(); }
            foreach (var item in res.Data.Results)
                if (item.Key == key) return new Locked<T> { Value = Deserialize<T>(item.Value), WriteLock = item.WriteLock };
            return new Locked<T>();
        }

        /// <summary>
        /// A custom item key from either access class: private first, then default. Used for pve_settings,
        /// which is created by hand in the dashboard (Cloud Save > Game Data) where the default class is the
        /// usual choice. Clients cannot write either class, so reading both is safe.
        /// </summary>
        public async Task<T> GetCustomAnyClass<T>(string customId, string key) where T : class
        {
            var priv = await GetCustom<T>(customId, key);
            if (priv.Value != null) return priv.Value;
            try
            {
                var res = await _api.CloudSaveData.GetCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId,
                    customId, new List<string> { key });
                foreach (var item in res.Data.Results)
                    if (item.Key == key) return Deserialize<T>(item.Value);
            }
            catch (ApiException e) when (IsNotFound(e)) { }
            return null;
        }

        /// <summary>Every key of a custom item, following the paging links.</summary>
        public async Task<List<Item>> GetAllCustom(string customId)
        {
            var all = new List<Item>();
            string after = null;
            for (int page = 0; page < 50; page++)
            {
                ApiResponse<GetItemsResponse> res;
                try
                {
                    res = await _api.CloudSaveData.GetPrivateCustomItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId,
                        customId, null, after);
                }
                catch (ApiException e) when (IsNotFound(e)) { break; }
                var results = res.Data.Results;
                if (results == null || results.Count == 0) break;
                all.AddRange(results);
                if (string.IsNullOrEmpty(res.Data.Links?.Next)) break;
                after = results[results.Count - 1].Key;
            }
            return all;
        }

        /// <summary>Writes a custom item key. A non-null <paramref name="writeLock"/> makes it fail on a concurrent change; "" or null for a new key.</summary>
        public Task SetCustom<T>(string customId, string key, T value, string writeLock) =>
            _api.CloudSaveData.SetPrivateCustomItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, customId,
                new SetItemBody(key, Serialize(value), string.IsNullOrEmpty(writeLock) ? null : writeLock));

        /// <summary>Deletes one custom item key. A missing key or custom ID counts as deleted.</summary>
        public async Task DeleteCustom(string customId, string key)
        {
            try
            {
                await _api.CloudSaveData.DeletePrivateCustomItemAsync(_ctx, _ctx.ServiceToken, key: key, projectId: _ctx.ProjectId, customId: customId);
            }
            catch (ApiException e) when (IsNotFound(e)) { }
        }

        // ── Account deletion ──────────────────────────────────────────────

        /// <summary>
        /// Deletes every Cloud Save key the player has, in all four access classes (default: the game save;
        /// protected: the PvE profile; public and private: unused today, cleared so nothing is left behind).
        /// A class with no data counts as deleted.
        /// </summary>
        public async Task DeleteAllPlayerData(string playerId)
        {
            await IgnoreNotFound(() => _api.CloudSaveData.DeleteItemsAsync(_ctx, _ctx.ServiceToken, projectId: _ctx.ProjectId, playerId: playerId));
            await IgnoreNotFound(() => _api.CloudSaveData.DeleteProtectedItemsAsync(_ctx, _ctx.ServiceToken, projectId: _ctx.ProjectId, playerId: playerId));
            await IgnoreNotFound(() => _api.CloudSaveData.DeletePublicItemsAsync(_ctx, _ctx.ServiceToken, projectId: _ctx.ProjectId, playerId: playerId));
            await IgnoreNotFound(() => _api.CloudSaveData.DeletePrivateItemsAsync(_ctx, _ctx.ServiceToken, projectId: _ctx.ProjectId, playerId: playerId));
        }

        static async Task IgnoreNotFound(Func<Task> call)
        {
            try { await call(); }
            catch (ApiException e) when (IsNotFound(e)) { }
        }

        // ── Protected player data (the PvE profile) ────────────────────────

        public async Task<Locked<PveProfile>> GetProfile(string playerId)
        {
            ApiResponse<GetItemsResponse> res;
            try
            {
                res = await _api.CloudSaveData.GetProtectedItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId,
                    playerId, new List<string> { PveRules.ProfileKey });
            }
            catch (ApiException e) when (IsNotFound(e)) { return new Locked<PveProfile>(); }
            foreach (var item in res.Data.Results)
                if (item.Key == PveRules.ProfileKey)
                    return new Locked<PveProfile> { Value = Deserialize<PveProfile>(item.Value), WriteLock = item.WriteLock };
            return new Locked<PveProfile>();
        }

        public Task SetProfile(string playerId, PveProfile profile, string writeLock) =>
            _api.CloudSaveData.SetProtectedItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId,
                new SetItemBody(PveRules.ProfileKey, Serialize(profile), string.IsNullOrEmpty(writeLock) ? null : writeLock));

        // ── Crystals (UGS Economy + the protected crystal_ledger item) ─────

        public async Task<Locked<CrystalLedger>> GetLedger(string playerId)
        {
            ApiResponse<GetItemsResponse> res;
            try
            {
                res = await _api.CloudSaveData.GetProtectedItemsAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId,
                    playerId, new List<string> { CrystalRules.LedgerKey });
            }
            catch (ApiException e) when (IsNotFound(e)) { return new Locked<CrystalLedger>(); }
            foreach (var item in res.Data.Results)
                if (item.Key == CrystalRules.LedgerKey)
                    return new Locked<CrystalLedger> { Value = Deserialize<CrystalLedger>(item.Value), WriteLock = item.WriteLock };
            return new Locked<CrystalLedger>();
        }

        public Task SetLedger(string playerId, CrystalLedger ledger, string writeLock) =>
            _api.CloudSaveData.SetProtectedItemAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId,
                new SetItemBody(CrystalRules.LedgerKey, Serialize(ledger), string.IsNullOrEmpty(writeLock) ? null : writeLock));

        /// <summary>The player's Crystals balance, or null when the currency isn't configured (and published) here.</summary>
        public async Task<long?> GetCrystals(string playerId)
        {
            var res = await _api.EconomyCurrencies.GetPlayerCurrenciesAsync(_ctx, _ctx.ServiceToken, _ctx.ProjectId, playerId);
            foreach (var c in res.Data.Results ?? new List<Unity.Services.Economy.Model.CurrencyBalanceResponse>())
                if (c.CurrencyId == CrystalRules.CurrencyId) return c.Balance;
            return null;
        }

        /// <summary>Adds <paramref name="amount"/> (> 0) Crystals; returns the new balance.</summary>
        public async Task<long> IncrementCrystals(string playerId, long amount)
        {
            var res = await _api.EconomyCurrencies.IncrementPlayerCurrencyBalanceAsync(_ctx, _ctx.ServiceToken,
                _ctx.ProjectId, playerId, CrystalRules.CurrencyId,
                new Unity.Services.Economy.Model.CurrencyModifyBalanceRequest(CrystalRules.CurrencyId, amount, null));
            return res.Data.Balance;
        }

        // ── Player names ──────────────────────────────────────────────────

        /// <summary>The player's chosen name, or null when they have none (never auto-generates one).</summary>
        public async Task<string> GetPlayerName(string playerId)
        {
            try
            {
                var res = await _api.PlayerNamesApi.GetNameAsync(_ctx, _ctx.ServiceToken, playerId, false, false, default);
                var p = res.Data;
                if (p == null || p.AutoGenerated || string.IsNullOrEmpty(p.Name)) return null;
                return p.Name;
            }
            catch (ApiException e) when (e.Response?.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        /// <summary>
        /// Read-modify-write with the write lock, retried on conflict. <paramref name="modify"/> gets the
        /// current value (null when missing) and returns the new value, or null to write nothing.
        /// </summary>
        public static async Task<T> WithRetry<T>(Func<Task<Locked<T>>> read, Func<T, T> modify,
                                                 Func<T, string, Task> write, int attempts = 5) where T : class
        {
            for (int i = 0; ; i++)
            {
                var cur = await read();
                var next = modify(cur.Value);
                if (next == null) return cur.Value;
                try
                {
                    await write(next, cur.WriteLock);
                    return next;
                }
                catch (Exception e) when (IsLockConflict(e) && i < attempts - 1)
                {
                    await Task.Delay(50 * (i + 1));
                }
            }
        }
    }
}
