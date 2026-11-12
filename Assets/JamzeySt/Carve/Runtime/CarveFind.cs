using UnityEngine;

namespace JamzeySt.Carve
{
    // FindObjectsByType without sort mode is 6.5+; older versions need FindObjectsSortMode.None.
    public static class CarveFind
    {
        public static T[] All<T>() where T : Object
        {
#if UNITY_6000_5_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);
#else
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#endif
        }
    }
}
