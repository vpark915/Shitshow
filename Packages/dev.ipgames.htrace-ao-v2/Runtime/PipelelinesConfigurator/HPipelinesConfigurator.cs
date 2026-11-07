#if UNITY_EDITOR
using System.IO;
using System.Linq;
using IPGames.HTraceAO.Runtime.Globals;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace IPGames.HTraceAO.Runtime.PipelelinesConfigurator
{
	internal class HPipelinesConfigurator
	{
		private static readonly string[] PipelineSymbols = { "NONE", "H_BIRP", "H_URP", "H_HDRP" };

		public static void UpdateDefines(HRenderPipeline hRenderPipeline)
		{
			string targetSymbol = GetPipelineSymbol(hRenderPipeline);

			UpdateScriptingDefineSymbols(targetSymbol);
			UpdateShaderPipelineDefine(targetSymbol);

			Debug.Log($"Defines updated successfully: {targetSymbol}");
		}

		private static string GetPipelineSymbol(HRenderPipeline hRenderPipeline)
		{
			switch (hRenderPipeline)
			{
				case HRenderPipeline.BIRP: return "H_BIRP";
				case HRenderPipeline.URP:  return "H_URP";
				case HRenderPipeline.HDRP: return "H_HDRP";
				default:                   return "NONE";
			}
		}

		private static void UpdateScriptingDefineSymbols(string targetSymbol)
		{
			var namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
			var currentDefines   = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget)
				.Split(';')
				.Select(define => define.Trim())
				.Where(define => !string.IsNullOrEmpty(define))
				.ToList();
			var defines          = currentDefines
				.Where(define => !PipelineSymbols.Contains(define))
				.ToList();

			defines.Add(targetSymbol);

			if (currentDefines.SequenceEqual(defines))
				return;

			PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, defines.ToArray());
		}

		private static void UpdateShaderPipelineDefine(string targetSymbol)
		{
			string filePath = Path.Combine(
				ConfiguratorUtils.GetHTraceFolderPath(),
				"Runtime",
				"RuntimeResources",
				"Headers",
				"HPipelineDefineAO.hlsl");

			string contents =
				$"#ifndef H_PIPELINE_DEFINE_AO\n" +
				$"#define H_PIPELINE_DEFINE_AO\n" +
				$"#define {targetSymbol}\n" +
				$"#endif\n";

			if (File.Exists(filePath) && File.ReadAllText(filePath) == contents)
				return;

			File.WriteAllText(filePath, contents);
			AssetDatabase.ImportAsset(ToAssetPath(filePath), ImportAssetOptions.ForceUpdate);
		}

		private static string ToAssetPath(string fullPath)
		{
			string projectRoot = Directory.GetCurrentDirectory().Replace("\\", "/") + "/";
			return fullPath.Replace("\\", "/").Replace(projectRoot, "");
		}
		
		public static void AlwaysIncludedShaders()
		{
		}
		
		public static void AddShaderToGraphicsSettings(string shaderName)
		{
			var shader = Shader.Find(shaderName);
			if (shader == null)
				return;

			var  graphicsSettings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
			var  serializedObject = new SerializedObject(graphicsSettings);
			var  arrayProp        = serializedObject.FindProperty("m_AlwaysIncludedShaders");
			bool hasShader        = false;
			for (int i = 0; i < arrayProp.arraySize; ++i)
			{
				var arrayElem = arrayProp.GetArrayElementAtIndex(i);
				if (shader == arrayElem.objectReferenceValue)
				{
					hasShader = true;
					break;
				}
			}

			if (!hasShader)
			{
				int arrayIndex = arrayProp.arraySize;
				arrayProp.InsertArrayElementAtIndex(arrayIndex);
				var arrayElem = arrayProp.GetArrayElementAtIndex(arrayIndex);
				arrayElem.objectReferenceValue = shader;

				serializedObject.ApplyModifiedProperties();

				AssetDatabase.SaveAssets();
			}
		}
		
	}
}
#endif
