using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace JamzeySt.Carve.Editor.HDRP
{
    // HDRP inspector for Carve Lit: keeps HDRP's Surface Options and Advanced blocks, replaces the raw property list.
    public class CarveMaterialGUI : LightingShaderGraphGUI
    {
        public CarveMaterialGUI()
        {
            uiBlocks.RemoveAll(b => b is TessellationOptionsUIBlock);
            int i = uiBlocks.FindIndex(b => b is ShaderGraphUIBlock);
            if (i >= 0) uiBlocks[i] = new CarveBlock();
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);
            CarveHDRPSetup.Fix(material);
        }

        public override void ValidateMaterial(Material material)
        {
            CarveHDRPSetup.SyncClip(material);
            base.ValidateMaterial(material);
            CarveLayers.SyncKeyword(material);
        }

        class CarveBlock : MaterialUIBlock
        {
            public override void LoadMaterialProperties() { }

            public override void OnGUI() => CarveMaterialDrawer.Draw(materialEditor, properties);
        }
    }
}
