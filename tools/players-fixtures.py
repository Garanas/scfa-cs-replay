#!/usr/bin/env python3
"""Writes the JSON:API fixtures of the sample Players API (SamplePlayersApi).

    python -I tools/players-fixtures.py

The output goes to src/FAForever.Vault.Viewer/Services/Players/Sample/ (embedded in the Viewer). Everything is
fictional and seeded (SEED below; rng draws the games, the people and the league, so a change to the order of
its draws changes every file after it), so a run always writes the same files. The documents follow the shape api.faforever.com
returns (data, included, meta.page, camelCase attributes, relationships with data {type, id}); the dates are
relative to ANCHOR, and the sample API shifts them to the present when it reads them.
"""
import json
import math
import os
import random
import uuid
from datetime import datetime, timedelta, timezone

ANCHOR = datetime(2026, 10, 16, 12, 0, 0, tzinfo=timezone.utc)
PLAYER_ID = 2049311
PLAYER = "Ashgrove"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "FAForever.Vault.Viewer", "Services", "Players", "Sample")

SEED = 20261016
rng = random.Random(SEED)


def iso(moment):
    return moment.strftime("%Y-%m-%dT%H:%M:%SZ")


def ref(kind, ident):
    return {"type": kind, "id": str(ident)}


def res(kind, ident, attrs=None, rels=None):
    resource = {"type": kind, "id": str(ident)}
    if attrs is not None:
        resource["attributes"] = attrs
    if rels:
        resource["relationships"] = {
            name: {"data": [ref(*item) for item in target] if isinstance(target, list) else ref(*target)}
            for name, target in rels.items()
        }
    return resource


class Doc:
    def __init__(self):
        self.data = []
        self.included = {}
        self.meta = None

    def add(self, resource):
        self.data.append(resource)

    def include(self, resource):
        self.included.setdefault((resource["type"], resource["id"]), resource)

    def page(self, records, size):
        self.meta = {"page": {"totalRecords": records, "totalPages": max(1, math.ceil(records / size)), "number": 1, "limit": size}}

    def dump(self):
        body = {"data": self.data}
        if self.included:
            body["included"] = list(self.included.values())
        if self.meta:
            body["meta"] = self.meta
        return body


def write(name, body):
    path = os.path.join(OUT, name)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(body, handle, separators=(",", ":"), ensure_ascii=False)
    print(f"{name}: {os.path.getsize(path) // 1024} KB")


# ---------------------------------------------------------------- people

ADJECTIVES = ["Grim", "Quiet", "Iron", "Swift", "Amber", "Frost", "Hollow", "Rustic", "Violet", "Lunar", "Brisk", "Cobalt", "Dusty", "Echo", "Feral", "Gilded"]
NOUNS = ["Fox", "Anvil", "Harbor", "Falcon", "Comet", "Rook", "Thistle", "Gale", "Ember", "Lynx", "Pylon", "Quartz", "Raven", "Sable", "Tundra", "Willow"]
CLAN_TAGS = [None, None, None, "TNK", "RFS", "GDA", "OPS", None]

opponents = []
used = set()
while len(opponents) < 60:
    name = f"{rng.choice(ADJECTIVES)}{rng.choice(NOUNS)}{rng.choice(['', '', str(rng.randint(2, 99)), '_NL', '_X'])}"
    if name in used or name == PLAYER:
        continue
    used.add(name)
    opponents.append({"id": 100100 + len(opponents) * 37, "login": name, "tag": rng.choice(CLAN_TAGS), "rating": rng.gauss(1180, 330)})

MAPS = [
    ("Theta Passage", "theta_passage.v0001"), ("Dual Gap", "dual_gap.v0004"), ("Emerald Crater", "emerald_crater.v0003"),
    ("Open Palms", "open_palms.v0002"), ("Crossfire Canal", "crossfire_canal.v0006"), ("Gap of Rohan", "gap_of_rohan.v0002"),
    ("Regor VI Highlands", "regor_vi_highlands.v0001"), ("Mirage", "mirage.v0005"), ("Sentry Point", "sentry_point.v0003"),
    ("Fields of Isis", "fields_of_isis.v0007"), ("Syrtis Major", "syrtis_major.v0003"), ("Wonder", "wonder.v0004"),
    ("Burial Mounds", "burial_mounds.v0002"), ("Eye of the Storm", "eye_of_the_storm.v0005"),
]
LARGE_MAPS = [MAPS[5], MAPS[6], MAPS[11], MAPS[12], MAPS[13], MAPS[9]]

# the six boards of the live API (2026-10-16)
BOARDS = [("global", 1), ("ladder_1v1", 2), ("tmm_2v2", 3), ("tmm_4v4_full_share", 4), ("tmm_4v4_share_until_death", 5), ("tmm_3v3", 6)]
BOARD_ID = dict(BOARDS)

# ---------------------------------------------------------------- games

counter = {"game": 24_310_000, "stats": 91_000_000, "journal": 55_000_000, "mapVersion": 0}
map_version_ids = {folder: 7000 + index for index, (_, folder) in enumerate(MAPS)}
map_ids = {folder: 3000 + index for index, (_, folder) in enumerate(MAPS)}


def sample_time(start, end):
    """A start time with more games on weekends and in the evening."""
    while True:
        moment = start + timedelta(seconds=rng.uniform(0, (end - start).total_seconds()))
        weight = 1.0 if moment.weekday() < 5 else 1.5
        hour_weight = [0.2, 0.1, 0.05, 0.03, 0.03, 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.7, 0.8, 0.9, 1.0, 1.1, 1.3, 1.5, 1.6, 1.5, 1.0, 0.5][moment.hour]
        if rng.random() < weight * hour_weight / 2.4:
            return moment


def length_minutes(mode):
    base = {"ladder_1v1": 17, "tmm_2v2": 24, "tmm_4v4_full_share": 36, "global": 28}[mode]
    return max(6, rng.lognormvariate(math.log(base), 0.38))


FACTION_WEIGHTS = [(3, 45), (1, 25), (2, 15), (4, 15)]  # Cybran, UEF, Aeon, Seraphim (faction indexes)


def pick_faction():
    return rng.choices([f for f, _ in FACTION_WEIGHTS], [w for _, w in FACTION_WEIGHTS])[0]


class Rater:
    """A rating that walks with the games: mean, deviation and the K factor of its board."""

    def __init__(self, mean, deviation, k):
        self.mean, self.deviation, self.k = mean, deviation, k

    @property
    def display(self):
        return self.mean - 3 * self.deviation


def make_games():
    # (mode, count, opponent spread, K)
    plan = [("ladder_1v1", 200, 110, 22), ("tmm_2v2", 45, 160, 18), ("tmm_4v4_full_share", 25, 220, 14), ("global", 70, 260, 20)]
    start = ANCHOR - timedelta(days=365)
    games = []
    for mode, count, spread, _ in plan:
        for _ in range(count):
            games.append({"mode": mode, "time": sample_time(start, ANCHOR - timedelta(hours=2)), "spread": spread})
    games.sort(key=lambda game: game["time"])

    raters = {
        "ladder_1v1": Rater(1385, 68, 22), "tmm_2v2": Rater(1290, 82, 18), "tmm_4v4_full_share": Rater(1240, 95, 14), "global": Rater(1810, 52, 20),
    }
    for game in games:
        mode = game["mode"]
        rater = raters[mode]
        counter["game"] += rng.randint(30, 400)
        counter["stats"] += 1
        game["id"] = counter["game"]
        game["own_stats"] = counter["stats"]
        game["minutes"] = length_minutes(mode)
        game["map"] = rng.choice(MAPS if mode in ("ladder_1v1", "tmm_2v2", "global") else LARGE_MAPS)
        game["faction"] = pick_faction()
        size = {"ladder_1v1": 1, "tmm_2v2": 2, "tmm_4v4_full_share": 4, "global": rng.choice([1, 1, 2, 3, 4, 6])}[mode]
        game["size"] = size

        mates = rng.sample(opponents, size - 1)
        foes = rng.sample([o for o in opponents if o not in mates], size)
        game["mates"], game["foes"] = mates, foes
        foe_display = rater.display + rng.gauss(0, game["spread"])
        mate_display = rater.display + rng.gauss(0, game["spread"])
        team_gap = (sum(o["rating"] for o in mates) + rater.display) / size - sum(o["rating"] for o in foes) / size
        expected = 1 / (1 + 10 ** (-(rater.display - foe_display + team_gap * 0.2) / 400)) if size == 1 else 1 / (1 + 10 ** (-team_gap / 400))
        win = rng.random() < min(0.9, max(0.1, expected * 0.9 + 0.08))
        game["win"] = win
        change = rater.k * ((1 if win else 0) - expected)
        before = (rater.mean, rater.deviation)
        new_deviation = max(46.0, rater.deviation * 0.997)
        new_mean = rater.mean + change
        rater.mean, rater.deviation = new_mean, new_deviation
        game["before"], game["after"] = before, (new_mean, new_deviation)
        game["end"] = game["time"] + timedelta(minutes=game["minutes"] + 1)
        game["scoreTime"] = game["time"] + timedelta(minutes=game["minutes"])
        game["score"] = int(game["minutes"] * rng.uniform(8, 30) + (60 if win else 0))
        game["foe_ratings"] = [(max(300, o["rating"] + rng.gauss(0, 20)), rng.uniform(55, 120)) for o in foes]
        game["mate_ratings"] = [(max(300, o["rating"] + rng.gauss(0, 20)), rng.uniform(55, 120)) for o in mates]
    return games, raters


games, raters = make_games()

# two rivals: some ladder games are against them (the head-to-head fixture), everywhere the same
RIVAL, OTHER_RIVAL = opponents[5], opponents[11]
head_to_head_games = []
for who, wanted in ((RIVAL, 11), (OTHER_RIVAL, 4)):
    for game in rng.sample([g for g in games if g["mode"] == "ladder_1v1" and g not in head_to_head_games], wanted):
        game["foes"] = [who]
        game["foe_ratings"] = [(who["rating"] + 40, 62.0)]
        head_to_head_games.append(game)


def other_stats(game, who, team, result, ratings):
    stats = []
    for person, (mean_display, deviation) in zip(who, ratings):
        counter["stats"] += 1
        stats.append({
            "id": counter["stats"], "player": person, "team": team, "faction": pick_faction(), "result": result,
            "beforeMean": round(mean_display + 3 * deviation, 2), "beforeDeviation": round(deviation, 2),
        })
    return stats


def build_game_resources(doc, game):
    """Adds the game, its map, its mod and every participant's statistics to the document's includes."""
    mode = game["mode"]
    own_team, foe_team = (2, 3) if mode != "global" or game["size"] > 1 else (1, 1)
    win = game["win"]
    if "others" not in game:
        game["others"] = other_stats(game, game["mates"], own_team, "VICTORY" if win else "DEFEAT", game["mate_ratings"]) \
            + other_stats(game, game["foes"], foe_team, "DEFEAT" if win else "VICTORY", game["foe_ratings"])
    map_name, folder = game["map"]
    version = map_version_ids[folder]
    mod_id, mod_name, mod_tech = (3, "Ladder 1v1", "ladder1v1") if mode == "ladder_1v1" else (1, "FAF", "faf")
    stats_refs = [("gamePlayerStats", game["own_stats"])] + [("gamePlayerStats", s["id"]) for s in game["others"]]

    doc.include(res("game", game["id"], {
        "name": game_name(game), "startTime": iso(game["time"]), "endTime": iso(game["end"]),
        "replayTicks": int(game["minutes"] * 600), "validity": "VALID",
    }, {"mapVersion": ("mapVersion", version), "featuredMod": ("featuredMod", mod_id), "playerStats": stats_refs}))
    doc.include(res("mapVersion", version, {"folderName": folder}, {"map": ("map", map_ids[folder])}))
    doc.include(res("map", map_ids[folder], {"displayName": map_name}))
    doc.include(res("featuredMod", mod_id, {"displayName": mod_name, "technicalName": mod_tech}))

    # the other players' statistics, and the player's own as a participant of the game
    for s in game["others"]:
        doc.include(res("gamePlayerStats", s["id"], {
            "team": s["team"], "faction": s["faction"], "result": s["result"], "ai": False,
            "beforeMean": s["beforeMean"], "beforeDeviation": s["beforeDeviation"],
        }, {"player": ("player", s["player"]["id"]), "game": ("game", game["id"])}))
        doc.include(res("player", s["player"]["id"], {"login": s["player"]["login"]}))
    doc.include(own_stats_resource(game))
    doc.include(res("player", PLAYER_ID, {"login": PLAYER}))


def game_name(game):
    return {"ladder_1v1": f"Ladder game {game['id']}", "tmm_2v2": "Matchmaker 2v2", "tmm_4v4_full_share": "Matchmaker 4v4"}.get(game["mode"], rng_name(game))


CUSTOM_NAMES = ["Friday night skirmish", "Come play, 1000+", "No rush 20", "Teams, balanced pls", "Casual, all welcome", "Experimentals only"]


def rng_name(game):
    return CUSTOM_NAMES[game["id"] % len(CUSTOM_NAMES)]


def own_stats_resource(game):
    own_team = 2 if game["mode"] != "global" or game["size"] > 1 else 1
    attributes = {
        "team": own_team, "faction": game["faction"], "result": "VICTORY" if game["win"] else "DEFEAT", "ai": False,
        "score": game["score"],
        "scoreTime": iso(game["scoreTime"]),
        "beforeMean": round(game["before"][0], 2), "beforeDeviation": round(game["before"][1], 2),
        "afterMean": round(game["after"][0], 2), "afterDeviation": round(game["after"][1], 2),
    }
    return res("gamePlayerStats", game["own_stats"], attributes, {"player": ("player", PLAYER_ID), "game": ("game", game["id"])})


def own_stats_with_journal(game, doc):
    counter["journal"] += 1
    game["journal_id"] = counter["journal"]
    board = game["mode"]
    journal = res("leaderboardRatingJournal", game["journal_id"], {
        "meanBefore": round(game["before"][0], 2), "deviationBefore": round(game["before"][1], 2),
        "meanAfter": round(game["after"][0], 2), "deviationAfter": round(game["after"][1], 2), "createTime": iso(game["scoreTime"]),
    }, {"leaderboard": ("leaderboard", BOARD_ID[board]), "gamePlayerStats": ("gamePlayerStats", game["own_stats"])})
    return journal


# ---------------------------------------------------------------- journal fixtures

for board, _ in BOARDS:
    board_games = [g for g in games if g["mode"] == board]
    if not board_games:
        continue
    doc = Doc()
    for game in board_games:
        build_game_resources(doc, game)
        doc.add(own_stats_with_journal(game, doc))
    doc.page(len(doc.data), 100)
    write(f"rating-journal-{board}.json", doc.dump())

# ---------------------------------------------------------------- matches (the 120 latest)

latest = sorted(games, key=lambda g: g["time"], reverse=True)[:120]
doc = Doc()
for game in latest:
    build_game_resources(doc, game)
    stats = own_stats_resource(game)
    stats["relationships"]["ratingChanges"] = {"data": [ref("leaderboardRatingJournal", game["journal_id"])]}
    doc.add(stats)
    doc.include(res("leaderboardRatingJournal", game["journal_id"], {
        "meanBefore": round(game["before"][0], 2), "deviationBefore": round(game["before"][1], 2),
        "meanAfter": round(game["after"][0], 2), "deviationAfter": round(game["after"][1], 2),
    }, {"leaderboard": ("leaderboard", BOARD_ID[game["mode"]])}))
    doc.include(res("leaderboard", BOARD_ID[game["mode"]], {"technicalName": game["mode"]}))
doc.page(len(doc.data), 25)
write("matches.json", doc.dump())

# ---------------------------------------------------------------- profile, search

CLAN = res("clan", 4417, {"name": "Iron Harvest", "tag": "IHV", "tagColor": "#c9a227", "createTime": "2022-02-01T10:00:00Z"})
MEMBERSHIP = res("clanMembership", 88120, {"createTime": "2022-03-12T19:00:00Z"}, {"clan": ("clan", 4417), "player": ("player", PLAYER_ID)})
PLAYER_NAMES = [
    res("nameRecord", 701, {"name": "grovestomp", "changeTime": "2023-01-09T20:14:00Z"}, {"player": ("player", PLAYER_ID)}),
    res("nameRecord", 702, {"name": "Ash_Grove", "changeTime": "2024-03-30T08:31:00Z"}, {"player": ("player", PLAYER_ID)}),
    res("nameRecord", 703, {"name": PLAYER, "changeTime": "2025-06-02T17:45:00Z"}, {"player": ("player", PLAYER_ID)}),
]


def avatar_resources(player_id, assignment_id, avatar_id, filename, tooltip, selected_one=True):
    """An avatar the player owns and wears (selected) and one owned but not worn: the current avatar is the selected assignment."""
    worn = res("avatarAssignment", assignment_id, {"selected": selected_one, "expiresAt": None, "createTime": "2022-04-01T10:00:00Z", "updateTime": "2024-01-15T18:30:00Z"},
               {"avatar": ("avatar", avatar_id), "player": ("player", player_id)})
    spare = res("avatarAssignment", assignment_id - 1, {"selected": False, "expiresAt": None, "createTime": "2021-09-01T10:00:00Z", "updateTime": "2022-04-01T10:00:00Z"},
                {"avatar": ("avatar", avatar_id - 1), "player": ("player", player_id)})
    return [
        worn, spare,
        res("avatar", avatar_id, {"url": f"https://content.faforever.com/faf/avatars/{filename}", "tooltip": tooltip, "filename": filename, "description": tooltip}),
        res("avatar", avatar_id - 1, {"url": "https://content.faforever.com/faf/avatars/bronze_star.png", "tooltip": "Bronze star", "filename": "bronze_star.png", "description": "Bronze star"}),
    ]


def profile_document(person_id, login, created, name_records, clan, membership, avatars):
    doc = Doc()
    rels = {"names": [("nameRecord", n["id"]) for n in name_records], "avatarAssignments": [("avatarAssignment", avatars[0]["id"]), ("avatarAssignment", avatars[1]["id"])]}
    if membership:
        rels["clanMembership"] = ("clanMembership", membership["id"])
    doc.add(res("player", person_id, {"login": login, "createTime": created, "updateTime": iso(ANCHOR - timedelta(hours=3))}, rels))
    for record in name_records:
        doc.include(record)
    if membership:
        doc.include(membership)
        doc.include(clan)
    for resource in avatars:
        doc.include(resource)
    return doc


write("player-2049311.json", profile_document(
    PLAYER_ID, PLAYER, "2021-05-14T21:03:00Z", PLAYER_NAMES, CLAN, MEMBERSHIP,
    avatar_resources(PLAYER_ID, 5501, 61, "bronze_commander.png", "Veteran commander")).dump())

# profiles of the two rivals (opponents of the head-to-head fixture): the compare and name search of the sample resolve them
for rival, created, avatar_file, tooltip in ((RIVAL, "2020-02-02T09:12:00Z", "silver_sword.png", "Silver sword"), (OTHER_RIVAL, "2022-08-19T14:40:00Z", "gold_anvil.png", "Gold anvil")):
    names = [res("nameRecord", 7000 + rival["id"] % 1000 * 2 + n, {"name": name, "changeTime": changed}, {"player": ("player", rival["id"])})
             for n, (name, changed) in enumerate(((f"old_{rival['login'].lower()}", "2023-05-05T20:00:00Z"), (rival["login"], "2024-07-07T20:00:00Z")))]
    clan = res("clan", 4400 + sum(map(ord, rival["tag"])), {"name": f"Clan {rival['tag']}", "tag": rival["tag"]}) if rival["tag"] else None
    membership = res("clanMembership", 90000 + rival["id"], {}, {"clan": ("clan", clan["id"]), "player": ("player", rival["id"])}) if clan else None
    write(f"player-{rival['id']}.json", profile_document(
        rival["id"], rival["login"], created, names, clan, membership,
        avatar_resources(rival["id"], 5600 + rival["id"] % 100 * 2 + 1, 80 + rival["id"] % 10 * 2 + 1, avatar_file, tooltip)).dump())

fixture_players = [{"id": PLAYER_ID, "login": PLAYER, "tag": "IHV"}] + [{"id": o["id"], "login": o["login"], "tag": o["tag"]} for o in opponents[:7]] \
    + [{"id": OTHER_RIVAL["id"], "login": OTHER_RIVAL["login"], "tag": OTHER_RIVAL["tag"]}]
former_names = {PLAYER_ID: ["grovestomp", "Ash_Grove"], opponents[1]["id"]: ["OldHarbor"], opponents[3]["id"]: ["Anvil_Bro", "rookie_one"], OTHER_RIVAL["id"]: ["PylonRustic", "rusty_pylon_nl"]}

players_doc = Doc()
names_doc = Doc()
for person in fixture_players:
    rels = {}
    if person["tag"]:
        clan_id = 4400 + sum(map(ord, person["tag"]))
        rels["clanMembership"] = ("clanMembership", clan_id)
        players_doc.include(res("clanMembership", clan_id, {}, {"clan": ("clan", clan_id)}))
        players_doc.include(res("clan", clan_id, {"name": f"Clan {person['tag']}", "tag": person["tag"]}))
        names_doc.include(res("clanMembership", clan_id, {}, {"clan": ("clan", clan_id)}))
        names_doc.include(res("clan", clan_id, {"name": f"Clan {person['tag']}", "tag": person["tag"]}))
    player_resource = res("player", person["id"], {"login": person["login"]}, rels)
    players_doc.add(player_resource)
    names_doc.include(player_resource)
    for index, name in enumerate(former_names.get(person["id"], [])):
        names_doc.add(res("nameRecord", 800 + person["id"] % 1000 * 3 + index, {"name": name, "changeTime": "2023-06-01T10:00:00Z"}, {"player": ("player", person["id"])}))
write("search-players.json", players_doc.dump())
write("search-names.json", names_doc.dump())

# ---------------------------------------------------------------- leaderboards, ratings

doc = Doc()
for board, ident in BOARDS:
    doc.add(res("leaderboard", ident, {"technicalName": board, "nameKey": f"leaderboard.{board}.name", "descriptionKey": f"leaderboard.{board}.description"}))
write("leaderboards.json", doc.dump())

LIFETIME = {"global": 1380, "ladder_1v1": 1710, "tmm_2v2": 410, "tmm_4v4_full_share": 220}
doc = Doc()
for board, ident in BOARDS[:4]:
    rater = raters[board]
    year = sum(1 for g in games if g["mode"] == board)
    total = LIFETIME[board] + year
    last = max((g for g in games if g["mode"] == board), key=lambda g: g["time"])
    doc.add(res("leaderboardRating", 600000 + ident, {
        "mean": round(rater.mean, 2), "deviation": round(rater.deviation, 2), "rating": round(rater.display, 2),
        "totalGames": total, "wonGames": int(total * (0.54 if board != "global" else 0.51)),
        "createTime": "2021-06-01T10:00:00Z", "updateTime": iso(last["scoreTime"]),
    }, {"player": ("player", PLAYER_ID), "leaderboard": ("leaderboard", ident)}))
    doc.include(res("leaderboard", ident, {"technicalName": board}))
write("ratings.json", doc.dump())

# ---------------------------------------------------------------- events

event_counts = {
    "d6a699b7-99bc-4a7f-b128-15e1e289a7b3": 2236,
    "3ebb0c4d-5e92-4446-bf52-d17ba9c5cd3c": 61450, "225e9b2e-ae09-4ae1-a198-eca8780b0fcd": 38210,
    "ea123d7f-bb2e-4a71-bd31-88859f0c3c00": 188930, "a1a3fd33-abe2-4e56-800a-b72f4c925825": 121400,
    "b5265b42-1747-4ba1-936c-292202637ce6": 14320, "3a7b3667-0f79-4ac7-be63-ba841fd5ef05": 9110,
    "a8ee4f40-1e30-447b-bc2c-b03065219795": 142_300, "3dd3ed78-ce78-4006-81fd-10926738fbf3": 98_700,
    "89d4f391-ed2d-4beb-a1ca-6b93db623c04": 63_900, "aebd750b-770b-4869-8e37-4d4cfdc480d0": 41_250,
    "92617974-8c1f-494d-ab86-65c2a95d1486": 17_480, "7f15c2be-80b7-4573-8f41-135f84773e0f": 10_990,
    "ed9fd79d-5ec7-4243-9ccf-f18c4f5baef1": 1_240, "701ca426-0943-4931-85af-6a08d36d9aaa": 835,
    "60bb1fc0-601b-45cd-bd26-83b1a1ac979b": 22_600, "e8e99a68-de1b-4676-860d-056ad2207119": 15_280,
    "96ccc66a-c5a0-4f48-acaa-888b00778b57": 642, "a6b51c26-64e6-4e7a-bda7-ea1cfe771ebb": 338,
    "ad193982-e7ca-465c-80b0-5493f9739559": 1_788, "56b06197-1890-42d0-8b59-25e1add8dc9a": 1_103,
    "1b900d26-90d2-43d0-a64e-ed90b74c3704": 834, "7be6fdc5-7867-4467-98ce-f7244a66625a": 481,
    "fefcb392-848f-4836-9683-300b283bc308": 486, "15b6c19a-6084-4e82-ada9-6c30e282191f": 302,
}
EVENT_NAMES = {
    "d6a699b7-99bc-4a7f-b128-15e1e289a7b3": "LOST_ACUS",
}
FACTION_PLAYS = ["ad193982-e7ca-465c-80b0-5493f9739559", "1b900d26-90d2-43d0-a64e-ed90b74c3704", "96ccc66a-c5a0-4f48-acaa-888b00778b57", "fefcb392-848f-4836-9683-300b283bc308"]
# A real player has no rows for "Ranked 1v1 games played", "Custom games played" and "Seconds played" (the
# game server stopped recording them), so the plays of the four factions are the one lifetime number.
LIFETIME_GAMES = sum(event_counts[key] for key in FACTION_PLAYS)
assert LIFETIME_GAMES == 3750, "the army tab and the compare count the same games"
doc = Doc()
for index, (event_id, count) in enumerate(event_counts.items()):
    doc.add(res("playerEvent", 900000 + index, {"currentCount": count}, {"player": ("player", PLAYER_ID), "event": ("event", event_id)}))
    doc.include(res("event", event_id, {"name": EVENT_NAMES.get(event_id, f"EVENT_{index}"), "type": "NUMERIC", "imageUrl": None}))
write("events.json", doc.dump())

# ---------------------------------------------------------------- achievements

ACHIEVEMENTS = [
    ("First Contact", "Play your first game.", "STANDARD", 0, 5, 40_100, 92.4),
    ("Commander in Chief", "Win 25 games.", "INCREMENTAL", 25, 25, 31_480, 71.6),
    ("Fortress Builder", "Build 50 point defences in one game.", "INCREMENTAL", 50, 40, 12_900, 29.3),
    ("Air Superiority", "Win a game with more than 400 air units built.", "STANDARD", 0, 60, 9_320, 21.2),
    ("Naval Doctrine", "Win 10 naval games.", "INCREMENTAL", 10, 50, 7_050, 16.0),
    ("Experimental Thinker", "Build an experimental unit in 100 games.", "INCREMENTAL", 100, 75, 4_410, 10.0),
    ("Unbroken", "Win ten ladder games in a row.", "STANDARD", 0, 100, 1_980, 4.5),
    ("Long Haul", "Play a game that lasts more than two hours.", "STANDARD", 0, 80, 3_120, 7.1),
    ("Seraphim Ascendant", "Win 100 games as Seraphim.", "INCREMENTAL", 100, 120, 1_240, 2.8),
    ("Tide Turner", "Win a game after losing your commander's first base.", "STANDARD", 0, 150, 880, 2.0),
    ("Grand Marshal", "Reach a rating of 2,000.", "STANDARD", 0, 200, 410, 0.9),
    ("Silent Partner", "Win a team game without losing a single unit.", "STANDARD", 0, 250, 96, 0.2),
]
achievement_ids = [str(uuid.uuid5(uuid.NAMESPACE_URL, f"faf-sample/{name}")) for name, *_ in ACHIEVEMENTS]
doc = Doc()
for index, (name, description, kind, steps, xp, unlockers, percent) in enumerate(ACHIEVEMENTS):
    doc.add(res("achievement", achievement_ids[index], {
        "name": name, "description": description, "type": kind, "totalSteps": steps or None,
        "revealedIconUrl": f"https://content.faforever.com/faf/achievements/{index + 1}_revealed.png",
        "unlockedIconUrl": f"https://content.faforever.com/faf/achievements/{index + 1}_unlocked.png",
        "initialState": "REVEALED", "experiencePoints": xp, "order": index + 1,
        "unlockersCount": unlockers, "unlockersPercent": percent,
    }))
write("achievements.json", doc.dump())

progress = [(0, "UNLOCKED", 0), (1, "UNLOCKED", 25), (2, "REVEALED", 31), (3, "UNLOCKED", 0), (4, "UNLOCKED", 10), (5, "REVEALED", 72), (7, "UNLOCKED", 0), (8, "REVEALED", 41), (9, "HIDDEN", 0)]
doc = Doc()
for index, (position, state, steps) in enumerate(progress):
    doc.add(res("playerAchievement", 400000 + index, {
        "currentSteps": steps, "state": state, "createTime": "2022-01-04T12:00:00Z", "updateTime": iso(ANCHOR - timedelta(days=3 + position * 17)),
    }, {"player": ("player", PLAYER_ID), "achievement": ("achievement", achievement_ids[position])}))
write("player-achievements.json", doc.dump())

# ---------------------------------------------------------------- rankings

TOTALS = {"global": 52_410, "ladder_1v1": 8_423, "tmm_2v2": 3_906, "tmm_4v4_full_share": 2_218, "tmm_4v4_share_until_death": 1_104, "tmm_3v3": 1_650}
top_names = []
used_names = set(used)
while len(top_names) < 60:
    name = f"{rng.choice(ADJECTIVES)}{rng.choice(NOUNS)}{rng.choice(['', str(rng.randint(2, 99)), '_x'])}"
    if name not in used_names:
        used_names.add(name)
        top_names.append(name)

# the ranking pages are made by SamplePlayersApi from the distribution below (a row per rank, so every page, board and filter shows other rows)

# the distribution: counts per 100-point bin, as the count requests answer (only meta.page.totalRecords is read)
distribution = {}
for board, _ in BOARDS:
    total = TOTALS[board]
    mean = {"global": 1500, "ladder_1v1": 1050, "tmm_2v2": 1000, "tmm_4v4_full_share": 950, "tmm_4v4_share_until_death": 900, "tmm_3v3": 980}[board]
    weights = [math.exp(-((100 * i + 50 - mean) / 380) ** 2 / 2) for i in range(24)]
    scale = total / sum(weights)
    distribution[board] = [{"data": [], "meta": {"page": {"totalRecords": int(w * scale), "totalPages": int(w * scale)}}} for w in weights]
write("distribution.json", distribution)

# ---------------------------------------------------------------- leagues

SEASON_IDS = [7, 8, 9]  # the ladder 1v1 league's seasons; the sample player plays the running one (the last)
LEAGUES = [  # (id, technicalName, boardTechnicalName, season id of the first of three seasons)
    (1, "1v1_league", "ladder_1v1", 7), (2, "2v2_league", "tmm_2v2", 10), (3, "4v4_full_share_league", "tmm_4v4_full_share", 13), (4, "3v3_league", "tmm_3v3", 16),
]
doc = Doc()
for league_id, technical, _, _ in LEAGUES:
    doc.add(res("league", league_id, {"technicalName": technical, "enabled": True, "nameKey": f"{technical}.name", "descriptionKey": f"{technical}.description",
                                      "imageUrl": f"https://content.faforever.com/leagues/{technical}.png", "mediumImageUrl": f"https://content.faforever.com/leagues/{technical}_m.png", "smallImageUrl": f"https://content.faforever.com/leagues/{technical}_s.png"}))
write("leagues.json", doc.dump())

doc = Doc()
season_dates = [(ANCHOR - timedelta(days=330), ANCHOR - timedelta(days=170)), (ANCHOR - timedelta(days=170), ANCHOR - timedelta(days=75)), (ANCHOR - timedelta(days=75), ANCHOR + timedelta(days=105))]
for league_id, technical, board, first_season in LEAGUES:
    for index in range(3):
        doc.add(res("leagueSeason", first_season + index, {
            "placementGames": 10, "placementGamesReturningPlayer": 3, "seasonNumber": index + 1, "nameKey": f"{technical.removesuffix('_league')}_season",
            "startDate": iso(season_dates[index][0]), "endDate": iso(season_dates[index][1]),
        }, {"league": ("league", league_id), "leaderboard": ("leagueLeaderboard", BOARD_ID[board])}))
    doc.include(res("league", league_id, {"technicalName": technical, "enabled": True}))
    doc.include(res("leagueLeaderboard", BOARD_ID[board], {"technicalName": board}))
write("seasons.json", doc.dump())

ACTIVE = SEASON_IDS[-1]
DIVISIONS = ["bronze", "silver", "gold", "diamond", "master"]
doc = Doc()
subdivision_ids = {}
band = 0
for d, division in enumerate(DIVISIONS):
    division_id = 2000 + d
    doc.include(res("leagueSeasonDivision", division_id, {"divisionIndex": d + 1, "nameKey": f"divisions.{division}.name", "descriptionKey": f"divisions.{division}.description"}, {"leagueSeason": ("leagueSeason", ACTIVE)}))
    for s in range(1, 6):
        sub_id = 3000 + d * 10 + s
        subdivision_ids[(d + 1, s)] = sub_id
        low = band * 90
        doc.add(res("leagueSeasonDivisionSubdivision", sub_id, {
            "subdivisionIndex": s, "nameKey": f"subdivisions.{division}.{s}.name", "descriptionKey": f"subdivisions.{division}.{s}.description",
            "minRating": low, "maxRating": low + 90 if band < 24 else None, "highestScore": 38 + (d * 5 + s) % 17,
            "imageUrl": f"https://content.faforever.com/leagues/{division}_{s}.png", "mediumImageUrl": f"https://content.faforever.com/leagues/{division}_{s}_m.png", "smallImageUrl": f"https://content.faforever.com/leagues/{division}_{s}_s.png",
        }, {"leagueSeasonDivision": ("leagueSeasonDivision", division_id)}))
        band += 1
write("subdivisions.json", doc.dump())

MY_SUB = subdivision_ids[(2, 3)]  # Silver III

# the journal: the player's ladder games of the season, a placement, then two promotions
season_games = [g for g in games if g["mode"] == "ladder_1v1" and g["time"] >= season_dates[-1][0]]
count = len(season_games)
placement, first_promotion, second_promotion = 10, int(count * 0.55), int(count * 0.8)
doc = Doc()
before_sub, score = None, 0
silver = {s: subdivision_ids[(2, s)] for s in range(1, 6)}
for number, game in enumerate(season_games, start=1):
    after_sub = before_sub
    if number == placement:
        after_sub, score = silver[1], 0
    elif number == first_promotion:
        after_sub, score = silver[2], 0
    elif number == second_promotion:
        after_sub, score = silver[3], 0
    delta = rng.randint(1, 3) if game["win"] else -rng.randint(0, 2)
    score_before = score
    score = max(0, score + delta) if number >= placement else 0
    rels = {"leagueSeason": ("leagueSeason", ACTIVE)}
    if before_sub:
        rels["leagueSeasonDivisionSubdivisionBefore"] = ("leagueSeasonDivisionSubdivision", before_sub)
    if after_sub:
        rels["leagueSeasonDivisionSubdivisionAfter"] = ("leagueSeasonDivisionSubdivision", after_sub)
    doc.add(res("leagueScoreJournal", 8_000_000 + number, {"gameId": game["id"], "loginId": PLAYER_ID, "scoreBefore": score_before, "scoreAfter": score, "gameCount": number}, rels))
    before_sub = after_sub
for s, sub_id in silver.items():
    doc.include(res("leagueSeasonDivisionSubdivision", sub_id, {"subdivisionIndex": s, "nameKey": f"subdivisions.silver.{s}.name"}, {"leagueSeasonDivision": ("leagueSeasonDivision", 2001)}))
doc.include(res("leagueSeasonDivision", 2001, {"divisionIndex": 2, "nameKey": "divisions.silver.name"}))
write("league-journal.json", doc.dump())
FINAL_SCORE = score

doc = Doc()
doc.add(res("leagueSeasonScore", 7_700_001, {"loginId": PLAYER_ID, "score": FINAL_SCORE, "gameCount": count, "returningPlayer": True},
            {"leagueSeason": ("leagueSeason", ACTIVE), "leagueSeasonDivisionSubdivision": ("leagueSeasonDivisionSubdivision", MY_SUB)}))
doc.include(res("leagueSeasonDivisionSubdivision", MY_SUB, {"subdivisionIndex": 3, "nameKey": "subdivisions.silver.3.name", "minRating": 450, "maxRating": 540, "highestScore": 44},
                {"leagueSeasonDivision": ("leagueSeasonDivision", 2001)}))
doc.include(res("leagueSeasonDivision", 2001, {"divisionIndex": 2, "nameKey": "divisions.silver.name"}, {"leagueSeason": ("leagueSeason", ACTIVE)}))
PLACEMENT_SEASON = 12  # the running 2v2 season
doc.add(res("leagueSeasonScore", 7_700_002, {"loginId": PLAYER_ID, "score": None, "gameCount": 1, "returningPlayer": False}, {"leagueSeason": ("leagueSeason", PLACEMENT_SEASON)}))
write("league-entries.json", doc.dump())

counts = {}
population = 8423
shape = [math.exp(-((i - 9) / 6.5) ** 2 / 2) for i in range(25)]
for i, ((d, s), sub_id) in enumerate(sorted(subdivision_ids.items())):
    counts[str(sub_id)] = {"data": [], "meta": {"page": {"totalRecords": int(shape[i] / sum(shape) * population), "totalPages": 1}}}
write("league-counts.json", counts)

standings = Doc()
league_players = Doc()
for index in range(50):
    login_id = PLAYER_ID if index == 16 else 410000 + index * 13
    score = FINAL_SCORE + (16 - index) if index != 16 else FINAL_SCORE
    standings.add(res("leagueSeasonScore", 7_800_000 + index, {"loginId": login_id, "score": max(score, 0), "gameCount": rng.randint(40, 90), "returningPlayer": rng.random() < 0.6},
                      {"leagueSeason": ("leagueSeason", ACTIVE), "leagueSeasonDivisionSubdivision": ("leagueSeasonDivisionSubdivision", MY_SUB)}))
    login = PLAYER if login_id == PLAYER_ID else top_names[(index + 7) % len(top_names)] + (str(index) if index >= len(top_names) else "")
    league_players.add(res("player", login_id, {"login": login}))
standings.page(counts[str(MY_SUB)]["meta"]["page"]["totalRecords"], 50)  # the count the subdivision's card shows
write("league-standings.json", standings.dump())
write("league-players.json", league_players.dump())

# ---------------------------------------------------------------- head to head

doc = Doc()
for game in sorted(head_to_head_games, key=lambda g: g["time"]):
    build_game_resources(doc, game)  # the game with everybody's statistics and players, the map and the mod as includes
    doc.add(own_stats_resource(game))
doc.page(len(doc.data), 100)
write("head-to-head.json", doc.dump())
print("rival:", RIVAL["id"], RIVAL["login"], "| other:", OTHER_RIVAL["id"], OTHER_RIVAL["login"])
