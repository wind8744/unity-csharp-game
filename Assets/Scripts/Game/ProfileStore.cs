using System.IO;
using LaneBattle.Core.Meta;
using UnityEngine;

namespace LaneBattle.Game
{
    /// <summary>프로필(전적·해금)을 persistentDataPath/profile.txt 에 저장한다.</summary>
    public static class ProfileStore
    {
        static Profile _current;
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "profile.txt");

        public static Profile Current
        {
            get
            {
                if (_current != null) return _current;
                try { _current = File.Exists(Path) ? Profile.Parse(File.ReadAllText(Path)) : new Profile(); }
                catch (System.Exception e) { Debug.LogWarning("프로필 읽기 실패: " + e.Message); _current = new Profile(); }
                return _current;
            }
        }

        public static void Save()
        {
            try { File.WriteAllText(Path, Current.Serialize()); }
            catch (System.Exception e) { Debug.LogWarning("프로필 저장 실패: " + e.Message); }
        }

        /// <summary>테스트·스크린샷용: 저장 없이 메모리 프로필로 바꿔치기.</summary>
        public static void UseTransient(Profile p) { _current = p; }
    }
}
