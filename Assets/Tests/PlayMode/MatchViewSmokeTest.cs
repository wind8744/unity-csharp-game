using System.Collections;
using LaneBattle.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LaneBattle.PlayTests
{
    public class MatchViewSmokeTest
    {
        [UnityTest]
        public IEnumerator MatchViewRunsWithBotsOnBothSides()
        {
            var go = new GameObject("MatchView");
            var view = go.AddComponent<MatchView>();
            view.BotPlaysHuman = true;
            yield return null;
            Assert.IsNotNull(view.Sim);
            view.Draw(); view.FastForward(1f);
            view.FastForward(90f);
            yield return null;
            Assert.Greater(view.Sim.Tick, 1800);
            Assert.Greater(view.Sim.OwnLane(0).Towers.Count + view.Sim.OwnLane(1).Towers.Count, 0);
            Assert.Greater(go.GetComponentsInChildren<SpriteRenderer>().Length, 20);
            Object.Destroy(go);
        }
    }
}
