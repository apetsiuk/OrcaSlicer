#version 110

const vec3 ZERO = vec3(0.0, 0.0, 0.0);
//BBS: add grey and orange
//const vec3 GREY = vec3(0.9, 0.9, 0.9);
const vec3 ORANGE = vec3(0.8, 0.4, 0.0);
const vec3 LightRed = vec3(0.78, 0.0, 0.0);
const vec3 LightBlue = vec3(0.73, 1.0, 1.0);
const float EPSILON = 0.0001;

struct PrintVolumeDetection
{
	// 0 = rectangle, 1 = circle, 2 = custom, 3 = invalid
	int type;
    // type = 0 (rectangle):
    // x = min.x, y = min.y, z = max.x, w = max.y
    // type = 1 (circle):
    // x = center.x, y = center.y, z = radius
	vec4 xy_data;
    // x = min z, y = max z
	vec2 z_data;
};

struct SlopeDetection
{
    bool actived;
	float normal_z;
    mat3 volume_world_normal_matrix;
};

uniform vec4 uniform_color;
uniform bool use_color_clip_plane;
uniform vec4 uniform_color_clip_plane_1;
uniform vec4 uniform_color_clip_plane_2;
uniform SlopeDetection slope;

//BBS: add outline_color
uniform bool is_outline;
uniform sampler2D depth_tex;
uniform vec2 screen_size;


#ifdef ENABLE_ENVIRONMENT_MAP
    uniform sampler2D environment_tex;
    uniform bool use_environment_tex;
#endif // ENABLE_ENVIRONMENT_MAP

uniform PrintVolumeDetection print_volume;

uniform float z_far;
uniform float z_near;

varying vec3 clipping_planes_dots;
varying float color_clip_plane_dot;

// x = diffuse, y = specular;
varying vec2 intensity;

varying vec4 world_pos;
varying float world_normal_z;
varying vec3 eye_normal;
varying vec3 fs_world_normal; // --- NEW: Add this line ---

vec3 getBackfaceColor(vec3 fill) {
    float brightness = 0.2126 * fill.r + 0.7152 * fill.g + 0.0722 * fill.b;
    return (brightness > 0.75) ? vec3(0.11, 0.165, 0.208) : vec3(0.988, 0.988, 0.988);
}

// Silhouette edge detection & rendering algorithem by leoneruggiero
// https://www.shadertoy.com/view/DslXz2
#define INFLATE 1

float GetTolerance(float d, float k)
{
    // -------------------------------------------
    // Find a tolerance for depth that is constant
    // in view space (k in view space).
    //
    // tol = k*ddx(ZtoDepth(z))
    // -------------------------------------------
    
    float A=-   (z_far+z_near)/(z_far-z_near);
    float B=-2.0*z_far*z_near /(z_far-z_near);
    
    d = d*2.0-1.0;
    
    return -k*(d+A)*(d+A)/B;   
}

float DetectSilho(vec2 fragCoord, vec2 dir)
{
    // -------------------------------------------
    //   x0 ___ x1----o 
    //          :\    : 
    //       r0 : \   : r1
    //          :  \  : 
    //          o---x2 ___ x3
    //
    // r0 and r1 are the differences between actual
    // and expected (as if x0..3 where on the same
    // plane) depth values.
    // -------------------------------------------
    
    float x0 = abs(texture2D(depth_tex, (fragCoord + dir*-2.0) / screen_size).r);
    float x1 = abs(texture2D(depth_tex, (fragCoord + dir*-1.0) / screen_size).r);
    float x2 = abs(texture2D(depth_tex, (fragCoord + dir* 0.0) / screen_size).r);
    float x3 = abs(texture2D(depth_tex, (fragCoord + dir* 1.0) / screen_size).r);
    
    float d0 = (x1-x0);
    float d1 = (x2-x3);
    
    float r0 = x1 + d0 - x2;
    float r1 = x2 + d1 - x1;
    
    float tol = GetTolerance(x2, 0.04);
    
    return smoothstep(0.0, tol*tol, max( - r0*r1, 0.0));

}

float DetectSilho(vec2 fragCoord)
{
    return max(
        DetectSilho(fragCoord, vec2(1,0)), // Horizontal
        DetectSilho(fragCoord, vec2(0,1))  // Vertical
        );
}

void main()
{
    if (any(lessThan(clipping_planes_dots, ZERO)))
        discard;

    vec4 color;
    // --- START MODIFICATION 1 ---
    //
    // 1. Define the range you want to "zoom in" on
    //float range_min = 0.0;
    //float range_max = 0.5;

    // 2. Get the absolute normal vector (all 3 components)
    //vec3 abs_normal = abs(fs_world_normal);

    // 3. Remap all three components (X, Y, and Z) at the same time.
    //    GLSL can perform this math on the entire vector at once.
    //vec3 remapped_color = clamp((abs_normal - range_min) / (range_max - range_min), 0.0, 1.0);

    // 4. Set the final color
    //color = vec4(remapped_color, uniform_color.a);
    //
    // --- END MODIFICATION ---

    // --- START MODIFICATION 2 ---
    //
    // 1. Define the color for flat surfaces (pointing up, Z=1.0)
    //vec3 flat_color = vec3(0.0, 0.9, 0.9); // Blue (perfectly flat)
    //vec3 steep_color = vec3(0.9, 0.0, 0.9); // Red (perfectly vertical)

    // 2. Get the flatness value (1.0 = flat, 0.0 = steep)
    //float t = abs(fs_world_normal.z);

    // 3. (NEW) Apply a power function to make it more sensitive.
    //    An exponent > 1.0 (like 4.0 or 8.0) makes the
    //    gradient "hug" the steep_color. Only very flat
    //    surfaces (t close to 1.0) will get the flat_color.
    //    Increase '8.0' to make it even more sensitive.

    //Instead of thinking of a "max," just think of a "practical range."
    //For a sharp curve, try values like 16.0, 32.0, or 64.0.
    //For a near-binary "on/off" switch (flat vs. not-flat), try 128.0 or 256.0.
    //Going higher than that will almost certainly behave identically to 256.0 due to precision limits.
    //float t_remapped = pow(t, 20.0);

    // 4. Use mix() to blend between the two colors.
    //    mix(A, B, t) = A*(1-t) + B*t
    //    When t=1.0 (flat), it returns 100% flat_color.
    //    When t=0.0 (steep), it returns 100% steep_color.
    //vec3 remapped_color = mix(steep_color, flat_color, t_remapped);

    // 5. Set the final color
    //color = vec4(remapped_color, uniform_color.a);
    //
    // --- END MODIFICATION ---

    // --- START MODIFICATION 3 ---
    //
    // 1. Define the colors
    //vec3 flat_color = vec3(0.0, 0.9, 0.9); // Blue (perfectly flat)
    //vec3 steep_color = vec3(0.7, 0.2, 0.7); // Red (perfectly vertical)

    // 2. Get the flatness value (1.0 = flat, 0.0 = steep)
    //float t = abs(fs_world_normal.z);

    // 3. Apply power for sensitivity
    //float t_remapped = pow(t, 32.0);

    // 4. Calculate the base gradient color
    //vec3 gradient_color = mix(steep_color, flat_color, t_remapped);

    // 5. (NEW) Calculate and draw contour lines
    // ---
    //float num_lines = 5.0; // How many lines to draw between 0 and 90 degrees
    //vec3 line_color = vec3(1.0, 1.0, 1.0); // Black

    // Get the derivative of 't' (how fast it changes per pixel)
    // This lets us draw a perfect, anti-aliased 1-pixel-thick line.
    //float d = fwidth(t) * num_lines;

    // 'fract(t * num_lines)' creates a repeating 0-1 sawtooth pattern.
    // 'line_check' will be 1.0 in the middle and 0.0 on the lines.
    //float line_check = smoothstep(0.0, d, fract(t * num_lines)) - 
    //                   smoothstep(1.0 - d, 1.0, fract(t * num_lines));

    // Invert it: now 'line_alpha' is 1.0 on the lines and 0.0 in the middle.
    //float line_alpha = 1.0 - line_check;

    // 6. (NEW) Blend the line color on top of the gradient color
    // mix(A, B, alpha) = (A * (1-alpha)) + (B * alpha)
    //vec3 final_color = mix(gradient_color, line_color, line_alpha);
    // ---

    // 7. Set the final color
    //color = vec4(final_color, uniform_color.a);
    //
    // --- END MODIFICATION ---

    // --- START MODIFICATION ---
    //
    // 1. Define the base gradient colors
    vec3 flat_color = vec3(0.0, 0.9, 0.9); // Blue
    vec3 steep_color = vec3(0.7, 0.2, 0.7); // Red

    // 2. Define the custom color and its range
    vec3 custom_color = vec3(1.0, 1.0, 1.0); // Bright Green
    float range_min = 0.00; // The Z-normal value to start the band
    float range_max = 0.05; // The Z-normal value to end the band

    // 3. Get the flatness value (1.0 = flat, 0.0 = steep)
    float t = abs(fs_world_normal.z);

    // 4. Calculate the base gradient color (using the sensitive pow)
    float t_remapped = pow(t, 164.0);
    vec3 base_gradient_color = mix(steep_color, flat_color, t_remapped);

    // 5. (NEW) Create a smooth, anti-aliased "mask" for the custom range.
    //    fwidth(t) gets the rate of change, giving us a 1-pixel width.
    float edge_width = fwidth(t);
    //    smoothstep creates a soft "on" switch at range_min
    float mask_on = smoothstep(range_min - edge_width, range_min, t);
    //    smoothstep creates a soft "off" switch at range_max
    float mask_off = smoothstep(range_max, range_max + edge_width, t);
    //    The final mask is 1.0 inside the band and 0.0 outside.
    float custom_mask = mask_on - mask_off;

    // 6. (NEW) Blend the custom color on top of the base gradient.
    //    mix(A, B, alpha) = (A * (1-alpha)) + (B * alpha)
    vec3 final_color = mix(base_gradient_color, custom_color, custom_mask);
    
    // 7. Set the final color
    color = vec4(final_color, uniform_color.a);
    //
    // --- END MODIFICATION ---


    /*
	if (use_color_clip_plane) {
		color.rgb = (color_clip_plane_dot < 0.0) ? uniform_color_clip_plane_1.rgb : uniform_color_clip_plane_2.rgb;
		color.a = uniform_color.a;
    }
    else
	    color = uniform_color;

    if (slope.actived) {
         if(world_pos.z<0.1&&world_pos.z>-0.1)
         {
                color.rgb = LightBlue;
                color.a = 0.8;
         }
         else if( world_normal_z < slope.normal_z - EPSILON)
         {
                color.rgb = color.rgb * 0.5 + LightRed * 0.5;
                color.a = 0.8;
         }
    }
    */
    // if the fragment is outside the print volume -> use darker color
	vec3 pv_check_min = ZERO;
	vec3 pv_check_max = ZERO;
    if (print_volume.type == 0) {
		// rectangle
		pv_check_min = world_pos.xyz - vec3(print_volume.xy_data.x, print_volume.xy_data.y, print_volume.z_data.x);
		pv_check_max = world_pos.xyz - vec3(print_volume.xy_data.z, print_volume.xy_data.w, print_volume.z_data.y);
	}
	else if (print_volume.type == 1) {
		// circle
		float delta_radius = print_volume.xy_data.z - distance(world_pos.xy, print_volume.xy_data.xy);
		pv_check_min = vec3(delta_radius, 0.0, world_pos.z - print_volume.z_data.x);
		pv_check_max = vec3(0.0, 0.0, world_pos.z - print_volume.z_data.y);
	}
	color.rgb = (any(lessThan(pv_check_min, ZERO)) || any(greaterThan(pv_check_max, ZERO))) ? mix(color.rgb, ZERO, 0.3333) : color.rgb;

    //BBS: add outline_color
    if (is_outline) {
        color = vec4(vec3(intensity.y) + color.rgb * intensity.x, color.a);
        vec2 fragCoord = gl_FragCoord.xy;
        float s = DetectSilho(fragCoord);
        // Makes silhouettes thicker.
        for(int i=1;i<=INFLATE; i++)
        {
           s = max(s, DetectSilho(fragCoord.xy + vec2(i, 0)));
           s = max(s, DetectSilho(fragCoord.xy + vec2(0, i)));
        }   
        gl_FragColor = vec4(mix(color.rgb, getBackfaceColor(color.rgb), s), color.a);
    }
#ifdef ENABLE_ENVIRONMENT_MAP
    else if (use_environment_tex)
        gl_FragColor = vec4(0.45 * texture(environment_tex, normalize(eye_normal).xy * 0.5 + 0.5).xyz + 0.8 * color.rgb * intensity.x, color.a);
#endif
    else
        gl_FragColor = vec4(vec3(intensity.y) + color.rgb * intensity.x, color.a);
}