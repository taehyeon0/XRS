using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.UI;
using Oculus.Platform;
using System;

public class LogInManager : MonoBehaviourPunCallbacks
{
    const string ROOM_NAME = "Meta Avatar Sdk Test Room";
    const float SPAWN_POS_Z = 1f;
    const string AVATAR_PREFAB_NAME = "Player";

    [SerializeField] GameObject m_camera;
    [SerializeField] float m_minSpawnPos_x = -3f;
    [SerializeField] float m_maxSpawnPos_x = 3f;
    [SerializeField] float m_minSpawnPos_z = -3f;
    [SerializeField] float m_maxSpawnPos_z = 3f;
    [SerializeField] ulong m_userId;

    //Singleton implementation
    private static LogInManager m_instance;
    public static LogInManager Instance
    {
        get
        {
            return m_instance;
        }
    }
    private void Awake()
    {
        if (m_instance == null)
        {
            m_instance = this;
        }
        else
        {
            Destroy(this.gameObject);
        }
    }

    void Start()
    {
        int num = UnityEngine.Random.Range(0, 1000);
        PhotonNetwork.NickName = "Player" + num.ToString();
        Debug.LogError("THAN: NickName Init: " + PhotonNetwork.NickName);

        StartCoroutine(SetUserIdFromLoggedInUser());
        StartCoroutine(ConnectToPhotonRoomOnceUserIdIsFound());
        StartCoroutine(InstantiateNetworkedAvatarOnceInRoom());
    }

    IEnumerator SetUserIdFromLoggedInUser()
    {
        if (OvrPlatformInit.status == OvrPlatformInitStatus.NotStarted)
        {
            OvrPlatformInit.InitializeOvrPlatform();
        }

        while (OvrPlatformInit.status != OvrPlatformInitStatus.Succeeded)
        {
            if (OvrPlatformInit.status == OvrPlatformInitStatus.Failed)
            {
                Debug.LogError("THAN: OVR Platform failed to initialise");
                yield break;
            }
            yield return null;
        }

        Users.GetLoggedInUser().OnComplete(message =>
        {
            if (message.IsError)
            {
                Debug.LogError("THAN: Getting Logged in user error " + message.GetError());
            }
            else
            {
                Debug.LogError("THAN: User id is " + message.Data.ID);
                m_userId = message.Data.ID;
            }
        });
    }

    IEnumerator ConnectToPhotonRoomOnceUserIdIsFound()
    {
        while (m_userId == 0)
        {
            Debug.LogError("THAN: Waiting for User id to be set before connecting to room");
            yield return null;
        }
        ConnectToPhotonRoom();
    }

    void ConnectToPhotonRoom()
    {
        PhotonNetwork.ConnectUsingSettings();
        Debug.LogError("THAN: ConnectUsingSettings 완료!");
    }

    public override void OnConnectedToMaster()
    {
        Debug.LogError("마스터 서버에 접속 완료!");
        PhotonNetwork.JoinLobby(TypedLobby.Default);
    }

    public override void OnJoinedLobby()
    {
        Debug.LogError("로비에 접속 완료!");

        // 방에 대한 설정을 한다.
        Photon.Realtime.RoomOptions ro = new Photon.Realtime.RoomOptions()
        {
            IsVisible = true,
            IsOpen = true,
            MaxPlayers = 8
        };

        PhotonNetwork.JoinOrCreateRoom(ROOM_NAME, ro, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.LogError("룸에 입장!");
    }

    IEnumerator InstantiateNetworkedAvatarOnceInRoom()
    {

        while (PhotonNetwork.InRoom == false)
        {
            Debug.LogError("THAN: Waiting to be in room before intantiating avatar");
            yield return null;
        }

        Debug.LogError("THAN : InstantiateNetworkedAvatar");
        InstantiateNetworkedAvatar();

    }

    void InstantiateNetworkedAvatar()
    {
        float rand_x = UnityEngine.Random.Range(m_minSpawnPos_x, m_maxSpawnPos_x);
        float rand_z = UnityEngine.Random.Range(m_minSpawnPos_z, m_maxSpawnPos_z);
        //float rand_x = 0f;
        //float rand_z = -3f;
        Vector3 spawnPos = new Vector3(rand_x, SPAWN_POS_Z, rand_z);
        Int64 userId = Convert.ToInt64(m_userId);
        object[] objects = new object[1] { userId };
        Debug.LogError("THAN: InstantiateNetworkedAvatar START");
        //PhotonNetwork.Instantiate(AVATAR_PREFAB_NAME, spawnPos, Quaternion.identity, 0, objects);
        GameObject myAvatar = PhotonNetwork.Instantiate(AVATAR_PREFAB_NAME, spawnPos, Quaternion.identity, 0, objects);
        //myAvatar.gameObject.transform.SetParent(GameObject.Find("OVRCameraRig").transform);
        m_camera.transform.SetParent(myAvatar.transform);
        m_camera.transform.localPosition = Vector3.zero;
        m_camera.transform.localRotation = Quaternion.identity;
        Debug.LogError("THAN: InstantiateNetworkedAvatar END");
    }
}
