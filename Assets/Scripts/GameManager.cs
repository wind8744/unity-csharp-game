using UnityEngine;

/// <summary>
/// 게임 전체 상태를 관리하는 싱글턴. 씬 전환 시에도 유지됩니다.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public int Score { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void AddScore(int amount)
    {
        Score += amount;
        Debug.Log($"Score: {Score}");
    }

    public void ResetScore()
    {
        Score = 0;
    }
}
