#!/usr/bin/env python3
"""Writes spec/fixtures/unpriced-cases.json (METRICS 9.10, DESIGN 9 *Unknown prices*).

Every expectation below is written by hand, including the copied text, so it checks the reference rather than repeats
it. `python3 spec/tools/unpriced.py` and `reference.py --check-fixtures` check them; both apps run the same cases
(diagnose, read, quote, live, reports) and must produce the report texts byte for byte.

Usage: python3 spec/tools/unpriced_cases.py
"""
import json
import os
from datetime import datetime, timezone

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "unpriced-cases.json")
NEKO = "opaque local router alias"
REVIEW = "internal model, no public price"
APP = {"version": "1.4.2", "platform": "macos", "os": "macos-26"}
WIN_APP = {"version": "1.4.2", "platform": "windows", "os": "windows-11"}
NOON = int(datetime(2026, 10, 26, 12, 0, tzinfo=timezone.utc).timestamp() * 1000)
FAR = 253_402_300_799_999  # 9999-12-31T23:59:59.999Z: no date in every time zone (METRICS 8)


def diag(reason, canonical=None, rule=None):
    return {"reason": reason, "canonical": canonical, "rule": rule}


diagnose = [dict(model=m, provider=p, fast=f, expect=e) for m, p, f, e in [
    ("gpt-5.5", None, False, None),
    ("claude-opus-5-5", None, True, None),
    ("gemini-3.1-flash-lite", None, False, None),
    ("qwen3-coder:30b", "ollama", False, None),
    ("big-model-free", None, False, None),
    (None, None, False, diag("noModel")),
    ("", "custom", False, diag("noModel")),
    (" \t ", None, False, diag("noModel")),
    ("mimo-v2.6-pro", "custom", False, None),
    ("mimo-v2.5-pro", "custom", False, diag("notInTable", "mimo-v2.5-pro")),
    ("  Mystery-Model[1m]  ", None, False, diag("notInTable", "mystery-model")),
    ("anthropic/claude-opus-9", None, False, diag("notInTable", "anthropic/claude-opus-9")),
    ("[proxy]", None, False, diag("notInTable")),
    ("qwen3-coder:30b", None, False, diag("notInTable", "qwen3-coder:30b")),
    ("gpt-oss:120b-cloud", "ollama", False, diag("notInTable", "gpt-oss:120b-cloud")),
    ("gpt-5.5-2026-04-23", None, False, diag("notInTable", "gpt-5.5-2026-04-23")),
    ("claude-sonnet-5-5", None, True, diag("noFastPrice", "claude-sonnet-5-5")),
    ("claude-sonnet-5-5-fast", None, False, diag("noFastPrice", "claude-sonnet-5-5")),
    ("anthropic/claude-sonnet-5.5-fast", None, False, diag("noFastPrice", "claude-sonnet-5-5")),
    ("gpt5.5", None, True, diag("noFastPrice", "gpt5.5")),
    ("neko-model-f1097c63", None, False, diag("unpriceable", "neko-model-f1097c63", NEKO)),
    ("Neko-Model-7", "custom", False, diag("unpriceable", "neko-model-7", NEKO)),
    ("codex-auto-review", None, False, diag("unpriceable", "codex-auto-review", REVIEW)),
    ("openai/codex-auto-review", None, True, diag("unpriceable", "codex-auto-review", REVIEW)),
]]


def side(model, provider, fast, requests, version=7):
    return {"model": model, "provider": provider, "fast": fast, "requests": requests, "version": version}


def fact(model, provider, fast, requests):
    return {"model": model, "provider": provider, "fast": fast, "requests": requests}


read = [
    {"name": "valid side rows are the facts, summed by trimmed model, provider and fast",
     "stored": {"fileVersion": 7, "requests": 9, "unpriced": 6, "models": {"neko-model-1": 4, " neko-model-1": 1,
                                                                            "": 1},
                "side": [side("neko-model-1", "custom", False, 4), side(" neko-model-1", "custom", False, 1),
                         side("", None, False, 1)]},
     "expect": {"requests": 9, "noCostData": 0, "facts": [fact("", None, False, 1),
                                                          fact("neko-model-1", "custom", False, 5)], "derived": 0}},
    {"name": "side rows of another analysis version are stale: derive from the per-model counts",
     "stored": {"fileVersion": 6, "requests": 5, "unpriced": 3, "models": {"mimo-v2.6-pro": 2, "gpt-5.5": 0},
                "side": [side("mimo-v2.6-pro", "custom", False, 3)]},
     "expect": {"requests": 5, "noCostData": 0, "facts": [fact("", None, False, 1),
                                                          fact("mimo-v2.6-pro", None, False, 2)], "derived": 3}},
    {"name": "side rows that do not add up to the stored count are ignored",
     "stored": {"fileVersion": 7, "requests": 4, "unpriced": 2, "models": {"codex-auto-review": 2},
                "side": [side("codex-auto-review", None, False, 1)]},
     "expect": {"requests": 4, "noCostData": 0, "facts": [fact("codex-auto-review", None, False, 2)],
                "derived": 2}},
    {"name": "no side rows (an older build wrote the file): derive",
     "stored": {"fileVersion": 5, "requests": 3, "unpriced": 3, "models": {"b": 1, "a": 1, "\u00e9": 1}, "side": []},
     "expect": {"requests": 3, "noCostData": 0, "facts": [fact("a", None, False, 1), fact("b", None, False, 1),
                                                          fact("\u00e9", None, False, 1)], "derived": 3}},
    {"name": "derived counts never exceed the stored unpriced count",
     "stored": {"fileVersion": 5, "requests": 9, "unpriced": 2, "models": {"x": 5}, "side": []},
     "expect": {"requests": 9, "noCostData": 0, "facts": [fact("x", None, False, 2)], "derived": 2}},
    {"name": "nothing unpriced: no facts, stray side rows ignored",
     "stored": {"fileVersion": 7, "requests": 2, "unpriced": 0, "models": {}, "side": [side("x", None, False, 1)]},
     "expect": {"requests": 2, "noCostData": 0, "facts": [], "derived": 0}},
    {"name": "no cost data (stored before costs existed) is not unpriced",
     "stored": {"fileVersion": 2, "requests": 8, "unpriced": None, "models": {}, "side": []},
     "expect": {"requests": 8, "noCostData": 8, "facts": [], "derived": 0}},
]

quote = [
    {"text": "neko-model-7", "expect": "\"neko-model-7\""},
    {"text": "", "expect": "\"\""},
    {"text": "a \"b\" \\c", "expect": "\"a \\\"b\\\" \\\\c\""},
    {"text": "tab\there\nnl\u0001\u007f\u0085\u2028\u00a0x", "expect": "\"tab\\there\\nnl\\u0001\\u007f\\u0085\\u2028\u00a0x\""},
    {"text": "\u6a21\u578b-\u4e00", "expect": "\"\u6a21\u578b-\u4e00\""},
    {"text": "/Users/zoe/models/llama.gguf", "expect": "\"<path>\""},
    {"text": "~/m.gguf", "expect": "\"<path>\""},
    {"text": "C:\\Models\\q.gguf", "expect": "\"<path>\""},
    {"text": "d:/q.gguf", "expect": "\"<path>\""},
    {"text": "File:///tmp/x", "expect": "\"<path>\""},
    {"text": "homebrew/llama-4", "expect": "\"homebrew/llama-4\""},
    {"text": "proxy/HOME/me/x", "expect": "\"<path>\""},
    {"text": "anthropic/claude-opus-9", "expect": "\"anthropic/claude-opus-9\""},
    {"text": "/Users/x", "redact": False, "expect": "\"/Users/x\""},
    {"text": "m" * 121, "expect": "\"" + "m" * 120 + "\u2026\""},
    {"text": "\u6a21" * 120, "expect": "\"" + "\u6a21" * 120 + "\""},
    {"text": "\U0001F600" * 121, "expect": "\"" + "\U0001F600" * 120 + "\u2026\""},
]


def resp(source, model, cost, provider=None, fast=False, end=NOON, requests=1):
    return {"source": source, "model": model, "provider": provider, "fast": fast, "endMs": end,
            "requestCount": requests, "costMicroUsd": cost}


live = [
    {"name": "today", "today": "2026-10-26",
     "responses": [resp("codex", "gpt-5.5", 1200), resp("codex", "neko-model-ab", None, "custom"),
                   resp("codex", "neko-model-ab", None, "custom"), resp("codex", " neko-model-ab ", None, "custom"),
                   resp("codex", None, None, "openai"), resp("codex", "", None, "openai"),
                   resp("codex", "mimo-v2.6-pro", None, "custom"),
                   resp("claudeCode", "claude-sonnet-5-5", None, fast=True),
                   resp("claudeCode", "claude-opus-5-5", 0),
                   resp("grokBuild", "grok-mystery", None, requests=3),
                   resp("pi", "gpt-5.5", None, end=FAR)],
     "expect": [
         {"day": "2026-10-26", "source": "codex", "requests": 7, "noCostData": 0, "derived": 0,
          "facts": [fact("", "openai", False, 2), fact("mimo-v2.6-pro", "custom", False, 1),
                    fact("neko-model-ab", "custom", False, 3)]},
         {"day": "2026-10-26", "source": "claudeCode", "requests": 2, "noCostData": 0, "derived": 0,
          "facts": [fact("claude-sonnet-5-5", None, True, 1)]},
         {"day": "2026-10-26", "source": "grokBuild", "requests": 3, "noCostData": 0, "derived": 0,
          "facts": [fact("grok-mystery", None, False, 3)]},
         {"day": "2026-10-26", "source": "pi", "requests": 1, "noCostData": 0, "derived": 0,
          "facts": [{"model": "gpt-5.5", "provider": None, "fast": False, "noDate": True, "requests": 1}]},
     ]},
]


def stored(day, source, requests, unpriced, models, sides, version=7):
    return {"day": day, "source": source, "stored": {"fileVersion": version, "requests": requests,
                                                     "unpriced": unpriced, "models": models, "side": sides}}


stored_parts = [
    stored("2026-10-19", "codex", 10, None, {}, [], 2),
    stored("2026-10-20", "codex", 40, 30, {"neko-model-ab": 25, "codex-auto-review": 3},
           [side("neko-model-ab", "custom", False, 25), side("codex-auto-review", "openai", False, 3),
            side("", "openai", False, 2)]),
    stored("2026-10-21", "codex", 12, 9, {"neko-model-ab": 4, "mimo-v2.6-pro": 5},
           [side("neko-model-ab", "custom", False, 4), side("mimo-v2.6-pro", "custom", False, 5)]),
    stored("2026-10-21", "codex", 6, 4, {"gpt-6.1-sol-mini": 2, "codex-auto-review": 1}, [], 5),
    stored("2026-10-22", "codex", 2, 0, {}, []),
    stored("2026-10-22", "codex", 6, 3, {"mimo-v2.5-pro": 3}, [side("mimo-v2.5-pro", "custom", False, 3)]),
    stored("2026-10-22", "opencode", 4, 0, {}, []),
    stored("2026-10-20", "claudeDesktop", 3, 1, {"claude-sonnet-5-5": 1}, [side("claude-sonnet-5-5", None, True, 1)]),
    stored("2026-10-21", "workbuddyAi", 5, 2, {"/Users/dev/models/q.gguf": 1, "Kimi-K3": 1},
           [side("/Users/dev/models/q.gguf", "custom", False, 1), side("Kimi-K3", "custom", False, 1)]),
    stored("2026-10-23", "antigravity", 7, 7, {"gemini-3.1-flash-b": 0}, [side("", None, False, 7)]),
]

HEADER = ("Agent Speed unpriced diagnostics 1\n"
          "app version=1.4.2 platform=macos os=macos-26\n"
          "prices version=2 updated=2026-10-15\n")
ROW = "row source=%s reason=%s requests=%d first=%s last=%s fast=%s model=%s provider=%s canonical=%s rule=%s\n"
NEKO_Q, REVIEW_Q = "\"%s\"" % NEKO, "\"%s\"" % REVIEW

# Under the current table v2, `mimo-v2.6-pro` (stored as unpriced) and the derived `gpt-6.1-sol-mini` are priced now:
# they count in `pricedNow`, not in a row. `mimo-v2.5-pro` is in no table.
all_text = HEADER + (
    "range view=custom first=2026-10-19 last=2026-10-23 days=5 source=all excluded=none\n"
    "source id=codex requests=76 unpriced=46 pricedNow=7 noCostData=10 derived=4 rows=6\n"
    + ROW % ("codex", "unpriceable", 29, "2026-10-20", "2026-10-21", "false", "\"neko-model-ab\"", "\"custom\"",
             "\"neko-model-ab\"", NEKO_Q)
    + ROW % ("codex", "unpriceable", 3, "2026-10-20", "2026-10-20", "false", "\"codex-auto-review\"", "\"openai\"",
             "\"codex-auto-review\"", REVIEW_Q)
    + ROW % ("codex", "notInTable", 3, "2026-10-22", "2026-10-22", "false", "\"mimo-v2.5-pro\"", "\"custom\"",
             "\"mimo-v2.5-pro\"", "null")
    + ROW % ("codex", "noModel", 2, "2026-10-20", "2026-10-20", "false", "\"\"", "\"openai\"", "null", "null")
    + ROW % ("codex", "noModel", 1, "2026-10-21", "2026-10-21", "false", "\"\"", "null", "null", "null")
    + ROW % ("codex", "unpriceable", 1, "2026-10-21", "2026-10-21", "false", "\"codex-auto-review\"", "null",
             "\"codex-auto-review\"", REVIEW_Q)
    + "source id=antigravity requests=7 unpriced=7 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("antigravity", "noModel", 7, "2026-10-23", "2026-10-23", "false", "\"\"", "null", "null", "null")
    + "source id=workbuddyAi requests=5 unpriced=2 pricedNow=0 noCostData=0 derived=0 rows=2\n"
    + ROW % ("workbuddyAi", "notInTable", 1, "2026-10-21", "2026-10-21", "false", "\"<path>\"", "\"custom\"",
             "\"<path>\"", "null")
    + ROW % ("workbuddyAi", "notInTable", 1, "2026-10-21", "2026-10-21", "false", "\"Kimi-K3\"", "\"custom\"",
             "\"kimi-k3\"", "null")
    + "source id=claudeDesktop requests=3 unpriced=1 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("claudeDesktop", "noFastPrice", 1, "2026-10-20", "2026-10-20", "true", "\"claude-sonnet-5-5\"", "null",
             "\"claude-sonnet-5-5\"", "null")
    + "source id=opencode requests=4 unpriced=0 pricedNow=0 noCostData=0 derived=0 rows=0\n"
    + "total requests=95 unpriced=56 pricedNow=7 noCostData=10 derived=4 rows=10\n")

one_source_text = HEADER + (
    "range view=yesterday first=2026-10-21 last=2026-10-21 days=1 source=codex excluded=none\n"
    "source id=codex requests=18 unpriced=13 pricedNow=7 noCostData=0 derived=4 rows=3\n"
    + ROW % ("codex", "unpriceable", 4, "2026-10-21", "2026-10-21", "false", "\"neko-model-ab\"", "\"custom\"",
             "\"neko-model-ab\"", NEKO_Q)
    + ROW % ("codex", "noModel", 1, "2026-10-21", "2026-10-21", "false", "\"\"", "null", "null", "null")
    + ROW % ("codex", "unpriceable", 1, "2026-10-21", "2026-10-21", "false", "\"codex-auto-review\"", "null",
             "\"codex-auto-review\"", REVIEW_Q)
    + "total requests=18 unpriced=13 pricedNow=7 noCostData=0 derived=4 rows=3\n")

# The same stored history while v1 is the current table: the models v2 added are not in the table yet.
older_table_text = ("Agent Speed unpriced diagnostics 1\n"
                    "app version=1.4.2 platform=macos os=macos-26\n"
                    "prices version=1 updated=2026-10-01\n"
                    "range view=custom first=2026-10-21 last=2026-10-22 days=2 source=codex excluded=none\n"
                    "source id=codex requests=26 unpriced=16 pricedNow=0 noCostData=0 derived=4 rows=6\n"
    + ROW % ("codex", "notInTable", 5, "2026-10-21", "2026-10-21", "false", "\"mimo-v2.6-pro\"", "\"custom\"",
             "\"mimo-v2.6-pro\"", "null")
    + ROW % ("codex", "unpriceable", 4, "2026-10-21", "2026-10-21", "false", "\"neko-model-ab\"", "\"custom\"",
             "\"neko-model-ab\"", NEKO_Q)
    + ROW % ("codex", "notInTable", 3, "2026-10-22", "2026-10-22", "false", "\"mimo-v2.5-pro\"", "\"custom\"",
             "\"mimo-v2.5-pro\"", "null")
    + ROW % ("codex", "notInTable", 2, "2026-10-21", "2026-10-21", "false", "\"gpt-6.1-sol-mini\"", "null",
             "\"gpt-6.1-sol-mini\"", "null")
    + ROW % ("codex", "noModel", 1, "2026-10-21", "2026-10-21", "false", "\"\"", "null", "null", "null")
    + ROW % ("codex", "unpriceable", 1, "2026-10-21", "2026-10-21", "false", "\"codex-auto-review\"", "null",
             "\"codex-auto-review\"", REVIEW_Q)
    + "total requests=26 unpriced=16 pricedNow=0 noCostData=0 derived=4 rows=6\n")

excluded_text = HEADER + (
    "range view=7d first=2026-10-20 last=2026-10-26 days=7 source=all excluded=codex,workbuddyAi\n"
    "source id=antigravity requests=7 unpriced=7 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("antigravity", "noModel", 7, "2026-10-23", "2026-10-23", "false", "\"\"", "null", "null", "null")
    + "source id=claudeDesktop requests=3 unpriced=1 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("claudeDesktop", "noFastPrice", 1, "2026-10-20", "2026-10-20", "true", "\"claude-sonnet-5-5\"", "null",
             "\"claude-sonnet-5-5\"", "null")
    + "source id=opencode requests=4 unpriced=0 pricedNow=0 noCostData=0 derived=0 rows=0\n"
    + "total requests=14 unpriced=8 pricedNow=0 noCostData=0 derived=0 rows=2\n")

empty_text = ("Agent Speed unpriced diagnostics 1\n"
              "app version=1.4.2 platform=windows os=windows-11\n"
              "prices version=2 updated=2026-10-15\n"
              "range view=custom first=2026-10-24 last=2026-10-25 days=2 source=all excluded=none\n"
              "total requests=0 unpriced=0 pricedNow=0 noCostData=0 derived=0 rows=0\n")

# The engine's today (Settings -> Cost): `mimo-v2.6-pro` was priced with an older table and waits for re-pricing.
live_text = HEADER + (
    "range view=live first=2026-10-26 last=2026-10-26 days=1 source=all excluded=none\n"
    "source id=codex requests=7 unpriced=6 pricedNow=1 noCostData=0 derived=0 rows=2\n"
    + ROW % ("codex", "unpriceable", 3, "2026-10-26", "2026-10-26", "false", "\"neko-model-ab\"", "\"custom\"",
             "\"neko-model-ab\"", NEKO_Q)
    + ROW % ("codex", "noModel", 2, "2026-10-26", "2026-10-26", "false", "\"\"", "\"openai\"", "null", "null")
    + "source id=grokBuild requests=3 unpriced=3 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("grokBuild", "notInTable", 3, "2026-10-26", "2026-10-26", "false", "\"grok-mystery\"", "null",
             "\"grok-mystery\"", "null")
    + "source id=claudeCode requests=2 unpriced=1 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("claudeCode", "noFastPrice", 1, "2026-10-26", "2026-10-26", "true", "\"claude-sonnet-5-5\"", "null",
             "\"claude-sonnet-5-5\"", "null")
    + "source id=pi requests=1 unpriced=1 pricedNow=0 noCostData=0 derived=0 rows=1\n"
    + ROW % ("pi", "noDate", 1, "2026-10-26", "2026-10-26", "false", "\"gpt-5.5\"", "null", "null", "null")
    + "total requests=13 unpriced=11 pricedNow=1 noCostData=0 derived=0 rows=5\n")

reports = [
    {"name": "all sources over five days", "app": APP,
     "view": {"view": "custom", "first": "2026-10-19", "last": "2026-10-23"}, "expect": all_text},
    {"name": "one source, one day", "app": APP,
     "view": {"view": "yesterday", "first": "2026-10-21", "last": "2026-10-21", "source": "codex"},
     "expect": one_source_text},
    {"name": "the reasons follow the current table", "app": APP, "table": "prices/v1.json",
     "view": {"view": "custom", "first": "2026-10-21", "last": "2026-10-22", "source": "codex"},
     "expect": older_table_text},
    {"name": "subscription sources excluded", "app": APP,
     "view": {"view": "7d", "first": "2026-10-20", "last": "2026-10-26", "excluded": ["workbuddyAi", "codex"]},
     "expect": excluded_text},
    {"name": "a range without data", "app": WIN_APP,
     "view": {"view": "custom", "first": "2026-10-24", "last": "2026-10-25"}, "expect": empty_text},
    {"name": "today from the engine", "app": APP, "input": "today",
     "view": {"view": "live", "first": "2026-10-26", "last": "2026-10-26"}, "expect": live_text},
]

doc = {
    "_comment": "Unpriced diagnostics (METRICS 9.10) against the fixture table `table` (the current table). "
                "`diagnose`: the reason, canonical key and rule text for a response's model, provider and fast flag "
                "(null = priced or free now). `read`: one stored contribution with its side rows -> facts (*Reading*). "
                "`quote`: one text value of the copied text (`redact` false only for rule texts). `live`: the engine's "
                "today responses -> one part per source (*Live*). `reports`: the copied text for a view over `stored` "
                "(each entry one contribution of a file; parts summed per day and source after *Reading*) or, for "
                "view `live`, over the `live` case named by `input`, with the report's `table` as the current table "
                "when it names one; byte-identical on both platforms, LF line ends. Written by "
                "spec/tools/unpriced_cases.py.",
    "table": "prices/v2.json",
    "diagnose": diagnose,
    "read": read,
    "quote": quote,
    "live": live,
    "stored": stored_parts,
    "reports": reports,
}

if __name__ == "__main__":
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")
    print("wrote", os.path.normpath(OUT))
