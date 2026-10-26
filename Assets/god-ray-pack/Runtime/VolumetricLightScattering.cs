using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Plugins.VolumetricLightScattering.Runtime
{
    // God rays from the sun (RenderSettings.sun, the scene's directional light): the sky round the sun, smeared outward
    // from the sun's spot on screen at low resolution and added onto the picture before post-processing, so bloom
    // catches them. Fades out as the camera turns away from the sun.
    // Ported to Unity 6's render graph: the pack's original used the pre-render-graph API (it no longer compiles) and
    // drew every opaque object again in black to find the sky; the depth buffer says where the sky is for free.
    // ponytail: the pack's "Diffuse Lighting" pass (a near-zero blur of the sky added again) is dropped
    public class VolumetricLightScattering : ScriptableRendererFeature
    {
        [System.Serializable]
        public class VolumetricLightScatteringSettings
        {
            [Range(0.1f, 1f)] public float ResolutionScale = 0.5f;
            [Range(0.0f, 1.0f)] public float Intensity = 0.5f;
            [Tooltip("How far the rays reach from the sun, as a share of the way to the screen's edge")]
            [Range(0.0f, 1.0f)] public float BlurWidth = 0.85f;
            [Tooltip("How much sky round the sun gives off rays, as a share of the screen's height. A high sun is off the top of\n" +
                     "the screen when the camera looks level, so this has to reach down to the sky that is on screen")]
            [Range(0.05f, 3f)] public float SunRadius = 1.6f;
        }

        public VolumetricLightScatteringSettings Quality = new VolumetricLightScatteringSettings();
        [Tooltip("god-ray-pack/Shaders/GodRays.shader (referenced here so builds include it)")]
        public Shader shader;

        Material material;
        LightScatteringPass pass;

        public override void Create()
        {
            if (!shader) shader = Shader.Find("Hidden/Volumetric/GodRays");
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new LightScatteringPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (!material || !RenderSettings.sun || (type != CameraType.Game && type != CameraType.SceneView)) return; // not probes or previews
            pass.material = material;
            pass.settings = Quality;
            renderer.EnqueuePass(pass);
        }

        class LightScatteringPass : ScriptableRenderPass
        {
            public Material material;
            public VolumetricLightScatteringSettings settings;

            static readonly int Center = Shader.PropertyToID("_Center"), Intensity = Shader.PropertyToID("_Intensity"),
                Width = Shader.PropertyToID("_BlurWidth"), Radius = Shader.PropertyToID("_SunRadius");

            class Data
            {
                public TextureHandle source;
                public Material material;
                public int pass;
                public Vector4 center;
                public float intensity, width, radius;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var cameraData = frame.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                Camera camera = cameraData.camera;
                Vector3 toSun = -RenderSettings.sun.transform.forward;
                float facing = Vector3.Dot(camera.transform.forward, toSun);
                if (facing <= 0) return; // the sun's behind: nothing to smear
                Vector3 sun = camera.WorldToViewportPoint(camera.transform.position + toSun);

                RenderTextureDescriptor small = cameraData.cameraTargetDescriptor;
                small.width = Mathf.Max(1, Mathf.RoundToInt(small.width * settings.ResolutionScale));
                small.height = Mathf.Max(1, Mathf.RoundToInt(small.height * settings.ResolutionScale));
                small.depthBufferBits = 0;
                small.depthStencilFormat = GraphicsFormat.None;
                small.msaaSamples = 1;
                TextureHandle sky = UniversalRenderer.CreateRenderGraphTexture(graph, small, "_GodRaysSky", false, FilterMode.Bilinear);
                TextureHandle rays = UniversalRenderer.CreateRenderGraphTexture(graph, small, "_GodRays", false, FilterMode.Bilinear);

                var center = new Vector4(sun.x, sun.y, 0, 0);
                float intensity = settings.Intensity * facing * facing;
                Blit(graph, "God Rays: Sky", resources.activeColorTexture, sky, 0, AccessFlags.Write, center, intensity, resources.cameraDepthTexture);
                Blit(graph, "God Rays: Smear", sky, rays, 1, AccessFlags.Write, center, intensity, TextureHandle.nullHandle);
                Blit(graph, "God Rays: Add", rays, resources.activeColorTexture, 2, AccessFlags.ReadWrite, center, intensity, TextureHandle.nullHandle);
            }

            void Blit(RenderGraph graph, string name, TextureHandle from, TextureHandle to, int shaderPass, AccessFlags access, Vector4 center, float intensity, TextureHandle depth)
            {
                using (var builder = graph.AddRasterRenderPass(name, out Data data))
                {
                    data.source = from;
                    data.material = material;
                    data.pass = shaderPass;
                    data.center = center;
                    data.intensity = intensity;
                    data.width = settings.BlurWidth;
                    data.radius = settings.SunRadius;
                    builder.UseTexture(from);
                    if (depth.IsValid())
                    {
                        builder.UseTexture(depth);
                        builder.UseAllGlobalTextures(true); // _CameraDepthTexture
                    }
                    builder.SetRenderAttachment(to, 0, access);
                    builder.SetRenderFunc((Data d, RasterGraphContext context) =>
                    {
                        d.material.SetVector(Center, d.center);
                        d.material.SetFloat(Intensity, d.intensity);
                        d.material.SetFloat(Width, d.width);
                        d.material.SetFloat(Radius, d.radius);
                        Blitter.BlitTexture(context.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, d.pass);
                    });
                }
            }
        }
    }
}
