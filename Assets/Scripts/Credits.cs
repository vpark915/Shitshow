using UnityEngine;

// The Credits sign beside the main menu (HuntMatch's Credits button), like the Settings sign: who made what, scrolled
// with the mouse wheel. The full list, with licenses and where each thing lives in the project, is Docs/CREDITS.md;
// keep the two in step.
public static class Credits
{
    // the credits sign is up (beside the main menu); Music closes it when the menus go
    public static bool Showing;

    static Vector2 scroll;
    static GUIStyle line;

    const string Text =
        "<b>ShitShow</b>\nA gingertew game\n\n" +
        "<b>Fonts</b>\n" +
        "Arimo by Steve Matteson (SIL Open Font License)\n" +
        "Overpass by Delve Withrington, Dave Bailey and Thomas Jockin, for Red Hat (SIL Open Font License)\n" +
        "Libre Caslon Text by Pablo Impallari (SIL Open Font License)\n\n" +
        "<b>Sounds</b>\n" +
        "Glock19_reload.wav by Fr3yr, freesound.org/s/100804 (CC BY 3.0)\n" +
        "9mm pistol shot by michorvath, freesound.org/s/427592 (CC0)\n" +
        "Punch impact hit by Universfield (Pixabay)\n" +
        "Bone break 5 by floraphonic (Pixabay)\n" +
        "22 rifle dryfiring by freesound_community (Pixabay)\n" +
        "Impossible horror body drop by imagne (Pixabay)\n" +
        "Footsteps dirt gravel by freesound_community (Pixabay)\n" +
        "Running on dirt road by spinopel (Pixabay)\n" +
        "Park by freesound_community (Pixabay)\n\n" +
        "<b>Characters and animals</b>\n" +
        "Pigeon and Seagull (Quirky Series) by Omabuarts Studio\n" +
        "PSX characters by Elbolilloduro (CC0)\n\n" +
        "<b>Props, plants and effects</b>\n" +
        "Forest Environment Dynamic Nature, foliage and water shaders by NatureManufacture\n" +
        "Low Poly Nature Bundle by LMHPOLY\n" +
        "Recreation park by Tolik_Cola\n" +
        "PSX Mega Pack by Pizza Doggy (the ammo magazine)\n" +
        "Low Poly Guns (the gun)\n" +
        "Toon Muzzleflash Pack by Infima Games\n" +
        "Flat Kit by Dustyroom\n" +
        "Quick Outline by Chris Nolet\n\n" +
        "<b>Animation</b>\n" +
        "Mixamo (Adobe)\n" +
        "Universal Animation Library by Quaternius (CC0)\n\n" +
        "<b>Textures and sky</b>\n" +
        "ambientCG (CC0)\n" +
        "Asphalt 02 by Rob Tuytel, Poly Haven (CC0)\n" +
        "Kloofendal 48d Partly Cloudy (Pure Sky) by Greg Zaal, sky edits by Jarod Guest, Poly Haven (CC0)\n\n" +
        "<b>Store art</b>\n" +
        "Capsule photo by Jonathan Rathgeb on Unsplash\n\n" +
        "<b>Built with</b>\n" +
        "Unity 6, Universal Render Pipeline, Netcode for GameObjects, Starter Assets (Unity)\n" +
        "URP Volumetric Light by Cristian Qiu\n" +
        "HTrace Ambient Occlusion by IPGames";

    // the credits sign, to the right of the menu that opened it (that menu's rect)
    public static void Panel(Rect menu)
    {
        if (!Showing) return;
        line ??= new GUIStyle { font = SubwayGUI.Font, fontSize = 16, richText = true, wordWrap = true, normal = { textColor = SubwayGUI.White } };
        SubwayGUI.Begin(new Rect(menu.xMax + 20, menu.y, 520, 640));
        SubwayGUI.Title("Credits");
        scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(510));
        GUILayout.Label(Text, line);
        GUILayout.EndScrollView();
        if (SubwayGUI.Button("Done")) Showing = false;
        SubwayGUI.End();
    }
}
