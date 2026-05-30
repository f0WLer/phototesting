// Accumulation pass: adds one new BGRA8 sample (optionally sRGB-linearised) to the running RGBA32F sum.
// Mirrors EmulsionProcessor.AccumulateBytes() physics, including the linearisation branch.
#version 330 core
in  vec2 v_uv;
out vec4 out_sum;

uniform sampler2D u_sample;   // RGBA8 - new frame blit from virtual camera
uniform sampler2D u_accum;    // RGBA32F - running channel sums
uniform bool      u_linearize;

float srgbToLinear(float c) {
    return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
}

void main() {
    vec3 s    = texture(u_sample, v_uv).rgb;
    vec3 prev = texture(u_accum,  v_uv).rgb;
    if (u_linearize)
        s = vec3(srgbToLinear(s.r), srgbToLinear(s.g), srgbToLinear(s.b));
    out_sum = vec4(prev + s, 1.0);
}
