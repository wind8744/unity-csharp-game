using System.Collections.Generic;
using LaneBattle.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LaneBattle.Editor
{
    /// <summary>Main.unity 를 코드로 만든다. 씬에는 카메라와 GameFlow 하나뿐, 나머지는 실행 중에 코드가 만든다.</summary>
    public static class SceneBuilder
    {
        public const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("LaneBattle/Build Main Scene")]
        public static void BuildMain()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.orthographic = true; c.orthographicSize = 6; c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.55f, 0.78f, 0.95f);
            cam.transform.position = new Vector3(0, 0, -10);
            new GameObject("GameFlow", typeof(GameFlow));
            EditorSceneManager.SaveScene(scene, MainScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScenePath, true) };
            PlayerSettings.productName = "ChokkomiSiege";
            PlayerSettings.companyName = "Chokkomi";
            PlayerSettings.defaultScreenWidth = 1280; PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            Debug.Log($"씬 생성: {MainScenePath}");
        }
    }
}
