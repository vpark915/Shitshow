using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IPGames.HTraceAO.Runtime.Wrappers
{
	public class RTWrapper
	{
		public RTHandle      rt;
#if NONE
		public RenderTexture rt;
#endif


#if NONE
		public void HTextureAlloc(string name, int width, int height, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0, TextureDimension textureDimension = TextureDimension.Tex2D
				, bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true, int mipCount = 1)
		{
			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? Globals.HRenderer.TextureXrSlices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureDimension.Tex2D : textureDimension;
			
			var dscr                   = new RenderTextureDescriptor(width, height);
			dscr.graphicsFormat    = graphicsFormat;
			dscr.volumeDepth       = volumeDepthOrSlices;
			dscr.depthBufferBits   = depthBufferBits; //only 24 bits contains Stencil buffer
			dscr.dimension         = textureDimension;
			dscr.useMipMap         = useMipMap;
			dscr.autoGenerateMips  = autoGenerateMips;
			dscr.enableRandomWrite = enableRandomWrite;
			dscr.useDynamicScale   = useDynamicScale;
			dscr.mipCount          = mipCount;
			
			rt      = new RenderTexture(dscr);
			rt.name = name;
			rt.Create();
		}

		private void HTextureAlloc(string name, int width, int height, RenderTextureFormat renderTextureFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0, TextureDimension textureDimension = TextureDimension.Tex2D,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true)
		{
			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? Globals.HRenderer.TextureXrSlices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureDimension.Tex2D : textureDimension;
      
			var dscr                   = new RenderTextureDescriptor(width, height);
			dscr.colorFormat       = renderTextureFormat;
			dscr.volumeDepth       = volumeDepthOrSlices;
			dscr.depthBufferBits   = depthBufferBits; //only 24 bits contains Stencil buffer
			dscr.dimension         = textureDimension;
			dscr.useMipMap         = useMipMap;
			dscr.autoGenerateMips  = autoGenerateMips;
			dscr.enableRandomWrite = enableRandomWrite;
			dscr.useDynamicScale   = useDynamicScale;
      
			rt      = new RenderTexture(dscr);
			rt.name = name;
			rt.Create();
		}


		public void HReAllocateIfNeeded(string name, int width, int height, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0, TextureDimension textureDimension = TextureDimension.Tex2D,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true, int mipCount = 1)
		{
			if (rt != null && rt.IsCreated() && rt.width == width && rt.height == height && rt.graphicsFormat == graphicsFormat)
				return;

			HRelease();
			HTextureAlloc(name, width, height, graphicsFormat, volumeDepthOrSlices, depthBufferBits, textureDimension, useMipMap, autoGenerateMips, enableRandomWrite, useDynamicScale, mipCount);
		}

		public void HReAllocateIfNeeded(string name, int width, int height, RenderTextureFormat renderTextureFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0, TextureDimension textureDimension = TextureDimension.Tex2D,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true)
		{
			if (rt != null && rt.IsCreated() && rt.width == width && rt.height == height && rt.format == renderTextureFormat)
				return;

			HRelease();
			HTextureAlloc(name, width, height, renderTextureFormat, volumeDepthOrSlices, depthBufferBits, textureDimension, useMipMap, autoGenerateMips, enableRandomWrite, useDynamicScale);
		}

		public void HRelease()
		{
			if (rt != null)
				rt.Release();
		}
#endif

		public void HTextureAlloc(string name, Vector2 scaleFactor, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(scaleFactor, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HTextureAlloc(string name, ScaleFunc scaleFunc, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(scaleFunc, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HTextureAlloc(string name, int width, int height, GraphicsFormat graphicsFormat, int volumeDepthOrSlices = -1, int depthBufferBits = 0,
			TextureDimension textureDimension = TextureDimension.Unknown,
			bool useMipMap = false, bool autoGenerateMips = false, bool enableRandomWrite = true, bool useDynamicScale = true) //useDynamicScale default = true for Upscalers switch between Hardware and Software
		{
			if (rt?.rt != null)
				return;

			volumeDepthOrSlices = volumeDepthOrSlices == -1 ? TextureXR.slices : volumeDepthOrSlices;
			textureDimension    = textureDimension == TextureDimension.Unknown ? TextureXR.dimension : textureDimension;

			rt = RTHandles.Alloc(width, height, volumeDepthOrSlices, dimension: textureDimension, colorFormat: graphicsFormat, name: name,
				enableRandomWrite: enableRandomWrite, useMipMap: useMipMap, useDynamicScale: useDynamicScale, autoGenerateMips: autoGenerateMips,
				depthBufferBits: (DepthBits)depthBufferBits);
		}

		public void HRelease()
		{
			RTHandles.Release(rt);
		}

		public void ReAllocateIfNeeded(string name, ref RenderTextureDescriptor inputDescriptor, int width = -1, int height = -1, int depth = -1,
			GraphicsFormat graphicsFormat = GraphicsFormat.None, TextureDimension dimension = TextureDimension.Unknown, bool useMipMap = false)
		{
			var dscr = inputDescriptor;

			dscr.width            = width != -1 ? width : dscr.width;
			dscr.height           = height != -1 ? height : dscr.height;
			dscr.volumeDepth      = depth != -1 ? depth : dscr.volumeDepth;
			dscr.graphicsFormat   = graphicsFormat != GraphicsFormat.None ? graphicsFormat : dscr.graphicsFormat;
			dscr.dimension        = dimension != TextureDimension.Unknown ? dimension : dscr.dimension;
			dscr.useMipMap        = useMipMap;
			dscr.autoGenerateMips = false;

#if !UNITY_2023_0_OR_NEWER
			dscr.msaaSamples = 1;
#endif

#if UNITY_2023_3_OR_NEWER
			RenderingUtils.ReAllocateHandleIfNeeded(ref rt, dscr, name: name);
#else
			RenderingUtils.ReAllocateIfNeeded(ref rt, dscr, name: name);
#endif
		}
	}
}
