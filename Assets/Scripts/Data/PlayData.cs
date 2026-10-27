using System;

namespace Hindsight.Data
{
    // Data transfer objects for the play JSON format (schemaVersion 1).
    // Field names match the JSON exactly so JsonUtility can deserialize them.
    // The schema is documented in CLAUDE.md; keep both in sync.

    /// <summary>One play: metadata, roster, and the full frame-by-frame tracking.</summary>
    [Serializable]
    public class PlayData
    {
        public int schemaVersion;
        public long gameId;
        public int playId;
        public string homeTeam;
        public string visitorTeam;
        public string description;
        public int quarter;
        public string gameClock;
        /// <summary>1-based drive within the game (derived from possession changes).</summary>
        public int driveNumber;
        public int down;
        public int yardsToGo;
        public string possessionTeam;
        public string defensiveTeam;
        /// <summary>Normalized field x of the line of scrimmage (offense moves toward +x).</summary>
        public float lineOfScrimmage;
        /// <summary>Normalized field x of the line to gain, capped at the goal line (110).</summary>
        public float firstDownLine;
        /// <summary>C, I, IN, S, or R from plays.csv.</summary>
        public string passResult;
        /// <summary>Tracking samples per second (10 for NGS data).</summary>
        public int frameRate;
        public int snapFrameId;
        public PlayerInfo[] players;
        public FrameData[] frames;

        // Only on the model's suggested plays (GET /plays/{id}/suggested); absent, so
        // false/0/empty, on recorded plays.
        /// <summary>True when this is the model's suggested version of a play, not a recording.</summary>
        public bool suggested;
        /// <summary>First frameId that is the model's prediction; earlier frames are recorded.</summary>
        public int simulationStartFrameId;
        /// <summary>The recorded play's description (description holds the model's reason).</summary>
        public string realPlayDescription;
        public Recommendation recommendation;
    }

    /// <summary>The model's verdict on a suggested play.</summary>
    [Serializable]
    public class Recommendation
    {
        /// <summary>e.g. "consider_alternative".</summary>
        public string decision;
        /// <summary>The receiver the model recommends throwing to.</summary>
        public long targetNflId;
        /// <summary>The receiver the ball actually went to.</summary>
        public long targetedNflId;
        /// <summary>The model's explanation in plain English.</summary>
        public string reason;
        /// <summary>How much more open the recommended receiver was predicted to be, in yards.</summary>
        public float separationAdvantageYards;
        public string caveat;
    }

    /// <summary>Plays the model has a suggested version of (GET /suggested).</summary>
    [Serializable]
    public class SuggestedList
    {
        public SuggestedEntry[] plays;
    }

    [Serializable]
    public class SuggestedEntry
    {
        /// <summary>"{gameId}_{playId}", used to fetch the suggested play.</summary>
        public string playId;
        public string decision;
        public long targetNflId;
        public string reason;
    }

    [Serializable]
    public class PlayerInfo
    {
        public long nflId;
        public string name;
        public int jerseyNumber;
        public string club;
        public string position;
        /// <summary>"offense" or "defense".</summary>
        public string side;

        /// <summary>"#17", or the position when the source has no jersey numbers (suggested plays).</summary>
        public string NumberLabel => jerseyNumber > 0 ? $"#{jerseyNumber}" : position;
    }

    [Serializable]
    public class FrameData
    {
        public int frameId;
        /// <summary>BEFORE_SNAP, SNAP, or AFTER_SNAP.</summary>
        public string frameType;
        /// <summary>Tracking event tagged on this frame (e.g. ball_snap, pass_forward), or null.</summary>
        public string @event;
        public BallState ball;
        public PlayerState[] players;
    }

    [Serializable]
    public class PlayerState
    {
        public long nflId;
        public float x;
        public float y;
        /// <summary>Orientation in degrees: 0 = +y, clockwise (90 = +x).</summary>
        public float o;
        /// <summary>Direction of motion in degrees, same convention as o.</summary>
        public float dir;
        /// <summary>Speed in yards/second.</summary>
        public float s;
    }

    [Serializable]
    public class BallState
    {
        public float x;
        public float y;
        public float s;
    }

    /// <summary>A game's plays in order (index.json / GET /plays).</summary>
    [Serializable]
    public class PlayIndex
    {
        public int schemaVersion;
        public long gameId;
        public string homeTeam;
        public string visitorTeam;
        /// <summary>Ordered by playId (chronological).</summary>
        public PlayIndexEntry[] plays;
    }

    /// <summary>Games the backend has loaded (GET /games).</summary>
    [Serializable]
    public class GameList
    {
        public int schemaVersion;
        public GameSummary[] games;
    }

    [Serializable]
    public class GameSummary
    {
        public long gameId;
        public string homeTeam;
        public string visitorTeam;
        public int playCount;
    }

    [Serializable]
    public class PlayIndexEntry
    {
        /// <summary>"{gameId}_{playId}", used to fetch the play.</summary>
        public string id;
        public long gameId;
        public int playId;
        public int quarter;
        public string gameClock;
        public int driveNumber;
        public string possessionTeam;
        public int down;
        public int yardsToGo;
        /// <summary>pass | run | sack | scramble | kneel | spike.</summary>
        public string playType;
        public string passResult;
        public int yardsGained;
        public bool touchdown;
        public string description;
    }
}
