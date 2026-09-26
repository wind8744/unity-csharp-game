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
