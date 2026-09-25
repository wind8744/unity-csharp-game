using System.Collections.Generic;
using LaneBattle.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaneBattle.Editor
{
    /// <summary>Prototype.unity 를 코드로 만든다. 씬 파일을 손으로 안 만져도 되게.</summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Prototype.unity";

        [MenuItem("LaneBattle/Build Prototype Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            var c = cam.GetComponent<Camera>();
            c.orthographic = true;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.12f, 0.13f, 0.16f);
            cam.transform.position = new Vector3(0, 0, -10);

            var ui = new GameObject("PrototypeUI", typeof(PrototypeUI));
            ui.GetComponent<PrototypeUI>().StartSeed = 1;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var s in EditorBuildSettings.scenes) if (s.path != ScenePath) scenes.Add(s);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"씬 생성: {ScenePath}");
        }
    }
}
