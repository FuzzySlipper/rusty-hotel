#!/usr/bin/env python3
"""Hotel's model audit: every resident look measured as combat places it, against its resident kind's body.

    python3 art/model-audit/audit.py [--out DIR]

Builds the manifest from content (combat/looks.json for model, scale, yaw and idle clip; combat/residents.json for the
body height and radius a look must fill) and runs the asset pipeline's model audit (tools/model-audit, needs Blender).
A look's model stands with its feet at the origin, about as tall as its kind's body and centred on it. Linux art tooling;
not part of the build.
"""
import argparse
import json
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CONTENT = os.path.join(REPO, "content")
# Within this share of the body height, and the footprint centred within this share of the body radius.
HEIGHT_TOLERANCE = 0.08
CENTRE_SHARE = 0.6


def manifest():
    looks = {l["id"]: l for l in json.load(open(os.path.join(CONTENT, "combat/looks.json")))["looks"]}
    models = []
    for kind in json.load(open(os.path.join(CONTENT, "combat/residents.json")))["residents"]:
        look = looks[kind["look"]]
        models.append({"id": kind["id"], "path": os.path.join(CONTENT, look["model"]), "scale": look["scale"],
                       "yawDegrees": look["yawDegrees"], "clip": look["clips"]["idle"],
                       # Tripo's humanoid rig, its left to its right at the collarbones: the steadiest pair, where the
                       # rig set under a long dress places feet, thighs and upper arms askew.
                       "sideBones": [["L_Clavicle", "R_Clavicle"]],
                       "expect": {"height": kind["height"], "heightTolerance": HEIGHT_TOLERANCE, "feetTolerance": 0.03,
                                  "centreTolerance": round(kind["radius"] * CENTRE_SHARE, 3)}})
    return {"reference": {"height": 1.8}, "groups": [{"name": "residents", "models": models}]}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default=os.path.join(REPO, ".runtime", "model-audit"))
    parser.add_argument("--asset-pipeline", default=os.environ.get("ASSET_PIPELINE", os.path.join(os.path.dirname(REPO), "asset-pipeline")))
    args = parser.parse_args()
    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, "manifest.json")
    json.dump(manifest(), open(path, "w"), indent=2)
    tool = os.path.join(args.asset_pipeline, "tools", "model-audit", "audit.py")
    sys.exit(subprocess.run([sys.executable, tool, "--manifest", path, "--out", args.out]).returncode)


if __name__ == "__main__":
    main()
