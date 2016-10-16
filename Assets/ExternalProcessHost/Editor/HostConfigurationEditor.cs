//If you do not want a user accessible inspector for the HostConfiguration
//comment out this #define. You may want to do this if you want to configure
//the HostConfiguration entirely in code.
#define EnableHostConfigurationEditor

//If you do not want the configuration to be saved (as a resource) comment out
//this #define. You may want to do this if you want to persist the
//configuration in some other form of storage.
#define EnableHostConfigurationSerialization

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace ExternalProcessHost
{
    #region config editor
    #if EnableHostConfigurationEditor
    [CustomEditor(typeof(HostConfiguration))]
    public class HostConfigurationEditor
        : Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (HostConfiguration)target;

            DrawStartupConfig(config);
            DrawPathConfig(config);

            var startEnable = HostConfiguration.Instance.StartupMode != StartupMode.Never;
            using (new EditorGUI.DisabledGroupScope(!startEnable))
            {
                if (GUILayout.Button("Start Now"))
                    HostConnection.Instance.Start();
            }

            if (GUI.changed)
                EditorUtility.SetDirty(config);
        }

        private void DrawStartupConfig(HostConfiguration config)
        {
            config.StartupMode = (StartupMode)EditorGUILayout.EnumPopup("Startup Mode", config.StartupMode);
        }

        private void DrawPathConfig(HostConfiguration config)
        {
            using (new EditorGUI.DisabledGroupScope(config.StartupMode == StartupMode.Never))
            {
                config.ExecutablePath = EditorGUILayout.DelayedTextField("Executable Path", config.ExecutablePath);
            }
        }

        public static void GoToSettings()
        {
            var config = HostConfiguration.Load();
            EditorApplication.delayCall += () => {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = config;
            };
        }
    }
#endif
    #endregion

    #region host configuration
#if EnableHostConfigurationSerialization
    [Serializable]
#endif
    public class HostConfiguration
#if EnableHostConfigurationSerialization
        : ScriptableObject
#endif
    {
        public static readonly string SettingsFileResourceName = "HostConfiguration";
        public static readonly string SettingsFilePath = "Assets/ExternalProcessHost/Resources/" + SettingsFileResourceName + ".asset";

        private static HostConfiguration _instance;
        public static HostConfiguration Instance
        {
            get { return _instance ?? (_instance = Load()); }
        }

#if EnableHostConfigurationSerialization
        [SerializeField]
#endif
        private StartupMode _startup;
        public StartupMode StartupMode
        {
            get { return _startup; }
            set
            {
                if (_startup != value)
                {
                    _startup = value;
                    EditorUtility.SetDirty(this);
                }
            }
        }

#if EnableHostConfigurationSerialization
        [SerializeField]
#endif
        private string _executablePath;
        public string ExecutablePath
        {
            get { return _executablePath; }
            set
            {
                if (_executablePath != value)
                {
                    _executablePath = value;
                    EditorUtility.SetDirty(this);
                }
            }
        }

        public static HostConfiguration Load()
        {
#if EnableHostConfigurationSerialization
            var asset = Resources.Load<HostConfiguration>(SettingsFileResourceName);
            if (asset == null)
            {
                asset = CreateInstance<HostConfiguration>();
                AssetDatabase.CreateAsset(asset, SettingsFilePath);
                AssetDatabase.SaveAssets();
            }

            _instance = asset;
            return asset;
        }
#else
        return new HostConfiguration();
#endif
    }

    public enum StartupMode
    {
        /// <summary>
        /// Automatically start the external process as soon as the editor launches
        /// </summary>
        Automatic,

        /// <summary>
        /// External process will be launched when it is required
        /// </summary>
        /// <remarks>
        /// May make the first call out to the external process quite slow
        /// </remarks>
        Delayed,

        /// <summary>
        /// Do not start the external process
        /// </summary>
        Never
    }
    #endregion

    #region host connection
    [InitializeOnLoad]
    public class HostConnection
    {
        #region singleton
        private static readonly HostConnection _instance;

        public static HostConnection Instance
        {
            get { return _instance; }
        }

        static HostConnection()
        {
            _instance = new HostConnection();
        }
        #endregion

        #region fields and properties
        private readonly object _startLock = new object();

        private Thread _thread;
        private bool _running = true;

        private Process _remoteProcess;

        private HostConfiguration _config;

        public bool IsRunning
        {
            get { return _remoteProcess != null && !_remoteProcess.HasExited; }
        }
        #endregion

        #region constructors
        public HostConnection()
        {
            EditorApplication.update += Update;
        }
        #endregion

        private void Update()
        {
            _config = HostConfiguration.Instance;

            if ((_thread == null || !_thread.IsAlive) && _running)
            {
                _thread = new Thread(ThreadEntry);
                _thread.Start();
            }
        }

        private void ThreadEntry()
        {
            while (_running)
            {
                if (!IsRunning && (_config.StartupMode == StartupMode.Automatic))
                {
                    //Process if not running, start it automatically
                    Start();
                }

                Thread.Sleep(8);
            }
        }

        public bool Start()
        {
            if (IsRunning)
                return true;

            if (!Monitor.TryEnter(_startLock))
                return false;

            try
            {
                _remoteProcess = null;

                if (string.IsNullOrEmpty(_config.ExecutablePath) || !File.Exists(_config.ExecutablePath))
                    return false;

                //Start the process
                try
                {
                    _remoteProcess = new Process {
                        StartInfo = new ProcessStartInfo {
                            //WindowStyle = ProcessWindowStyle.Hidden,
                            //CreateNoWindow = true,
                            UseShellExecute = false,
                            FileName = _config.ExecutablePath,
                            Arguments = string.Format("parent:{0}", Process.GetCurrentProcess().Id),
                        },
                        EnableRaisingEvents = true
                    };
                    _remoteProcess.Start();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError("Failed to start remote process");
                    UnityEngine.Debug.LogError(e);
                    _remoteProcess = null;

                    throw;
                }
            }
            finally
            {
                Monitor.Exit(_startLock);
            }

            return IsRunning;
        }
    }
    #endregion
}
