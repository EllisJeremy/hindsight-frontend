using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hindsight.Data
{
    /// <summary>
    /// Owns the play list and loads plays by position in it. Raises PlayLoaded when a
    /// play is ready. Plays come from the backend API by default; StreamingAssets is an
    /// offline fallback that reads the exporter's JSON directly.
    /// </summary>
    public class PlayLoader : MonoBehaviour
    {
        // Backend is 0 so scenes saved before it existed (StreamingAssets was 0) switch to it.
        public enum SourceKind { Backend, StreamingAssets }

        /// <summary>The schemaVersion this client understands (see CLAUDE.md).</summary>
        public const int SchemaVersion = 1;

        [SerializeField] SourceKind source = SourceKind.Backend;
        [Tooltip("API root (footballcapstone/apps/api). Used when source is Backend.")]
        [SerializeField] string apiBaseUrl = "http://localhost:8000";
        [Tooltip("Game to load from the backend. 0 = the first game it lists.")]
        [SerializeField] long gameId;

        public event Action<PlayData> PlayLoaded;
        public event Action<string> LoadFailed;

        public PlayIndex Index => index;
        public IReadOnlyList<PlayIndexEntry> Plays => index?.plays ?? Array.Empty<PlayIndexEntry>();
        public int CurrentIndex { get; private set; } = -1;
        public bool IsLoading { get; private set; }
        /// <summary>Plays the model has a suggested version of (pass plays it reviewed). Entries
        /// need not be in Plays if the API serves suggestions for another game.</summary>
        public IReadOnlyList<SuggestedEntry> Suggested => suggested;
        /// <summary>The play whose suggested version is showing, or null when showing a recording.</summary>
        public string CurrentSuggestedId { get; private set; }
        public int CurrentSuggestedIndex => CurrentSuggestedId == null ? -1 : suggested.FindIndex(s => s.playId == CurrentSuggestedId);

        IPlaySource playSource;
        PlayIndex index;
        readonly Dictionary<string, PlayData> cache = new();
        readonly List<SuggestedEntry> suggested = new();
        int requestVersion;

        public IPlaySource Source => playSource ??= source == SourceKind.Backend
            ? new ApiPlaySource(apiBaseUrl, gameId)
            : WebPlaySource.StreamingAssets();

        /// <summary>Fetch the play list, then load the first play.</summary>
        public async Awaitable InitializeAsync()
        {
            try
            {
                index = await Source.LoadIndexAsync();
            }
            catch (Exception e)
            {
                Fail($"Could not load play index: {e.Message}");
                return;
            }

            if (index == null || index.schemaVersion != SchemaVersion)
            {
                Fail($"Play index has schemaVersion {index?.schemaVersion}, this client reads {SchemaVersion}.");
                index = null;
                return;
            }
            DropOtherGames();
            if (Plays.Count == 0)
            {
                Fail("Play index is empty.");
                return;
            }
            await LoadSuggestedListAsync();
            await LoadAtAsync(0);
        }

        /// <summary>The API's default play folder lists video clips (game 999000001) inside this
        /// game's index; they would show up as an extra Q1 in the navigator.</summary>
        void DropOtherGames()
        {
            var kept = Array.FindAll(index.plays ?? Array.Empty<PlayIndexEntry>(), p => p.gameId == index.gameId);
            int dropped = (index.plays?.Length ?? 0) - kept.Length;
            if (dropped > 0) Debug.LogWarning($"[PlayLoader] Ignoring {dropped} index entries from other games.");
            index.plays = kept;
        }

        /// <summary>Optional: without suggested plays the app still replays recordings.</summary>
        async Awaitable LoadSuggestedListAsync()
        {
            suggested.Clear();
            try
            {
                SuggestedList list = await Source.LoadSuggestedListAsync();
                if (list?.plays != null) suggested.AddRange(list.plays);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayLoader] No suggested plays: {e.Message}");
            }
        }

        public bool HasSuggested(string playId) => suggested.Exists(s => s.playId == playId);

        public void Next() => _ = LoadAtAsync(Wrap(CurrentIndex + 1));
        public void Previous() => _ = LoadAtAsync(Wrap(CurrentIndex - 1));

        /// <summary>Switch between the current recorded play and the model's suggested version.
        /// From a suggested play this always returns to the recorded play at CurrentIndex.</summary>
        public void ToggleSuggested()
        {
            if (CurrentSuggestedId != null)
            {
                _ = LoadAtAsync(CurrentIndex);
                return;
            }
            string id = CurrentIndex >= 0 && CurrentIndex < Plays.Count ? Plays[CurrentIndex].id : null;
            if (id != null && HasSuggested(id))
                _ = LoadSuggestedAsync(id);
            else
                Fail(suggested.Count == 0
                    ? "The API has no suggested plays (see SUGGESTED_PATH in its README)."
                    : $"No suggested version of {id} (the model only reviews pass plays). G browses the {suggested.Count} that exist.");
        }

        /// <summary>Step through the suggested plays, whether or not they are in the play index.</summary>
        public void NextSuggested(int direction)
        {
            if (suggested.Count == 0)
            {
                Fail("The API has no suggested plays (see SUGGESTED_PATH in its README).");
                return;
            }
            int at = CurrentSuggestedIndex;
            int next = at < 0
                ? (direction > 0 ? 0 : suggested.Count - 1)
                : ((at + direction) % suggested.Count + suggested.Count) % suggested.Count;
            _ = LoadSuggestedAsync(suggested[next].playId);
        }

        public async Awaitable LoadAtAsync(int i)
        {
            if (Plays.Count == 0) return;
            i = Wrap(i);
            string id = Plays[i].id;
            CurrentIndex = i;
            CurrentSuggestedId = null;
            await LoadAsync(id, $"play {id}", () => Source.LoadPlayAsync(id));
        }

        public async Awaitable LoadSuggestedAsync(string playId)
        {
            // Follow along in the play list so the navigator and Real point at the same play.
            int i = IndexOf(playId);
            if (i >= 0) CurrentIndex = i;
            CurrentSuggestedId = playId;
            await LoadAsync($"suggested:{playId}", $"suggested play {playId}", () => Source.LoadSuggestedAsync(playId));
        }

        async Awaitable LoadAsync(string cacheKey, string label, Func<Awaitable<PlayData>> fetch)
        {
            int version = ++requestVersion;

            if (!cache.TryGetValue(cacheKey, out var play))
            {
                IsLoading = true;
                try
                {
                    play = await fetch();
                }
                catch (Exception e)
                {
                    if (version == requestVersion) IsLoading = false;
                    Fail($"Could not load {label}: {e.Message}");
                    return;
                }
                cache[cacheKey] = play;
            }

            // A newer request started while this one was in flight; drop this result.
            if (version != requestVersion) return;
            IsLoading = false;

            if (play == null || play.schemaVersion != SchemaVersion)
            {
                Fail($"{label} has schemaVersion {play?.schemaVersion}, this client reads {SchemaVersion}.");
                return;
            }
            if (play.frames == null || play.frames.Length == 0)
            {
                Fail($"{label} has no frames.");
                return;
            }
            PlayLoaded?.Invoke(play);
        }

        int IndexOf(string playId)
        {
            for (int i = 0; i < Plays.Count; i++)
                if (Plays[i].id == playId) return i;
            return -1;
        }

        int Wrap(int i) => Plays.Count == 0 ? 0 : ((i % Plays.Count) + Plays.Count) % Plays.Count;

        void Fail(string message)
        {
            Debug.LogError($"[PlayLoader] {message}");
            LoadFailed?.Invoke(message);
        }
    }
}
