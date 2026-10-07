# Adopts tileable PBR sets made by asset-pipeline's tools/materials into the hotel (offline; never on the build path).
#   python3 art/materials-01/adopt.py <asset-pipeline material output folder> [id ...]
# For each material in sets.json: copies its request, manifest and ORM map into art/materials-01/<id>/, writes the
# runtime albedo and normal as RGBA8 PNG (the pinned Engine admits RGBA8; RGB pixels are unchanged) into
# content/materials/<id>/, and prints the surface entries for content/scene/surfaces.json: the repeat in metres (its
# height from the map's own aspect) and the mean roughness the scalar material takes until it samples the ORM map.
import hashlib, json, os, shutil, sys
from PIL import Image, ImageStat

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SETS = json.load(open(os.path.join(HERE, "sets.json")))
source = sys.argv[1]
only = sys.argv[2:]

def sha(path): return hashlib.sha256(open(path, "rb").read()).hexdigest()

entries = []
for id, spec in SETS["sets"].items():
    if only and id not in only: continue
    src = os.path.join(source, spec["run"], id)
    art = os.path.join(HERE, id); os.makedirs(art, exist_ok=True)
    out = os.path.join(ROOT, "content", "materials", id); os.makedirs(out, exist_ok=True)
    shutil.copy(os.path.join(src, "material.json"), os.path.join(art, "material.json"))
    shutil.copy(os.path.join(src, "orm.png"), os.path.join(art, "orm.png"))
    for name in ("albedo", "normal"):
        Image.open(os.path.join(src, name + ".png")).convert("RGBA").save(os.path.join(out, name + ".png"), optimize=True)
    manifest = json.load(open(os.path.join(src, "material.json")))
    width_m = manifest["repeat_m"][0]
    w, h = Image.open(os.path.join(src, "albedo.png")).size
    roughness = ImageStat.Stat(Image.open(os.path.join(src, "orm.png")).convert("RGB").split()[1]).mean[0] / 255
    entry = {"id": id, "color": spec.get("color", [1, 1, 1]), "texture": f"materials/{id}/albedo.png",
             "tileWidth": round(width_m, 3), "tileHeight": round(width_m * h / w, 3),
             "roughness": round(roughness, 2), "normalMap": f"materials/{id}/normal.png", "normalScale": spec["normalScale"]}
    entries.append(entry)
    print("ADOPTED", id, f"{w}x{h}", "seam", manifest["seams"]["albedo"])
print(json.dumps(entries, indent=1))
