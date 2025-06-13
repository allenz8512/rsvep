using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace nz.alle.rsvep
{
    [InitializeOnLoad]
    internal static class RsvepHook
    {
        internal static readonly bool PrintDebug = false;

        private static RsvepSettings s_RsvepSettings;

        private static PlayModeStateChange s_CurrentPlaymodeState;

        private static Dictionary<int, SerializedObject> s_BackupData = new();

        /// <summary>
        /// 判断Unity Editor是否在进入Playmode前Reload Domain
        /// </summary>
        private static bool DoesReloadDomain
        {
            get
            {
                if (EditorSettings.enterPlayModeOptionsEnabled)
                {
                    return EditorSettings.enterPlayModeOptions == EnterPlayModeOptions.None
                           || EditorSettings.enterPlayModeOptions
                           == EnterPlayModeOptions.DisableSceneReload;
                }
                else
                {
                    return true;
                }
            }
        }


        static RsvepHook()
        {
            // 加载或新建设置
            CreateOrLoadSettings();
            // 监听Playmode切换
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            // 在ScriptableObject资产的Inspector中显示重置按钮
            UnityEditor.Editor.finishedDefaultHeaderGUI -= DisplayResetOnExitPlaymodeToggle;
            UnityEditor.Editor.finishedDefaultHeaderGUI += DisplayResetOnExitPlaymodeToggle;
        }

        private static void CreateOrLoadSettings()
        {
            if (s_RsvepSettings == null)
            {
                if (RsvepSettings.SettingsFileExists())
                {
                    s_RsvepSettings = RsvepSettings.Load();
                    if (s_RsvepSettings != null)
                    {
                        if (PrintDebug)
                        {
                            Debug.Log("Load settings successful.");
                        }
                        return;
                    }
                }

                s_RsvepSettings = new RsvepSettings();
                RsvepSettings.Save(s_RsvepSettings);
                Debug.Log("Create settings successful.");
            }
        }

        /// <summary>
        /// Playmode状态改变时被调用
        /// </summary>
        /// <param name="state">当前Playmode状态</param>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (PrintDebug)
            {
                Debug.Log($"Playmode change to {state}");
            }
            s_CurrentPlaymodeState = state;

            switch (state)
            {
                case PlayModeStateChange.EnteredEditMode:
                    RevertScriptableObjectsValues();
                    break;
                case PlayModeStateChange.ExitingEditMode:
                    if (PrintDebug)
                    {
                        Debug.Log($"DoesReloadDomain: {DoesReloadDomain}");
                    }
                    if (!DoesReloadDomain)
                    {
                        BackupScriptableObjectsValues();
                    }
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.ExitingPlayMode:
                default:
                    break;
            }
        }

        /// <summary>
        /// 在Assembly Reload后被调用
        /// </summary>
        private static void OnAfterAssemblyReload()
        {
            if (PrintDebug)
            {
                Debug.Log("OnAfterAssemblyReload");
            }
            // 这里不能使用s_CurrentPlaymodeState判断当前Playmode状态，因为值在编译后会被清空
            if (EditorApplication.isPlayingOrWillChangePlaymode && DoesReloadDomain)
            {
                BackupScriptableObjectsValues();
            }
        }

        /// <summary>
        /// 在ScriptableObject资产的Inspector中显示重置按钮
        /// </summary>
        /// <param name="editor">当前Inspector的Editor</param>
        private static void DisplayResetOnExitPlaymodeToggle(UnityEditor.Editor editor)
        {
            if (editor.target is ScriptableObject scriptable)
            {
                CreateOrLoadSettings();

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.FlexibleSpace();

                    int instanceId = scriptable.GetInstanceID();
                    bool toRevert = s_RsvepSettings.AssetToResetInstanceIds.Contains(instanceId);

                    if (s_CurrentPlaymodeState == PlayModeStateChange.EnteredEditMode
                        && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        if (AssetDatabase.IsSubAsset(instanceId))
                        {
                            string assetPath = AssetDatabase.GetAssetPath(instanceId);
                            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath) as ScriptableObject;
                            if (mainAsset && s_RsvepSettings.AssetToResetInstanceIds.Contains(mainAsset.GetInstanceID()))
                            {
                                EditorGUILayout.LabelField("Revert values when exit playmode");
                                goto EndHorizontal;
                            }
                        }
                        
                        EditorGUI.BeginChangeCheck();
                        bool newReset = EditorGUILayout.Toggle(
                            "Revert values when exit playmode",
                            toRevert
                        );
                        if (EditorGUI.EndChangeCheck() && newReset != toRevert)
                        {
                            if (newReset)
                            {
                                s_RsvepSettings.AssetToResetInstanceIds.Add(instanceId);
                                if (PrintDebug)
                                {
                                    Debug.Log(
                                        $"Instance '{instanceId}' set to revert when exit playmode"
                                    );
                                }
                            }
                            else
                            {
                                s_RsvepSettings.AssetToResetInstanceIds.Remove(instanceId);
                                if (PrintDebug)
                                {
                                    Debug.Log(
                                        $"Instance '{instanceId}' set to not revert when exit playmode"
                                    );
                                }
                            }
                            RsvepSettings.Save(s_RsvepSettings);
                        }
                    }
                    else
                    {
                        if (s_BackupData.ContainsKey(instanceId))
                        {
                            EditorGUILayout.PrefixLabel("Values will revert when exit playmode");
                            if (GUILayout.Button("Cancel"))
                            {
                                s_BackupData.Remove(instanceId);
                            }
                        }
                    }
                    EndHorizontal: ;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 保存ScriptableObject的值
        /// </summary>
        private static void BackupScriptableObjectsValues()
        {
            if (PrintDebug)
            {
                Debug.Log("BackupScriptableObjectsValues");
            }

            s_BackupData.Clear();

            CreateOrLoadSettings();
            if (s_RsvepSettings.AssetToResetInstanceIds.Count == 0)
            {
                return;
            }

            foreach (int instanceId in s_RsvepSettings.AssetToResetInstanceIds)
            {
                string assetPath = AssetDatabase.GetAssetPath(instanceId);
                if (string.IsNullOrEmpty(assetPath))
                {
                    continue;
                }
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (Object asset in assets)
                {
                    if (asset is ScriptableObject scriptable)
                    {
                        SerializedObject dataObj = new SerializedObject(scriptable);
                        dataObj.Update();
                        int instanceID = asset.GetInstanceID();
                        s_BackupData.Add(instanceID, dataObj);
                        if (PrintDebug)
                        {
                            Debug.Log($"Instance '{instanceID}' data has been backup.");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 恢复ScriptableObject的值
        /// </summary>
        private static void RevertScriptableObjectsValues()
        {
            if (PrintDebug)
            {
                Debug.Log("RestoreScriptableObjectsValues");
            }

            if (s_BackupData.Count == 0)
            {
                return;
            }

            foreach ((int instanceId, SerializedObject dataObj) in s_BackupData)
            {
                ScriptableObject asset =
                    EditorUtility.InstanceIDToObject(instanceId) as ScriptableObject;
                if (asset)
                {
                    SerializedObject scriptableSerialObj = new SerializedObject(asset);
                    SerializedProperty dataProp = dataObj.GetIterator();
                    while (dataProp.NextVisible(true))
                    {
                        SerializedProperty scriptableProp =
                            scriptableSerialObj.FindProperty(dataProp.propertyPath);
                        if (scriptableProp != null)
                        {
                            scriptableSerialObj.CopyFromSerializedProperty(dataProp);
                        }
                    }
                    scriptableSerialObj.ApplyModifiedProperties();
                    if (PrintDebug)
                    {
                        Debug.Log($"Instance '{instanceId}' data has been revert.");
                    }
                }
            }

            s_BackupData.Clear();

            EditorApplication.RepaintHierarchyWindow();
        }
    }
}