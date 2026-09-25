using System.Collections;
using LaneBattle.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaneBattle.PlayTests
{
    public class WaveViewSmokeTest
    {
        [UnityTest]
        public IEnumerator WaveViewRunsWavesAndSends()
        {
            var go = new GameObject("WaveView");
            var view = go.AddComponent<WaveView>();
            yield return null;
            Assert.IsNotNull(view.Sim);
            view.EnemySend(0);
            view.FastForward(40f); // 첫 기본 웨이브(30초)까지 지나감
            yield return null;
            Assert.AreEqual(1, view.Sim.NextWaveIndex);
            Assert.Greater(view.Sim.Kills + view.Sim.Leaked, 0);
            Assert.Greater(go.GetComponentsInChildren<SpriteRenderer>().Length, 10);
            Object.Destroy(go);
        }
    }
}
