# Hotel asset sources

Runtime assets live under `content/`. This directory retains editable sources,
exact prompts and portable provenance. These authoring tools never run during
`rusty dev` or `rusty build` and add no sibling checkout dependency.

## Wallpaper and carpet

`hotel-kit-01/prompts.json` records the human direction and exact prompts for
three built-in image-generation calls. The `*-source.png` files are the original
1254-square RGB outputs. Their runtime PNG derivatives add an opaque alpha
channel because the pinned Engine accepts RGBA8 PNG; RGB pixels and dimensions
are unchanged. `assets.json` records both sets of hashes.

`content/scene/surfaces.json` supplies wall repeat dimensions in metres and surface
tints. One carpet texture supplies teal room, olive room and burgundy corridor
variants. Texture repetition, filtering, decoding and resource ownership remain
Engine services. Inspect repeats from player eye height after changing scale.

`tools/author-normals.py` derives the tileable normal maps (`*-normal.png`) from
those runtime textures: printed-ink emboss and seeded paper grain for the
wallpapers, seeded nap modulated by the weave for the carpet, all differentiated
with wrap-around operations so each map tiles like its source. It needs numpy
and Pillow and runs offline only. `surfaces.json` sets each map's strength.

## Light fittings

`fittings-01/build.py` builds the sconce, ceiling lamp, pendant and service-lamp
meshes parametrically in Blender (no images, prompts or provider calls) and
saves an editable `.blend` master for each beside it; `provenance.json` records
hashes. Run from the repository root:

```text
blender -b --python art/fittings-01/build.py
```

## Furniture

`furniture-01/request.json` fixes the goal, the shared style suffix and one
text prompt per piece; each piece is one paid Tripo P2 text-to-model generation
(triangles, 10,000 faces), retained as `furniture-01/native/<piece>.glb`.
`furniture-01/prepare.py` turns, fits and cleans each download using
`pieces.json` (its quarter turn, the fixture footprint, and the working-surface
height finds and props rest on), saves a `.blend` master per piece, writes
`content/models/furniture/<piece>.glb`, and renders inspection views into
ignored `.runtime/mesh-review/furniture/`. `provenance.json` records the
provider tasks, hashes and use limits; raw receipts carry signed URLs and are
not retained. The lounge armchair below belongs to the same set. The whole set
shares one budget, `triangleBudget` in `pieces.json` (12,500 triangles per
piece); generation targets sit under it (10,000 faces for these pieces, 12,000
for the armchair) and `prepare.py` fails if any GLB in
`content/models/furniture/` exceeds it.

```text
blender -b --python art/furniture-01/prepare.py
```

## Lounge armchair

`armchair-01/request.json` fixes the goal, the text prompt and the one paid
Tripo P2 text-to-model generation (triangles, 12,000 faces). `native-01.glb` is
Tripo's download; `prepare.py` makes `armchair.blend` and
`content/models/furniture/armchair.glb` and renders inspection views into
ignored `.runtime/mesh-review/`. `provenance.json` records the provider task,
hashes and use limits. Raw provider receipts carry signed URLs and are not
retained.

```text
blender -b --python art/armchair-01/prepare.py
```

## Expedition recorder

`expedition-recorder-01/request.json` fixes the goal, source prompt, attempt
allowance and triangle budget. `source-01.png` is the original generated image;
the image was critiqued before reconstruction. `native-01.fbx` is Tripo's native
download. `recorder.blend` is the local editable master, and
`content/models/recorder.glb` is its runtime derivative. `provenance.json` records
the provider task, source/derivative hashes and bounded use decision.

To regenerate the local conversion and actual mesh inspection renders with
Blender installed, run from the repository root:

```text
blender -b --python art/expedition-recorder-01/prepare.py
```

This imports the retained FBX, normalizes its floor pivot and longest horizontal
extent to 0.58 m, saves the master, exports GLB and renders six views into ignored
`.runtime/mesh-review/`. It makes no provider call. Material default roughness
and metallic values do not override linked texture inputs. Camera-axis filenames
are inspection labels, not assertions about the recorder's semantic front.

The mesh has 8,735 triangles and is used as static desk dressing. It preserves
the reel/case/meter silhouette but simplifies tape, paint wear and small controls.
Its unseen rear is an extrapolation. The final use limits are retained in `provenance.json`. It is not a pickup or close-up hero prop.
Raw provider receipts with temporary signed URLs remain ignored; the portable
receipt contains no credentials or signed URLs. Queue execution diagnostics and
player-view evidence belong in Den, not the source-asset ledger.

## Held items

`held-01/request.json` fixes the goal, the shared style and one subject per held item (pry bar, survey pistol, service
bell, flare pistol). Each `source-<piece>.png` is one GPT image call (the codex-image-gen relay), its whole spec the
style followed by the subject. Each mesh is one local TRELLIS.2 bf16 image-to-3D run on the owner's RTX 5090 through
asset-pipeline's native ComfyUI runner (15,000 faces, remesh grid 256, 2K bakes); `runs/<piece>/` keeps the exact
graph and run record, and `native/<piece>.glb` the download. There is no paid call.
`held-01/prepare.py` turns each download so its business end points along the hand's forward, scales it to its real
length, puts its origin at the grip (`pieces.json`), downsizes textures to 1024 JPEG and scales the metallic channel
down (`metallicScale`): the Hotel lights props without a reflected environment, so fully metallic bakes read black.
It writes `content/models/held/<piece>.glb`, fails above `triangleBudget`, and saves the prepared `<piece>.blend`
master, its textures kept beside it as JPEG files in `textures/` rather than packed so the master stays small.

```text
blender -b --python art/held-01/prepare.py
```

Where each model sits in the hand is presentation tuning in `content/combat/held.json`, not part of the mesh.
`provenance.json` records hashes, run times and use limits.

## Spirits

`spirits-01/request.json` fixes the shared style (an upright, symmetric bell-headed moth with its wings spread flat)
and one subject per spirit; each `source-<spirit>.png` is one GPT image call, and each mesh one local TRELLIS.2 bf16
run on den-patch's RTX 5090 (12,000 faces), with `runs/<spirit>/` and `native/<spirit>.glb` retained as for the held
items. `spirits-01/prepare.py` turns each moth to face the runtime -z, scales it to its `width`, and splits it by
region into a body and two wings (`pieces.json`: the hinge line, boxes such as the head and antennae that stay with
the body, and for wings set behind the body the back slab that goes with them). Each wing's origin is its hinge; the
script prints the hinges for the spirit's `look`. Textures are 1024 JPEG, metal is softened as for the held items,
and a faint emission from the base colour (`glow`) lets the spirits read in dim rooms. It writes
`content/models/spirits/<spirit>-{body,left,right}.glb` and saves the prepared `<spirit>.blend` master (body and wings),
its textures as JPEG files in `textures/`.

```text
blender -b --python art/spirits-01/prepare.py
```

## Find props

`props-01/request.json` fixes the shared style (a find resting as it would on a table, three-quarter view) and one
subject per prop. Each `source-<prop>.png` is one GPT image call; each mesh is one local TRELLIS.2 bf16 run on
den-patch at 6,000 faces, or, where TRELLIS could not reconstruct a thin or hollow object even at a higher budget,
one paid Tripo P2 image-to-model generation from the same source (`provenance.json` names which, with the provider
task). `native/<prop>.glb` is the download. `props-01/prepare.py` turns each prop, fits it uniformly inside its
display's footprint (`pieces.json` `fit`), stands it on the ground with its origin at the centre of its base, and
writes `content/models/props/<prop>.glb` with 1024 JPEG textures and softened metal, saving each prepared `<prop>.blend`
master with its textures as JPEG files in `textures/`. The displays in
`content/scene/fixtures.json` show these as find models; the flat ledger page stays an authored box.

```text
blender -b --python art/props-01/prepare.py
```

## Ambient audio and primitive props

`tools/author-ambience.py` authors the two deterministic mono PCM WAV loops in
`content/audio/` using Python's standard library. This is optional offline
authoring; the shipped WAV files require no Python at runtime. The ventilation
bed is global and the tape-machine loop is spatial, with volumes and range in
`content/excursions/west-wing/ambience.json`. Engine owns all playback and listener motion. Later interactive
audio must be balanced against these quiet beds.

Doors, field cases, notebook, pencil and abstract framed
pictures are product-authored box compositions; trim is built from the kit's
trim styles in `content/scene/kit.json`, and light fittings are the models above: fixtures in
`content/scene/fixtures.json`, placed by `content/excursions/west-wing/plan.json`. They introduce
no external art dependency. The owner-supplied historical screenshots remain in
`docs/references/` as direction rather than runtime textures or interface art.

## Source history and reviews

The six original image/mesh critique reports, including the Hotel-lighting
follow-ups, are preserved verbatim with their source hashes in Den document
`[doc: rusty-hotel/repository-docs-archive-2026-10-04]`. `decision-01.json` and
`provenance.json` point to that archive. Those reports describe a particular
review round; the retained source files and final use limits are the authoring
authority. Playtest captures, queue incidents and task progress remain in Den.
