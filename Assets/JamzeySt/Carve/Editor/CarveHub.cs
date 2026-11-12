using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace JamzeySt.Carve.Editor
{
    // Central place for everything Carve: health checks, material batch editing, conversion, mesh tools, debug and help.
    public class CarveHub : EditorWindow
    {
        enum Tab { Overview, Materials, Convert, Meshes, Debug, Help }

        struct TabInfo
        {
            public string label, title, description, section;
            public CarveGlyph glyph;
        }

        static readonly Dictionary<Tab, TabInfo> s_Tabs = new Dictionary<Tab, TabInfo>
        {
            [Tab.Overview] = new TabInfo { label = "Dashboard", title = "Dashboard", section = "Workspace", glyph = CarveGlyph.Dashboard,
                                           description = "Project health, what's in your scenes and one-click shortcuts." },
            [Tab.Materials] = new TabInfo { label = "Materials", title = "Materials", section = "Workspace", glyph = CarveGlyph.Materials,
                                            description = "Every Carve material in the project. Select several to edit them together." },
            [Tab.Convert] = new TabInfo { label = "Convert", title = "Convert to Carve", section = "Workspace", glyph = CarveGlyph.Convert,
                                          description = "Turn Lit and LayeredLit materials into Carve, in place or as copies, and back again." },
            [Tab.Meshes] = new TabInfo { label = "Meshes", title = "Meshes & Physics", section = "Tools", glyph = CarveGlyph.Cube,
                                         description = "Silhouette data for modular meshes, relief colliders and relief meshes." },
            [Tab.Debug] = new TabInfo { label = "Debug", title = "Debug & Performance", section = "Tools", glyph = CarveGlyph.Gauge,
                                        description = "Debug views, global overrides for A/B checks, and what Carve costs on the GPU." },
            [Tab.Help] = new TabInfo { label = "Help", title = "Guides & Support", section = "Learn", glyph = CarveGlyph.Help,
                                       description = "Quick start, Carve in your own Shader Graphs, and support." },
        };

        static readonly Dictionary<CarveDebugView, string> s_DebugInfo = new Dictionary<CarveDebugView, string>
        {
            [CarveDebugView.Off] = "Normal rendering.",
            [CarveDebugView.Curvature] = "Red = convex along U, blue = convex along V (curved silhouettes).",
            [CarveDebugView.Steps] = "Ray-march cost per pixel: blue = few steps, red = many.",
            [CarveDebugView.Silhouette] = "Magenta = rays clipped into the outline, gray = the height each ray hit.",
            [CarveDebugView.SelfShadow] = "White = lit, black = shadowed by the relief.",
            [CarveDebugView.Occlusion] = "White = open, black = occluded by surrounding relief.",
            [CarveDebugView.Cliff] = "Orange = steep walls where cliff shading takes over.",
            [CarveDebugView.SpecularOcclusion] = "White = reflections visible, black = blocked by the relief.",
            [CarveDebugView.TriplanarBlend] = "Red / green / blue = share of the X / Y / Z projection (triplanar materials).",
            [CarveDebugView.Layers] = "Gray / red / green / blue = layers 0 / 1 / 2 / 3 (Layered Lit).",
        };

        static readonly (string name, float min, float max, float refine, float shadow)[] s_Presets =
        {
            ("Low", 6, 16, 1, 6), ("Medium", 8, 32, 2, 10), ("High", 12, 48, 3, 12), ("Ultra", 16, 64, 4, 16),
        };

        const string TabKey = "JamzeySt.Carve.Hub.Tab";
        const string DepthKey = "JamzeySt.Carve.Hub.DepthScale";
        const string StepKey = "JamzeySt.Carve.Hub.StepScale";
        const string DebugKey = "JamzeySt.Carve.Hub.Debug";
        internal const string PrefsPrefix = "JamzeySt.Carve.Hub.";
        const string SubGraphPath = "Assets/JamzeySt/Carve/Shaders/ShaderGraph/Carve POM.shadersubgraph";
        static string ExampleGraphPath => CarveProject.PipelineName == "URP"
            ? "Assets/JamzeySt/Carve/Shaders/ShaderGraph/Examples/URP/Carve POM Example URP.shadergraph"
            : "Assets/JamzeySt/Carve/Shaders/ShaderGraph/Examples/HDRP/Carve POM Example.shadergraph";
        const float NarrowWidth = 640f;

        readonly Dictionary<Tab, VisualElement> m_Nav = new Dictionary<Tab, VisualElement>();
        readonly Dictionary<Tab, Label> m_Badges = new Dictionary<Tab, Label>();
        VisualElement m_NavPanel;
        CarveIcon m_NavToggleIcon;
        VisualElement m_HealthPill;
        VisualElement m_Content;
        VisualElement m_PageActions;
        VisualElement m_Page;
        Tab m_Tab;
        bool m_Narrow;
        TwoPaneSplitView m_Split;
        List<CarveCheck> m_Checks = new List<CarveCheck>();

        List<Material> m_AllMaterials = new List<Material>();
        List<Material> m_Shown = new List<Material>();
        List<Material> m_Selected = new List<Material>();
        ListView m_List;
        Label m_ListCount;
        VisualElement m_Details;
        UnityEditor.Editor m_MatEditor;
        string m_Search = "";
        bool m_SceneOnly;
        readonly HashSet<Material> m_ConvertSkip = new HashSet<Material>();

        [MenuItem("Tools/Carve/Carve Hub", priority = 0)]
        public static void Open()
        {
            var w = GetWindow<CarveHub>();
            w.titleContent = new GUIContent("Carve Hub");
            w.minSize = new Vector2(380, 340);
            w.Show();
        }

        // Opens the Hub on a page: Overview, Materials, Convert, Meshes, Debug or Help.
        public static CarveHub Open(string page)
        {
            Open();
            var w = GetWindow<CarveHub>();
            w.ShowPage(page);
            return w;
        }

        public void ShowPage(string page)
        {
            if (System.Enum.TryParse<Tab>(page, true, out var t) && rootVisualElement.childCount > 0) Select(t);
        }

        // The Materials page with one material selected and its settings shown.
        public void ShowMaterial(Material material)
        {
            if (material == null || rootVisualElement.childCount == 0) return;
            m_Selected = new List<Material> { material };
            m_Search = "";
            m_SceneOnly = false;
            Select(Tab.Materials);
        }

        [InitializeOnLoadMethod]
        static void RestoreGlobals()
        {
            CarveGlobals.DepthScale = SessionState.GetFloat(DepthKey, 1f);
            CarveGlobals.StepScale = SessionState.GetFloat(StepKey, 1f);
            CarveGlobals.DebugView = (CarveDebugView)SessionState.GetInt(DebugKey, 0);
        }

        void OnEnable() => CarveCostMeter.Changed += OnMeterChanged;

        void OnMeterChanged()
        {
            if (m_Content != null && m_Tab == Tab.Debug) Refresh();
        }

        void OnDisable()
        {
            CarveCostMeter.Changed -= OnMeterChanged;
            if (m_MatEditor != null) DestroyImmediate(m_MatEditor);
        }

        // ---------------- Frame: header, sidebar, page header

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = FindStyleSheet();
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("carve-hub");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("carve-hub--light");
            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            var header = Add(root, new VisualElement(), "hub-header");
            Add(header, new CarveLogo(), "hub-logo");
            var brand = Add(header, new VisualElement(), "hub-brand");
            Add(brand, new Label(CarveInfo.Product.ToUpperInvariant()), "hub-wordmark");
            Add(brand, new Label(CarveInfo.Slogan), "hub-slogan");
            Add(header, new VisualElement(), "hub-spacer");
            var pills = Add(header, new VisualElement(), "hub-pills");
            m_HealthPill = Add(pills, new VisualElement(), "hub-pill", "hub-pill--health");
            m_HealthPill.tooltip = "Project health: open the Dashboard";
            m_HealthPill.AddManipulator(new Clickable(() => Select(Tab.Overview)));
            Add(m_HealthPill, new VisualElement(), "hub-pill-dot");
            Add(m_HealthPill, new Label(), "hub-pill-text");
            Add(pills, new Label(CarveProject.PipelineName), "hub-pill").tooltip = "Active render pipeline";
            Add(pills, new Label("v" + CarveInfo.Version), "hub-pill").tooltip = "Carve version";

            var body = Add(root, new VisualElement(), "hub-body");
            m_NavPanel = Add(body, new VisualElement(), "hub-nav");
            var toggle = Add(m_NavPanel, new VisualElement(), "hub-nav-toggle");
            m_NavToggleIcon = Add(toggle, new CarveIcon(CarveGlyph.Chevron), "nav-toggle-icon");
            toggle.AddManipulator(new Clickable(() => SetNavCollapsed(!NavCollapsed)));
            string section = null;
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                var tab = t;
                var info = s_Tabs[t];
                if (info.section != section)
                {
                    section = info.section;
                    Add(m_NavPanel, new Label(section.ToUpperInvariant()), "hub-nav-section");
                }
                var item = Add(m_NavPanel, new VisualElement(), "hub-nav-item");
                item.tooltip = info.title;
                Add(item, new VisualElement(), "nav-marker");
                Add(item, new CarveIcon(info.glyph), "nav-icon");
                Add(item, new Label(info.label), "nav-label");
                m_Badges[t] = Add(item, new Label(), "nav-badge", "nav-badge--hidden");
                item.AddManipulator(new Clickable(() => Select(tab)));
                m_Nav[t] = item;
            }
            Add(m_NavPanel, new VisualElement(), "hub-spacer");
            var foot = Add(m_NavPanel, new VisualElement(), "hub-nav-foot");
            Add(foot, new Label($"{CarveInfo.Product} {CarveInfo.Version}"), "nav-foot-text");
            Add(foot, new Label("by " + CarveInfo.Publisher.TrimEnd('.')), "nav-foot-text");
            SetNavCollapsed(NavCollapsed);

            m_Content = Add(body, new VisualElement(), "hub-content");
            // Narrow windows stack the material list above its details and drop the slogan.
            m_Content.RegisterCallback<GeometryChangedEvent>(e =>
            {
                bool narrow = e.newRect.width > 0f && e.newRect.width < NarrowWidth;
                root.EnableInClassList("carve-hub--narrow", narrow);
                if (narrow == m_Narrow) return;
                m_Narrow = narrow;
                if (m_Tab == Tab.Materials) Select(m_Tab);
            });
            Select((Tab)SessionState.GetInt(TabKey, 0));
        }

        static bool NavCollapsed => EditorPrefs.GetBool(PrefsPrefix + "NavCollapsed", false);

        void SetNavCollapsed(bool collapsed)
        {
            EditorPrefs.SetBool(PrefsPrefix + "NavCollapsed", collapsed);
            m_NavPanel.EnableInClassList("hub-nav--collapsed", collapsed);
            m_NavToggleIcon.Flip = collapsed;
            m_NavToggleIcon.parent.tooltip = collapsed ? "Expand the sidebar" : "Collapse the sidebar";
        }

        internal static StyleSheet FindStyleSheet()
        {
            foreach (var guid in AssetDatabase.FindAssets("CarveHub t:StyleSheet"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == "CarveHub") return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            }
            return null;
        }

        void Select(Tab t)
        {
            m_Tab = t;
            SessionState.SetInt(TabKey, (int)t);
            foreach (var kv in m_Nav) kv.Value.EnableInClassList("hub-nav-item--selected", kv.Key == t);
            RunChecks();
            m_Content.Clear();
            m_Split = null;
            m_List = null;
            if (m_MatEditor != null) DestroyImmediate(m_MatEditor);

            var info = s_Tabs[t];
            var head = Add(m_Content, new VisualElement(), "page-header");
            Add(head, new CarveIcon(info.glyph), "page-icon");
            var titles = Add(head, new VisualElement(), "page-titles");
            Add(titles, new Label(info.title), "page-title");
            Add(titles, new Label(info.description), "page-desc");
            m_PageActions = Add(head, new VisualElement(), "page-actions");

            if (t == Tab.Materials)
                m_Page = Add(m_Content, new VisualElement(), "mat-page");
            else
            {
                var scroll = Add(m_Content, new ScrollView(), "hub-scroll");
                m_Page = Add(scroll, new VisualElement(), "hub-page");
            }
            switch (t)
            {
                case Tab.Overview: BuildOverview(); break;
                case Tab.Materials: BuildMaterials(); break;
                case Tab.Convert: BuildConvert(); break;
                case Tab.Meshes: BuildMeshes(); break;
                case Tab.Debug: BuildDebug(); break;
                case Tab.Help: BuildHelp(); break;
            }
        }

        void Refresh()
        {
            CarveProject.InvalidateScan();
            Select(m_Tab);
        }

        void PageAction(string label, System.Action action, bool primary = false, string tooltip = null)
        {
            var b = Add(m_PageActions, new Button(action) { text = label, tooltip = tooltip }, "hub-button");
            if (primary) b.AddToClassList("hub-button--primary");
        }

        static int Severity(CarveCheckLevel l) => l == CarveCheckLevel.Error ? 3 : l == CarveCheckLevel.Warning ? 2 : l == CarveCheckLevel.Info ? 1 : 0;

        // Runs every health check once per page build; feeds the header pill, the sidebar badges and the Dashboard.
        void RunChecks()
        {
            m_Checks = new List<CarveCheck>();
            foreach (var check in CarveProject.Checks)
            {
                CarveCheck c;
                try { c = check(); }
                catch (System.Exception e) { c = new CarveCheck { level = CarveCheckLevel.Error, title = "Check failed", detail = e.Message }; }
                m_Checks.Add(c);
            }
            int issues = m_Checks.Count(c => Severity(c.level) >= 2);
            bool error = m_Checks.Any(c => c.level == CarveCheckLevel.Error);
            m_HealthPill.EnableInClassList("hub-pill--ok", issues == 0);
            m_HealthPill.EnableInClassList("hub-pill--warn", issues > 0 && !error);
            m_HealthPill.EnableInClassList("hub-pill--error", error);
            m_HealthPill.Q<Label>().text = issues == 0 ? "All checks pass" : $"{issues} to fix";

            SetBadge(Tab.Overview, issues, true);
            SetBadge(Tab.Materials, CarveProject.ProjectMaterials().Count, false);
            SetBadge(Tab.Convert, CarveConverter.Candidates(true).Count, false);
        }

        void SetBadge(Tab t, int value, bool alert)
        {
            var b = m_Badges[t];
            b.text = value > 99 ? "99+" : value.ToString();
            b.EnableInClassList("nav-badge--hidden", value == 0);
            b.EnableInClassList("nav-badge--alert", alert);
        }

        // ---------------- Dashboard

        void BuildOverview()
        {
            PageAction("Refresh", Refresh, tooltip: "Re-run the checks and rescan the open scenes.");

            var stats = Add(m_Page, new VisualElement(), "stat-row");
            var sun = CarveSun.Current;
            Stat(stats, CarveGlyph.Materials, CarveProject.ProjectMaterials().Count.ToString(), "Carve materials", () => Select(Tab.Materials));
            Stat(stats, CarveGlyph.Cube, CarveProject.SceneRenderers().Count.ToString(), "Objects in open scenes", null);
            Stat(stats, CarveGlyph.Mesh, BakedMeshes().Count.ToString(), "Baked meshes", () => Select(Tab.Meshes));
            Stat(stats, CarveGlyph.Pyramid, CarvePyramidBaker.AllPyramids().Count.ToString(), "Height pyramids", null);
            Stat(stats, CarveGlyph.Sun, sun != null ? sun.name : "None", "Sun for self-shadowing", null);

            var grid = Add(m_Page, new VisualElement(), "hub-grid");
            var issues = m_Checks.Where(c => Severity(c.level) >= 1).OrderByDescending(c => Severity(c.level)).ToList();
            var passed = m_Checks.Where(c => Severity(c.level) == 0).ToList();
            int toFix = issues.Count(c => Severity(c.level) >= 2);
            var health = Card(grid, "Project health", toFix == 0 ? $"All {m_Checks.Count} checks pass" : $"{toFix} to fix · {passed.Count} pass", "Health", true);
            foreach (var c in issues)
            {
                var row = Add(health, new VisualElement(), "check-row", "check-row--issue");
                Add(row, new VisualElement(), "check-dot", "check-dot--" + c.level.ToString().ToLowerInvariant());
                var text = Add(row, new VisualElement(), "check-body");
                Add(text, new Label(c.title), "check-title");
                if (!string.IsNullOrEmpty(c.detail)) Add(text, new Label(c.detail), "check-detail");
                if (c.fix != null)
                {
                    var fix = c.fix;
                    Add(row, new Button(() => { fix(); Refresh(); }) { text = c.fixLabel ?? "Fix" }, "hub-button", "hub-button--primary", "check-fix");
                }
            }
            if (issues.Count == 0)
            {
                var ok = Add(health, new VisualElement(), "health-ok");
                Add(ok, new CarveIcon(CarveGlyph.Check), "health-ok-icon");
                Add(ok, new Label("Everything Carve needs is in place."), "health-ok-text");
            }
            if (passed.Count > 0)
            {
                Add(health, new Label("PASSING"), "list-label");
                foreach (var c in passed)
                {
                    var row = Add(health, new VisualElement(), "check-row", "check-row--passed");
                    row.tooltip = c.detail;
                    Add(row, new CarveIcon(CarveGlyph.Check), "check-tick");
                    Add(row, new Label(c.title), "check-passed-title");
                }
            }

            var actions = Card(grid, "Quick actions", "Common jobs, one click away", "Actions", true);
            var tiles = Add(actions, new VisualElement(), "action-grid");
            ActionTile(tiles, CarveGlyph.Plus, "New Carve material", "Created in the selected Project folder", CreateMaterial);
            ActionTile(tiles, CarveGlyph.Convert, "Convert Lit materials", "Keep textures, tiling and POM depth", () => Select(Tab.Convert));
            ActionTile(tiles, CarveGlyph.Cube, "Bake silhouette data", "Carved outlines on modular meshes", () => Select(Tab.Meshes));
            ActionTile(tiles, CarveGlyph.Collider, "Relief colliders", "Physics that follows the relief", () => Select(Tab.Meshes));
            ActionTile(tiles, CarveGlyph.Gauge, "Measure GPU cost", "Carve on vs flat, per pass", () => { Select(Tab.Debug); CarveCostMeter.Start(); });
            ActionTile(tiles, CarveGlyph.Graph, "Shader Graph node", "Carve in your own graphs", () => Select(Tab.Help));
        }

        void Stat(VisualElement parent, CarveGlyph glyph, string value, string label, System.Action click)
        {
            var tile = Add(parent, new VisualElement(), "stat-tile");
            Add(tile, new CarveIcon(glyph), "stat-icon");
            var text = Add(tile, new VisualElement(), "stat-text");
            Add(text, new Label(value), "stat-value");
            Add(text, new Label(label), "stat-label");
            if (click == null) return;
            tile.AddToClassList("stat-tile--link");
            tile.AddManipulator(new Clickable(click));
        }

        void ActionTile(VisualElement parent, CarveGlyph glyph, string title, string detail, System.Action click)
        {
            var tile = Add(parent, new VisualElement(), "action-tile");
            Add(tile, new CarveIcon(glyph), "action-icon");
            var text = Add(tile, new VisualElement(), "action-text");
            Add(text, new Label(title), "action-title");
            Add(text, new Label(detail), "action-detail");
            tile.AddManipulator(new Clickable(click));
        }

        static void CreateMaterial()
        {
            var shader = Shader.Find($"Carve/{CarveProject.PipelineName}/Carve Lit");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Carve", $"No Carve Lit shader for {CarveProject.PipelineName} in this project.", "OK");
                return;
            }
            ProjectWindowUtil.CreateAsset(new Material(shader), "New Carve Material.mat");
        }

        // ---------------- Materials

        void BuildMaterials()
        {
            m_AllMaterials = CarveProject.ProjectMaterials();
            m_Split = new TwoPaneSplitView(0, m_Narrow ? 220f : 300f, m_Narrow ? TwoPaneSplitViewOrientation.Vertical : TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = m_Narrow ? "carve-hub-split-v" : "carve-hub-split-h"
            };
            Add(m_Page, m_Split, "mat-split");
            var left = Add(m_Split, new VisualElement(), "mat-left");
            var rightScroll = Add(m_Split, new ScrollView(), "mat-right");
            m_Details = rightScroll.contentContainer;

            var bar = Add(left, new VisualElement(), "hub-toolbar");
            var hide = Add(bar, new VisualElement(), "hub-icon-button");
            hide.tooltip = "Hide the material list (more room for the settings)";
            Add(hide, new CarveIcon(CarveGlyph.Chevron), "hub-icon-button-glyph");
            hide.AddManipulator(new Clickable(() => SetListCollapsed(true)));
            var search = Add(bar, new ToolbarSearchField(), "hub-search");
            search.value = m_Search;
            search.RegisterValueChangedCallback(e => { m_Search = e.newValue; Filter(); });

            var filterRow = Add(left, new VisualElement(), "list-filter");
            var sceneOnly = Add(filterRow, new Toggle { text = "Open scenes only", value = m_SceneOnly, tooltip = "Only materials used in the open scenes" }, "chip-toggle");
            sceneOnly.RegisterValueChangedCallback(e => { m_SceneOnly = e.newValue; Filter(); });
            m_ListCount = Add(filterRow, new Label(), "list-count");

            m_List = Add(left, new ListView { fixedItemHeight = 46, selectionType = SelectionType.Multiple }, "mat-list");
            m_List.makeItem = () =>
            {
                var item = new VisualElement();
                item.AddToClassList("mat-item");
                Add(item, new Image { scaleMode = ScaleMode.ScaleToFit }, "mat-thumb");
                var texts = Add(item, new VisualElement(), "mat-texts");
                Add(texts, new Label(), "mat-name");
                Add(texts, new Label(), "mat-info");
                Add(item, new Label(), "mat-tag");
                return item;
            };
            m_List.bindItem = (e, i) =>
            {
                var m = m_Shown[i];
                e.Q<Image>(className: "mat-thumb").image = AssetPreview.GetAssetPreview(m) ?? AssetPreview.GetMiniThumbnail(m);
                e.Q<Label>(className: "mat-name").text = m.name;
                e.Q<Label>(className: "mat-info").text = Summary(m);
                e.Q<Label>(className: "mat-tag").text = Kind(m);
            };
            m_List.selectionChanged += objs =>
            {
                m_Selected = objs.OfType<Material>().ToList();
                SelectUsers();
                BuildMaterialDetails();
            };
            m_List.itemsChosen += objs => { var m = objs.OfType<Material>().FirstOrDefault(); if (m != null) EditorGUIUtility.PingObject(m); };
            Filter();
            var keep = m_Selected.Select(m => m_Shown.IndexOf(m)).Where(i => i >= 0).ToList();
            if (keep.Count > 0) m_List.SetSelectionWithoutNotify(keep);
            else m_Selected.Clear();
            BuildMaterialDetails();
            // Previews arrive asynchronously and other tools edit materials: rebind now and then.
            m_List.schedule.Execute(() => { if (m_Tab == Tab.Materials) m_List?.RefreshItems(); }).Every(750);
            if (EditorPrefs.GetBool(PrefsPrefix + "ListCollapsed", false)) m_Split.schedule.Execute(() => SetListCollapsed(true));
        }

        static string Kind(Material m) =>
            m.shader.name.Contains("Layered") ? "LAYERED" : m.shader.name.Contains("Decal") ? "DECAL" : "LIT";

        void SetListCollapsed(bool collapsed)
        {
            EditorPrefs.SetBool(PrefsPrefix + "ListCollapsed", collapsed);
            if (m_Split == null) return;
            if (collapsed) m_Split.CollapseChild(0);
            else m_Split.UnCollapse();
            BuildMaterialDetails();
        }

        // Objects in the open scenes that use any of the selected materials.
        List<GameObject> Users() =>
            CarveProject.SceneRenderers().Where(r => r != null && r.sharedMaterials.Any(m_Selected.Contains)).Select(r => r.gameObject).Distinct().ToList();

        // Same as picking them in the Hierarchy: selection outline, Hierarchy highlight, F to frame.
        void SelectUsers()
        {
            var users = Users();
            if (users.Count > 0) Selection.objects = users.ToArray();
        }

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode != KeyCode.F || e.actionKey || e.altKey || e.shiftKey || EditorGUIUtility.editingTextField) return;
            if (e.target is VisualElement ve && ve.GetFirstOfType<TextField>() != null) return;
            FrameSelection();
            e.StopPropagation();
        }

        static void FrameSelection()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null) return;
            sv.FrameSelected();
            sv.Focus();
        }

        void Filter()
        {
            IEnumerable<Material> src = m_AllMaterials;
            if (m_SceneOnly)
            {
                var used = new HashSet<Material>(CarveProject.SceneRenderers().SelectMany(r => r.sharedMaterials));
                src = src.Where(used.Contains);
            }
            if (!string.IsNullOrEmpty(m_Search))
                src = src.Where(m => m.name.IndexOf(m_Search, System.StringComparison.OrdinalIgnoreCase) >= 0);
            m_Shown = src.ToList();
            m_List.itemsSource = m_Shown;
            m_List.Rebuild();
            m_ListCount.text = m_Shown.Count == m_AllMaterials.Count ? $"{m_Shown.Count} materials" : $"{m_Shown.Count} of {m_AllMaterials.Count}";
        }

        static string Summary(Material m)
        {
            string[] modes = { "Off", "UV Tile", "Baked" };
            float F(string p) => m.HasProperty(p) ? m.GetFloat(p) : 0f;
            int I(string p, int max) => Mathf.Clamp(Mathf.RoundToInt(F(p)), 0, max);
            string mapping = I("_CarveMapping", 2) > 0 ? " · Triplanar" : "";
            return $"{F("_CarveDepth") * 100f:0.#} cm · {F("_CarveMinSteps"):0}-{F("_CarveMaxSteps"):0} steps · Edges {modes[I("_CarveSilhouette", 2)]}{mapping}";
        }

        void BuildMaterialDetails()
        {
            var right = m_Details;
            if (right == null) return;
            right.Clear();
            if (m_MatEditor != null) DestroyImmediate(m_MatEditor);
            if (EditorPrefs.GetBool(PrefsPrefix + "ListCollapsed", false))
            {
                var show = Add(right, new VisualElement(), "list-hidden-bar");
                Add(show, new Button(() => SetListCollapsed(false)) { text = "Show materials", tooltip = "Show the material list" }, "hub-button");
                Add(show, new Label(m_Selected.Count == 0 ? "No material selected." : string.Join(", ", m_Selected.Select(m => m.name))), "list-hidden-text");
            }
            if (m_Shown.Count == 0)
            {
                Empty(right, CarveGlyph.Materials, "No Carve materials yet", "Create one, or convert your Lit materials: textures, tiling and POM depth carry over.",
                      ("New Carve material", CreateMaterial), ("Convert Lit materials", () => Select(Tab.Convert)));
                return;
            }
            if (m_Selected.Count == 0)
            {
                Empty(right, CarveGlyph.Materials, "Select a material", "Pick one or more materials on the left. Several at once are edited together, and their objects are selected in the scene.");
                return;
            }

            var quick = Card(right, m_Selected.Count == 1 ? "Quick settings" : $"Batch edit · {m_Selected.Count} materials", "Applies to every selected material. Undo works.", "QuickSettings");

            var quality = SettingRow(quick, "Quality", "Ray-march steps for the view, refinement and shadow rays.");
            var preset = s_Presets.Select((p, i) => (p, i)).FirstOrDefault(x => m_Selected.All(m =>
                Mathf.Approximately(m.GetFloat("_CarveMinSteps"), x.p.min) && Mathf.Approximately(m.GetFloat("_CarveMaxSteps"), x.p.max) &&
                Mathf.Approximately(m.GetFloat("_CarveRefineSteps"), x.p.refine) && Mathf.Approximately(m.GetFloat("_CarveShadowSteps"), x.p.shadow)));
            int presetIndex = preset.p.name == null ? -1 : preset.i;
            Segmented(quality, s_Presets.Select((p, i) => (p.name, $"{p.min}-{p.max} steps, {p.refine} refine, {p.shadow} shadow steps", (System.Action)(() => ApplyPreset(p)), i == presetIndex)).ToList());
            if (presetIndex < 0) Add(quality, new Label("Custom"), "setting-hint");

            var tracing = SettingRow(quick, "Tracing", "Pyramid marches only the height band each ray can hit (a baked min/max pyramid).");
            bool allPyramid = m_Selected.All(m => m.HasProperty("_CarveTracing") && m.GetFloat("_CarveTracing") > 0.5f);
            bool allLinear = m_Selected.All(m => m.HasProperty("_CarveTracing") && m.GetFloat("_CarveTracing") < 0.5f);
            Segmented(tracing, new List<(string, string, System.Action, bool)>
            {
                ("Linear", "Fixed steps along the ray.", () => Apply("Carve tracing Linear", m => m.SetFloat("_CarveTracing", 0f)), allLinear),
                ("Pyramid", "Faster: skips empty space (pyramid baked now if needed).", () =>
                {
                    Apply("Carve tracing Pyramid", m => m.SetFloat("_CarveTracing", 1f));
                    int n = CarvePyramidBaker.Assign(m_Selected);
                    if (n > 0) ShowNotification(new GUIContent($"Baked {n} height pyramid(s)"));
                }, allPyramid),
            });

            var depth = SettingRow(quick, "Depth", "Scales the relief depth of every selected material.");
            var depths = m_Selected.Where(m => m.HasProperty("_CarveDepth")).Select(m => m.GetFloat("_CarveDepth") * 100f).ToList();
            Segmented(depth, new[] { 0.5f, 0.8f, 1.25f, 2f }.Select(f => ($"× {f}", $"Depth × {f}", (System.Action)(() =>
                Apply("Scale Carve depth", m => m.SetFloat("_CarveDepth", Mathf.Clamp(m.GetFloat("_CarveDepth") * f, 0f, 0.5f)))), false)).ToList());
            if (depths.Count > 0)
                Add(depth, new Label(depths.Min() == depths.Max() ? $"{depths[0]:0.#} cm" : $"{depths.Min():0.#}-{depths.Max():0.#} cm"), "setting-hint");

            var features = SettingRow(quick, "Features", "Turn the relief lighting features on or off.");
            FeatureToggle(features, "Self Shadow", "_CarveSelfShadow");
            FeatureToggle(features, "Relief Occlusion", "_CarveOcclusion");
            FeatureToggle(features, "Cliff Shading", "_CarveCliff");

            var users = Users();
            var scene = SettingRow(quick, "In scene", "Objects in the open scenes that use these materials.");
            Add(scene, new Label(users.Count == 0 ? "Not used in the open scenes" : $"{users.Count} object(s), selected"), "setting-value");
            if (users.Count > 0)
            {
                Add(scene, new Button(SelectUsers) { text = "Select", tooltip = "Select every object in the open scenes that uses these materials." }, "hub-button", "hub-button--small");
                Add(scene, new Button(() => { SelectUsers(); FrameSelection(); }) { text = "Frame (F)", tooltip = "Frame them in the Scene view. F does the same while the Hub has focus." }, "hub-button", "hub-button--small");
            }

            var footer = Add(quick, new VisualElement(), "card-footer");
            Add(footer, new Button(() => { Selection.objects = m_Selected.ToArray(); }) { text = "Select in Project" }, "hub-button");
            Add(footer, new Button(() =>
            {
                int n = CarveProject.BakeFilters(CarveProject.UnbakedFilters(m_Selected));
                ShowNotification(new GUIContent(n == 0 ? "Nothing to bake" : $"Baked {n} mesh(es)"));
            }) { text = "Bake meshes using these", tooltip = "Bake silhouette data for every mesh in the open scenes that uses these materials and needs it." }, "hub-button");
            var back = m_Selected.Select(CarveConverter.PlanBack).Where(c => c != null).ToList();
            if (back.Count > 0)
                Add(footer, new Button(() =>
                {
                    if (!EditorUtility.DisplayDialog("Carve", $"Convert {back.Count} material(s) back to {back[0].to}?\n\nTextures, colors, tiling and POM depth carry over. Carve-only features (silhouettes, self-shadowing, relief occlusion...) are dropped. Undo restores them.", "Convert back", "Cancel"))
                        return;
                    CarveConverter.ConvertBack(back);
                    m_Selected.Clear();
                    Refresh();
                }) { text = "Back to Lit", tooltip = "Switch these materials back to Lit / LayeredLit (Undo restores them)." }, "hub-button");

            var inspector = Card(right, m_Selected.Count == 1 ? m_Selected[0].name : $"Inspector · {m_Selected.Count} materials", m_Selected.Count == 1 ? m_Selected[0].shader.name : "Shared settings", "Inspector");
            foreach (var m in m_Selected) UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(m, true);
            m_MatEditor = UnityEditor.Editor.CreateEditor(m_Selected.ToArray());
            Add(inspector, new InspectorElement(m_MatEditor), "mat-inspector");
        }

        void ApplyPreset((string name, float min, float max, float refine, float shadow) p)
        {
            Apply("Carve quality " + p.name, m =>
            {
                m.SetFloat("_CarveMinSteps", p.min);
                m.SetFloat("_CarveMaxSteps", p.max);
                m.SetFloat("_CarveRefineSteps", p.refine);
                m.SetFloat("_CarveShadowSteps", p.shadow);
            });
        }

        void FeatureToggle(VisualElement parent, string label, string prop)
        {
            bool on = m_Selected.All(m => m.HasProperty(prop) && m.GetFloat(prop) > 0f);
            var t = Add(parent, new Toggle { text = label, value = on }, "chip-toggle");
            t.RegisterValueChangedCallback(e => Apply(label, m => m.SetFloat(prop, e.newValue ? 1f : 0f), false));
        }

        void Apply(string undo, System.Action<Material> action, bool rebuild = true)
        {
            Undo.RecordObjects(m_Selected.ToArray(), undo);
            foreach (var m in m_Selected)
            {
                action(m);
                EditorUtility.SetDirty(m);
            }
            m_List?.RefreshItems();
            SceneView.RepaintAll();
            if (rebuild) BuildMaterialDetails();
        }

        // ---------------- Convert

        static bool ConvertScene { get => EditorPrefs.GetBool(PrefsPrefix + "ConvertScene", true); set => EditorPrefs.SetBool(PrefsPrefix + "ConvertScene", value); }
        static bool ConvertCopies { get => EditorPrefs.GetBool(PrefsPrefix + "ConvertCopies", false); set => EditorPrefs.SetBool(PrefsPrefix + "ConvertCopies", value); }
        static bool ConvertSwap { get => EditorPrefs.GetBool(PrefsPrefix + "ConvertSwap", true); set => EditorPrefs.SetBool(PrefsPrefix + "ConvertSwap", value); }
        static bool ConvertGenerate { get => EditorPrefs.GetBool(PrefsPrefix + "ConvertGenerate", true); set => EditorPrefs.SetBool(PrefsPrefix + "ConvertGenerate", value); }

        void BuildConvert()
        {
            PageAction("Refresh", Refresh, tooltip: "Rescan for Lit materials.");

            var options = Card(m_Page, "Options", "Maps, colors, tiling, POM depth, triplanar and layer settings carry over", "ConvertOptions");
            var scope = SettingRow(options, "Look in", "Which Lit materials to list.");
            Segmented(scope, new List<(string, string, System.Action, bool)>
            {
                ("Open scenes", "Materials used in the open scenes.", () => { ConvertScene = true; Refresh(); }, ConvertScene),
                ("Whole project", "Every Lit material under Assets/.", () => { ConvertScene = false; Refresh(); }, !ConvertScene),
            });
            var mode = SettingRow(options, "Mode", "In place keeps the same material assets (Undo works); copies leave the originals untouched.");
            Segmented(mode, new List<(string, string, System.Action, bool)>
            {
                ("Convert in place", "Undo works.", () => { ConvertCopies = false; Refresh(); }, !ConvertCopies),
                ("Make Carve copies", "Originals untouched.", () => { ConvertCopies = true; Refresh(); }, ConvertCopies),
            });
            if (ConvertCopies)
            {
                var swap = Add(mode, new Toggle { text = "Use the copies in open scenes", value = ConvertSwap, tooltip = "Swap the copies onto the objects in the open scenes." }, "chip-toggle");
                swap.RegisterValueChangedCallback(e => ConvertSwap = e.newValue);
            }
            var heights = SettingRow(options, "Height maps", "Materials with a normal map but no height map get one generated (Carve's height map generator).");
            var generate = Add(heights, new Toggle { text = "Generate missing height maps", value = ConvertGenerate }, "chip-toggle");
            generate.RegisterValueChangedCallback(e => { ConvertGenerate = e.newValue; Refresh(); });

            var mats = CarveConverter.Candidates(ConvertScene);
            m_ConvertSkip.RemoveWhere(m => m == null);
            if (mats.Count == 0)
            {
                Empty(m_Page, CarveGlyph.Convert, "Nothing to convert",
                      ConvertScene ? "No Lit or LayeredLit materials in the open scenes." : "No Lit or LayeredLit materials in the project.");
                return;
            }

            var plans = mats.Select(CarveConverter.Plan).ToList();
            var list = Card(m_Page, "Materials", $"{plans.Count} found · untick any you want to keep as they are", "ConvertList");
            var header = list.parent.Q(className: "hub-card-actions");
            Button go = null;
            var toggles = new List<Toggle>();
            void UpdateGo()
            {
                int n = plans.Count(c => !m_ConvertSkip.Contains(c.source));
                go.text = $"Convert {n}";
                go.SetEnabled(n > 0);
            }
            Add(header, new Button(() => { m_ConvertSkip.Clear(); foreach (var t in toggles) t.SetValueWithoutNotify(true); UpdateGo(); }) { text = "All" }, "hub-button", "hub-button--small");
            Add(header, new Button(() => { foreach (var c in plans) m_ConvertSkip.Add(c.source); foreach (var t in toggles) t.SetValueWithoutNotify(false); UpdateGo(); }) { text = "None" }, "hub-button", "hub-button--small");
            go = Add(header, new Button(() =>
            {
                var chosen = plans.Where(c => !m_ConvertSkip.Contains(c.source)).ToList();
                var done = CarveConverter.Convert(chosen, ConvertCopies, ConvertSwap, ConvertGenerate);
                ShowNotification(new GUIContent($"Converted {done.Count} material(s)"));
                m_Selected = done;
                Select(Tab.Materials);
            }), "hub-button", "hub-button--primary");

            foreach (var c in plans)
            {
                var plan = c;
                var row = Add(list, new VisualElement(), "convert-row");
                var t = Add(row, new Toggle { value = !m_ConvertSkip.Contains(plan.source) }, "convert-toggle");
                toggles.Add(t);
                t.RegisterValueChangedCallback(e => { if (e.newValue) m_ConvertSkip.Remove(plan.source); else m_ConvertSkip.Add(plan.source); UpdateGo(); });
                var body = Add(row, new VisualElement(), "convert-body");
                var head = Add(body, new VisualElement(), "convert-head");
                var name = Add(head, new Label(plan.source.name), "convert-name");
                name.tooltip = AssetDatabase.GetAssetPath(plan.source) + "\nClick to ping.";
                name.RegisterCallback<ClickEvent>(_ => EditorGUIUtility.PingObject(plan.source));
                Add(head, new Label(plan.from), "tag");
                Add(head, new Label("→"), "convert-arrow");
                Add(head, new Label(plan.to), "tag", "tag--accent");
                Add(body, new Label("Keeps " + string.Join(", ", plan.carried)), "check-detail");
                string height = HeightNote(plan.source);
                if (height != null) Add(body, new Label(height), "check-detail");
                if (plan.lost.Count > 0) Add(body, new Label("Not carried over: " + string.Join(", ", plan.lost)), "check-detail", "convert-lost");
            }
            UpdateGo();
        }

        static string HeightNote(Material m)
        {
            bool layered = m.HasProperty("_LayerCount") || m.shader.name.Contains("Layered");
            string[] suffixes = layered ? Enumerable.Range(0, Mathf.Clamp(Mathf.RoundToInt(m.GetFloat("_LayerCount")), 2, 4)).Select(i => i.ToString()).ToArray() : new[] { "" };
            int missing = 0, canGenerate = 0;
            foreach (var sfx in suffixes)
            {
                if (CarveConverter.HasTex(m, "_HeightMap" + sfx)) continue;
                missing++;
                if (CarveConverter.HasTex(m, "_NormalMap" + sfx)) canGenerate++;
            }
            if (missing == 0) return null;
            string what = layered ? $"{missing} layer(s) without a height map" : "No height map";
            if (canGenerate == 0) return what + (layered ? " (flat layers)." : ": stays flat until one is assigned.");
            return what + (ConvertGenerate ? ": generated from the normal map." : ": turn on \"Generate missing height maps\" to make one.");
        }

        // ---------------- Meshes & Physics

        void BuildMeshes()
        {
            var grid = Add(m_Page, new VisualElement(), "hub-grid");

            var bake = Card(grid, "Silhouette data", "Carved outlines on box and modular meshes", "Silhouettes", true);
            Add(bake, new Label("Baked silhouettes read each face's outline (UV2) and curvature (UV3) from the mesh. Baking writes a copy of the mesh and swaps it on the renderer; the original is untouched."), "hub-text");
            var unbaked = CarveProject.UnbakedFilters();
            var row = Add(bake, new VisualElement(), "card-footer");
            Add(row, new Button(() =>
            {
                var filters = Selection.gameObjects.SelectMany(g => g.GetComponentsInChildren<MeshFilter>()).ToList();
                int n = CarveProject.BakeFilters(filters);
                ShowNotification(new GUIContent(n == 0 ? "Select objects with meshes first" : $"Baked {n} mesh(es)"));
                Refresh();
            }) { text = "Bake selected objects" }, "hub-button", "hub-button--primary");
            var all = Add(row, new Button(() => { CarveProject.BakeFilters(unbaked); Refresh(); }) { text = unbaked.Count > 0 ? $"Bake all that need it ({unbaked.Count})" : "All baked" }, "hub-button");
            all.SetEnabled(unbaked.Count > 0);
            Add(bake, new Label("Saved to " + CarveMeshBaker.DefaultFolder), "hub-note");

            var relief = Card(grid, "Relief colliders & meshes", "Real geometry from the carved surface", "Relief", true);
            Add(relief, new Label("A collider lets characters and physics objects rest in the grooves; a mesh is for close-ups, LODs or export. Uses the material's own relief (layers, anti-tiling, detail, triplanar)."), "hub-text");
            var meshCm = Add(relief, new FloatField("Mesh spacing (cm)") { value = Mathf.Round(CarveReliefBaker.MeshSpacing * 1000f) / 10f, tooltip = "Longest edge of the baked mesh. Smaller = more detail, more vertices." }, "hub-field");
            meshCm.RegisterValueChangedCallback(e => CarveReliefBaker.MeshSpacing = e.newValue * 0.01f);
            var colCm = Add(relief, new FloatField("Collider spacing (cm)") { value = Mathf.Round(CarveReliefBaker.ColliderSpacing * 1000f) / 10f, tooltip = "Longest edge of the collider mesh. Physics needs less detail than rendering." }, "hub-field");
            colCm.RegisterValueChangedCallback(e => CarveReliefBaker.ColliderSpacing = e.newValue * 0.01f);
            var reliefRow = Add(relief, new VisualElement(), "card-footer");
            Add(reliefRow, new Button(() =>
            {
                int n = CarveReliefBaker.AddColliders(CarveReliefBaker.SelectedRenderers());
                ShowNotification(new GUIContent(n == 0 ? "Select objects with Carve materials first" : $"Added {n} relief collider(s)"));
            }) { text = "Add relief collider", tooltip = "Adds (or updates) a MeshCollider with the carved surface on the selected objects; their other colliders are disabled." }, "hub-button", "hub-button--primary");
            Add(reliefRow, new Button(() =>
            {
                int n = CarveReliefBaker.BakeMeshes(CarveReliefBaker.SelectedRenderers(), true);
                ShowNotification(new GUIContent(n == 0 ? "Select objects with Carve materials first" : $"Baked {n} relief mesh(es)"));
            }) { text = "Bake relief mesh", tooltip = "Saves a displaced mesh and adds a copy of the selected object that uses it; the original is hidden, Undo restores it." }, "hub-button");
            Add(relief, new Label("Saved to " + CarveReliefBaker.Folder), "hub-note");

            var meshes = BakedMeshes();
            var list = Card(m_Page, "Baked meshes", meshes.Count == 0 ? "None yet" : $"{meshes.Count} in {CarveMeshBaker.DefaultFolder}", "BakedMeshes");
            if (meshes.Count == 0)
                Add(list, new Label("Meshes you bake for silhouettes show up here."), "hub-note");
            foreach (var mesh in meshes)
            {
                var r = Add(list, new VisualElement(), "table-row");
                Add(r, new CarveIcon(CarveGlyph.Mesh), "table-icon");
                Add(r, new Label(mesh.name), "table-name");
                Add(r, new Label($"{mesh.vertexCount:N0} vertices"), "table-meta");
                var m = mesh;
                Add(r, new Button(() => EditorGUIUtility.PingObject(m)) { text = "Ping" }, "hub-button", "hub-button--small");
            }
        }

        static List<Mesh> BakedMeshes()
        {
            var list = new List<Mesh>();
            if (!AssetDatabase.IsValidFolder(CarveMeshBaker.DefaultFolder)) return list;
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { CarveMeshBaker.DefaultFolder }))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                if (mesh != null) list.Add(mesh);
            }
            return list;
        }

        // ---------------- Debug & Performance

        void BuildDebug()
        {
            PageAction("Reset overrides", () => { SetGlobals(1f, 1f, CarveDebugView.Off); Refresh(); }, tooltip: "Debug view off, depth and steps back to 1.");

            var views = Card(m_Page, "Debug view", "Shows one ingredient of the relief on every Carve material in the Scene and Game views", "DebugView");
            var chips = Add(views, new VisualElement(), "chip-grid");
            foreach (CarveDebugView v in System.Enum.GetValues(typeof(CarveDebugView)))
            {
                var view = v;
                var chip = Add(chips, new Button(() => { SetGlobals(CarveGlobals.DepthScale, CarveGlobals.StepScale, view); Refresh(); })
                    { text = ObjectNames.NicifyVariableName(v.ToString()), tooltip = s_DebugInfo[v] }, "chip");
                if (v == CarveGlobals.DebugView) chip.AddToClassList("chip--active");
            }
            var callout = Add(views, new VisualElement(), "callout");
            Add(callout, new Label(ObjectNames.NicifyVariableName(CarveGlobals.DebugView.ToString())), "callout-title");
            Add(callout, new Label(s_DebugInfo[CarveGlobals.DebugView]), "callout-text");

            var grid = Add(m_Page, new VisualElement(), "hub-grid");
            var overrides = Card(grid, "Global overrides", "This editor session only: for A/B checks and performance tests", "Overrides", true);
            var depth = Add(overrides, new Slider("Depth scale", 0f, 2f) { value = CarveGlobals.DepthScale, showInputField = true }, "hub-field");
            depth.RegisterValueChangedCallback(e => SetGlobals(e.newValue, CarveGlobals.StepScale, CarveGlobals.DebugView));
            var steps = Add(overrides, new Slider("Step scale", 0.25f, 2f) { value = CarveGlobals.StepScale, showInputField = true }, "hub-field");
            steps.RegisterValueChangedCallback(e => SetGlobals(CarveGlobals.DepthScale, e.newValue, CarveGlobals.DebugView));
            var ab = SettingRow(overrides, "Relief", "Flat = every Carve material renders without relief, to compare.");
            Segmented(ab, new List<(string, string, System.Action, bool)>
            {
                ("Carved", "Normal depth.", () => depth.value = 1f, CarveGlobals.DepthScale > 0f),
                ("Flat", "Relief off everywhere.", () => depth.value = 0f, CarveGlobals.DepthScale <= 0f),
            });

            var perf = Card(grid, "GPU cost", "What Carve costs in the open Scene and Game views", "Performance", true);
            Add(perf, new Label("Alternates Carve on and flat for about 2 seconds and reports the difference per render pass. Keep a view visible while it runs."), "hub-text");
            if (!string.IsNullOrEmpty(CarveCostMeter.Result)) Add(perf, new Label(CarveCostMeter.Result), "result-block");
            var perfRow = Add(perf, new VisualElement(), "card-footer");
            var measure = Add(perfRow, new Button(CarveCostMeter.Start) { text = CarveCostMeter.Running ? "Measuring..." : "Measure Carve cost" }, "hub-button", "hub-button--primary");
            measure.SetEnabled(!CarveCostMeter.Running);
            Add(perf, new Label("Cheaper: a lower quality preset (Materials), Auto LOD and Pyramid tracing on, a shorter Distance Fade, fewer Cast Shadow Steps."), "hub-note");
        }

        static void SetGlobals(float depth, float steps, CarveDebugView view)
        {
            CarveGlobals.DepthScale = depth;
            CarveGlobals.StepScale = steps;
            CarveGlobals.DebugView = view;
            SessionState.SetFloat(DepthKey, depth);
            SessionState.SetFloat(StepKey, steps);
            SessionState.SetInt(DebugKey, (int)view);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        // ---------------- Guides & Support

        void BuildHelp()
        {
            var grid = Add(m_Page, new VisualElement(), "hub-grid");
            var start = Card(grid, "Quick start", "From a flat material to carved relief", "QuickStart", true);
            Steps(start,
                $"Create a material with Carve/{CarveProject.PipelineName}/Carve Lit, or convert a Lit material (Convert page).",
                "Assign a Height Map (linear, white = top) and set Depth in centimeters.",
                "Box and modular meshes: Edge Silhouettes = Baked, then bake the meshes (Meshes page).",
                "Rocks, columns and pipes: Curved Silhouettes = Auto.",
                "Check the Dashboard and fix anything it flags (TAA, sun, baked data).");
            var startRow = Add(start, new VisualElement(), "card-footer");
            Add(startRow, new Button(CreateMaterial) { text = "New Carve material" }, "hub-button", "hub-button--primary");

            var graphs = Card(grid, "Your own Shader Graphs", "The Carve POM node", "ShaderGraphs", true);
            Steps(graphs,
                "Add the node: Create Node > Carve > Carve POM, and connect your Height Map.",
                "Sample your textures at Parallax UV with Mip Sampling Mode = Gradient, wired to DDX and DDY.",
                "Multiply Base Color by Shadow and Ambient Occlusion by Relief Occlusion.",
                CarveProject.PipelineName == "URP"
                    ? "URP: enable Alpha Clipping in Graph Settings and connect Alpha (URP Shader Graph has no depth offset: Carve Lit writes real depth, the node doesn't)."
                    : "HDRP: enable Depth Offset and Alpha Clipping in Graph Settings, then connect Depth Offset and Alpha.",
                "Faster: right-click the height map > Carve > Bake Height Pyramid, and plug it into Height Pyramid.");
            var graphRow = Add(graphs, new VisualElement(), "card-footer");
            Add(graphRow, new Button(() => AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(ExampleGraphPath))) { text = "Open example graph" }, "hub-button", "hub-button--primary")
                .SetEnabled(AssetDatabase.LoadMainAssetAtPath(ExampleGraphPath) != null);
            Add(graphRow, new Button(() => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(SubGraphPath))) { text = "Show node" }, "hub-button");

            var support = Card(m_Page, "Support", $"{CarveInfo.Product} {CarveInfo.Version} by {CarveInfo.Publisher.TrimEnd('.')}", "Support");
            Add(support, new Label($"Questions, ideas or a bug? Write to {CarveInfo.SupportEmail}. Bug reports include your Unity version, pipeline and Carve version automatically."), "hub-text");
            var row = Add(support, new VisualElement(), "card-footer");
            Add(row, new Button(() => Application.OpenURL($"mailto:{CarveInfo.SupportEmail}?subject=Carve%20support")) { text = "Email support" }, "hub-button", "hub-button--primary");
            Add(row, new Button(() => Application.OpenURL($"mailto:{CarveInfo.SupportEmail}?subject=Carve%20bug%20report&body=Unity%20{Application.unityVersion}%2C%20{CarveProject.PipelineName}%2C%20Carve%20{CarveInfo.Version}")) { text = "Report a bug" }, "hub-button");
            var docs = Add(row, new Button(CarveSetupWindow.OpenDocumentation) { text = "Documentation", tooltip = "The Carve manual (PDF)." }, "hub-button");
            docs.SetEnabled(CarveSetupWindow.DocumentationPath != null);
        }

        static void Steps(VisualElement parent, params string[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var row = Add(parent, new VisualElement(), "step");
                Add(row, new Label((i + 1).ToString()), "step-number");
                Add(row, new Label(lines[i]), "step-text");
            }
        }

        // ---------------- Building blocks

        // A card with a title, optional subtitle and header actions; the header folds it (remembered per card). Returns the content area.
        internal static VisualElement Card(VisualElement parent, string title, string subtitle = null, string id = null, bool cell = false)
        {
            string key = PrefsPrefix + "Card." + (id ?? title);
            var card = Add(parent, new VisualElement(), "hub-card");
            if (cell) card.AddToClassList("grid-cell");
            var head = Add(card, new VisualElement(), "hub-card-header");
            Add(head, new CarveIcon(CarveGlyph.Caret), "hub-card-chevron");
            var titles = Add(head, new VisualElement(), "hub-card-titles");
            Add(titles, new Label(title), "hub-card-title");
            if (!string.IsNullOrEmpty(subtitle)) Add(titles, new Label(subtitle), "hub-card-subtitle");
            Add(head, new VisualElement(), "hub-card-actions");
            var content = Add(card, new VisualElement(), "hub-card-content");
            void ApplyState(bool collapsed)
            {
                card.EnableInClassList("hub-card--collapsed", collapsed);
                head.tooltip = collapsed ? "Click to expand" : "Click to collapse";
            }
            ApplyState(EditorPrefs.GetBool(key, false));
            head.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button) return;
                bool collapsed = !card.ClassListContains("hub-card--collapsed");
                EditorPrefs.SetBool(key, collapsed);
                ApplyState(collapsed);
            });
            return content;
        }

        // A labelled row inside a card: fixed label column, controls to the right. Returns the controls container.
        internal static VisualElement SettingRow(VisualElement parent, string label, string tooltip)
        {
            var row = new VisualElement();
            row.AddToClassList("setting-row");
            if (parent.childCount == 0) row.AddToClassList("setting-row--first");
            parent.Add(row);
            var l = Add(row, new Label(label), "setting-label");
            l.tooltip = tooltip;
            return Add(row, new VisualElement(), "setting-controls");
        }

        internal static void Segmented(VisualElement parent, IList<(string label, string tooltip, System.Action action, bool active)> items)
        {
            var seg = Add(parent, new VisualElement(), "segmented");
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var b = Add(seg, new Button(it.action) { text = it.label, tooltip = it.tooltip }, "segment");
                if (i == 0) b.AddToClassList("segment--first");
                if (i == items.Count - 1) b.AddToClassList("segment--last");
                if (it.active) b.AddToClassList("segment--active");
            }
        }

        static void Empty(VisualElement parent, CarveGlyph glyph, string title, string text, params (string label, System.Action action)[] buttons)
        {
            var box = Add(parent, new VisualElement(), "empty-state");
            Add(box, new CarveIcon(glyph), "empty-icon");
            Add(box, new Label(title), "empty-title");
            Add(box, new Label(text), "empty-text");
            if (buttons.Length == 0) return;
            var row = Add(box, new VisualElement(), "empty-actions");
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = Add(row, new Button(buttons[i].action) { text = buttons[i].label }, "hub-button");
                if (i == 0) b.AddToClassList("hub-button--primary");
            }
        }

        internal static T Add<T>(VisualElement parent, T element, params string[] classes) where T : VisualElement
        {
            foreach (var c in classes) element.AddToClassList(c);
            parent.Add(element);
            return element;
        }
    }
}
