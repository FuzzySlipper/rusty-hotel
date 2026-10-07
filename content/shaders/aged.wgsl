// Aged hotel surfaces: the standard shade over a base colour worn, faded and stained in world space, so the marks run
// across repeats and box edges and no two stretches of one wallpaper or carpet match. Each surface's look comes from
// its `aging` profile in scene/aging.json (SurfaceAging):
//   parameters[0]: wear strength, wear patch size (m), fade strength, fade reach (m)
//   parameters[1]: stain strength, stain size (m), stain band foot and head (world height, m; head <= foot: everywhere)
//   parameters[2]: seam strip width (m; 0: no seams), seam lift strength
//   parameters[3]: stain colour (rgb)
// product_map_a is the weathering noise (linear): R wear, G stains, B seam lift, A grain. Its wear patches run about a
// third of its repeat and its stains about a sixth, so a profile's sizes set the repeat at three and six times theirs.

#import rusty::types::Surface
#import rusty::material::{material, product_map_a, product_sampler_a}
#import rusty::view::{frame, lights, clusters}
#import rusty::lighting::fragment_cluster
#import rusty::shade::standard_shade

// `rusty::lighting`'s cluster row length.
const CLUSTER_STRIDE: u32 = 64u;

fn noise(at: vec2<f32>) -> vec4<f32> {
    return textureSampleLevel(product_map_a, product_sampler_a, at, 0.0);
}

// The surface's own plane in metres: the floor plan for a floor or ceiling, along the wall and up it for a wall.
fn plane(position: vec3<f32>, normal: vec3<f32>) -> vec2<f32> {
    let n = abs(normal);
    if n.y > 0.7 { return position.xz; }
    if n.x > n.z { return vec2<f32>(position.z, position.y); }
    return vec2<f32>(position.x, position.y);
}

// How much one light has bleached this point: close lamps more, the faces turned to them most.
fn bleach(index: u32, position: vec3<f32>, normal: vec3<f32>, reach: f32) -> f32 {
    let light = lights[index];
    // Point (3) and spot (4) lamps only; sky and ambient light fade nothing here.
    if light.color_kind.w < 2.5 { return 0.0; }
    let to_light = light.position_range.xyz - position;
    let distance = length(to_light);
    let within = clamp(1.0 - distance / reach, 0.0, 1.0);
    return within * within * (0.4 + 0.6 * max(dot(normal, to_light / max(distance, 1e-4)), 0.0));
}

fn exposure(position: vec3<f32>, normal: vec3<f32>, reach: f32) -> f32 {
    var sum = 0.0;
    if frame.cluster_grid.w == 1u {
        let base = fragment_cluster(position) * CLUSTER_STRIDE;
        let count = min(clusters[base], CLUSTER_STRIDE - 1u);
        for (var slot = 0u; slot < count; slot = slot + 1u) {
            sum += bleach(clusters[base + 1u + slot], position, normal, reach);
        }
    } else {
        for (var index = frame.counts.y; index < frame.counts.y + frame.counts.x; index = index + 1u) {
            sum += bleach(index, position, normal, reach);
        }
    }
    return min(sum, 1.0);
}

fn shade(input: Surface) -> vec4<f32> {
    var surface = input;
    let wear = material.parameters[0];
    let stain = material.parameters[1];
    let seam = material.parameters[2];
    let at = plane(surface.world_position, surface.normal);
    var colour = surface.base.rgb;
    let grain = noise(at / 0.37).a - 0.5;

    // Wear: broad patches darkened and dulled toward brown.
    let worn = smoothstep(0.55, 0.85, noise(at / (wear.y * 3.0)).r + grain * 0.15) * wear.x;
    colour = mix(colour, colour * vec3<f32>(0.78, 0.74, 0.68), worn);

    // Fade: near a lamp the colour washes out toward a paler grey of itself.
    if wear.z > 0.0 {
        let faded = exposure(surface.world_position, surface.normal, wear.w) * wear.z;
        let grey = dot(colour, vec3<f32>(0.2126, 0.7152, 0.0722));
        colour = mix(colour, vec3<f32>(grey) * 1.12 + 0.04, faded);
    }

    // Water stains: blotches with a darker tide line at their rim, within their band of height.
    if stain.x > 0.0 {
        var band = 1.0;
        if stain.w > stain.z { band = smoothstep(stain.z, stain.w, surface.world_position.y); }
        let field = noise(at / (stain.y * 6.0) + vec2<f32>(0.31, 0.67)).g + grain * 0.06;
        let inside = smoothstep(0.74, 0.79, field);
        let rim = smoothstep(0.74, 0.76, field) * (1.0 - smoothstep(0.76, 0.83, field));
        let amount = band * stain.x;
        colour = mix(colour, colour * material.parameters[3].rgb, inside * amount * 0.6);
        colour = mix(colour, colour * material.parameters[3].rgb * 0.85, rim * amount * 0.7);
    }

    // Seam lift: at the paper's strip joins, a thin shadowed gap and a lit lifted edge, where the noise says it peels.
    if seam.x > 0.0 {
        let across = at.x / seam.x;
        let strip = floor(across + 0.5);
        let offset = (across - strip) * seam.x;
        let lifted = smoothstep(0.55, 0.85, noise(vec2<f32>(strip * 0.173, at.y / 2.4)).b) * seam.y;
        let gap = 1.0 - smoothstep(0.001, 0.004, abs(offset));
        let edge = smoothstep(0.004, 0.006, offset) * (1.0 - smoothstep(0.006, 0.016, offset));
        colour = colour * (1.0 - gap * lifted * 0.6) + edge * lifted * 0.05;
    }

    surface.base = vec4<f32>(colour, surface.base.a);
    return standard_shade(surface);
}
