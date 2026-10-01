#!/usr/bin/env python3
"""Export NFL Big Data Bowl 2025 plays to the Hindsight play JSON format.

Reads the raw CSVs in Data/raw/ and writes one JSON file per play to
Assets/StreamingAssets/plays/{gameId}_{playId}.json plus an index.json listing them.
The JSON schema is documented in CLAUDE.md; the future backend serves the same format.

Coordinates are normalized so the offense always moves toward +x (left to right):
for plays with playDirection == "left", x -> 120 - x, y -> 160/3 - y, o/dir -> +180 deg.

Usage:
    python3 tools/export_plays.py                       # default game + plays
    python3 tools/export_plays.py --game 2022090800     # auto-pick 5 pass plays
    python3 tools/export_plays.py --game 2022090800 --plays 56 2979
"""

import argparse
import json
import math
from pathlib import Path

import pandas as pd

SCHEMA_VERSION = 1
FIELD_LENGTH = 120.0
FIELD_WIDTH = 160.0 / 3.0  # 53.33 yards; the data uses this exact width
FRAME_RATE = 10
TRACKING_WEEKS = range(1, 10)

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_RAW = ROOT / "Data" / "raw"
DEFAULT_OUT = ROOT / "Assets" / "StreamingAssets" / "plays"

# LA vs BUF, 2022 season opener: short completion, 53 yd TD, 47 yd deep ball,
# interception, deep incompletion. Mix of both offenses and both play directions.
DEFAULT_GAME = 2022090800
DEFAULT_PLAYS = [56, 2979, 2688, 2908, 2400]

TRACKING_COLS = [
    "gameId", "playId", "nflId", "displayName", "frameId", "frameType",
    "jerseyNumber", "club", "playDirection", "x", "y", "s", "o", "dir", "event",
]


def r2(v):
    """Round to 2 decimals; NaN becomes None (JSON null)."""
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return None
    return round(float(v), 2)


def str_or_none(v):
    return None if pd.isna(v) else str(v)


def load_game_tracking(raw: Path, game_id: int) -> pd.DataFrame:
    """Scan tracking weeks in chunks and return only rows for one game."""
    for week in TRACKING_WEEKS:
        path = raw / f"tracking_week_{week}.csv"
        if not path.exists():
            continue
        parts = []
        for chunk in pd.read_csv(path, usecols=TRACKING_COLS, chunksize=1_000_000):
            hit = chunk[chunk.gameId == game_id]
            if not hit.empty:
                parts.append(hit)
            elif parts:
                break  # rows are grouped by game; we've passed it
        if parts:
            print(f"Found game {game_id} in {path.name}")
            return pd.concat(parts, ignore_index=True)
    raise SystemExit(f"Game {game_id} not found in any tracking_week_*.csv in {raw}")


def pick_pass_plays(plays: pd.DataFrame, count: int) -> list:
    """Pick a varied set of pass plays: interception, deep, incomplete, then short completions."""
    p = plays[plays.passResult.isin(["C", "I", "IN"]) & (plays.playNullifiedByPenalty == "N")]
    picks = []
    for subset in (
        p[p.passResult == "IN"],
        p[p.passLength >= 20].sort_values("passLength", ascending=False),
        p[p.passResult == "I"],
        p[p.passResult == "C"],
    ):
        for pid in subset.playId:
            if pid not in picks:
                picks.append(int(pid))
                break
    for pid in p.playId:
        if len(picks) >= count:
            break
        if int(pid) not in picks:
            picks.append(int(pid))
    return sorted(picks[:count])


def normalize(df: pd.DataFrame) -> pd.DataFrame:
    """Flip plays moving left so the offense always moves toward +x."""
    df = df.copy()
    left = df.playDirection == "left"
    df.loc[left, "x"] = FIELD_LENGTH - df.loc[left, "x"]
    df.loc[left, "y"] = FIELD_WIDTH - df.loc[left, "y"]
    df.loc[left, "o"] = (df.loc[left, "o"] + 180.0) % 360.0
    df.loc[left, "dir"] = (df.loc[left, "dir"] + 180.0) % 360.0
    return df


def build_play(play: pd.Series, game: pd.Series, track: pd.DataFrame, players: pd.DataFrame) -> dict:
    direction = track.playDirection.iloc[0]
    track = normalize(track).sort_values(["frameId", "nflId"])

    # absoluteYardlineNumber is in raw tracking coordinates, so normalize it like x.
    los = float(play.absoluteYardlineNumber)
    if direction == "left":
        los = FIELD_LENGTH - los
    first_down = min(los + float(play.yardsToGo), FIELD_LENGTH - 10.0)  # cap at the goal line

    is_ball = track.displayName == "football"
    people = track[~is_ball]

    roster = (
        people.drop_duplicates("nflId")[["nflId", "displayName", "jerseyNumber", "club"]]
        .merge(players[["nflId", "position"]], on="nflId", how="left")
        .sort_values(["club", "jerseyNumber"])
    )
    player_list = [
        {
            "nflId": int(r.nflId),
            "name": r.displayName,
            "jerseyNumber": int(r.jerseyNumber) if not pd.isna(r.jerseyNumber) else 0,
            "club": r.club,
            "position": str_or_none(r.position) or "",
            "side": "offense" if r.club == play.possessionTeam else "defense",
        }
        for r in roster.itertuples()
    ]

    snap = track[track.frameType == "SNAP"].frameId
    frames = []
    for frame_id, f in track.groupby("frameId", sort=True):
        ball = f[f.displayName == "football"]
        ppl = f[f.displayName != "football"]
        event = f.event.dropna()
        frames.append({
            "frameId": int(frame_id),
            "frameType": f.frameType.iloc[0],
            "event": str(event.iloc[0]) if not event.empty else None,
            "ball": {"x": r2(ball.x.iloc[0]), "y": r2(ball.y.iloc[0]), "s": r2(ball.s.iloc[0])}
            if not ball.empty else None,
            "players": [
                {"nflId": int(r.nflId), "x": r2(r.x), "y": r2(r.y), "o": r2(r.o), "dir": r2(r.dir), "s": r2(r.s)}
                for r in ppl.itertuples()
            ],
        })

    return {
        "schemaVersion": SCHEMA_VERSION,
        "gameId": int(play.gameId),
        "playId": int(play.playId),
        "homeTeam": game.homeTeamAbbr,
        "visitorTeam": game.visitorTeamAbbr,
        "description": play.playDescription,
        "quarter": int(play.quarter),
        "gameClock": play.gameClock,
        "down": int(play.down),
        "yardsToGo": int(play.yardsToGo),
        "possessionTeam": play.possessionTeam,
        "defensiveTeam": play.defensiveTeam,
        "lineOfScrimmage": r2(los),
        "firstDownLine": r2(first_down),
        "passResult": str_or_none(play.passResult) or "",
        "frameRate": FRAME_RATE,
        "snapFrameId": int(snap.iloc[0]) if not snap.empty else 0,
        "players": player_list,
        "frames": frames,
    }


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--raw", type=Path, default=DEFAULT_RAW)
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT)
    ap.add_argument("--game", type=int, default=DEFAULT_GAME)
    ap.add_argument("--plays", type=int, nargs="*", help="playIds to export (default: curated or auto-picked)")
    ap.add_argument("--count", type=int, default=5, help="plays to auto-pick when --plays is omitted")
    args = ap.parse_args()

    games = pd.read_csv(args.raw / "games.csv")
    plays = pd.read_csv(args.raw / "plays.csv")
    players = pd.read_csv(args.raw / "players.csv")

    game_rows = games[games.gameId == args.game]
    if game_rows.empty:
        raise SystemExit(f"gameId {args.game} not in games.csv")
    game = game_rows.iloc[0]
    game_plays = plays[plays.gameId == args.game]

    if args.plays:
        play_ids = args.plays
    elif args.game == DEFAULT_GAME:
        play_ids = DEFAULT_PLAYS
    else:
        play_ids = pick_pass_plays(game_plays, args.count)

    tracking = load_game_tracking(args.raw, args.game)
    args.out.mkdir(parents=True, exist_ok=True)

    index = []
    for pid in play_ids:
        row = game_plays[game_plays.playId == pid]
        track = tracking[tracking.playId == pid]
        if row.empty or track.empty:
            print(f"  skip {pid}: missing play or tracking rows")
            continue
        data = build_play(row.iloc[0], game, track, players)
        play_key = f"{args.game}_{pid}"
        with open(args.out / f"{play_key}.json", "w") as fh:
            json.dump(data, fh, separators=(",", ":"))
        index.append({
            "id": play_key,
            "gameId": data["gameId"],
            "playId": data["playId"],
            "description": data["description"],
            "possessionTeam": data["possessionTeam"],
        })
        print(f"  wrote {play_key}.json ({len(data['frames'])} frames, {len(data['players'])} players)")

    with open(args.out / "index.json", "w") as fh:
        json.dump({"schemaVersion": SCHEMA_VERSION, "plays": index}, fh, indent=2)
    print(f"Wrote index.json with {len(index)} plays to {args.out}")


if __name__ == "__main__":
    main()
