using System;
using System.Threading.Tasks;
using Unity.Netcode;
// 필수 참조
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }
    private void Awake() => Instance = this;

    async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            Debug.Log("유니티 서비스 로그인 성공");
        }
        catch (Exception e)
        {
            Debug.LogError($"서비스 초기화 실패: {e.Message}");
        }
    }

    public async Task<string> CreateRelay()
    {
        try
        {
            // 최대 2인 할당
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(1);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // [핵심] 리플렉션 대신 직접 캐스팅하여 사용합니다.
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            // 유니티 공식 확장 메서드를 사용하여 서버 데이터를 설정합니다.
            transport.SetRelayServerData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
            Debug.Log($"릴레이 생성 성공! 코드: {joinCode}");
            return joinCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"릴레이 생성 실패: {e.Message}");
            return null;
        }
    }

    public async Task<bool> JoinRelay(string joinCode)
    {
        try
        {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

            // 참가자용 데이터 설정
            transport.SetRelayServerData(
                joinAllocation.RelayServer.IpV4,
                (ushort)joinAllocation.RelayServer.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData
            );

            bool success = NetworkManager.Singleton.StartClient();
            Debug.Log("릴레이 참가 시도: " + success);
            return success;
        }
        catch (Exception e)
        {
            Debug.LogError($"릴레이 참가 실패: {e.Message}");
            return false;
        }
    }
}
