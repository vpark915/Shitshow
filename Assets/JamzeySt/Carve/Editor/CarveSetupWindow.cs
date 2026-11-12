using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using static JamzeySt.Carve.Editor.CarveHub;

namespace JamzeySt.Carve.Editor
{
    // Opens once on first import: checks the project and the open scene (one-click fixes), then points to the demo scenes,
    // a first material and the manual.
    public class CarveSetupWindow : EditorWindow
    {
        const string ShownKey = "JamzeySt.Carve.SetupShown.";

        static readonly (string scene, string title, string detail)[] s_Demos =
        {
            ("Carve_Showcase", "Showcase", "Every feature on its own station"),
            ("Carve_Village", "Walled town", "A whole town, with the Quality Manager"),
            ("Carve_Minimal", "Minimal", "The smallest working setup"),
        };

        VisualElement m_Page;

        [MenuItem("Tools/Carve/Setup Wizard", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<CarveSetupWindow>(true, "Carve Setup", true);
            w.minSize = new Vector2(520, 640);
            w.Show();
        }

        [InitializeOnLoadMethod]
        static void OpenOnFirstImport()
        {
            if (Application.isBatchMode) return;
            string key = ShownKey + Application.dataPath.GetHashCode();
            if (EditorPrefs.GetBool(key, false)) return;
            EditorApplication.delayCall += () =>
            {
                EditorPrefs.SetBool(key, true);
                Open();
            };
        }

        public static string DocumentationPath =>
            AssetDatabase.FindAssets("Carve Documentation").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => p.EndsWith(".pdf"));

        public static void OpenDocumentation()
        {
            string path = DocumentationPath;
            if (path != null) Application.OpenURL("file:///" + System.IO.Path.GetFullPath(path).Replace('\\', '/'));
            else EditorUtility.DisplayDialog("Carve", "Carve Documentation.pdf is not in the project.", "OK");
        }

        void OnEnable() => AssetDatabase.importPackageCompleted += OnPackageImported;
        void OnDisable() => AssetDatabase.importPackageCompleted -= OnPackageImported;

        void OnPackageImported(string name)
        {
            if (m_Page == null) return;
            CarveProject.InvalidateScan();
            Build();
        }

        void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = FindStyleSheet();
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("carve-hub");
            if (!EditorGUIUtility.isProSkin) root.AddToClassList("carve-hub--light");

            var header = Add(root, new VisualElement(), "hub-header");
            Add(header, new CarveLogo(), "hub-logo");
            var brand = Add(header, new VisualElement(), "hub-brand");
            Add(brand, new Label(CarveInfo.Product.ToUpperInvariant()), "hub-wordmark");
            Add(brand, new Label(CarveInfo.Subtitle), "hub-slogan");
            Add(header, new VisualElement(), "hub-spacer");
            var pills = Add(header, new VisualElement(), "hub-pills");
            Add(pills, new Label(CarveProject.PipelineName), "hub-pill").tooltip = "Active render pipeline";
            Add(pills, new Label("v" + CarveInfo.Version), "hub-pill").tooltip = "Carve version";

            var scroll = Add(root, new ScrollView(), "hub-scroll");
            var banner = AssetDatabase.FindAssets("Carve_SetupBanner t:Texture2D").Select(AssetDatabase.GUIDToAssetPath)
                                      .Select(AssetDatabase.LoadAssetAtPath<Texture2D>).FirstOrDefault();
            if (banner != null) Add(scroll, new VisualElement(), "setup-banner").style.backgroundImage = banner;
            m_Page = Add(scroll, new VisualElement(), "hub-page");
            Build();
        }

        void Build()
        {
            m_Page.Clear();
            Add(m_Page, new Label("Thanks for choosing Carve. Four steps get you from import to carved relief in your own scene."), "hub-text", "setup-intro");

            // 1. Health: the Hub's checks, issues first, each with its fix.
            var checks = new List<CarveCheck>();
            foreach (var check in CarveProject.Checks)
            {
                try { checks.Add(check()); }
                catch (System.Exception e) { checks.Add(new CarveCheck { level = CarveCheckLevel.Error, title = "Check failed", detail = e.Message }); }
            }
            var issues = checks.Where(c => c.level != CarveCheckLevel.Ok).OrderByDescending(c => c.level).ToList();
            int toFix = issues.Count(c => c.level >= CarveCheckLevel.Warning);
            var health = Card(m_Page, "1  Check your project", toFix == 0 ? "Everything Carve needs is in place" : $"{toFix} to fix · the open scene is checked too", "Setup.Health");
            Add(health.parent.Q(className: "hub-card-actions"), new Button(() => { CarveProject.InvalidateScan(); Build(); }) { text = "Re-check" }, "hub-button", "hub-button--small");
            foreach (var c in issues)
            {
                var row = Add(health, new VisualElement(), "check-row", "check-row--issue");
                Add(row, new VisualElement(), "check-dot", "check-dot--" + c.level.ToString().ToLowerInvariant());
                var text = Add(row, new VisualElement(), "check-body");
                Add(text, new Label(c.title), "check-title");
                if (!string.IsNullOrEmpty(c.detail)) Add(text, new Label(c.detail), "check-detail");
                if (c.fix == null) continue;
                var fix = c.fix;
                Add(row, new Button(() => { fix(); CarveProject.InvalidateScan(); Build(); }) { text = c.fixLabel ?? "Fix" }, "hub-button", "hub-button--primary", "check-fix");
            }
            foreach (var c in checks.Where(c => c.level == CarveCheckLevel.Ok))
            {
                var row = Add(health, new VisualElement(), "check-row", "check-row--passed");
                row.tooltip = c.detail;
                Add(row, new CarveIcon(CarveGlyph.Check), "check-tick");
                Add(row, new Label(c.title), "check-passed-title");
            }

            // 2. Demo scenes.
            var demos = Card(m_Page, "2  Explore the demos", "WASD to move, C turns Carve on and off, V cycles the debug views, F1 shows every key", "Setup.Demos");
            var tiles = Add(demos, new VisualElement(), "action-grid");
            foreach (var (scene, title, detail) in s_Demos)
            {
                string path = ScenePath(CarveProject.PipelineName == "URP" ? scene + "_URP" : scene);
                if (path != null) Tile(tiles, CarveGlyph.Dashboard, title, detail, () => OpenScene(path));
            }

            // 3. First material.
            var first = Card(m_Page, "3  Carve your own materials", "New, converted from Lit, and on modular meshes", "Setup.First");
            var make = Add(first, new VisualElement(), "action-grid");
            Tile(make, CarveGlyph.Plus, "New Carve material", "Created in the selected Project folder", NewMaterial);
            Tile(make, CarveGlyph.Convert, "Convert Lit materials", "Keeps textures, tiling and POM depth", () => CarveHub.Open("Convert"));
            Tile(make, CarveGlyph.Cube, "Bake silhouette data", "Carved outlines on modular meshes", () => CarveHub.Open("Meshes"));
            Tile(make, CarveGlyph.Gauge, "Add a Quality Manager", "Presets and adaptive quality per scene", () => EditorApplication.ExecuteMenuItem("GameObject/Carve/Quality Manager"));

            // 4. Learn and support.
            var learn = Card(m_Page, "4  Learn more", "The manual, the Carve Hub and support", "Setup.Learn");
            Add(learn, new Label("The Carve Hub (Tools > Carve > Carve Hub) keeps these checks, edits many materials at once and measures what Carve costs on the GPU. " +
                                 $"Questions or a bug: {CarveInfo.SupportEmail}."), "hub-text");
            var row2 = Add(learn, new VisualElement(), "card-footer");
            Add(row2, new Button(() => CarveHub.Open()) { text = "Open Carve Hub" }, "hub-button", "hub-button--primary");
            var docs = Add(row2, new Button(OpenDocumentation) { text = "Documentation (PDF)" }, "hub-button");
            docs.SetEnabled(DocumentationPath != null);
            Add(row2, new Button(() => Application.OpenURL($"mailto:{CarveInfo.SupportEmail}?subject=Carve%20support")) { text = "Email support" }, "hub-button");

            Add(m_Page, new Label("This window opens once per project. Reopen it from Tools > Carve > Setup Wizard."), "hub-text", "setup-foot");
        }

        static void Tile(VisualElement parent, CarveGlyph glyph, string title, string detail, System.Action click)
        {
            var tile = Add(parent, new VisualElement(), "action-tile");
            Add(tile, new CarveIcon(glyph), "action-icon");
            var text = Add(tile, new VisualElement(), "action-text");
            Add(text, new Label(title), "action-title");
            Add(text, new Label(detail), "action-detail");
            tile.AddManipulator(new Clickable(click));
        }

        static string ScenePath(string name) =>
            AssetDatabase.FindAssets(name + " t:Scene").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);

        static void OpenScene(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
        }

        static void NewMaterial()
        {
            var shader = Shader.Find($"Carve/{CarveProject.PipelineName}/Carve Lit");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Carve", $"No Carve Lit shader for {CarveProject.PipelineName} in this project.", "OK");
                return;
            }
            ProjectWindowUtil.CreateAsset(new Material(shader), "New Carve Material.mat");
        }
    }
}
