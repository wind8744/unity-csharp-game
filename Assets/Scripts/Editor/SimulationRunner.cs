using System.IO;
using LaneBattle.Core;
using UnityEditor;
using UnityEngine;

namespace LaneBattle.Editor
{
    /// <summary>
    /// 메뉴 또는 배치 모드(-executeMethod LaneBattle.Editor.SimulationRunner.RunBatch)로 시뮬레이션을 돌려
    /// Docs/design/sim-latest.md 에 결과를 쓴다.
    /// </summary>
    public static class SimulationRunner
    {
        [MenuItem("LaneBattle/Run Simulation (1000 games)")]
        public static void RunBatch()
        {
            string md = SimulationReport.Build(1000, 1);
            string path = Path.Combine(Application.dataPath, "..", "Docs", "design", "sim-latest.md");
            File.WriteAllText(path, md);
            Debug.Log(md);
            Debug.Log($"시뮬레이션 결과 저장: {path}");
        }
    }
}
