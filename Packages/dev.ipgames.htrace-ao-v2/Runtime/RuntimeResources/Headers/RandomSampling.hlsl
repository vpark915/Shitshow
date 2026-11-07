#ifndef RANDOM_SAMPLING
#define RANDOM_SAMPLING

Texture2D<float>  g_OwenScrambledTexture;
Texture2D<float>  g_ScramblingTileXSPP;
Texture2D<float>  g_RankingTileXSPP;
Texture2D<float2> g_ScramblingTexture;
Texture3D<float>  g_STBNTexture;
Texture3D<float2> g_STBNTexture2D;

float H_STBN_RANDOM_1D(uint2 ScreenCoord, uint FrameIndex)
{
    uint3 WrappedCoordinate = uint3(ScreenCoord, FrameIndex) & uint3(127, 127, 15);
    return g_STBNTexture.Load(uint4(WrappedCoordinate, 0)).x;
}

float2 H_STBN_RANDOM_2D(uint2 ScreenCoord, uint FrameIndex)
{
    uint3 WrappedCoordinate = uint3(ScreenCoord, FrameIndex) & uint3(127, 127, 15);
    return g_STBNTexture2D.Load(uint4(uint3(WrappedCoordinate.xy, FrameIndex), 0)).xy;
}

float ScramblingValueFloat(uint2 pixelCoord)
{
    pixelCoord = pixelCoord & 255;
    return g_ScramblingTexture[uint2(pixelCoord.x, pixelCoord.y)].x;
}

float2 ScramblingValueFloat2(uint2 pixelCoord)
{
    pixelCoord = pixelCoord & 255;
    return g_ScramblingTexture[uint2(pixelCoord.x, pixelCoord.y)].xy;
}

uint ScramblingValueUInt(uint2 pixelCoord)
{
    pixelCoord = pixelCoord & 255;
    return clamp((uint)(g_ScramblingTexture[uint2(pixelCoord.x, pixelCoord.y)].x * 256.0), 0, 255);
}

uint2 ScramblingValueUInt2(uint2 pixelCoord)
{
    pixelCoord = pixelCoord & 255;
    return clamp((uint2)(g_ScramblingTexture[uint2(pixelCoord.x, pixelCoord.y)] * 256.0), uint2(0,0), uint2(255, 255));
}

// Wrapper to sample the scrambled low Low-Discrepancy sequence (returns a float)
float GetLDSequenceSampleFloat(uint sampleIndex, uint sampleDimension)
{
    // Make sure arguments are in the right range
    sampleIndex = sampleIndex & 255;
    // sampleDimension = sampleDimension & 255;

    // Fetch the sequence value and return it
    return g_OwenScrambledTexture[uint2(sampleDimension, sampleIndex)];
}

// Wrapper to sample the scrambled low Low-Discrepancy sequence (returns an unsigned int)
uint GetLDSequenceSampleUInt(uint sampleIndex, uint sampleDimension)
{
    // Make sure arguments are in the right range
    sampleIndex = sampleIndex & 255;
    // sampleDimension = sampleDimension & 255;

    // Fetch the sequence value and return it
    return clamp((uint)(g_OwenScrambledTexture[uint2(sampleDimension, sampleIndex)] * 256.0), 0, 255);
}

// This is an implementation of the method from the paper
// "A Low-Discrepancy Sampler that Distributes Monte Carlo Errors as a Blue Noise in Screen Space" by Heitz et al.
float GetBNDSequenceSample(uint2 pixelCoord, uint sampleIndex, uint sampleDimension)
{
    // wrap arguments
    pixelCoord = pixelCoord & 127;
    sampleIndex = sampleIndex & 255;
    sampleDimension = sampleDimension & 255;

    // xor index based on optimized ranking
    uint rankingIndex = (pixelCoord.x + pixelCoord.y * 128) * 8 + (sampleDimension & 7);
    uint rankedSampleIndex = sampleIndex ^ clamp((uint)(g_RankingTileXSPP[uint2(rankingIndex & 127, rankingIndex / 128)] * 256.0), 0, 255);

    // fetch value in sequence
    uint value = clamp((uint)(g_OwenScrambledTexture[uint2(sampleDimension, rankedSampleIndex.x)] * 256.0), 0, 255);

    // If the dimension is optimized, xor sequence value based on optimized scrambling
    uint scramblingIndex = (pixelCoord.x + pixelCoord.y * 128) * 8 + (sampleDimension & 7);
    float scramblingValue = min(g_ScramblingTileXSPP[uint2(scramblingIndex & 127, scramblingIndex / 128)], 0.999);
    value = value ^ uint(scramblingValue * 256.0);

    // Convert to float (to avoid the same 1/256th quantization everywhere, we jitter by the pixel scramblingValue)
    return (scramblingValue + value) / 256.0;
}


// Cache for per-pixel constants (dimension 0 and 1 for XY jitter)
struct CachedBNDSeqeunce
{
    uint RankedIndexXor0;      // XOR mask for dimension 0
    uint RankedIndexXor1;      // XOR mask for dimension 1
    uint ScramblingValueU0;    // Scrambling XOR for dimension 0
    uint ScramblingValueU1;    // Scrambling XOR for dimension 1
    float ScramblingFloat0;    // For final jitter (dimension 0)
    float ScramblingFloat1;    // For final jitter (dimension 1)
};

// Call this ONCE per pixel before the ray loop
CachedBNDSeqeunce InitializeBNDSeqeunce(uint2 pixelCoord)
{
    CachedBNDSeqeunce cache;
    
    // Wrap pixel coord (same as original)
    pixelCoord = pixelCoord & 127;
    
    // Precompute ranking indices for dimensions 0 and 1
    uint rankingIndex0 = (pixelCoord.x + pixelCoord.y * 128) * 8 + 0;
    uint rankingIndex1 = (pixelCoord.x + pixelCoord.y * 128) * 8 + 1;
    
    // Fetch ranking values (these are per-pixel constants!)
    cache.RankedIndexXor0 = clamp((uint)(g_RankingTileXSPP[uint2(rankingIndex0 & 127, rankingIndex0 / 128)] * 256.0), 0, 255);
    cache.RankedIndexXor1 = clamp((uint)(g_RankingTileXSPP[uint2(rankingIndex1 & 127, rankingIndex1 / 128)] * 256.0), 0, 255);
    
    // Precompute scrambling indices (same for both dimensions when & 7)
    uint scramblingIndex0 = (pixelCoord.x + pixelCoord.y * 128) * 8 + 0;
    uint scramblingIndex1 = (pixelCoord.x + pixelCoord.y * 128) * 8 + 1;
    
    // Fetch scrambling values (also per-pixel constants!)
    float scrambling0 = min(g_ScramblingTileXSPP[uint2(scramblingIndex0 & 127, scramblingIndex0 / 128)], 0.999);
    float scrambling1 = min(g_ScramblingTileXSPP[uint2(scramblingIndex1 & 127, scramblingIndex1 / 128)], 0.999);
    
    cache.ScramblingValueU0 = uint(scrambling0 * 256.0);
    cache.ScramblingValueU1 = uint(scrambling1 * 256.0);
    cache.ScramblingFloat0 = scrambling0;
    cache.ScramblingFloat1 = scrambling1;
    
    return cache;
}

// Convenience function to get 2D jitter in one call
float2 GetBNDSequenceSamplesCached(CachedBNDSeqeunce cache, uint sampleIndex)
{
    sampleIndex = sampleIndex & 255;
    
    // Ranked sample indices for both dimensions
    uint rankedSampleIndex0 = sampleIndex ^ cache.RankedIndexXor0;
    uint rankedSampleIndex1 = sampleIndex ^ cache.RankedIndexXor1;
    
    // Two texture fetches (down from 6 in the original!)
    uint value0 = clamp((uint)(g_OwenScrambledTexture[uint2(0, rankedSampleIndex0)] * 256.0), 0, 255);
    uint value1 = clamp((uint)(g_OwenScrambledTexture[uint2(1, rankedSampleIndex1)] * 256.0), 0, 255);
    
    // XOR with scrambling
    value0 = value0 ^ cache.ScramblingValueU0;
    value1 = value1 ^ cache.ScramblingValueU1;
    
    // Convert to float with jitter
    return float2(
        (cache.ScramblingFloat0 + value0) / 256.0,
        (cache.ScramblingFloat1 + value1) / 256.0
    );
}
#endif
