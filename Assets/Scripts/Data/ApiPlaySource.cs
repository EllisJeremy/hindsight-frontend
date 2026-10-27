using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Hindsight.Data
{
    /// <summary>
    /// Loads plays from the Football Capstone API (footballcapstone/apps/api):
    ///   GET {baseUrl}/games                -> GameList (used to pick a game when gameId is 0)
    ///   GET {baseUrl}/plays?gameId={id}    -> PlayIndex
    ///   GET {baseUrl}/plays/{playId}       -> PlayData
    ///   GET {baseUrl}/suggested            -> SuggestedList
    ///   GET {baseUrl}/plays/{playId}/suggested -> PlayData (the model's suggested version)
    /// UnityWebRequest only, so it is WebGL safe (the API allows any origin).
    /// </summary>
    public class ApiPlaySource : IPlaySource
    {
        readonly string baseUrl;
        long gameId;

        /// <param name="baseUrl">API root, e.g. http://localhost:8000.</param>
        /// <param name="gameId">Game to load; 0 picks the first game the API lists.</param>
        public ApiPlaySource(string baseUrl, long gameId = 0)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.gameId = gameId;
        }

        public async Awaitable<PlayIndex> LoadIndexAsync()
        {
            if (gameId == 0)
            {
                var games = JsonUtility.FromJson<GameList>(await GetTextAsync($"{baseUrl}/games"));
                if (games?.games == null || games.games.Length == 0)
                    throw new Exception($"{baseUrl} has no games loaded. Export plays and check the API's PLAYS_DIR.");
                gameId = games.games[0].gameId;
            }
            string json = await GetTextAsync($"{baseUrl}/plays?gameId={gameId}");
            return JsonUtility.FromJson<PlayIndex>(json);
        }

        public async Awaitable<PlayData> LoadPlayAsync(string playId)
        {
            string json = await GetTextAsync($"{baseUrl}/plays/{UnityWebRequest.EscapeURL(playId)}");
            return JsonUtility.FromJson<PlayData>(json);
        }

        public async Awaitable<SuggestedList> LoadSuggestedListAsync()
        {
            string json = await GetTextAsync($"{baseUrl}/suggested");
            return JsonUtility.FromJson<SuggestedList>(json);
        }

        public async Awaitable<PlayData> LoadSuggestedAsync(string playId)
        {
            string json = await GetTextAsync($"{baseUrl}/plays/{UnityWebRequest.EscapeURL(playId)}/suggested");
            return JsonUtility.FromJson<PlayData>(json);
        }

        static async Awaitable<string> GetTextAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();
            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    return request.downloadHandler.text;
                case UnityWebRequest.Result.ConnectionError:
                    throw new Exception($"GET {url} failed: {request.error}. Is the API running? (cd apps/api && uvicorn api.main:app)");
                default:
                    // FastAPI errors look like {"detail": "..."}; show that instead of just the status code.
                    string detail = null;
                    try { detail = JsonUtility.FromJson<ApiError>(request.downloadHandler.text)?.detail; }
                    catch (ArgumentException) { }
                    throw new Exception($"GET {url} failed: {request.error}{(string.IsNullOrEmpty(detail) ? "" : $" ({detail})")}");
            }
        }

        [Serializable]
        class ApiError
        {
            public string detail;
        }
    }
}
