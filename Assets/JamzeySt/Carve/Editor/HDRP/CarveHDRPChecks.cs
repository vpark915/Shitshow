using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace JamzeySt.Carve.Editor.HDRP
{
    static class CarveHDRPChecks
    {
        const string SceneViewAAKey = "HDRP:SceneViewCamera:Antialiasing";

        [InitializeOnLoadMethod]
        static void Register()
        {
            CarveProject.AddCheck(GameCameraTAA);
            CarveProject.AddCheck(SceneViewTAA);
            if (CarveCostMeter.Markers.Count == 0)
            {
                CarveCostMeter.Markers.Add(("DeferredDepthPrepass", "Depth prepass"));
                CarveCostMeter.Markers.Add(("GBuffer", "GBuffer"));
                CarveCostMeter.Markers.Add(("ForwardOpaque", "Forward"));
                CarveCostMeter.Markers.Add(("RenderShadowMaps", "Shadow maps"));
            }
        }

        static CarveCheck GameCameraTAA()
        {
            var cam = Camera.main;
            if (cam == null)
                return new CarveCheck { level = CarveCheckLevel.Info, title = "No Main Camera in the open scenes", detail = "Tag your game camera MainCamera so Carve can check its anti-aliasing." };
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd != null && hd.antialiasing == HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing)
                return CarveCheck.Ok("Game camera uses TAA", cam.name);
            return new CarveCheck
            {
                level = CarveCheckLevel.Warning,
                title = "Game camera is not using TAA",
                detail = "POM detail is finer than a pixel; TAA cuts the shimmer about 3.5x.",
                fixLabel = "Use TAA",
                fix = () =>
                {
                    var data = cam.GetComponent<HDAdditionalCameraData>();
                    if (data == null) data = Undo.AddComponent<HDAdditionalCameraData>(cam.gameObject);
                    Undo.RecordObject(data, "Use TAA");
                    data.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                }
            };
        }

        static CarveCheck SceneViewTAA()
        {
            bool taa = EditorPrefs.GetInt(SceneViewAAKey, 0) == (int)HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            bool refresh = true;
            foreach (SceneView sv in SceneView.sceneViews)
                refresh &= sv.sceneViewState.alwaysRefresh;
            if (taa && refresh)
                return CarveCheck.Ok("Scene view uses TAA with Always Refresh");
            return new CarveCheck
            {
                level = CarveCheckLevel.Warning,
                title = taa ? "Scene view TAA without Always Refresh" : "Scene view has no anti-aliasing",
                detail = "Relief between stones flickers while the Scene view camera moves.",
                fixLabel = "Fix",
                fix = () =>
                {
                    EditorPrefs.SetInt(SceneViewAAKey, (int)HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing);
                    foreach (SceneView sv in SceneView.sceneViews)
                    {
                        var state = sv.sceneViewState;
                        state.alwaysRefresh = true;
                        sv.sceneViewState = state;
                        sv.Repaint();
                    }
                    if (!taa) EditorUtility.RequestScriptReload();
                }
            };
        }
    }
}
