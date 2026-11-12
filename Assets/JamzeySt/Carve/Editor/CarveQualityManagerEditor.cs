using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace JamzeySt.Carve.Editor
{
    // Carve Quality Manager inspector in the Carve Hub's look: preset, the settings it sets (editable in Custom), scale and
    // adaptive, and a live readout in Play mode.
    [CustomEditor(typeof(CarveQualityManager))]
    public class CarveQualityManagerEditor : UnityEditor.Editor
    {
        static readonly float[] k_TargetRates = { 30f, 60f, 90f, 120f };

        VisualElement m_Root;
        CarveQualityPreset m_BuiltPreset;
        bool m_BuiltAdaptive, m_BuiltPlaying;
        Label m_StatusPill, m_ScaleValue, m_FrameValue, m_FrameLabel, m_StepsValue, m_DistanceValue;
        VisualElement m_FrameTile, m_ScaleFill;

        CarveQualityManager Manager => (CarveQualityManager)target;

        public override VisualElement CreateInspectorGUI()
        {
            m_Root = new VisualElement();
            var hub = CarveHub.FindStyleSheet();
            if (hub != null) m_Root.styleSheets.Add(hub);
            var own = FindStyleSheet("CarveQuality");
            if (own != null) m_Root.styleSheets.Add(own);
            m_Root.AddToClassList("carve-hub");
            m_Root.AddToClassList("carve-quality");
            if (!EditorGUIUtility.isProSkin) m_Root.AddToClassList("carve-hub--light");

            Build();
            Undo.undoRedoPerformed += Rebuild;
            m_Root.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Rebuild);
            m_Root.schedule.Execute(UpdateLive).Every(150);
            return m_Root;
        }

        void Rebuild()
        {
            if (m_Root == null || target == null) return;
            m_Root.Clear();
            Build();
        }

        void Build()
        {
            serializedObject.Update();
            var mgr = Manager;
            m_BuiltPreset = mgr.preset;
            m_BuiltAdaptive = mgr.adaptive;
            m_BuiltPlaying = Application.isPlaying;

            BuildHeader();
            var page = Add(m_Root, new VisualElement(), "quality-page");
            BuildPreset(page);
            BuildSettings(page);
            BuildScale(page);
            BuildLive(page);
            UpdateLive();
        }

        // ---------------- Header

        void BuildHeader()
        {
            var header = Add(m_Root, new VisualElement(), "hub-header");
            Add(header, new CarveLogo(), "hub-logo");
            var brand = Add(header, new VisualElement(), "hub-brand");
            Add(brand, new Label("QUALITY"), "hub-wordmark");
            Add(brand, new Label("Every Carve material in the scene"), "hub-slogan");
            Add(header, new VisualElement(), "hub-spacer");
            var pills = Add(header, new VisualElement(), "hub-pills");
            m_StatusPill = Add(pills, new Label(), "hub-pill");
        }

        // ---------------- Preset

        void BuildPreset(VisualElement page)
        {
            var mgr = Manager;
            var card = CarveHub.Card(page, "Preset", "One click for the whole scene", "Quality.Preset");
            var row = Add(card, new VisualElement(), "quality-fill");
            var items = new List<(string, string, System.Action, bool)>();
            foreach (CarveQualityPreset p in System.Enum.GetValues(typeof(CarveQualityPreset)))
            {
                var preset = p;
                items.Add((p.ToString(), CarveQualitySettings.Describe(p), () => SetPreset(preset), mgr.preset == p));
            }
            CarveHub.Segmented(row, items);

            var callout = Add(card, new VisualElement(), "callout", "quality-callout");
            Add(callout, new Label(mgr.preset.ToString()), "callout-title");
            Add(callout, new Label(CarveQualitySettings.Describe(mgr.preset)), "callout-text");
            if (mgr.preset != CarveQualityPreset.Custom)
            {
                var actions = Add(card, new VisualElement(), "quality-actions");
                var b = Add(actions, new Button(Customize) { text = "Customize", tooltip = "Copy this preset into Custom and edit every setting." }, "hub-button", "hub-button--small");
                b.AddToClassList("hub-button--primary");
            }
        }

        void SetPreset(CarveQualityPreset p)
        {
            Undo.RecordObject(Manager, "Carve Quality Preset");
            Manager.SetPreset(p);
            EditorUtility.SetDirty(Manager);
            Rebuild();
        }

        void Customize()
        {
            var mgr = Manager;
            Undo.RecordObject(mgr, "Customize Carve Quality");
            mgr.custom = mgr.Settings;
            mgr.SetPreset(CarveQualityPreset.Custom);
            EditorUtility.SetDirty(mgr);
            Rebuild();
        }

        // ---------------- Settings (editable in Custom, the preset's values otherwise)

        void BuildSettings(VisualElement page)
        {
            var mgr = Manager;
            bool custom = mgr.preset == CarveQualityPreset.Custom;
            var s = mgr.Settings;
            string sub = custom ? null : $"Set by {mgr.preset} · Customize to edit";

            var relief = CarveHub.Card(page, "Relief", sub ?? "How much each ray works and how far relief reaches", "Quality.Relief");
            SliderRow(relief, "March steps", "Relief march steps (and Auto LOD), relative to each material's own.", "marchSteps", s.marchSteps, 0.25f, 2f, custom);
            SliderRow(relief, "Relief distance", "Scales every material's Fade Start and Fade End. Lower flattens the relief closer to the camera.", "reliefDistance", s.reliefDistance, 0.1f, 2f, custom);

            var light = CarveHub.Card(page, "Lighting", sub ?? "Self-shadow, relief occlusion and cliff shading", "Quality.Lighting");
            SliderRow(light, "Shadow steps", "Self-shadow and reflection ray steps, relative to each material's own.", "shadowSteps", s.shadowSteps, 0.25f, 2f, custom);
            var extras = CarveHub.SettingRow(light, "Extras distance", "Self-shadow, relief occlusion and cliff shading end at this many meters (0 = no limit). The relief itself stays.");
            var field = Add(extras, new FloatField(), "quality-number");
            if (custom) field.BindProperty(Custom("extrasDistance"));
            else field.SetValueWithoutNotify(s.extrasDistance);
            field.SetEnabled(custom);
            Add(extras, new Label(s.extrasDistance > 0f ? "m" : "m  (0 = no limit)"), "quality-unit");
            var occ = CarveHub.SettingRow(light, "Occlusion", "Relief occlusion directions per pixel. Auto: 8 on large rings, 4 on small ones (TAA blends alternating sets).");
            var occItems = new List<(string, string, System.Action, bool)>();
            foreach (CarveOcclusionSamples o in System.Enum.GetValues(typeof(CarveOcclusionSamples)))
            {
                var value = o;
                occItems.Add((o == CarveOcclusionSamples.Auto ? "Auto" : o == CarveOcclusionSamples.Four ? "4 dirs" : "8 dirs", null,
                              () => SetCustomEnum("occlusionSamples", (int)value), s.occlusionSamples == o));
            }
            CarveHub.Segmented(occ, occItems);
            occ.SetEnabled(custom);

            var features = CarveHub.Card(page, "Features", sub ?? "Scene-wide strength, times each material's own", "Quality.Features");
            SliderRow(features, "Self-shadow", "Relief self-shadowing from the sun.", "selfShadow", s.selfShadow, 0f, 1f, custom);
            SliderRow(features, "Relief occlusion", "Ambient occlusion from the surrounding relief.", "reliefOcclusion", s.reliefOcclusion, 0f, 1f, custom);
            SliderRow(features, "Cliff & specular", "Cliff shading on steep walls and specular occlusion.", "cliffAndSpecular", s.cliffAndSpecular, 0f, 1f, custom);
            SliderRow(features, "Anti-tiling", "Anti-tiling strength on materials that use it.", "antiTiling", s.antiTiling, 0f, 1f, custom);
        }

        SerializedProperty Custom(string field) => serializedObject.FindProperty("custom").FindPropertyRelative(field);

        void SliderRow(VisualElement card, string label, string tooltip, string field, float value, float lo, float hi, bool editable)
        {
            var row = CarveHub.SettingRow(card, label, tooltip);
            var slider = Add(row, new Slider(lo, hi) { showInputField = true }, "quality-slider");
            if (editable) slider.BindProperty(Custom(field));
            else slider.SetValueWithoutNotify(value);
            slider.SetEnabled(editable);
        }

        void SetCustomEnum(string field, int value)
        {
            serializedObject.Update();
            Custom(field).enumValueIndex = value;
            serializedObject.ApplyModifiedProperties();
            Rebuild();
        }

        // ---------------- Scale and adaptive

        void BuildScale(VisualElement page)
        {
            var mgr = Manager;
            var card = CarveHub.Card(page, "Scale", "Steps and distances scaled together to hold a frame rate", "Quality.Scale");

            var scale = CarveHub.SettingRow(card, "Quality scale", "Scales march steps, shadow steps, relief distance and extras distance together (1 = the settings above).");
            var slider = Add(scale, new Slider(0.1f, 1f) { showInputField = true }, "quality-slider");
            slider.BindProperty(serializedObject.FindProperty("qualityScale"));
            slider.SetEnabled(!(mgr.adaptive && Application.isPlaying));

            var adaptive = CarveHub.SettingRow(card, "Adaptive", "Play mode: lower Quality Scale while frames run over the target, raise it again when there is room.");
            CarveHub.Segmented(adaptive, new List<(string, string, System.Action, bool)>
            {
                ("Off", "Quality Scale stays where you set it.", () => SetAdaptive(false), !mgr.adaptive),
                ("On", "Hold the target frame rate in Play mode.", () => SetAdaptive(true), mgr.adaptive),
            });
            if (!mgr.adaptive) return;

            var target = CarveHub.SettingRow(card, "Target", "Frame rate adaptive holds (GPU frame time when the platform reports it).");
            var rates = new List<(string, string, System.Action, bool)>();
            foreach (var r in k_TargetRates)
            {
                float rate = r;
                rates.Add(($"{r:0}", $"{1000f / r:0.0} ms per frame", () => SetTarget(rate), Mathf.Approximately(mgr.targetFrameRate, r)));
            }
            CarveHub.Segmented(target, rates);
            var fps = Add(target, new FloatField(), "quality-number");
            fps.BindProperty(serializedObject.FindProperty("targetFrameRate"));
            Add(target, new Label("fps"), "quality-unit");

            var min = CarveHub.SettingRow(card, "Minimum", "Adaptive never scales below this.");
            Add(min, new Slider(0.1f, 1f) { showInputField = true }, "quality-slider").BindProperty(serializedObject.FindProperty("minimumScale"));
            var speed = CarveHub.SettingRow(card, "Response", "Scale change per second at full error. Raising is three times slower than lowering, so it settles instead of pumping.");
            Add(speed, new Slider(0.05f, 2f) { showInputField = true }, "quality-slider").BindProperty(serializedObject.FindProperty("responseSpeed"));
        }

        void SetAdaptive(bool on)
        {
            Undo.RecordObject(Manager, "Carve Adaptive Quality");
            Manager.adaptive = on;
            EditorUtility.SetDirty(Manager);
            Rebuild();
        }

        void SetTarget(float rate)
        {
            Undo.RecordObject(Manager, "Carve Target Frame Rate");
            Manager.targetFrameRate = rate;
            EditorUtility.SetDirty(Manager);
            Rebuild();
        }

        // ---------------- Live readout

        void BuildLive(VisualElement page)
        {
            var card = CarveHub.Card(page, "Live", Application.isPlaying ? "What Carve runs at right now" : "Enter Play mode to see the frame time and adaptive at work", "Quality.Live");
            var stats = Add(card, new VisualElement(), "quality-stats");
            var scaleTile = Stat(stats, "Scale in use", out m_ScaleValue);
            var meter = Add(scaleTile, new VisualElement(), "quality-meter");
            m_ScaleFill = Add(meter, new VisualElement(), "quality-meter-fill");
            m_FrameTile = Stat(stats, "Frame", out m_FrameValue);
            m_FrameLabel = m_FrameTile.Q<Label>(className: "quality-stat-label");
            Stat(stats, "March steps", out m_StepsValue);
            Stat(stats, "Relief distance", out m_DistanceValue);
        }

        static VisualElement Stat(VisualElement parent, string label, out Label value)
        {
            var tile = Add(parent, new VisualElement(), "quality-stat");
            value = Add(tile, new Label("–"), "quality-stat-value");
            Add(tile, new Label(label), "quality-stat-label");
            return tile;
        }

        void UpdateLive()
        {
            if (m_Root == null || target == null || m_ScaleValue == null) return;
            var mgr = Manager;
            // Presets and adaptive can change from outside (Undo, the demo HUD's F2 / F3, scripts): rebuild to match.
            if (mgr.preset != m_BuiltPreset || mgr.adaptive != m_BuiltAdaptive || Application.isPlaying != m_BuiltPlaying)
            {
                Rebuild();
                return;
            }

            bool active = CarveQualityManager.Active == mgr;
            bool other = CarveQualityManager.Active != null && !active;
            m_StatusPill.text = active ? mgr.preset.ToString() : other ? "Overridden" : "Inactive";
            m_StatusPill.tooltip = active ? "This manager sets every Carve material in the scene." :
                                   other ? $"Another Carve Quality Manager is in charge ({CarveQualityManager.Active.name})." : "Enable the component to apply it.";
            m_StatusPill.EnableInClassList("hub-pill--ok", active);
            m_StatusPill.EnableInClassList("hub-pill--warn", other);

            var s = mgr.Settings;
            float k = Mathf.Clamp(Application.isPlaying ? mgr.CurrentScale : mgr.qualityScale, 0.1f, 1f);
            m_ScaleValue.text = k.ToString("0.00");
            m_ScaleFill.style.width = Length.Percent(k * 100f);
            m_StepsValue.text = $"× {s.marchSteps * k:0.00}";
            m_DistanceValue.text = $"× {s.reliefDistance * k:0.00}";

            bool live = Application.isPlaying && mgr.MeasuredFrameMs > 0f;
            m_FrameValue.text = live ? $"{mgr.MeasuredFrameMs:0.0} ms" : "–";
            m_FrameLabel.text = live ? $"{(mgr.UsingGpuTime ? "GPU" : "Frame")} · target {mgr.FrameBudgetMs:0.0} ms" : "Frame time";
            m_FrameTile.EnableInClassList("quality-stat--ok", live && mgr.MeasuredFrameMs <= mgr.FrameBudgetMs);
            m_FrameTile.EnableInClassList("quality-stat--warn", live && mgr.MeasuredFrameMs > mgr.FrameBudgetMs);
        }

        // ---------------- Helpers

        static T Add<T>(VisualElement parent, T element, params string[] classes) where T : VisualElement => CarveHub.Add(parent, element, classes);

        static StyleSheet FindStyleSheet(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name + " t:StyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == name) return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            return null;
        }

        [MenuItem("GameObject/Carve/Quality Manager", false, 20)]
        static void Create(MenuCommand cmd)
        {
            var existing = Object.FindAnyObjectByType<CarveQualityManager>();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return;
            }
            var go = new GameObject("Carve Quality", typeof(CarveQualityManager));
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Carve Quality Manager");
            Selection.activeObject = go;
        }
    }
}
