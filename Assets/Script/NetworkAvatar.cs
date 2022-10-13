using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Oculus.Avatar2;
using System;

public class NetworkAvatar : OvrAvatarEntity
{
    public enum AssetSource
    {
        /// Load from one of the preloaded .zip files
        Zip,

        /// Load a loose glb file directly from StreamingAssets
        StreamingAssets,
    }

    [System.Serializable]
    private struct AssetData
    {
        public AssetSource source;
        public string path;
    }

    [SerializeField] int m_avatarToUseInZipFolder = 2;
    PhotonView m_photonView;
    List<byte[]> m_streamedDataList = new List<byte[]>(); 
    int m_maxBytesToLog = 15;
    [SerializeField] ulong m_instantiationData; 
    float m_cycleStartTime = 0; 
    float m_intervalToSendData = 0.08f;

    [Header("Assets")]
    [Tooltip("Asset paths to load, and whether each asset comes from a preloaded zip file or directly from StreamingAssets. See Preset Asset settings on OvrAvatarManager for how this maps to the real file name.")]
    [SerializeField]
    private List<AssetData> _assets = new List<AssetData> { new AssetData { source = AssetSource.Zip, path = "0" } };

    [Tooltip("Adds an underscore between the path and the postfix.")]
    [SerializeField]
    private bool _underscorePostfix = true;

    [Header("Sample Avatar Entity")]
    [Tooltip("A version of the avatar with additional textures will be loaded to portray more accurate human materials (requiring shader support).")]
    [SerializeField]
    private bool _highQuality = false;

    [Tooltip("Filename Postfix (WARNING: Typically the postfix is Platform specific, such as \"_rift.glb\")")]
    [SerializeField]
    private string _overridePostfix = String.Empty;

    protected override void Awake()
    {
        ConfigureAvatarEntity();
        base.Awake();
    }

    // Start is called before the first frame update
    void Start()
    {
        m_instantiationData = GetUserIdFromPhotonInstantiationData();
        _userId = m_instantiationData;
        StartCoroutine(TryToLoadUser());
    }

    void ConfigureAvatarEntity()
    {
        m_photonView = GetComponent<PhotonView>();
        if (m_photonView.IsMine)
        {
            Debug.LogError("THAN: ConfigureAvatarEntity IsMine START");
            SetIsLocal(true);
            Debug.LogError("THAN: IsLocal: " + IsLocal);
            _creationInfo.features = Oculus.Avatar2.CAPI.ovrAvatar2EntityFeatures.Preset_Default;
            SampleInputManager sampleInputManager = OvrAvatarManager.Instance.gameObject.GetComponent<SampleInputManager>();
            SetBodyTracking(sampleInputManager);
            OvrAvatarLipSyncContext lipSyncInput = GameObject.FindObjectOfType<OvrAvatarLipSyncContext>();
            SetLipSync(lipSyncInput);
            gameObject.name = "MyAvatar";
            Debug.LogError("THAN: ConfigureAvatarEntity IsMine END");
        }
        else
        {
            Debug.LogError("THAN: ConfigureAvatarEntity Other START");
            SetIsLocal(false);
            Debug.LogError("THAN: IsLocal: " + IsLocal);
            _creationInfo.features = Oculus.Avatar2.CAPI.ovrAvatar2EntityFeatures.Preset_Remote;
            SampleInputManager sampleInputManager = OvrAvatarManager.Instance.gameObject.GetComponent<SampleInputManager>();
            SetBodyTracking(sampleInputManager);
            OvrAvatarLipSyncContext lipSyncInput = GameObject.FindObjectOfType<OvrAvatarLipSyncContext>();
            SetLipSync(lipSyncInput);
            gameObject.name = "OtherAvatar";
            Debug.LogError("THAN: ConfigureAvatarEntity Other END");
        }
    }

    IEnumerator TryToLoadUser()
    {
        Debug.LogError("THAN: TryToLoadUser START");
        Debug.LogError("THAN: userId: " + _userId);

        LoadLocalAvatar();
        Debug.LogError("THAN: TryToLoadUser END");
        yield break;


        /*
              var hasAvatarRequest = OvrAvatarManager.Instance.UserHasAvatarAsync(_userId);
              Debug.LogError("THAN: userId: " + _userId);
              while (hasAvatarRequest.IsCompleted == false)
              {
                  Debug.LogError("THAN: hasAvatarRequest.IsCompleted == false");
                  yield return null;
              }

              switch (hasAvatarRequest.Result)
              {
                  case OvrAvatarManager.HasAvatarRequestResultCode.HasAvatar:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.HasAvatar");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.HasNoAvatar:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.HasNoAvatar");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.SendFailed:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.SendFailed");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.RequestFailed:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.RequestFailed");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.BadParameter:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.BadParameter");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.RequestCancelled:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.RequestCancelled");
                      break;

                  case OvrAvatarManager.HasAvatarRequestResultCode.UnknownError:
                  default:
                      Debug.LogError("THAN: HasAvatarRequestResultCode.UnknownError");
                      break;
              }
 
            Debug.LogError("THAN: LoadUser");
            LoadUser();
            Debug.LogError("THAN: TryToLoadUser END");
        */
    }

    private void LoadLocalAvatar()
    {
        Debug.LogError("THAN: LoadLocalAvatar START");
        // Zip asset paths are relative to the inside of the zip.
        // Zips can be loaded from the OvrAvatarManager at startup or by calling OvrAvatarManager.Instance.AddZipSource
        // Assets can also be loaded individually from Streaming assets
        var path = new string[1];
        foreach (var asset in _assets)
        {
            bool isFromZip = (asset.source == AssetSource.Zip);

            string assetPostfix = (_underscorePostfix ? "_" : "")
                + OvrAvatarManager.Instance.GetPlatformGLBPostfix(isFromZip)
                + OvrAvatarManager.Instance.GetPlatformGLBVersion(_highQuality, isFromZip)
                + OvrAvatarManager.Instance.GetPlatformGLBExtension(isFromZip);
            if (!String.IsNullOrEmpty(_overridePostfix))
            {
                assetPostfix = _overridePostfix;
            }

            path[0] = asset.path + assetPostfix;
            if (isFromZip)
            {
                LoadAssetsFromZipSource(path);
            }
            else
            {
                LoadAssetsFromStreamingAssets(path);
            }
        }
        Debug.LogError("THAN: LoadLocalAvatar START");
    }

    private void LateUpdate()
    {
        float elapsedTime = Time.time - m_cycleStartTime;
        if (elapsedTime > m_intervalToSendData)
        {
            RecordAndSendStreamDataIfMine();
            m_cycleStartTime = Time.time;
        }

    }

    void RecordAndSendStreamDataIfMine()
    {
        if (m_photonView.IsMine)
        {
            byte[] bytes = RecordStreamData(activeStreamLod);
            m_photonView.RPC("RecieveStreamData", RpcTarget.Others, bytes);
        }
    }

    [PunRPC]
    public void RecieveStreamData(byte[] bytes)
    {
        m_streamedDataList.Add(bytes);
    }

    void LogFirstFewBytesOf(byte[] bytes)
    {
        for (int i = 0; i < m_maxBytesToLog; i++)
        {
            string bytesString = Convert.ToString(bytes[i], 2).PadLeft(8, '0');
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (m_streamedDataList.Count > 0)
        {
            if (IsLocal == false)
            {
                byte[] firstBytesInList = m_streamedDataList[0];
                if (firstBytesInList != null)
                {
                    ApplyStreamData(firstBytesInList);
                }
                m_streamedDataList.RemoveAt(0);
            }
        }
    }

    ulong GetUserIdFromPhotonInstantiationData()
    {
        PhotonView photonView = GetComponent<PhotonView>();
        object[] instantiationData = photonView.InstantiationData;
        Int64 data_as_int = (Int64)instantiationData[0];
        return Convert.ToUInt64(data_as_int);
    }
}
