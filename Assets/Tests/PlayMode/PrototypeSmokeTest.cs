using System.Collections;
using LaneBattle.Core;
using LaneBattle.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LaneBattle.PlayTests
{
    public class PrototypeSmokeTest
    {
        [UnityTest]
        public IEnumerator UiBuildsAndMatchPlaysThroughPhases()
        {
            var go = new GameObject("PrototypeUI");
            var ui = go.AddComponent<PrototypeUI>();
            yield return null;

            var m = ui.Match;
            Assert.AreEqual(1, m.Engine.State.Turn);
            Assert.AreEqual(MatchPhase.Plan, m.Phase);
            Assert.Greater(go.GetComponentsInChildren<Button>(true).Length, 5);

            var hand = m.AvailableHand();
            int idx = -1;
            for (int i = 0; i < hand.Count; i++) if (Catalog.Unit(hand[i]).Cost == 1) { idx = i; break; }
            if (idx >= 0) { m.SelectCard(idx); Assert.IsTrue(m.PlaceSelected(Lane.Top)); }

            Assert.IsTrue(m.Confirm());
            Assert.AreEqual(MatchPhase.Reveal, m.Phase);
            Assert.AreEqual(1, m.Report.Turn);
            Assert.AreEqual(1, m.EnemyHistory.Count);
            yield return null;
            m.Advance();
            Assert.AreEqual(MatchPhase.Combat, m.Phase);
            yield return null;
            m.Advance();
            Assert.AreEqual(MatchPhase.Plan, m.Phase);
            Assert.AreEqual(2, m.Engine.State.Turn);
            if (idx >= 0) Assert.AreEqual(1, m.Engine.LaneUnits(0, Lane.Top).Count);

            Assert.IsTrue(m.NeedsAugmentChoice);
            Assert.IsFalse(m.Confirm());
            m.ChooseAugment(0);
            Assert.IsTrue(m.Confirm());

            int guard = 0;
            while (!m.IsOver && guard++ < 30)
            {
                if (m.Phase != MatchPhase.Plan) { m.Advance(); continue; }
                if (m.NeedsAugmentChoice) m.ChooseAugment(0);
                m.Confirm();
                yield return null;
            }
            Assert.IsTrue(m.IsOver);
            Object.Destroy(go);
        }
    }
}
