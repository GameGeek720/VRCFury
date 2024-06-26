using System.IO;
using System.Linq;
using UnityEditor;
using VF.Updater;

namespace VF {
    internal static class BadInstallDetector {
        [InitializeOnLoadMethod]
        private static void Init() {
            if (!UpdateMenuItem.IsVrcfuryALocalPackage()) return;
            var manifestPath = "Packages/manifest.json";
            if (!File.Exists(manifestPath)) return;
            var manifestContainsVrcfury = File.ReadLines(manifestPath)
                .Any(line => line.Contains("com.vrcfury.vrcfury"));
            if (!manifestContainsVrcfury) return;
            EditorUtility.DisplayDialog(
                "VRCFury",
                "The VRCFury install is partially corrupt. The updater may have broken, or you may have updated " +
                "from an old manual install to a new version using the VCC.\n" +
                "\n" +
                "Please download and import " +
                "https://vrcfury.com/installer to resolve this issue.",
                "Ok"
            );
        }
    }
}
