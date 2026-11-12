using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace JamzeySt.Carve.Editor
{
    // Undo/Redo for strokes: Unity's undo records a version number; the masks' pixels for each version are kept here.
    public class CarvePaintHistory : ScriptableObject
    {
        [SerializeField] int m_Version;
        static CarvePaintHistory s_Instance;
        static int s_Counter;
        const int MaxStates = 40;
        static readonly Dictionary<Texture2D, SortedList<int, byte[]>> s_States = new Dictionary<Texture2D, SortedList<int, byte[]>>();

        static CarvePaintHistory Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = CreateInstance<CarvePaintHistory>();
                    s_Instance.hideFlags = HideFlags.HideAndDontSave;
                    Undo.undoRedoPerformed += OnUndoRedo;
                }
                return s_Instance;
            }
        }

        internal static void Remember(Texture2D mask)
        {
            if (!s_States.TryGetValue(mask, out var list)) s_States[mask] = list = new SortedList<int, byte[]>();
            if (!list.ContainsKey(Instance.m_Version)) list[Instance.m_Version] = mask.GetPixelData<byte>(0).ToArray();
        }

        internal static void Push(IEnumerable<Texture2D> masks, string name)
        {
            var arr = masks.ToArray();
            if (arr.Length == 0) return;
            int from = Instance.m_Version;
            Undo.RecordObject(Instance, name);
            Instance.m_Version = ++s_Counter;
            foreach (var m in arr)
            {
                var list = s_States[m];
                // A new stroke after an undo drops the redo branch.
                foreach (var k in list.Keys.Where(k => k > from).ToList()) list.Remove(k);
                list[Instance.m_Version] = m.GetPixelData<byte>(0).ToArray();
                while (list.Count > MaxStates) list.RemoveAt(0);
            }
        }

        static void OnUndoRedo()
        {
            if (s_Instance == null) return;
            int v = s_Instance.m_Version;
            foreach (var kv in s_States)
            {
                var mask = kv.Key;
                if (mask == null) continue;
                int best = int.MinValue;
                foreach (var k in kv.Value.Keys) if (k <= v && k > best) best = k;
                if (best == int.MinValue) continue;
                var data = kv.Value[best];
                var cur = mask.GetPixelData<byte>(0);
                if (cur.Length == data.Length && cur.SequenceEqual(data)) continue;
                mask.SetPixelData(data, 0);
                mask.Apply(true, false);
                EditorUtility.SetDirty(mask);
                CarveLayerPainter.Reload(mask);
            }
            SceneView.RepaintAll();
        }
    }
}
