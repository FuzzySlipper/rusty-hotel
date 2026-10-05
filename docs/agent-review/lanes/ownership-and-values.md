# Lane: Ownership and values

Use for cross-owner changes, definitions, or tuning. Does each decision and
value live with the owner that gives it meaning?

Quote the assumption/value at file/line, name its current and intended owners,
and show which uses become wrong when it changes. C# owns product policy and
state; Engine owns its named mechanisms; DOM code owns presentation and semantic
actions. Separate authored definitions, runtime state, and transient projections.

Gameplay values (timings, durations, ranges, damage, costs) and player-facing
text belong in typed, domain-organized content; flag them in C# or JS call sites,
including literals that repeat a definition's name or number. Flag additions to
a catch-all file, record, class or template that should have been split along
domain lines (AGENTS.md "Content and code organization"), and any vocabulary
(bindings, HUD facts, interactables, saved fields) declared in more than one
place. Structural and mathematical constants stay near the algorithm. Do not
move vocabulary without moving authority, or introduce a factory/interface
merely for stylistic consistency.
