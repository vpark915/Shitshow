#ifndef CARVE_DECAL_URP_INCLUDED
#define CARVE_DECAL_URP_INCLUDED

// URP: Carve Decal's core with URP's hooks (absolute positions; self-shadow in every decal technique).
#define CARVE_CAMERA_DISTANCE(p) length(_WorldSpaceCameraPos - (p))
#define CARVE_DECAL_LIT_PASS 1
#include "../Common/CarveDecalCore.hlsl"

#endif
