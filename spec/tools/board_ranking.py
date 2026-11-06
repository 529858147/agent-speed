#!/usr/bin/env python3
"""Reference for the leaderboard board's stable ranking (spec/DESIGN.md 2c, Ranking stability).

  python3 spec/tools/board_ranking.py --write   # regenerates spec/fixtures/board-ranking-cases.json
  python3 spec/tools/board_ranking.py           # checks the fixture against this reference

Each engine tick gives every group its live value (the sum of its members' 3 s live rates) and latest activity.
"""
import json
import os
import sys

FIXTURE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "board-ranking-cases.json")
IDLE = 0.05


class Ranking:
    def __init__(self):
        self.order = []          # group ids, first place first
        self.pressure = {}       # (challenger, defender) -> consecutive ticks the challenger has led

    @staticmethod
    def leads(a, b):
        if a["live"] < IDLE and b["live"] < IDLE:
            return a["lastActiveMs"] > b["lastActiveMs"] + 1000
        return a["live"] > b["live"] * 1.1 + 1

    @staticmethod
    def overwhelms(a, b):
        return a["live"] > b["live"] * 2 + 20

    def tick(self, groups):
        by_id = {g["id"]: g for g in groups}
        pressure = {}
        for a in groups:
            for b in groups:
                if a is not b and self.leads(a, b):
                    pressure[(a["id"], b["id"])] = self.pressure.get((a["id"], b["id"]), 0) + 1
        self.pressure = pressure
        order = [i for i in self.order if i in by_id]
        # Groups new to the board enter at the place of their value, without an event.
        for g in sorted((g for g in groups if g["id"] not in self.order),
                        key=lambda g: (-g["live"], -g["lastActiveMs"], g["id"])):
            at = next((k for k, i in enumerate(order) if by_id[i]["live"] < g["live"]), len(order))
            order.insert(at, g["id"])
        baseline = list(order)
        for i in range(1, len(order)):
            j = i
            while j > 0:
                a, b = by_id[order[j]], by_id[order[j - 1]]
                if self.pressure.get((a["id"], b["id"]), 0) >= 2 or self.overwhelms(a, b):
                    order[j - 1], order[j] = order[j], order[j - 1]
                    j -= 1
                else:
                    break
        # Only passing someone is an event: places gained because a group left or moved down are not.
        events = []
        for rank, gid in enumerate(order, 1):
            before = baseline.index(gid) + 1
            if rank < before:
                events.append({"type": "newLeader" if rank == 1 else "overtake", "id": gid, "from": before, "to": rank})
        self.order = order
        return list(order), events


def g(gid, live, active=0):
    return {"id": gid, "live": live, "lastActiveMs": active}


CASES = [
    ("initial order follows the values", [[g("a", 50), g("b", 120), g("c", 80)]]),
    ("a lead inside 10 % never passes", [[g("a", 100), g("b", 90)], [g("a", 100), g("b", 108)], [g("a", 100), g("b", 110)],
                                          [g("a", 100), g("b", 110.9)]]),
    ("a lead must hold for two ticks", [[g("a", 100), g("b", 90)], [g("a", 100), g("b", 130)], [g("a", 100), g("b", 95)],
                                         [g("a", 100), g("b", 130)], [g("a", 100), g("b", 131)]]),
    ("an overwhelming lead passes at once", [[g("a", 100), g("b", 90)], [g("a", 100), g("b", 221)]]),
    ("a climber passes several groups in one tick", [[g("a", 300), g("b", 200), g("c", 100), g("d", 50)],
                                                     [g("a", 300), g("b", 200), g("c", 100), g("d", 360)],
                                                     [g("a", 300), g("b", 200), g("c", 100), g("d", 360)]]),
    ("the second place takes the lead", [[g("a", 200), g("b", 150)], [g("a", 120), g("b", 180)], [g("a", 110), g("b", 185)]]),
    ("idle groups order by latest activity", [[g("a", 0, 1000), g("b", 0, 5000)], [g("a", 0, 1000), g("b", 0, 5000)],
                                              [g("a", 0, 1000), g("b", 0, 5000)]]),
    ("new groups enter at their place without an event", [[g("a", 300), g("b", 100)], [g("a", 300), g("b", 100), g("c", 200)]]),
    ("groups that leave are dropped", [[g("a", 300), g("b", 200), g("c", 100)], [g("a", 300), g("c", 100)],
                                       [g("a", 300), g("c", 100), g("b", 50)]]),
    ("near-equal values never flap", [[g("a", 100), g("b", 99)], [g("a", 99), g("b", 104)], [g("a", 104), g("b", 99)],
                                      [g("a", 98), g("b", 106)], [g("a", 105), g("b", 100)]]),
]


def build():
    cases = []
    for name, ticks in CASES:
        ranking = Ranking()
        out = []
        for groups in ticks:
            order, events = ranking.tick(groups)
            out.append({"groups": groups, "order": order, "events": events})
        cases.append({"name": name, "ticks": out})
    return {"_comment": "DESIGN 2c Ranking stability. Feed each tick's groups to one ranking (a fresh one per case) and "
                        "compare the order (first place first) and the events of that tick. Generated by "
                        "spec/tools/board_ranking.py --write.", "cases": cases}


def main():
    data = build()
    if "--write" in sys.argv:
        with open(FIXTURE, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
            f.write("\n")
        print("wrote", FIXTURE)
        return 0
    with open(FIXTURE, encoding="utf-8") as f:
        expected = json.load(f)
    if expected != data:
        print("board-ranking-cases.json differs from the reference")
        return 1
    print("board ranking reference matches %d cases" % len(data["cases"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
