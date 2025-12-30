using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

public class NetworkGridManager : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject blackPiecePrefab;
    public GameObject whitePiecePrefab;
    public GameObject trapEffectPrefab;
    public GameObject trapVisualPrefab;

    private int[,] board = new int[15, 15];
    private int[,] publicTraps = new int[15, 15];
    private int[,] playerTraps = new int[15, 15];

    private int blackTrapCount = 0;
    private int whiteTrapCount = 0;
    private const int MaxTrapCount = 3;

    private string lastTrapMessage = "";

    // 호스트가 현재 어떤 색상인지 저장하는 변수 (1: 흑돌, 2: 백돌)
    // 흑돌은 무조건 선공(turnPlayer=1)이므로, 이 값에 따라 호스트의 순서가 결정됩니다.
    public NetworkVariable<int> hostColor = new NetworkVariable<int>(1);
    public NetworkVariable<int> turnPlayer = new NetworkVariable<int>(1);
    public NetworkVariable<int> winner = new NetworkVariable<int>(0);

    public override void OnNetworkSpawn()
    {
        if (IsServer) GenerateRandomPublicTraps();
    }

    void GenerateRandomPublicTraps()
    {
        System.Array.Clear(publicTraps, 0, publicTraps.Length);
        int count = 0;
        while (count < 15)
        {
            int x = Random.Range(0, 15);
            int y = Random.Range(0, 15);
            if (publicTraps[x, y] == 0) { publicTraps[x, y] = 3; count++; }
        }
    }

    void Update()
    {
        if (!IsClient || winner.Value != 0) return;

        // 내 돌의 색상 결정 (호스트면 hostColor, 클라이언트면 그 반대)
        int myColor = IsHost ? hostColor.Value : (hostColor.Value == 1 ? 2 : 1);
        if (turnPlayer.Value != myColor) return;

        if (Input.GetMouseButtonDown(0)) HandleInput(false, myColor);
        else if (Input.GetMouseButtonDown(1)) HandleInput(true, myColor);
    }

    void HandleInput(bool isTrapMode, int myColor)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            int x = Mathf.RoundToInt(hit.point.x);
            int y = Mathf.RoundToInt(hit.point.z);

            if (isTrapMode) RequestPlaceTrapRpc(x, y, myColor);
            else RequestPlacePieceRpc(x, y, myColor);
        }
    }

    [Rpc(SendTo.Server)]
    public void RequestPlacePieceRpc(int x, int y, int playerType)
    {
        if (winner.Value != 0 || turnPlayer.Value != playerType) return;
        if (x < 0 || x >= 18 || y < 0 || y >= 18 || board[x, y] != 0) return;

        ClearTrapMessageRpc();

        if (playerTraps[x, y] == playerType)
        {
            playerTraps[x, y] = 0;
            RemoveMyTrapVisualRpc(x, y, playerType);
        }

        int piecesToRemove = 0;
        bool publicTriggered = false;
        bool enemyTriggered = false;

        if (publicTraps[x, y] == 3) { piecesToRemove += 2; publicTraps[x, y] = 0; publicTriggered = true; }
        if (playerTraps[x, y] != 0 && playerTraps[x, y] != playerType) { piecesToRemove += 2; playerTraps[x, y] = 0; enemyTriggered = true; }

        if (piecesToRemove > 0)
        {
            string trapTypeLabel = "";
            if (publicTriggered && enemyTriggered) trapTypeLabel = "랜덤 + 상대방 함정";
            else if (publicTriggered) trapTypeLabel = "랜덤 함정";
            else if (enemyTriggered) trapTypeLabel = "상대방 함정";

            RemoveRandomPieces(piecesToRemove, playerType);
            NotifyTrapTriggeredRpc(x, y, playerType, $"{trapTypeLabel} 발동! 내 돌 {piecesToRemove}개 제거!");
        }
        else
        {
            board[x, y] = playerType;
            SpawnPieceRpc(x, y, playerType);
            if (CheckWinCondition(x, y, playerType)) winner.Value = playerType;
        }
        turnPlayer.Value = (turnPlayer.Value == 1) ? 2 : 1;
    }

    [Rpc(SendTo.Server)]
    public void RequestPlaceTrapRpc(int x, int y, int playerType, RpcParams rpcParams = default)
    {
        if (winner.Value != 0 || turnPlayer.Value != playerType) return;
        if ((playerType == 1 && blackTrapCount >= MaxTrapCount) || (playerType == 2 && whiteTrapCount >= MaxTrapCount)) return;
        if (board[x, y] != 0 || playerTraps[x, y] == playerType) return;

        ClearTrapMessageRpc();
        playerTraps[x, y] = playerType;
        if (playerType == 1) blackTrapCount++; else whiteTrapCount++;

        UpdateTrapUIRpc(blackTrapCount, whiteTrapCount);
        ConfirmTrapPlacementRpc(x, y, RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        turnPlayer.Value = (turnPlayer.Value == 1) ? 2 : 1;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void ConfirmTrapPlacementRpc(int x, int y, RpcParams rpcParams = default)
    {
        if (trapVisualPrefab != null)
        {
            GameObject t = Instantiate(trapVisualPrefab, new Vector3(x, 0.05f, y), Quaternion.identity);
            t.tag = "TrapVisual";
        }
    }

    [Rpc(SendTo.Everyone)]
    void RemoveMyTrapVisualRpc(int x, int y, int playerType)
    {
        int myColor = IsHost ? hostColor.Value : (hostColor.Value == 1 ? 2 : 1);
        if (myColor == playerType)
        {
            GameObject[] visuals = GameObject.FindGameObjectsWithTag("TrapVisual");
            foreach (GameObject v in visuals)
                if (Vector3.Distance(v.transform.position, new Vector3(x, 0.05f, y)) < 0.1f) { Destroy(v); break; }
        }
    }

    void RemoveRandomPieces(int count, int targetPlayerType)
    {
        for (int i = 0; i < count; i++)
        {
            List<Vector2Int> myPieces = new List<Vector2Int>();
            for (int r = 0; r < 15; r++)
                for (int c = 0; c < 15; c++)
                    if (board[r, c] == targetPlayerType) myPieces.Add(new Vector2Int(r, c));

            if (myPieces.Count > 0)
            {
                int randomIndex = Random.Range(0, myPieces.Count);
                Vector2Int target = myPieces[randomIndex];
                board[target.x, target.y] = 0;
                DestroyPieceRpc(target.x, target.y);
            }
        }
    }

    [Rpc(SendTo.Everyone)]
    void SpawnPieceRpc(int x, int y, int playerType)
    {
        GameObject prefab = (playerType == 1) ? blackPiecePrefab : whitePiecePrefab;
        Instantiate(prefab, new Vector3(x, 0.1f, y), Quaternion.identity);
    }

    [Rpc(SendTo.Everyone)]
    void DestroyPieceRpc(int x, int y)
    {
        GameObject[] pieces = GameObject.FindGameObjectsWithTag("Piece");
        foreach (GameObject p in pieces)
            if (Vector3.Distance(p.transform.position, new Vector3(x, 0.1f, y)) < 0.1f) { Destroy(p); break; }
    }

    [Rpc(SendTo.Everyone)]
    void NotifyTrapTriggeredRpc(int x, int y, int playerType, string message)
    {
        lastTrapMessage = message;
        if (trapEffectPrefab != null) Instantiate(trapEffectPrefab, new Vector3(x, 0.5f, y), Quaternion.identity);
        GameObject[] visuals = GameObject.FindGameObjectsWithTag("TrapVisual");
        foreach (GameObject v in visuals)
            if (Vector3.Distance(v.transform.position, new Vector3(x, 0.05f, y)) < 0.1f) Destroy(v);
    }

    [Rpc(SendTo.Everyone)]
    void ClearTrapMessageRpc() => lastTrapMessage = "";

    [Rpc(SendTo.Server)]
    public void RequestRestartRpc(bool swapTurn)
    {
        System.Array.Clear(board, 0, board.Length);
        System.Array.Clear(playerTraps, 0, playerTraps.Length);
        blackTrapCount = 0; whiteTrapCount = 0; winner.Value = 0;
        lastTrapMessage = "";

        // 순서 바꾸기 체크 시 호스트의 색상을 반전 (1->2 or 2->1)
        if (swapTurn) hostColor.Value = (hostColor.Value == 1) ? 2 : 1;

        turnPlayer.Value = 1; // 게임은 항상 1번(흑돌)부터 시작
        GenerateRandomPublicTraps();
        RestartGameClientRpc();
    }

    [Rpc(SendTo.Everyone)]
    void RestartGameClientRpc()
    {
        foreach (GameObject p in GameObject.FindGameObjectsWithTag("Piece")) Destroy(p);
        foreach (GameObject t in GameObject.FindGameObjectsWithTag("TrapVisual")) Destroy(t);
    }

    [Rpc(SendTo.Everyone)]
    void UpdateTrapUIRpc(int black, int white) { blackTrapCount = black; whiteTrapCount = white; }

    private void OnGUI()
    {
        int myColor = IsHost ? hostColor.Value : (hostColor.Value == 1 ? 2 : 1);
        bool isMyTurn = turnPlayer.Value == myColor;

        string colorName = myColor == 1 ? "흑돌(선공)" : "백돌(후공)";
        string turnStatus = isMyTurn ? "<color=white>[내 차례]</color>" : "<color=white>[상대 차례]</color>";
        int myRemaining = MaxTrapCount - (myColor == 1 ? blackTrapCount : whiteTrapCount);

        GUI.Box(new Rect(20, Screen.height / 2 - 100, 240, 90), $"<b>{colorName}</b>\n{turnStatus}\n남은 함정 설치 가능 개수: {myRemaining}개");

        if (!string.IsNullOrEmpty(lastTrapMessage))
        {
            Color oldBg = GUI.backgroundColor;
            Color oldContent = GUI.color;
            GUI.backgroundColor = Color.black;
            GUI.color = new Color(1f, 1f, 0.4f); // 밝은 레몬 노란색
            GUI.Box(new Rect(Screen.width / 2 - 250, 100, 500, 50), $"<b><size=16>⚠️ {lastTrapMessage}</size></b>");
            GUI.backgroundColor = oldBg;
            GUI.color = oldContent;
        }

        if (IsServer)
        {
            if (GUI.Button(new Rect(Screen.width - 250, 20, 110, 40), "새 게임")) RequestRestartRpc(false);
            if (GUI.Button(new Rect(Screen.width - 130, 20, 110, 40), "순서 교체")) RequestRestartRpc(true);
        }

        if (winner.Value != 0)
        {
            string winMsg = winner.Value == 1 ? "흑돌 승리!" : "백돌 승리!";
            GUI.Box(new Rect(Screen.width / 2 - 120, Screen.height / 2 - 70, 240, 120), $"<size=20>{winMsg}</size>");
            if (IsServer)
            {
                if (GUI.Button(new Rect(Screen.width / 2 - 110, Screen.height / 2, 100, 40), "새 게임")) RequestRestartRpc(false);
                if (GUI.Button(new Rect(Screen.width / 2 + 10, Screen.height / 2, 100, 40), "순서 교체")) RequestRestartRpc(true);
            }
        }
    }

    bool CheckWinCondition(int x, int y, int type)
    {
        int[] dx = { 1, 0, 1, 1 }, dy = { 0, 1, 1, -1 };
        for (int i = 0; i < 4; i++)
        {
            int count = 1;
            for (int j = 1; j < 5; j++)
            {
                int nx = x + dx[i] * j, ny = y + dy[i] * j;
                if (nx >= 0 && nx < 15 && ny >= 0 && ny < 15 && board[nx, ny] == type) count++; else break;
            }
            for (int j = 1; j < 5; j++)
            {
                int nx = x - dx[i] * j, ny = y - dy[i] * j;
                if (nx >= 0 && nx < 15 && ny >= 0 && ny < 15 && board[nx, ny] == type) count++; else break;
            }
            if (count >= 5) return true;
        }
        return false;
    }
}