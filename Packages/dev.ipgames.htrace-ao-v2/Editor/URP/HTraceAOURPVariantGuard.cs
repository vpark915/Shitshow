#if UNITY_EDITOR
using IPGames.HTraceAO.Runtime.URP.Infrastructure;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HTraceAO.Editor.URP
{
	public sealed class HTraceAOURPVariantGuard : IShaderVariantStripper
	{
		private const string ScreenSpaceOcclusionKeyword = "_SCREEN_SPACE_OCCLUSION";

		private static bool? s_IsNeeded;

		public bool active => IsNeeded();

		public bool CanRemoveVariant(Shader shader, ShaderSnippetData shaderVariant, ShaderCompilerData shaderCompilerData)
		{
			if (shader == null || !IsKeywordEnabled(shader, shaderCompilerData, ScreenSpaceOcclusionKeyword))
				return true;

			return false;
		}

		private static bool IsNeeded()
		{
			if (!s_IsNeeded.HasValue)
				s_IsNeeded = HasRendererWithHTrace();

			return s_IsNeeded.Value;
		}

		private static bool HasRendererWithHTrace()
		{
			var rendererGuids = AssetDatabase.FindAssets("t:UniversalRendererData");
			foreach (var rendererGuid in rendererGuids)
			{
				var rendererPath = AssetDatabase.GUIDToAssetPath(rendererGuid);
				var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
				if (rendererData == null)
					continue;

				foreach (var feature in rendererData.rendererFeatures)
				{
					if (feature == null || !feature.isActive)
						continue;

					if (feature is HTraceAORendererFeature)
						return true;
				}
			}

			return false;
		}

		private static bool IsKeywordEnabled(Shader shader, ShaderCompilerData shaderCompilerData, string keywordName)
		{
			return shaderCompilerData.shaderKeywordSet.IsEnabled(shader.keywordSpace.FindKeyword(keywordName));
		}
	}
}
#endif
