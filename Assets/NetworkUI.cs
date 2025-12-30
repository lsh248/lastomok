using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public static NetworkUI Instance;

    private string joinCodeInput = "";
    private string currentJoinCode = "";
    private string alertMessage = "";

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            GUILayout.BeginArea(new Rect(20, 20, 250, 250), GUI.skin.box);
            GUILayout.Label("<b>[ 함정 오목 ]</b>");

            if (GUILayout.Button("방 만들기 (Host)", GUILayout.Height(40))) CreateGame();

            GUILayout.Space(20);
            GUILayout.Label("방 코드 입력:");
            joinCodeInput = GUILayout.TextField(joinCodeInput);
            if (GUILayout.Button("방 참가하기 (Client)", GUILayout.Height(40))) JoinGame();
            GUILayout.EndArea();
        }
        else
        {
            GUILayout.BeginArea(new Rect(20, 20, 250, 150), GUI.skin.box);
            if (NetworkManager.Singleton.IsHost)
                GUILayout.Label($"<b>방 코드: {currentJoinCode}</b>");

            if (!string.IsNullOrEmpty(alertMessage))
                GUILayout.Label($"<color=yellow>{alertMessage}</color>");

            if (GUILayout.Button("나가기"))
            {
                NetworkManager.Singleton.Shutdown();
                alertMessage = "";
            }
            GUILayout.EndArea();
        }
    }

    async void CreateGame() => currentJoinCode = await RelayManager.Instance.CreateRelay();
    async void JoinGame() => await RelayManager.Instance.JoinRelay(joinCodeInput);
    public void DisplayAlert(string msg) { alertMessage = msg; Invoke(nameof(ClearAlert), 3f); }
    void ClearAlert() => alertMessage = "";
}