using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Sends/receives save data via AWS API Gateway → Lambda → DynamoDB.
    /// Auth token (from Cognito) must be set before calling cloud methods.
    /// </summary>
    public class CloudSaveService : ICloudSaveService
    {
        readonly string _baseUrl;
        string _authToken;

        public bool IsAvailable =>
            Application.internetReachability != NetworkReachability.NotReachable;

        public CloudSaveService(string apiBaseUrl)
        {
            _baseUrl = apiBaseUrl.TrimEnd('/');
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public void SetAuthToken(string token) => _authToken = token;

        public async Task<SaveData> FetchAsync(string playerId)
        {
            string url = $"{_baseUrl}/save/{playerId}";
            using var req = UnityWebRequest.Get(url);
            AddAuthHeader(req);

            await SendAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[CloudSave] Fetch failed: {req.error}");
                return null;
            }

            return JsonUtility.FromJson<SaveData>(req.downloadHandler.text);
        }

        public async Task PushAsync(SaveData data)
        {
            string url  = $"{_baseUrl}/save/{data.playerId}";
            string json = JsonUtility.ToJson(data);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using var req = new UnityWebRequest(url, "PUT")
            {
                uploadHandler   = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            AddAuthHeader(req);

            await SendAsync(req);

            if (req.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"[CloudSave] Push failed: {req.error}");
        }

        void AddAuthHeader(UnityWebRequest req)
        {
            if (!string.IsNullOrEmpty(_authToken))
                req.SetRequestHeader("Authorization", $"Bearer {_authToken}");
        }

        static Task SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<bool>();
            var op  = req.SendWebRequest();
            op.completed += _ => tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
