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
            go.SetActive(false);
            var view = go.AddComponent<MatchView>();
            view.BotPlaysHuman = true;
            go.SetActive(true);
            yield return null;
            Assert.IsNotNull(view.Sim);
            view.FastForward(90f);
            yield return null;
            Assert.Greater(view.Sim.Tick, 1700);
            view.FastForward(120f);   // 8분 판이라 너무 오래 돌리면 기지 파괴로 끝날 수 있다
            yield return null;
            Assert.IsFalse(view.Sim.IsOver);
            int alive0 = 0, alive1 = 0;
            foreach (var t in view.Sim.OwnLane(0).Towers) if (t.Alive) alive0++;
            foreach (var t in view.Sim.OwnLane(1).Towers) if (t.Alive) alive1++;
            Assert.AreEqual(alive0, view.MyLaneRenderer.TowerVisualCount, "판매·합성·합치기로 사라진 타워 그림이 남지 않는다");
            Assert.AreEqual(alive1, view.EnemyLaneRenderer.TowerVisualCount);
            // 판매를 명령으로 넣어 그림이 지워지는지 직접 확인
            var mine = view.Sim.OwnLane(0);
            LaneBattle.Core.Wave.Tower victim = null;
            foreach (var t in mine.Towers) if (t.Alive && t.Owner == 0) { victim = t; break; }
            if (victim != null)
            {
                view.BotPlaysHuman = false;
                view.Sell(victim.Id);
                view.FastForward(0.1f);
                yield return null;
                Assert.IsNull(mine.TowerAt(victim.Id));
                int aliveNow = 0; foreach (var t in mine.Towers) if (t.Alive) aliveNow++;
                Assert.AreEqual(aliveNow, view.MyLaneRenderer.TowerVisualCount, "판매한 타워 그림이 사라진다");
            }
            Assert.Greater(view.Sim.OwnLane(0).Towers.Count + view.Sim.OwnLane(1).Towers.Count, 0);
            Assert.Greater(go.GetComponentsInChildren<SpriteRenderer>().Length, 50);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator TeamMatchWithBotAlliesRuns()
        {
            var go = new GameObject("MatchView");
            go.SetActive(false);
            var view = go.AddComponent<MatchView>();
            view.BotPlaysHuman = true; view.PlayersPerTeam = 3;
            go.SetActive(true);
            yield return null;
            view.FastForward(60f);
            yield return null;
            Assert.AreEqual(3, view.Sim.Cfg.PlayersPerTeam);
            Assert.Greater(view.Sim.OwnLane(0).Towers.Count, 2);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator TitleScreenBuildsAndStartsMatch()
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            yield return null;
            Assert.IsNotNull(Object.FindFirstObjectByType<TitleScreen>());
            flow.StartMatch(2);
            yield return null;
            var view = Object.FindFirstObjectByType<MatchView>();
            Assert.IsNotNull(view);
            Assert.AreEqual(2, view.PlayersPerTeam);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator OnlineLobbyAndHostMatchRunOverLoopback()
        {
            var go = new GameObject("GameFlow");
            var flow = go.AddComponent<GameFlow>();
            yield return null;
            flow.ShowOnline();
            yield return null;
            Assert.IsNotNull(Object.FindFirstObjectByType<OnlineLobby>());
            // 루프백으로 호스트+클라 세션을 만들고 호스트 쪽 경기 화면을 띄운다
            var ht = new LaneBattle.Core.Net.LoopbackTransport(); var ct = new LaneBattle.Core.Net.LoopbackTransport();
            var host = new LaneBattle.Core.Net.NetSession(ht, true, "h"); var client = new LaneBattle.Core.Net.NetSession(ct, false, "c");
            LaneBattle.Core.Net.LoopbackTransport.Connect(ht, ct);
            client.Poll(); host.Poll(); client.Poll();
            host.SetPlayersPerTeam(2); client.Poll();
            host.StartMatch(5); client.Poll();
            flow.StartOnlineMatch(host);
            yield return null;
            var view = Object.FindFirstObjectByType<MatchView>();
            Assert.IsNotNull(view);
            Assert.AreEqual(2, view.PlayersPerTeam);
            for (int i = 0; i < 40; i++) yield return null;
            Assert.Greater(view.Sim.Tick, 5, "호스트는 스스로 턴을 만들어 진행한다");
            var cs = client.CreateSim();
            int guard = 0;
            while (cs.Tick < view.Sim.Tick && guard++ < 10000) { client.Poll(); if (!client.TryStep()) break; }
            Assert.AreEqual(view.Sim.Tick, cs.Tick);
            Assert.AreEqual(view.Sim.Hash(), cs.Hash(), "클라는 받은 턴만으로 같은 상태가 된다");
            Object.Destroy(go);
            client.Dispose();
        }

        [Test]
        public void EverySpriteAndSoundTheGameUsesExists()
        {
            foreach (var d in LaneBattle.Core.Wave.WaveCatalog.Towers)
                for (int f = 0; f < 2; f++) Assert.IsTrue(Art.Has($"tower_{d.Id}_{f}"), $"tower_{d.Id}_{f}");
            foreach (var d in LaneBattle.Core.Wave.WaveCatalog.Towers) Assert.IsTrue(Art.Has($"tower_{d.Id}_atk"), $"tower_{d.Id}_atk");
            foreach (var d in LaneBattle.Core.Wave.WaveCatalog.Attackers)
                for (int f = 0; f < 2; f++) Assert.IsTrue(Art.Has($"creep_{d.Id}_{f}"), $"creep_{d.Id}_{f}");
            foreach (var n in new[] { "tile_grass_0", "tile_path", "tile_slot", "tile_slot_hover", "base", "gate", "logo", "title_bg", "ui_panel", "ui_panel_dark", "ui_button", "ui_card", "ui_card_rare", "ui_card_hero", "ui_slot", "icon_coin", "icon_heart", "star_2", "star_3", "fx_shadow", "proj_arrow", "fx_hit_0", "fx_boom_3", "fx_star_3", "fx_smoke_2", "aug_resource" })
                Assert.IsTrue(Art.Has(n), n);
            foreach (var n in new[] { "shoot_arrow", "shoot_cannon", "hit", "death_small", "leak", "build", "merge", "fuse", "draw", "send", "win", "lose", "bgm_title", "bgm_battle", "bgm_tension", "click" })
                Assert.IsNotNull(Resources.Load<AudioClip>("Audio/" + n), n);
        }
    }
}
