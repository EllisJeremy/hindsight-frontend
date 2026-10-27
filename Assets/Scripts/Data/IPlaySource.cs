using UnityEngine;

namespace Hindsight.Data
{
    /// <summary>
    /// Where plays come from: the backend API (ApiPlaySource) or JSON files in
    /// StreamingAssets (WebPlaySource, offline). Both serve the same schema.
    /// </summary>
    public interface IPlaySource
    {
        Awaitable<PlayIndex> LoadIndexAsync();
        Awaitable<PlayData> LoadPlayAsync(string playId);
        /// <summary>Plays the model has a suggested version of; empty when the source has none.</summary>
        Awaitable<SuggestedList> LoadSuggestedListAsync();
        /// <summary>The model's suggested version of a play (recorded to the throw, predicted after).</summary>
        Awaitable<PlayData> LoadSuggestedAsync(string playId);
    }
}
