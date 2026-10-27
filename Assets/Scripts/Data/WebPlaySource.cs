using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Hindsight.Data
{
    /// <summary>
    /// Loads plays over UnityWebRequest from a base URL:
    ///   {baseUrl}/index.json      -> PlayIndex
    ///   {baseUrl}/{playId}.json   -> PlayData
    /// Used for StreamingAssets (file:// or http:// in WebGL); the backend uses ApiPlaySource.
    /// No System.IO, so it is WebGL safe.
    /// </summary>
    public class WebPlaySource : IPlaySource
    {
        readonly string baseUrl;

        public WebPlaySource(string baseUrl)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
        }

        /// <summary>Source reading from Assets/StreamingAssets/plays.</summary>
        public static WebPlaySource StreamingAssets(string subfolder = "plays")
        {
            string root = Application.streamingAssetsPath;
            // Desktop/editor return a plain file path; UnityWebRequest needs a URI.
            // WebGL and Android already return a URL.
            if (!root.Contains("://"))
                root = "file://" + root;
            return new WebPlaySource($"{root}/{subfolder}");
        }

        public async Awaitable<PlayIndex> LoadIndexAsync()
        {
            string json = await GetTextAsync($"{baseUrl}/index.json");
            return JsonUtility.FromJson<PlayIndex>(json);
        }

        public async Awaitable<PlayData> LoadPlayAsync(string playId)
        {
            string json = await GetTextAsync($"{baseUrl}/{UnityWebRequest.EscapeURL(playId)}.json");
            return JsonUtility.FromJson<PlayData>(json);
        }

        // Suggested plays are converted by the backend, so offline there are none.
        public Awaitable<SuggestedList> LoadSuggestedListAsync()
        {
            var done = new AwaitableCompletionSource<SuggestedList>();
            done.SetResult(new SuggestedList { plays = Array.Empty<SuggestedEntry>() });
            return done.Awaitable;
        }

        public Awaitable<PlayData> LoadSuggestedAsync(string playId) =>
            throw new NotSupportedException("Suggested plays come from the backend; set PlayLoader's Source to Backend.");

        static async Awaitable<string> GetTextAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"GET {url} failed: {request.error}");
            return request.downloadHandler.text;
        }
    }
}
