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
        public IEnumerator UiBuildsAndOneTurnPlaysThrough()
        {
            var go = new GameObject("PrototypeUI");
            var ui = go.AddComponent<PrototypeUI>();
            yield return null;

            Assert.IsNotNull(ui.Match);
            Assert.AreEqual(1, ui.Match.Engine.State.Turn);
            Assert.Greater(go.GetComponentsInChildren<Button>(true).Length, 5, "버튼이 만들어져야 한다");

            // 손패에서 1코스트 카드를 골라 탑에 배치 예정 → 확정
            var hand = ui.Match.AvailableHand();
            int idx = -1;
            for (int i = 0; i < hand.Count; i++) if (Catalog.Unit(hand[i]).Cost == 1) { idx = i; break; }
            if (idx >= 0)
            {
                ui.Match.SelectCard(idx);
                Assert.IsTrue(ui.Match.PlaceSelected(Lane.Top));
                Assert.AreEqual(1, ui.Match.Pending.Count);
            }
            Assert.IsTrue(ui.Match.Confirm());
            yield return null;

            Assert.AreEqual(2, ui.Match.Engine.State.Turn);
            if (idx >= 0) Assert.AreEqual(1, ui.Match.Engine.LaneUnits(0, Lane.Top).Count);

            // 2턴: 증강 선택 전엔 확정 불가, 선택 후 가능
            Assert.IsTrue(ui.Match.NeedsAugmentChoice);
            Assert.IsFalse(ui.Match.Confirm());
            ui.Match.ChooseAugment(0);
            Assert.IsTrue(ui.Match.Confirm());
            Assert.AreEqual(3, ui.Match.Engine.State.Turn);

            // 끝까지 진행해도 예외 없이 종료
            int guard = 0;
            while (!ui.Match.IsOver && guard++ < 20)
            {
                if (ui.Match.NeedsAugmentChoice) ui.Match.ChooseAugment(0);
                ui.Match.Confirm();
                yield return null;
            }
            Assert.IsTrue(ui.Match.IsOver);
            Object.Destroy(go);
        }
    }
}
