using System.Collections.Generic;
using LaneBattle.Core.Meta;
using LaneBattle.Core.Wave;
using NUnit.Framework;

namespace LaneBattle.Tests
{
    public class ProfileTests
    {
        static MatchSummary Win(int leaked = 3) => new MatchSummary { Win = true, PlayersPerTeam = 1, MyBaseHp = 40 - leaked, MyBaseMax = 40, MyLeaked = leaked, MaxStar = 1, Sent = 10 };

        [Test]
        public void FirstWinUnlocksTitleAndFlawlessUnlocksMap()
        {
            var p = new Profile();
            var fresh = p.Apply(Win(0));
            var ids = fresh.ConvertAll(u => u.Id);
            CollectionAssert.Contains(ids, "first_win");
            CollectionAssert.Contains(ids, "flawless");
            Assert.AreEqual("새내기 지휘관", p.Title);
            CollectionAssert.Contains(p.UnlockedMaps(), "굽이 다섯");
            Assert.AreEqual(0, p.Apply(Win(0)).Count, "같은 해금은 두 번 안 열린다");
            Assert.AreEqual(2, p.Matches); Assert.AreEqual(2, p.Wins);
        }

        [Test]
        public void SecretAugmentsNeedTheirUnlocks()
        {
            var p = new Profile();
            Assert.AreEqual(0, p.AllowedSecretAugments().Count);
            p.Apply(new MatchSummary { MaxStar = 3 });
            CollectionAssert.AreEquivalent(new[] { AugmentId.StarBlessing }, p.AllowedSecretAugments());
            for (int i = 0; i < 9; i++) p.Apply(new MatchSummary());
            Assert.IsTrue(p.Has("veteran10"));
        }

        [Test]
        public void ProfileRoundTripsThroughText()
        {
            var p = new Profile();
            p.Apply(new MatchSummary { Win = true, Sent = 70, MissionDone = MissionId.Horde, HiddenMade = new HashSet<int> { 16, 20 } });
            var back = Profile.Parse(p.Serialize());
            CollectionAssert.AreEquivalent(new[] { 16, 20 }, back.HiddenDiscovered);
            Assert.AreEqual(p.Matches, back.Matches); Assert.AreEqual(p.Wins, back.Wins);
            CollectionAssert.AreEquivalent(p.Unlocks, back.Unlocks);
            Assert.IsTrue(back.MissionsDone.Contains(MissionId.Horde));
            Assert.AreEqual(p.Title, back.Title);
            Assert.IsTrue(back.HardBotUnlocked);
        }

        [Test]
        public void SecretAugmentsAreOfferedOnlyWhenAllowed()
        {
            int Count(MatchConfig cfg)
            {
                int seen = 0;
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    var m = new MatchSim(cfg, seed);
                    m.Step();   // 레벨 1 증강
                    foreach (var a in m.Player(0, 0).Offers) if (FunCatalog.IsSecret(a)) seen++;
                }
                return seen;
            }
            Assert.AreEqual(0, Count(new MatchConfig()));
            Assert.Greater(Count(new MatchConfig { AllowedSecretAugments = new HashSet<AugmentId> { AugmentId.StarBlessing, AugmentId.Alchemy, AugmentId.Veteran } }), 0);
        }

        [Test]
        public void VeteranAndAlchemyChangeRules()
        {
            var m = new MatchSim(new MatchConfig { FunLayer = false }, 2);
            var p = m.Player(0, 0);
            p.Augments.Add(AugmentId.Veteran); p.Augments.Add(AugmentId.Alchemy);
            Assert.AreEqual(m.Cfg.DrawCost - 1, p.DrawCost(m.Cfg)); Assert.AreEqual(m.Cfg.HandMax + 1, p.HandMax(m.Cfg));
            m.Step(new List<MatchCommand> { MatchCommand.Build(0, 0, 1, 0, 0), MatchCommand.Build(0, 0, 4, 3, 0) });
            var lane = m.OwnLane(0);
            m.Step(new List<MatchCommand> { MatchCommand.Fuse(0, 0, lane.TowerAtCell(0, 0).Id, lane.TowerAtCell(3, 0).Id) });
            Assert.IsFalse(lane.TowerAtCell(0, 0).Upgraded, "연금술은 ★3 이 될 때만");
            Assert.AreEqual(1, p.FusedKinds.Count);
            // 궁수 9개 → ★3 이 되는 순간 연금술로 강화
            p.Gold = 500;
            for (int i = 0; i < 9; i++) m.Step(new List<MatchCommand> { MatchCommand.Build(0, 0, 1, 4 + i, 0) });
            for (int k = 0; k < 3; k++) m.Step(new List<MatchCommand> { MatchCommand.Merge(0, 0, lane.TowerAtCell(4 + k * 3, 0).Id) });
            m.Step(new List<MatchCommand> { MatchCommand.Merge(0, 0, lane.TowerAtCell(4, 0).Id) });
            var s3 = lane.TowerAtCell(4, 0);
            Assert.AreEqual(3, s3.Star); Assert.IsTrue(s3.Upgraded, "연금술: ★3 즉시 강화");
        }

        [Test]
        public void UnlockMapsExistAndAreValid()
        {
            foreach (var u in Profile.Catalog)
                if (u.Kind == UnlockKind.Map) Assert.IsNotNull(MapCatalog.ByName(u.Payload), u.Payload);
            Assert.Less(MapCatalog.TwoBends.LengthCells, MapCatalog.OneVsOne.LengthCells);
            Assert.Greater(MapCatalog.FiveBends.LengthCells, MapCatalog.OneVsOne.LengthCells);
        }

        [Test]
        public void TutorialAndTipsFlagsRoundTrip()
        {
            var p = new Profile();
            Assert.IsFalse(p.TutorialDone); Assert.IsTrue(p.Tips);
            p.TutorialDone = true; p.Tips = false;
            var q = Profile.Parse(p.Serialize());
            Assert.IsTrue(q.TutorialDone); Assert.IsFalse(q.Tips);
            Assert.IsTrue(Profile.Parse("matches=3\n").Tips, "옛 프로필엔 팁 켬");
        }
    }
}
