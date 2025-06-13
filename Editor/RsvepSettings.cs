using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace nz.alle.rsvep
{
    /// <summary>
    /// Settings for Rsvep
    /// </summary>
    [Serializable]
    public class RsvepSettings : ISerializationCallbackReceiver
    {
        public HashSet<int> AssetToResetInstanceIds = new();

        [SerializeField]
        private List<string> m_ScriptableAssetGlobalIds = new List<string>();

        public void OnBeforeSerialize()
        {
            if (RsvepHook.PrintDebug)
            {
                Debug.Log("RsvepSettings OnBeforeSerialize");
            }
            m_ScriptableAssetGlobalIds.Clear();
            foreach (int instanceId in AssetToResetInstanceIds)
            {
                GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(instanceId);
                if (globalId.identifierType != 0)
                {
                    m_ScriptableAssetGlobalIds.Add(globalId.ToString());
                }
            }
        }

        public void OnAfterDeserialize()
        {
            if (RsvepHook.PrintDebug)
            {
                Debug.Log("RsvepSettings OnAfterDeserialize");
            }
            AssetToResetInstanceIds.Clear();
            foreach (string globalIdString in m_ScriptableAssetGlobalIds)
            {
                if (GlobalObjectId.TryParse(globalIdString, out GlobalObjectId globalId)
                    && globalId.identifierType != 0)
                {
                    int instanceId = GlobalObjectId.GlobalObjectIdentifierToInstanceIDSlow(globalId);
                    if (instanceId != 0)
                    {
                        AssetToResetInstanceIds.Add(instanceId);
                    }
                }
            }
            m_ScriptableAssetGlobalIds.Clear();
        }

        /// <summary>
        /// Path to settings file
        /// </summary>
        private const string SettingFilepath = "Assets/Editor Default Resources/RsvepSettings.asset";

        /// <summary>
        /// Check if settings file exists
        /// </summary>
        /// <returns>True if settings file exists</returns>
        internal static bool SettingsFileExists()
        {
            string filepath = Path.Combine(Application.dataPath[..^6], SettingFilepath);
            return File.Exists(filepath);
        }

        /// <summary>
        /// Load settings
        /// </summary>
        /// <returns>Settings</returns>
        internal static RsvepSettings Load()
        {
            RsvepSettings settings = null;
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SettingFilepath);
            if (asset && !string.IsNullOrEmpty(asset.text))
            {
                try
                {
                    settings = JsonUtility.FromJson<RsvepSettings>(asset.text);
                    if (RsvepHook.PrintDebug)
                    {
                        Debug.Log("Rsvep settings file loaded.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return settings;
        }

        /// <summary>
        /// Save settings
        /// </summary>
        /// <param name="settings">Settings</param>
        internal static void Save(RsvepSettings settings)
        {
            string settingsText = JsonUtility.ToJson(settings);
            TextAsset asset = new TextAsset(settingsText);
            if (!AssetDatabase.IsValidFolder("Assets/Editor Default Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Editor Default Resources");
            }
            AssetDatabase.CreateAsset(asset, SettingFilepath);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            if (RsvepHook.PrintDebug)
            {
                Debug.Log("Rsvep settings file saved.");
            }
        }
    }
}