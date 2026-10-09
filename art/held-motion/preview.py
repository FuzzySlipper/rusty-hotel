#!/usr/bin/env python3
"""Held-motion preview strips: each held look's actions, seen from the first-person camera at moments through them.

    python3 art/held-motion/preview.py [--session SID] [--looks prybar,coach-gun] [--frames 8] [--out DIR]

Drives a crew-services playtest session of the rusty-hotel-ui-debug profile (started and stopped here unless --session
names one) in held, action-driven time: for each look it holds one of its items with the developer `hotel.dev.hold`
command, fills the investigator's tracks, starts each of the item's actions with its ordinary control, and captures
world frames (no page UI) at even moments from the action's start to its end. One strip per look and action goes to
DIR (default .runtime/held-motion/), labelled with the time and phase and, at each key's moment, the key's pose, plus
contact.png with every strip. An action that needs a carried item (a reload's cartridges) gets one, with room for what
it gives. An action that does not start is reported and the command exits 1 rather than publishing an idle strip.
What it shows is the Engine's own tween, as the game plays it. Linux art tooling; not part of the build. For stepping
keys and playing slowly in a live session, use the hotel.dev.motion console commands instead.
"""
import argparse
import json
import os
import subprocess
import sys
import time

from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONTENT = os.path.join(REPO, "content")
PROFILE = "rusty-hotel-ui-debug"
# The part of the frame the right hand's items move through.
CROP = (420, 140, 1280, 720)
CONTROLS = ("primary", "secondary")


def run(*args):
    return subprocess.run(args, capture_output=True, text=True).stdout


def assist(session, op):
    return json.loads(run("playtest", "assist", session, "--json", json.dumps(op)) or "{}")


def pinned_live_debug():
    props = open(os.path.join(REPO, "Directory.Build.props")).read()
    version = props.split("<RustyEnginePackageVersion>")[1].split("<")[0]
    cache = os.environ.get("RUSTY_ENGINE_CACHE") or os.path.join(os.environ.get("XDG_CACHE_HOME", os.path.expanduser("~/.cache")), "rusty-engine")
    return os.path.join(cache, "pairs", version, "runtime-pack", "bin", "rusty-live-debug")


def content():
    load = lambda path: json.load(open(os.path.join(CONTENT, path)))
    actions = {a["id"]: a for a in load("actions/actions.json")["actions"]}
    every = load("supplies/items.json")["items"]
    items = {}
    for item in every:
        look = (item.get("wear") or {}).get("look")
        if look and look not in items:
            items[look] = item
    motions = load("combat/held-motion.json")["motions"]
    looks = {l["look"]: l for l in load("combat/held.json")["looks"]}
    return items, actions, every, motions, looks


def key_moments(motion, action):
    """Each key of an action's track: (seconds from its start, pose), as held-motion.json times them."""
    timing = action["timing"]
    keys = motion["tracks"].get(action["id"]) or motion["tracks"]["default"]
    base = {"Windup": 0, "Commit": timing["windup"], "Recovery": timing["windup"] + timing["commit"]}
    span = {"Windup": timing["windup"], "Commit": timing["commit"], "Recovery": timing["recovery"]}
    return [(base[k["phase"]] + span[k["phase"]] * k["at"], k["pose"]) for k in keys]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--session")
    parser.add_argument("--looks", default="")
    parser.add_argument("--frames", type=int, default=8)
    parser.add_argument("--out", default=os.path.join(REPO, ".runtime", "held-motion"))
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    items, actions, every, motions, held = content()
    failures = []
    looks = [l for l in args.looks.split(",") if l] or list(items)

    session, owned = args.session, False
    if not session:
        started = json.loads(run("playtest", "start", PROFILE) or "{}")
        session = (started.get("session") or started).get("session_id") or started.get("id")
        if not session:
            sys.exit("Could not start a playtest session: " + json.dumps(started)[:400])
        owned = True
    try:
        status = run("playtest", "status", session)
        port = status.split('"port": ')[1].split(",")[0].strip()
        origin = f"http://127.0.0.1:{port}"
        live = pinned_live_debug()
        debug = lambda command: run(live, "--origin", origin, "--command", command)
        # Past the title menu, into held time, standing in the corridor looking down it.
        for _ in range(60):
            if "position" in debug("hotel.inspect"):
                break
            time.sleep(3)
        for choice in ("continue", "new"):
            run("playtest", "browser", session, "--json", json.dumps({"op": "click", "selector": f'[data-choice="{choice}"]:not([hidden])'}))
            run("playtest", "browser", session, "--json", json.dumps({"op": "click", "selector": "[data-confirm-yes]"}))
        assist(session, {"op": "time", "mode": "action-driven"})
        debug("hotel.dev.view 0 -6 0 0")

        strips = []
        for look in looks:
            item = items[look]
            debug(f"hotel.dev.hold {item['id']}")
            for control, action_id in zip(CONTROLS, item["wear"]["actions"]):
                action = actions[action_id]
                debug("hotel.dev.fill-tracks")
                # An action paid with a carried item (a reload's cartridges) gets one, and room in the track it fills.
                if cost := action["cost"].get("item"):
                    supply = next(i for i in every if cost in i.get("classifications", []))
                    debug(f"hotel.dev.give-supply {supply['id']} 1")
                    debug(f"hotel.dev.set-track {cost} 0")
                assist(session, {"op": "advance", "ms": 400})
                timing = action["timing"]
                total = timing["windup"] + timing["commit"] + timing["recovery"]
                keys = key_moments(motions[held[look]["motion"]], action)
                moments = sorted(set([round(total * i / (args.frames - 1), 3) for i in range(args.frames)] + [round(t, 3) for t, _ in keys]))
                elapsed = assist(session, {"op": "act", "id": control, "ms": 17}).get("advancedMs", 0) / 1000
                if json.loads(debug("hotel.inspect") or "{}").get("attackPhase", "Ready") == "Ready":
                    failures.append(f"{look} · {action_id}: the action did not start ({json.loads(debug('hotel.inspect') or '{}').get('weapon')})")
                    assist(session, {"op": "advance", "ms": 300})
                    continue
                frames = []
                for moment in moments:
                    if moment > elapsed:
                        elapsed += assist(session, {"op": "advance", "ms": (moment - elapsed) * 1000}).get("advancedMs", 0) / 1000
                    shot = assist(session, {"op": "world-frame"})
                    phase = ("windup" if elapsed < timing["windup"] else "commit" if elapsed < timing["windup"] + timing["commit"] else "recovery")
                    pose = next((p for t, p in keys if abs(t - moment) < 1e-3), None)
                    frames.append((Image.open(shot["path"]).crop(CROP), f"{elapsed:.2f} s {phase}" + (f" · {pose}" if pose else "")))
                assist(session, {"op": "advance", "ms": (timing["cooldown"] + 0.3) * 1000})
                strips.append((f"{look} · {action_id}", frames))
                strip_image(f"{look} · {action_id}", frames).save(os.path.join(args.out, f"{look}-{action_id}.png"))
        if not strips:
            sys.exit("No action started:\n" + "\n".join(failures))
        contact = Image.new("RGB", (strips[0][1][0][0].width // 2 * max(len(f) for _, f in strips), (strips[0][1][0][0].height // 2 + 24) * len(strips)), "black")
        for row, (title, frames) in enumerate(strips):
            contact.paste(strip_image(title, frames, half=True), (0, row * (frames[0][0].height // 2 + 24)))
        contact.save(os.path.join(args.out, "contact.png"))
        print(f"{len(strips)} strips in {args.out} (contact.png has them all).")
        if failures:
            print("Not shown:\n" + "\n".join(failures))
            sys.exit(1)
    finally:
        if owned:
            run("playtest", "stop", session)


def strip_image(title, frames, half=False):
    width, height = frames[0][0].size
    if half:
        width, height = width // 2, height // 2
    strip = Image.new("RGB", (width * len(frames), height + 24), "black")
    draw = ImageDraw.Draw(strip)
    draw.text((6, 4), title, fill=(235, 225, 200))
    for i, (image, label) in enumerate(frames):
        strip.paste(image.resize((width, height)), (i * width, 24))
        draw.text((i * width + 6, 28), label, fill=(255, 230, 160))
    return strip


if __name__ == "__main__":
    main()
