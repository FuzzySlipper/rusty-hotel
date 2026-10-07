# Adopts tileable PBR sets made by asset-pipeline's tools/materials into the hotel (offline; never on the build path).
#   python3 art/materials-01/adopt.py <asset-pipeline material output folder> [id ...]
#   python3 art/materials-01/adopt.py --verify [id ...]
# For each material in sets.json: copies its manifest and every map it declares (albedo, normal, both heights,
# roughness, AO and the packed ORM) byte for byte into art/materials-01/<id>/, refusing a map whose hash differs from
# the manifest's; writes the runtime albedo, normal and ORM as RGBA8 PNG (the pinned Engine admits RGBA8; RGB pixels
# are unchanged) into content/materials/<id>/; and prints the surface entries for content/scene/surfaces.json: the
# repeat in metres (its height from the map's own aspect), the ORM map, and the stochastic tiling sets.json gives a
# set without a direction.
# --verify checks the retained maps against their manifests' hashes and the runtime maps' pixels against them.
import hashlib, json, os, shutil, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SETS = json.load(open(os.path.join(HERE, "sets.json")))
RUNTIME = ("albedo", "normal", "orm")
verify = sys.argv[1:2] == ["--verify"]
source = None if verify else sys.argv[1]
only = sys.argv[2:]

def sha(path): return hashlib.sha256(open(path, "rb").read()).hexdigest()

def check(id, art, manifest):
    """Every declared map is retained with its recorded hash, and each runtime map draws the same pixels."""
    for name, spec in manifest["maps"].items():
        path = os.path.join(art, spec["file"])
        if not os.path.exists(path): sys.exit(f"{id}: {spec['file']} is missing")
        if sha(path) != spec["sha256"]: sys.exit(f"{id}: {spec['file']} does not match its manifest hash")
    for name in RUNTIME:
        retained = Image.open(os.path.join(art, manifest["maps"][name]["file"])).convert("RGBA")
        runtime = Image.open(os.path.join(ROOT, "content", "materials", id, name + ".png"))
        if runtime.mode != "RGBA" or retained.tobytes() != runtime.tobytes(): sys.exit(f"{id}: runtime {name}.png differs from its map")

entries = []
for id, spec in SETS["sets"].items():
    if only and id not in only: continue
    art = os.path.join(HERE, id)
    if verify:
        check(id, art, json.load(open(os.path.join(art, "material.json"))))
        print("VERIFIED", id)
        continue
    src = os.path.join(source, spec["run"], id)
    manifest = json.load(open(os.path.join(src, "material.json")))
    os.makedirs(art, exist_ok=True)
    shutil.copy(os.path.join(src, "material.json"), os.path.join(art, "material.json"))
    for map in manifest["maps"].values():
        shutil.copy(os.path.join(src, map["file"]), os.path.join(art, map["file"]))
    out = os.path.join(ROOT, "content", "materials", id); os.makedirs(out, exist_ok=True)
    for name in RUNTIME:
        Image.open(os.path.join(art, manifest["maps"][name]["file"])).convert("RGBA").save(os.path.join(out, name + ".png"), optimize=True)
    check(id, art, manifest)
    width_m = manifest["repeat_m"][0]
    w, h = Image.open(os.path.join(art, "albedo.png")).size
    entry = {"id": id, "color": spec.get("color", [1, 1, 1]), "texture": f"materials/{id}/albedo.png",
             "tileWidth": round(width_m, 3), "tileHeight": round(width_m * h / w, 3), "roughness": 1,
             "normalMap": f"materials/{id}/normal.png", "normalScale": spec["normalScale"], "ormMap": f"materials/{id}/orm.png"}
    # A designed repeat stays registered; a surface without a direction may take hex tiling to break its repeat.
    if "stochasticTiling" in spec: entry["stochasticTiling"] = spec["stochasticTiling"]
    entries.append(entry)
    print("ADOPTED", id, f"{w}x{h}", "seam", manifest["seams"]["albedo"])
if entries: print(json.dumps(entries, indent=1))
