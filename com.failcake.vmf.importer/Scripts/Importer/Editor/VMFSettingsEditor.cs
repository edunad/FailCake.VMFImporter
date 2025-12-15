#region

#if UNITY_EDITOR
using UnityEditor;
#endif

#endregion

namespace FailCake.VMF
{
    #if UNITY_EDITOR
    [CustomEditor(typeof(VMFSettings))]
    public class VMFSettingsEditor : Editor
    {
        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            /*if (GUILayout.Button("Reload VPKs"))
            {
                VMFImporter.LoadSettings();
                Debug.Log("VPK files reloaded");
            }*/
        }
    }
    #endif
}