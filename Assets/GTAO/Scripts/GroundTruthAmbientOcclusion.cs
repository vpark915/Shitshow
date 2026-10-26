using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// Shitshow: bladesero/GTAO_URP (URP 12 Execute path) ported to render graph. Same shader passes, same params.
// No OnCameraCleanup in render graph; URP zeroes _AmbientOcclusionParam per camera, which switches AO off when this doesn't run.
[DisallowMultipleRendererFeature("Ground Truth Ambient Occlusion")]
[Tooltip("Ground truth ambient occlusion: darkens creases, holes and surfaces close to each other.")]
public class GroundTruthAmbientOcclusion : ScriptableRendererFeature
{
    public enum DepthSource { Depth, DepthNormals }
    public enum NormalQuality { Low, Medium, High }

    [Serializable]
    public class Settings
    {
        public bool Downsample = false;
        [Tooltip("Darken the lit image after opaques (stronger, hits direct light too) instead of only ambient light.")]
        public bool AfterOpaque = false;
        public DepthSource Source = DepthSource.DepthNormals;
        public NormalQuality NormalSamples = NormalQuality.Medium;
        public float Intensity = 3.0f;
        [Range(0, 1)] public float DirectLightingStrength = 0.25f;
        [Tooltip("World units.")] public float Radius = 0.7f;
        [Range(1, 16)] public int SampleCount = 4;
    }

    [SerializeField] Shader m_Shader;
    [SerializeField] Settings m_Settings = new Settings();

    Material m_Material;
    GTAOPass m_Pass;

    public override void Create()
    {
        if (m_Shader == null) m_Shader = Shader.Find("Hidden/Universal Render Pipeline/GroundTruthAmbientOcclusion");
        m_Pass = new GTAOPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        var type = renderingData.cameraData.cameraType;
        if (type == CameraType.Preview || type == CameraType.Reflection) return;
        if (m_Settings.Intensity <= 0 || m_Settings.Radius <= 0 || m_Settings.SampleCount <= 0) return;
        if (m_Shader == null) return;
        if (m_Material == null) m_Material = CoreUtils.CreateEngineMaterial(m_Shader);

        m_Pass.Setup(m_Settings, m_Material);
        renderer.EnqueuePass(m_Pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(m_Material);

    class GTAOPass : ScriptableRenderPass
    {
        enum Passes { AO = 0, BlurHorizontal = 1, BlurVertical = 2, BlurFinal = 3, AfterOpaque = 4 }

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int SSAOParams = Shader.PropertyToID("_SSAOParams");
        static readonly int SSAOTexture = Shader.PropertyToID("_ScreenSpaceOcclusionTexture");
        static readonly int AOParam = Shader.PropertyToID("_AmbientOcclusionParam");
        static readonly int SourceSize = Shader.PropertyToID("_SourceSize");
        static readonly int CameraDepth = Shader.PropertyToID("_CameraDepthTexture");
        static readonly int CameraNormals = Shader.PropertyToID("_CameraNormalsTexture");
        static readonly int ViewXExtent = Shader.PropertyToID("_CameraViewXExtent");
        static readonly int ViewYExtent = Shader.PropertyToID("_CameraViewYExtent");
        static readonly int ViewZExtent = Shader.PropertyToID("_CameraViewZExtent");
        static readonly int ProjectionParams2 = Shader.PropertyToID("_ProjectionParams2");
        static readonly int ViewProjections = Shader.PropertyToID("_CameraViewProjections");
        static readonly int ViewTopLeftCorner = Shader.PropertyToID("_CameraViewTopLeftCorner");
        static readonly int UVToView = Shader.PropertyToID("_SSAO_UVToView");

        readonly Vector4[] m_TopLeft = new Vector4[2], m_XExtent = new Vector4[2], m_YExtent = new Vector4[2], m_ZExtent = new Vector4[2];
        readonly Matrix4x4[] m_ViewProjections = new Matrix4x4[2];
        Settings m_Settings;
        Material m_Material;

        class PassData
        {
            public Material material;
            public bool afterOpaque;
            public float directLightingStrength;
            public Vector4 aoSize, fullSize;
            public TextureHandle ao, blur1, blur2, final, depth, normals, color, colorDepth;
        }

        public GTAOPass() => profilingSampler = new ProfilingSampler("GTAO");

        public void Setup(Settings settings, Material material)
        {
            m_Settings = settings;
            m_Material = material;
            // After the prepasses so the depth (and normals) textures exist, like URP's own SSAO
            renderPassEvent = settings.AfterOpaque ? RenderPassEvent.AfterRenderingOpaques : RenderPassEvent.AfterRenderingPrePasses + 1;
            ConfigureInput(settings.Source == DepthSource.DepthNormals
                ? ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal
                : ScriptableRenderPassInput.Depth);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var resourceData = frameData.Get<UniversalResourceData>();
            int divider = m_Settings.Downsample ? 2 : 1;
            SetupMaterial(cameraData, divider);

            var full = cameraData.cameraTargetDescriptor;
            full.msaaSamples = 1;
            full.depthStencilFormat = GraphicsFormat.None;
            full.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm; // AO in r, packed normal in gba
            var ao = full;
            ao.width /= divider;
            ao.height /= divider;
            var final = full;
            if (SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Render))
                final.graphicsFormat = GraphicsFormat.R8_UNorm;

            using var builder = renderGraph.AddUnsafePass<PassData>("GTAO", out var data, profilingSampler);
            builder.AllowGlobalStateModification(true);

            data.material = m_Material;
            data.afterOpaque = m_Settings.AfterOpaque;
            data.directLightingStrength = m_Settings.DirectLightingStrength;
            data.aoSize = new Vector4(ao.width, ao.height, 1f / ao.width, 1f / ao.height);
            data.fullSize = new Vector4(full.width, full.height, 1f / full.width, 1f / full.height);
            data.ao = UniversalRenderer.CreateRenderGraphTexture(renderGraph, ao, "_GTAO_Occlusion", false, FilterMode.Bilinear);
            data.blur1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, full, "_GTAO_Blur1", false, FilterMode.Bilinear);
            data.blur2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, full, "_GTAO_Blur2", false, FilterMode.Bilinear);
            data.final = UniversalRenderer.CreateRenderGraphTexture(renderGraph, final, "_GTAO_Final", false, FilterMode.Bilinear);
            builder.UseTexture(data.ao, AccessFlags.ReadWrite);
            builder.UseTexture(data.blur1, AccessFlags.ReadWrite);
            builder.UseTexture(data.blur2, AccessFlags.ReadWrite);
            builder.UseTexture(data.final, AccessFlags.ReadWrite);

            data.depth = resourceData.cameraDepthTexture;
            if (data.depth.IsValid()) builder.UseTexture(data.depth);
            data.normals = m_Settings.Source == DepthSource.DepthNormals ? resourceData.cameraNormalsTexture : TextureHandle.nullHandle;
            if (data.normals.IsValid()) builder.UseTexture(data.normals);

            if (data.afterOpaque)
            {
                data.color = resourceData.activeColorTexture;
                data.colorDepth = resourceData.activeDepthTexture;
                builder.UseTexture(data.color, AccessFlags.ReadWrite);
                builder.UseTexture(data.colorDepth);
            }
            else
            {
                builder.SetGlobalTextureAfterPass(data.final, SSAOTexture);
            }

            builder.SetRenderFunc(static (PassData d, UnsafeGraphContext context) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                if (d.depth.IsValid()) d.material.SetTexture(CameraDepth, d.depth);
                if (d.normals.IsValid()) d.material.SetTexture(CameraNormals, d.normals);

                cmd.SetGlobalVector(SourceSize, d.aoSize);
                Draw(cmd, d.material, null, d.ao, Passes.AO);
                Draw(cmd, d.material, d.ao, d.blur1, Passes.BlurHorizontal);
                cmd.SetGlobalVector(SourceSize, d.fullSize);
                Draw(cmd, d.material, d.blur1, d.blur2, Passes.BlurVertical);
                Draw(cmd, d.material, d.blur2, d.final, Passes.BlurFinal);

                // x = 1: AmbientOcclusion.hlsl uses it as on/off (the URP 12 original set 0, which disables AO in URP 17)
                cmd.SetGlobalVector(AOParam, new Vector4(1f, 0f, 0f, d.directLightingStrength));
                if (d.afterOpaque)
                {
                    cmd.SetGlobalTexture(SSAOTexture, d.final);
                    cmd.SetRenderTarget((RTHandle)d.color, (RTHandle)d.colorDepth);
                    cmd.DrawProcedural(Matrix4x4.identity, d.material, (int)Passes.AfterOpaque, MeshTopology.Triangles, 3);
                }
                else
                {
                    cmd.EnableShaderKeyword(ShaderKeywordStrings.ScreenSpaceOcclusion);
                }
            });
        }

        static void Draw(CommandBuffer cmd, Material material, RTHandle source, RTHandle target, Passes pass)
        {
            if (source != null) cmd.SetGlobalTexture(BaseMap, source);
            cmd.SetRenderTarget(target, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawProcedural(Matrix4x4.identity, material, (int)pass, MeshTopology.Triangles, 3);
        }

        // Straight from the original OnCameraSetup
        void SetupMaterial(UniversalCameraData cameraData, int divider)
        {
            var camera = cameraData.camera;
            m_Material.SetVector(SSAOParams, new Vector4(m_Settings.Intensity, m_Settings.Radius, 1f / divider, m_Settings.SampleCount));

            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 proj = cameraData.GetProjectionMatrix();
            m_ViewProjections[0] = proj * view;

            // camera view space without translation, used by ReconstructViewPos() to calculate the view vector
            Matrix4x4 cview = view;
            cview.SetColumn(3, new Vector4(0, 0, 0, 1));
            Matrix4x4 inv = (proj * cview).inverse;
            Vector4 topLeft = inv.MultiplyPoint(new Vector4(-1, 1, -1, 1));
            Vector4 topRight = inv.MultiplyPoint(new Vector4(1, 1, -1, 1));
            Vector4 bottomLeft = inv.MultiplyPoint(new Vector4(-1, -1, -1, 1));
            Vector4 farCentre = inv.MultiplyPoint(new Vector4(0, 0, 1, 1));
            m_TopLeft[0] = topLeft;
            m_XExtent[0] = topRight - topLeft;
            m_YExtent[0] = bottomLeft - topLeft;
            m_ZExtent[0] = farCentre;

            float invHalfTanFov = 1 / Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            var focalLen = new Vector2(invHalfTanFov * ((float)camera.pixelHeight / camera.pixelWidth), invHalfTanFov);
            var invFocalLen = new Vector2(1 / focalLen.x, 1 / focalLen.y);
            m_Material.SetVector(UVToView, new Vector4(2 * invFocalLen.x, 2 * invFocalLen.y, -invFocalLen.x, -invFocalLen.y));
            m_Material.SetVector(ProjectionParams2, new Vector4(1f / camera.nearClipPlane, 0, 0, 0));
            m_Material.SetMatrixArray(ViewProjections, m_ViewProjections);
            m_Material.SetVectorArray(ViewTopLeftCorner, m_TopLeft);
            m_Material.SetVectorArray(ViewXExtent, m_XExtent);
            m_Material.SetVectorArray(ViewYExtent, m_YExtent);
            m_Material.SetVectorArray(ViewZExtent, m_ZExtent);

            CoreUtils.SetKeyword(m_Material, "_ORTHOGRAPHIC", camera.orthographic);
            bool depthNormals = m_Settings.Source == DepthSource.DepthNormals;
            CoreUtils.SetKeyword(m_Material, "_SOURCE_DEPTH", !depthNormals);
            CoreUtils.SetKeyword(m_Material, "_SOURCE_DEPTH_NORMALS", depthNormals);
            CoreUtils.SetKeyword(m_Material, "_RECONSTRUCT_NORMAL_LOW", m_Settings.NormalSamples == NormalQuality.Low);
            CoreUtils.SetKeyword(m_Material, "_RECONSTRUCT_NORMAL_MEDIUM", m_Settings.NormalSamples == NormalQuality.Medium);
            CoreUtils.SetKeyword(m_Material, "_RECONSTRUCT_NORMAL_HIGH", m_Settings.NormalSamples == NormalQuality.High);
        }
    }
}
