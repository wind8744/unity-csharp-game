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
        public IEnumerator WaveViewRunsAndSpawnsUnits()
        {
            var go = new GameObject("WaveView");
            var view = go.AddComponent<WaveView>();
            yield return null;
            Assert.IsNotNull(view.Sim);
            view.FastForward(6f);
            yield return null;
            Assert.Greater(view.Sim.Tick, 100);
            Assert.Greater(view.Sim.Units.Count, 10);
            Assert.Greater(go.GetComponentsInChildren<SpriteRenderer>().Length, 10);
            Object.Destroy(go);
        }
    }
}
