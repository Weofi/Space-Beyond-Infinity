using UnityEditor;
using UnityEngine;

namespace Voidward.EditorTools
{
    /// <summary>
    /// Development builds for agents (#65): the same builds as BuildMenu, but with editor logging muted while the
    /// player builds, so a Unity_RunCommand call does not return hundreds of foreign shader warnings. Afterwards
    /// one line is logged: "[Dev] end build &lt;result&gt;".
    /// </summary>
    public static class DevBuild
    {
        public static void WindowsDevQuiet() => Quiet(BuildMenu.BuildWindowsDev);
        public static void MacDevQuiet() => Quiet(BuildMenu.BuildMacDev);

        static void Quiet(System.Action build)
        {
            Debug.Log("[Dev] begin build");
            var logger = Debug.unityLogger;
            bool was = logger.logEnabled;
            var types = new[] { LogType.Log, LogType.Warning, LogType.Error, LogType.Assert, LogType.Exception };
            var traces = new StackTraceLogType[types.Length];
            for (int i = 0; i < types.Length; i++) { traces[i] = Application.GetStackTraceLogType(types[i]); Application.SetStackTraceLogType(types[i], StackTraceLogType.None); }
            string result = "ok";
            try
            {
                logger.logEnabled = false;
                build();
            }
            catch (System.Exception e) { result = "FAILED: " + e.Message; }
            finally
            {
                logger.logEnabled = was;
                for (int i = 0; i < types.Length; i++) Application.SetStackTraceLogType(types[i], traces[i]);
            }
            Debug.Log("[Dev] end build " + result + " (see the [Voidward] Build line in the editor log)");
        }
    }
}
