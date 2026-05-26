// Develop pass: converts the RGBA32F accumulated sums to a tone-mapped RGBA8 output.
// Mirrors ExposureAccumulationBuffer.Develop() exactly, including weight normalisation.
#version 330 core
in  vec2 v_uv;
out vec4 out_color;

uniform sampler2D u_accum;
uniform float u_inv_ref;
uniform bool  u_spectral;
uniform bool  u_hd_curve;
uniform float u_red_sens;
uniform float u_green_sens;
uniform float u_blue_sens;
uniform float u_dev_strength;
uniform float u_gamma;

float hdCurve(float E, float k, float g) {
    float d = log(1.0 + E * k) / log(10.0);
    return pow(max(d, 0.0), g);
}

void main() {
    vec3 sum = texture(u_accum, v_uv).rgb;
    vec3 E   = sum * u_inv_ref;

    vec3 result;
    if (u_spectral) {
        float e = E.r * u_red_sens + E.g * u_green_sens + E.b * u_blue_sens;
        float v = u_hd_curve ? hdCurve(e, u_dev_strength, u_gamma) : e;
        result  = vec3(clamp(v, 0.0, 1.0));
    } else {
        if (u_hd_curve) {
            result = clamp(vec3(
                hdCurve(E.r, u_dev_strength, u_gamma),
                hdCurve(E.g, u_dev_strength, u_gamma),
                hdCurve(E.b, u_dev_strength, u_gamma)), 0.0, 1.0);
        } else {
            result = clamp(E, 0.0, 1.0);
        }
    }
    out_color = vec4(result, 1.0);
}
