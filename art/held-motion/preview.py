#!/usr/bin/env python3
"""Held-motion preview strips: each held look's actions, seen from the first-person camera at moments through them.

    python3 art/held-motion/preview.py [--session SID] [--looks prybar,coach-gun] [--frames 8] [--out DIR]

Drives a crew-services playtest session of the rusty-hotel-ui-debug profile (started and stopped here unless --session
names one) in held, action-driven time: for each look it holds one of its items with the developer `hotel.dev.hold`
command, fills the investigator's tracks, starts each of the item's actions with its ordinary control, and captures
world frames (no page UI) at rest, action keys, even moments and after settling. One strip per look and action goes to
DIR (default .runtime/held-motion/), labelled with the time and phase and, at each key's moment, the key's pose, plus
contact.png with every strip. An action that needs a carried item (a reload's cartridges) gets one, with room for what
it gives. An action that does not start is reported and the command exits 1 rather than publishing an idle strip. A frame
whose drawn wrist sits more than a centimetre from its grip (hotel.inspect's armsShortfall) is labelled OUT OF REACH in
red and listed: that pose puts the item beyond the arm.
Full original frames are copied into originals/; manifest.json indexes the requested and admitted times, frame/step
receipts, inspection facts, source identity and authored files. Strips retain the full viewport, including the left arm.
--actions filters actions. --viewer seeks the Engine motion viewer without requiring supplies or admitting an attack;
its evidence is labelled separately from the ordinary-input default. Linux art tooling; not part of the build.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
import time

from PIL import Image, ImageDraw

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONTENT = os.path.join(REPO, "content")
PROFILE = "rusty-hotel-ui-debug"
CONTROLS = ("primary", "secondary")
# How far (metres) a drawn wrist may sit from its grip before the frame is flagged out of reach.
REACH_TOLERANCE = 0.01


def run(*args, optional=False):
    result = subprocess.run(args, capture_output=True, text=True)
    # A failed input/query must not turn into an apparently successful strip of an idle or different weapon.
    if result.returncode and not optional:
        raise RuntimeError(f"{args[0]} failed: {result.stderr or result.stdout}")
    return result.stdout


def assist(session, op):
    return json.loads(run("playtest", "assist", session, "--json", json.dumps(op)) or "{}")


def advance(session, ms):
    """Admit long recovery/cooldown intervals in the Engine's supported windows; return actual admitted milliseconds."""
    admitted = 0
    while ms > 1e-6:
        window = min(ms, 2000)
        admitted += assist(session, {"op": "advance", "ms": window})["advancedMs"]
        ms -= window
    return admitted


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
    parser.add_argument("--actions", default="", help="Comma-separated action ids; omitted means every action of each look")
    parser.add_argument("--viewer", action="store_true", help="Seek the motion viewer; presentation evidence only, without inventory/action admission")
    parser.add_argument("--reset", action="store_true", help="Reset the unsaved excursion before granting fixtures; leaves the refuge checkpoint untouched")
    parser.add_argument("--frames", type=int, default=8)
    parser.add_argument("--out", default=os.path.join(REPO, ".runtime", "held-motion"))
    args = parser.parse_args()
    if args.frames < 2:
        parser.error("--frames needs at least two samples to show the start and end")
    os.makedirs(args.out, exist_ok=True)
    originals = os.path.join(args.out, "originals")
    os.makedirs(originals, exist_ok=True)
    items, actions, every, motions, held = content()
    failures = []
    unreachable = []
    looks = [l for l in args.looks.split(",") if l] or list(items)
    selected_actions = set(filter(None, args.actions.split(",")))
    manifest = {"complete": False, "resetExcursion": args.reset, "mode": "viewer" if args.viewer else "ordinary-input", "source": run("git", "-C", REPO, "rev-parse", "HEAD").strip(),
                "workingTree": run("git", "-C", REPO, "status", "--short"), "frames": [], "failures": failures, "unreachable": unreachable}
    manifest["authoredFiles"] = {path: json.load(open(os.path.join(CONTENT, path))) for path in
        ("combat/held.json", "combat/held-motion.json", "combat/stances.json", "combat/arms.json", "actions/actions.json")}
    def record():
        with open(os.path.join(args.out, "manifest.json"), "w") as file:
            json.dump(manifest, file, indent=2)

    session, owned = args.session, False
    if not session:
        started = json.loads(run("playtest", "start", PROFILE) or "{}")
        session = (started.get("session") or started).get("session_id") or started.get("id")
        if not session:
            sys.exit("Could not start a playtest session: " + json.dumps(started)[:400])
        owned = True
    try:
        status = json.loads(run("playtest", "status", session))
        port = status["session"]["launch"]["host"]["port"]
        manifest["session"] = session
        manifest["launch"] = status["session"]["launch"]["host"]
        origin = f"http://127.0.0.1:{port}"
        live = pinned_live_debug()
        debug = lambda command: run(live, "--origin", origin, "--command", command)
        # Past the title menu, into held time, standing in the corridor looking down it.
        for _ in range(60):
            if "position" in run(live, "--origin", origin, "--command", "hotel.inspect", optional=True):
                break
            time.sleep(3)
        for choice in ("continue", "new"):
            run("playtest", "browser", session, "--json", json.dumps({"op": "click", "selector": f'[data-choice="{choice}"]:not([hidden])'}), optional=True)
            run("playtest", "browser", session, "--json", json.dumps({"op": "click", "selector": "[data-confirm-yes]"}), optional=True)
        assist(session, {"op": "time", "mode": "action-driven"})
        if args.reset:
            debug("hotel.dev.return-to-entrance")
        debug("hotel.dev.view 0 -6 0 0")
        debug("hotel.dev.motion.off")

        strips = []
        for look in looks:
            item = items[look]
            if not args.viewer:
                debug(f"hotel.dev.hold {item['id']}")
            for control, action_id in zip(CONTROLS, item["wear"]["actions"]):
                if selected_actions and action_id not in selected_actions:
                    continue
                action = actions[action_id]
                if not args.viewer:
                    debug("hotel.dev.fill-tracks")
                # An action paid with a carried item (a reload's cartridges) gets one, and room in the track it fills.
                if not args.viewer and (cost := action["cost"].get("item")):
                    supply = next(i for i in every if cost in i.get("classifications", []))
                    debug(f"hotel.dev.give-supply {supply['id']} 1")
                    debug(f"hotel.dev.set-track {cost} 0")
                assist(session, {"op": "advance", "ms": 400})
                timing = action["timing"]
                total = timing["windup"] + timing["commit"] + timing["recovery"]
                keys = key_moments(motions[held[look]["motion"]], action)
                moments = sorted(set([round(total * i / (args.frames - 1), 3) for i in range(args.frames)] + [round(t, 3) for t, _ in keys]))
                if args.viewer:
                    debug(f"hotel.dev.motion {look} {action_id}")
                    viewer_duration = json.loads(debug("tunables.describe"))["timeline"]["duration"]
                elapsed = 0
                frames = []
                def capture(moment, label):
                    shot = assist(session, {"op": "world-frame"})
                    inspection = json.loads(debug("hotel.inspect"))
                    short = max(inspection.get("armsShortfall", [0, -1]))
                    if short > REACH_TOLERANCE:
                        label += f" · OUT OF REACH {short * 100:.0f} cm"
                        unreachable.append(f"{look} · {action_id} at {elapsed:.3f} s: wrist {short * 100:.1f} cm short")
                    name = f"{look}-{action_id}-{len(frames):02}{os.path.splitext(shot['path'])[1]}"
                    original = os.path.join(originals, name)
                    shutil.copyfile(shot["path"], original)
                    manifest["frames"].append({"look": look, "action": action_id, "requestedSeconds": moment,
                        "elapsedSeconds": elapsed, "label": label, "original": original, "worldFrame": shot, "inspection": inspection})
                    record()
                    frames.append((Image.open(original).convert("RGB"), label))
                assist(session, {"op": "advance", "ms": 50})
                capture(0, "rest before input" if not args.viewer else "rest · viewer")
                if not args.viewer:
                    elapsed = assist(session, {"op": "act", "id": control, "ms": 17}).get("advancedMs", 0) / 1000
                if not args.viewer and json.loads(debug("hotel.inspect")).get("attackPhase", "Ready") == "Ready":
                    failures.append(f"{look} · {action_id}: the action did not start ({json.loads(debug('hotel.inspect') or '{}').get('weapon')})")
                    assist(session, {"op": "advance", "ms": 300})
                    continue
                for moment in moments:
                    if moment == 0:
                        continue
                    if args.viewer:
                        # The C# timeline's single-precision duration can be just below the JSON sum (1.6199999).
                        seek = min(moment, viewer_duration)
                        debug(f"hotel.dev.motion.at {seek}")
                        assist(session, {"op": "advance", "ms": 50})
                        elapsed = seek
                    elif moment > elapsed:
                        elapsed += advance(session, (moment - elapsed) * 1000) / 1000
                    phase = ("windup" if elapsed < timing["windup"] else "commit" if elapsed < timing["windup"] + timing["commit"] else "recovery")
                    pose = next((p for t, p in keys if abs(t - moment) < 1e-3), None)
                    label = f"{elapsed:.2f} s {phase}" + (f" · {pose}" if pose else "")
                    capture(moment, label)
                settled = advance(session, (timing["cooldown"] + 0.3) * 1000) / 1000
                if not args.viewer:
                    elapsed += settled
                capture(elapsed, "settled after action" if not args.viewer else "end held · viewer")
                strips.append((f"{look} · {action_id}", frames))
                strip_image(f"{look} · {action_id}", frames).save(os.path.join(args.out, f"{look}-{action_id}.png"))
                print(f"Captured {look} · {action_id} ({len(frames)} original frames).", flush=True)
        if not strips:
            sys.exit("No action started:\n" + "\n".join(failures))
        contact = Image.new("RGB", (strips[0][1][0][0].width // 2 * max(len(f) for _, f in strips), (strips[0][1][0][0].height // 2 + 24) * len(strips)), "black")
        for row, (title, frames) in enumerate(strips):
            contact.paste(strip_image(title, frames, half=True), (0, row * (frames[0][0].height // 2 + 24)))
        contact.save(os.path.join(args.out, "contact.png"))
        print(f"{len(strips)} strips in {args.out} (contact.png has them all).")
        if unreachable:
            print("Out of the arms' reach:\n" + "\n".join(unreachable))
        if failures:
            print("Not shown:\n" + "\n".join(failures))
            sys.exit(1)
        manifest["complete"] = True
    except Exception as error:
        manifest["error"] = str(error)
        raise
    finally:
        record()
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
        draw.text((i * width + 6, 28), label, fill=(255, 90, 80) if "OUT OF REACH" in label else (255, 230, 160))
    return strip


if __name__ == "__main__":
    main()
