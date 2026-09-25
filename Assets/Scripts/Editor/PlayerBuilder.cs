using System.IO;
using UnityEditor;
using UnityEngine;

namespace LaneBattle.Editor
{
    public static class PlayerBuilder
    {
        [MenuItem("LaneBattle/Build macOS Player")]
        public static void BuildMac()
        {
            string dir = Path.Combine(Application.dataPath, "..", "Builds", "mac");
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = Path.Combine(dir, "LaneBattlePrototype.app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"빌드 결과: {report.summary.result}, {report.summary.totalSize / 1024 / 1024} MB, 오류 {report.summary.totalErrors}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
