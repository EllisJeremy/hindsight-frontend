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
    }
}
