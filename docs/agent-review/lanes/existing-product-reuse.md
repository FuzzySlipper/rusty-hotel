# Lane: Existing product reuse

**Always check.** Does the change extend this repository's actual owner, or
introduce competing state or behavior?

Start with `AGENTS.md` and `docs/architecture.md`, then search the implementation
and callers. The architecture map names the current Player, Scene, Route, Supplies, Combat,
Spirit and Expedition owners. HotelProduct composes their lifecycle; HotelHud
projects their facts. The DOM owns presentation and semantic UI actions.
Extend those owners rather than placing new game state in the entry point,
a parallel interaction registry, or the browser.

An actionable finding names the existing type/member, the new duplicate at
file/line, the overlapping authority, and the callers or invariant that now
can disagree. For misplaced behavior, show the concrete domain owner and why
putting the behavior elsewhere creates competing responsibility.

New files, similar names, or a direct method are not defects. Prefer explicit
composition and extending a real domain owner. Do not prescribe a universal
framework, registry, bus, or speculative abstraction as the replacement.
