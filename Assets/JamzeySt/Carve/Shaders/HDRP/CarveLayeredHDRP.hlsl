#ifndef CARVE_LAYERED_HDRP_INCLUDED
#define CARVE_LAYERED_HDRP_INCLUDED

#define CARVE_LAYERED
// Compiled layer count from the material keyword (Auto = read _LayerCount at runtime).
#if defined(_CARVE_LAYERS_L2)
    #define CARVE_LAYER_COUNT 2
#elif defined(_CARVE_LAYERS_L3)
    #define CARVE_LAYER_COUNT 3
#elif defined(_CARVE_LAYERS_L4)
    #define CARVE_LAYER_COUNT 4
#endif
#include "CarveLitHDRP.hlsl"

#endif
