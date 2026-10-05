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

## Ambient audio and primitive props

`tools/author-ambience.py` authors the two deterministic mono PCM WAV loops in
`content/audio/` using Python's standard library. This is optional offline
authoring; the shipped WAV files require no Python at runtime. The ventilation
bed is global and the tape-machine loop is spatial, with volumes and range in
`content/excursions/west-wing/ambience.json`. Engine owns all playback and listener motion. Later interactive
audio must be balanced against these quiet beds.

Trim, doors, lights, furniture, field cases, notebook, pencil and abstract framed
pictures are product-authored box compositions in
`content/excursions/west-wing/geometry.json`. They introduce
no external art dependency. The owner-supplied historical screenshots remain in
`docs/references/` as direction rather than runtime textures or interface art.

## Source history and reviews

The six original image/mesh critique reports, including the Hotel-lighting
follow-ups, are preserved verbatim with their source hashes in Den document
`[doc: rusty-hotel/repository-docs-archive-2026-10-04]`. `decision-01.json` and
`provenance.json` point to that archive. Those reports describe a particular
review round; the retained source files and final use limits are the authoring
authority. Playtest captures, queue incidents and task progress remain in Den.
