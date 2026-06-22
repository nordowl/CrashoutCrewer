using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Mirror;

[DisallowMultipleComponent]
[AddComponentMenu("Network/Network Manager")]
[HelpURL("https://mirror-networking.gitbook.io/docs/components/network-manager")]
public class NetworkManager : MonoBehaviour
{
	[Header("Configuration")]
	[FormerlySerializedAs("m_DontDestroyOnLoad")]
	[Tooltip("Should the Network Manager object be persisted through scene changes?")]
	public bool dontDestroyOnLoad = true;

	[FormerlySerializedAs("m_RunInBackground")]
	[Tooltip("Multiplayer games should always run in the background so the network doesn't time out.")]
	public bool runInBackground = true;

	[Header("Auto-Start Options")]
	[Tooltip("Choose whether Server or Client should auto-start in headless builds")]
	public HeadlessStartOptions headlessStartMode;

	[Tooltip("Headless Start Mode in Editor\nwhen enabled, headless start mode will be used in editor as well.")]
	public bool editorAutoStart;

	[Tooltip("Server / Client send rate per second.\nUse 60-100Hz for fast paced games like Counter-Strike to minimize latency.\nUse around 30Hz for games like WoW to minimize computations.\nUse around 1-10Hz for slow paced games like EVE.")]
	[FormerlySerializedAs("serverTickRate")]
	public int sendRate = 60;

	[Header("Scene Management")]
	[Scene]
	[FormerlySerializedAs("m_OfflineScene")]
	[Tooltip("Scene that Mirror will switch to when the client or server is stopped")]
	public string offlineScene = "";

	[Scene]
	[FormerlySerializedAs("m_OnlineScene")]
	[Tooltip("Scene that Mirror will switch to when the server is started. Clients will recieve a Scene Message to load the server's current scene when they connect.")]
	public string onlineScene = "";

	[Range(0f, 60f)]
	[Tooltip("Optional delay that can be used after disconnecting to show a 'Connection lost...' message or similar before loading the offline scene, which may take a long time in big projects.")]
	public float offlineSceneLoadDelay;

	[Header("Network Info")]
	[Tooltip("Transport component attached to this object that server and client will use to connect")]
	public Transport transport;

	[FormerlySerializedAs("m_NetworkAddress")]
	[Tooltip("Network Address where the client should connect to the server. Server does not use this for anything.")]
	public string networkAddress = "localhost";

	[FormerlySerializedAs("m_MaxConnections")]
	[Tooltip("Maximum number of concurrent connections.")]
	public int maxConnections = 100;

	[Tooltip("When enabled, the server automatically disconnects inactive connections after the configured timeout.")]
	public bool disconnectInactiveConnections;

	[Tooltip("Timeout in seconds for server to automatically disconnect inactive connections if 'disconnectInactiveConnections' is enabled.")]
	public float disconnectInactiveTimeout = 60f;

	[Header("Authentication")]
	[Tooltip("Authentication component attached to this object")]
	public NetworkAuthenticator authenticator;

	[Header("Player Object")]
	[FormerlySerializedAs("m_PlayerPrefab")]
	[Tooltip("Prefab of the player object. Prefab must have a Network Identity component. May be an empty game object or a full avatar.")]
	public GameObject playerPrefab;

	[FormerlySerializedAs("m_AutoCreatePlayer")]
	[Tooltip("Should Mirror automatically spawn the player after scene change?")]
	public bool autoCreatePlayer = true;

	[FormerlySerializedAs("m_PlayerSpawnMethod")]
	[Tooltip("Round Robin or Random order of Start Position selection")]
	public PlayerSpawnMethod playerSpawnMethod;

	[FormerlySerializedAs("m_SpawnPrefabs")]
	[HideInInspector]
	public List<GameObject> spawnPrefabs = new List<GameObject>();

	public static List<Transform> startPositions = new List<Transform>();

	public static int startPositionIndex;

	[Header("Security")]
	[Tooltip("For security, it is recommended to disconnect a player if a networked action triggers an exception\nThis could prevent components being accessed in an undefined state, which may be an attack vector for exploits.\nHowever, some games may want to allow exceptions in order to not interrupt the player's experience.")]
	public bool exceptionsDisconnect = true;

	[Header("Snapshot Interpolation")]
	public SnapshotInterpolationSettings snapshotSettings = new SnapshotInterpolationSettings();

	[Header("Connection Quality")]
	[Tooltip("Method to use for connection quality evaluation.\nSimple: based on rtt and jitter.\nPragmatic: based on snapshot interpolation adjustment.")]
	public ConnectionQualityMethod evaluationMethod;

	[Tooltip("Interval in seconds to evaluate connection quality.\nSet to 0 to disable connection quality evaluation.")]
	[Range(0f, 60f)]
	[FormerlySerializedAs("connectionQualityInterval")]
	public float evaluationInterval = 3f;

	[Header("Interpolation UI - Requires Editor / Dev Build")]
	public bool timeInterpolationGui;

	internal static NetworkConnection clientReadyConnection;

	protected bool clientLoadedScene;

	private bool finishStartHostPending;

	public static AsyncOperation loadingSceneAsync;

	private SceneOperation clientSceneOperation;

	public static NetworkManager singleton { get; internal set; }

	public int numPlayers => NetworkServer.connections.Count((KeyValuePair<int, NetworkConnectionToClient> kv) => kv.Value.identity != null);

	public bool isNetworkActive
	{
		get
		{
			if (!NetworkServer.active)
			{
				return NetworkClient.active;
			}
			return true;
		}
	}

	public NetworkManagerMode mode { get; private set; }

	public static string networkSceneName { get; protected set; } = "";

	public virtual void OnValidate()
	{
		maxConnections = Mathf.Max(maxConnections, 0);
		if (playerPrefab != null && !playerPrefab.TryGetComponent<NetworkIdentity>(out var _))
		{
			Debug.LogError("NetworkManager - Player Prefab must have a NetworkIdentity.");
			playerPrefab = null;
		}
		if (playerPrefab != null && spawnPrefabs.Contains(playerPrefab))
		{
			Debug.LogWarning("NetworkManager - Player Prefab doesn't need to be in Spawnable Prefabs list too. Removing it.");
			spawnPrefabs.Remove(playerPrefab);
		}
	}

	public virtual void Reset()
	{
		NetworkManager[] componentsInChildren = base.transform.root.GetComponentsInChildren<NetworkManager>();
		foreach (NetworkManager networkManager in componentsInChildren)
		{
			if (networkManager != this)
			{
				Debug.LogError($"{base.name} detected another component of type {typeof(NetworkManager)} in its hierarchy on {networkManager.name}. There can only be one, please remove one of them.");
				break;
			}
		}
	}

	public virtual void Awake()
	{
		if (InitializeSingleton())
		{
			ApplyConfiguration();
			networkSceneName = offlineScene;
			SceneManager.sceneLoaded += OnSceneLoaded;
		}
	}

	public virtual void Start()
	{
		if (Utils.IsHeadless() && (!Application.isEditor || editorAutoStart))
		{
			switch (headlessStartMode)
			{
			case HeadlessStartOptions.AutoStartServer:
				StartServer();
				break;
			case HeadlessStartOptions.AutoStartClient:
				StartClient();
				break;
			}
		}
	}

	public virtual void Update()
	{
		ApplyConfiguration();
	}

	public virtual void LateUpdate()
	{
		UpdateScene();
	}

	private bool IsServerOnlineSceneChangeNeeded()
	{
		if (!string.IsNullOrWhiteSpace(onlineScene) && !Utils.IsSceneActive(onlineScene))
		{
			return onlineScene != offlineScene;
		}
		return false;
	}

	private void ApplyConfiguration()
	{
		NetworkServer.tickRate = sendRate;
		NetworkClient.snapshotSettings = snapshotSettings;
		NetworkClient.connectionQualityInterval = evaluationInterval;
		NetworkClient.connectionQualityMethod = evaluationMethod;
	}

	private void SetupServer()
	{
		InitializeSingleton();
		NetworkServer.disconnectInactiveConnections = disconnectInactiveConnections;
		NetworkServer.disconnectInactiveTimeout = disconnectInactiveTimeout;
		NetworkServer.exceptionsDisconnect = exceptionsDisconnect;
		if (runInBackground)
		{
			Application.runInBackground = true;
		}
		if (authenticator != null)
		{
			authenticator.OnStartServer();
			authenticator.OnServerAuthenticated.AddListener(OnServerAuthenticated);
		}
		ConfigureHeadlessFrameRate();
		NetworkServer.Listen(maxConnections);
		RegisterServerMessages();
	}

	public void StartServer()
	{
		if (NetworkServer.active)
		{
			Debug.LogWarning("Server already started.");
			return;
		}
		mode = NetworkManagerMode.ServerOnly;
		SetupServer();
		OnStartServer();
		if (IsServerOnlineSceneChangeNeeded())
		{
			ServerChangeScene(onlineScene);
		}
		else
		{
			NetworkServer.SpawnObjects();
		}
	}

	private void SetupClient()
	{
		InitializeSingleton();
		NetworkClient.exceptionsDisconnect = exceptionsDisconnect;
		if (runInBackground)
		{
			Application.runInBackground = true;
		}
		if (authenticator != null)
		{
			authenticator.OnStartClient();
			authenticator.OnClientAuthenticated.AddListener(OnClientAuthenticated);
		}
	}

	public void StartClient()
	{
		if (NetworkClient.active)
		{
			Debug.LogWarning("Client already started.");
			return;
		}
		if (string.IsNullOrWhiteSpace(networkAddress))
		{
			Debug.LogError("Must set the Network Address field in the manager");
			return;
		}
		mode = NetworkManagerMode.ClientOnly;
		SetupClient();
		ConfigureHeadlessFrameRate();
		RegisterClientMessages();
		NetworkClient.Connect(networkAddress);
		OnStartClient();
	}

	public void StartClient(Uri uri)
	{
		if (NetworkClient.active)
		{
			Debug.LogWarning("Client already started.");
			return;
		}
		mode = NetworkManagerMode.ClientOnly;
		SetupClient();
		RegisterClientMessages();
		networkAddress = uri.Host;
		NetworkClient.Connect(uri);
		OnStartClient();
	}

	public void StartHost()
	{
		if (NetworkServer.active || NetworkClient.active)
		{
			Debug.LogWarning("Server or Client already started.");
			return;
		}
		mode = NetworkManagerMode.Host;
		SetupServer();
		if (IsServerOnlineSceneChangeNeeded())
		{
			finishStartHostPending = true;
			ServerChangeScene(onlineScene);
		}
		else
		{
			FinishStartHost();
		}
	}

	private void FinishStartHost()
	{
		NetworkClient.ConnectHost();
		OnStartServer();
		OnStartHost();
		NetworkServer.SpawnObjects();
		SetupClient();
		RegisterClientMessages();
		HostMode.InvokeOnConnected();
		OnStartClient();
	}

	public void StopHost()
	{
		OnStopHost();
		StopClient();
		StopServer();
	}

	public void StopServer()
	{
		if (NetworkServer.active)
		{
			if (authenticator != null)
			{
				authenticator.OnServerAuthenticated.RemoveListener(OnServerAuthenticated);
				authenticator.OnStopServer();
			}
			if (base.gameObject != null && base.gameObject.scene.name == "DontDestroyOnLoad" && !string.IsNullOrWhiteSpace(offlineScene) && SceneManager.GetActiveScene().path != offlineScene)
			{
				SceneManager.MoveGameObjectToScene(base.gameObject, SceneManager.GetActiveScene());
			}
			OnStopServer();
			NetworkServer.Shutdown();
			mode = NetworkManagerMode.Offline;
			if (!string.IsNullOrWhiteSpace(offlineScene))
			{
				ServerChangeScene(offlineScene);
			}
			startPositionIndex = 0;
			networkSceneName = "";
		}
	}

	public void StopClient()
	{
		if (mode != NetworkManagerMode.Offline)
		{
			if (mode == NetworkManagerMode.Host)
			{
				OnServerDisconnect(NetworkServer.localConnection);
			}
			NetworkClient.Disconnect();
		}
	}

	public virtual void OnApplicationQuit()
	{
		if (NetworkClient.isConnected)
		{
			StopClient();
		}
		if (NetworkServer.active)
		{
			StopServer();
		}
		ResetStatics();
	}

	public virtual void ConfigureHeadlessFrameRate()
	{
		if (Utils.IsHeadless())
		{
			Application.targetFrameRate = sendRate;
		}
	}

	private bool InitializeSingleton()
	{
		if (singleton != null && singleton == this)
		{
			return true;
		}
		if (dontDestroyOnLoad)
		{
			if (singleton != null)
			{
				Debug.LogWarning("Multiple NetworkManagers detected in the scene. Only one NetworkManager can exist at a time. The duplicate NetworkManager will be destroyed.");
				UnityEngine.Object.Destroy(base.gameObject);
				return false;
			}
			singleton = this;
			if (Application.isPlaying)
			{
				base.transform.SetParent(null);
				UnityEngine.Object.DontDestroyOnLoad(base.gameObject);
			}
		}
		else
		{
			singleton = this;
		}
		if (transport == null)
		{
			if (!TryGetComponent<Transport>(out var component))
			{
				Debug.LogError("No Transport on Network Manager...add a transport and assign it.");
				return false;
			}
			Debug.LogWarning($"No Transport assigned to Network Manager - Using {component} found on same object.");
			transport = component;
		}
		Transport.active = transport;
		return true;
	}

	private void RegisterServerMessages()
	{
		NetworkServer.OnConnectedEvent = OnServerConnectInternal;
		NetworkServer.OnDisconnectedEvent = OnServerDisconnect;
		NetworkServer.OnErrorEvent = OnServerError;
		NetworkServer.OnTransportExceptionEvent = OnServerTransportException;
		NetworkServer.RegisterHandler<AddPlayerMessage>(OnServerAddPlayerInternal);
		NetworkServer.ReplaceHandler<ReadyMessage>(OnServerReadyMessageInternal);
	}

	private void RegisterClientMessages()
	{
		NetworkClient.OnConnectedEvent = OnClientConnectInternal;
		NetworkClient.OnDisconnectedEvent = OnClientDisconnectInternal;
		NetworkClient.OnErrorEvent = OnClientError;
		NetworkClient.OnTransportExceptionEvent = OnClientTransportException;
		NetworkClient.RegisterHandler<NotReadyMessage>(OnClientNotReadyMessageInternal, requireAuthentication: false);
		NetworkClient.RegisterHandler<SceneMessage>(OnClientSceneInternal, requireAuthentication: false);
		if (playerPrefab != null)
		{
			NetworkClient.RegisterPrefab(playerPrefab);
		}
		foreach (GameObject item in spawnPrefabs.Where((GameObject t) => t != null))
		{
			NetworkClient.RegisterPrefab(item);
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	public static void ResetStatics()
	{
		if ((bool)singleton)
		{
			singleton.StopHost();
		}
		startPositions.Clear();
		startPositionIndex = 0;
		clientReadyConnection = null;
		loadingSceneAsync = null;
		networkSceneName = string.Empty;
		singleton = null;
	}

	public virtual void OnDestroy()
	{
	}

	public virtual void ServerChangeScene(string newSceneName)
	{
		if (string.IsNullOrWhiteSpace(newSceneName))
		{
			Debug.LogError("ServerChangeScene empty scene name");
			return;
		}
		if (NetworkServer.isLoadingScene && newSceneName == networkSceneName)
		{
			Debug.LogError("Scene change is already in progress for " + newSceneName);
			return;
		}
		if (!NetworkServer.active && newSceneName != offlineScene)
		{
			Debug.LogError("ServerChangeScene can only be called on an active server.");
			return;
		}
		NetworkServer.SetAllClientsNotReady();
		networkSceneName = newSceneName;
		OnServerChangeScene(newSceneName);
		NetworkServer.isLoadingScene = true;
		loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName);
		if (NetworkServer.active)
		{
			NetworkServer.SendToAll(new SceneMessage
			{
				sceneName = newSceneName
			});
		}
		startPositionIndex = 0;
		startPositions.Clear();
	}

	internal void ClientChangeScene(string newSceneName, SceneOperation sceneOperation = SceneOperation.Normal, bool customHandling = false)
	{
		if (string.IsNullOrWhiteSpace(newSceneName))
		{
			Debug.LogError("ClientChangeScene empty scene name");
			return;
		}
		OnClientChangeScene(newSceneName, sceneOperation, customHandling);
		if (NetworkServer.active)
		{
			return;
		}
		NetworkClient.isLoadingScene = true;
		clientSceneOperation = sceneOperation;
		if (customHandling)
		{
			return;
		}
		switch (sceneOperation)
		{
		case SceneOperation.Normal:
			loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName);
			break;
		case SceneOperation.LoadAdditive:
			if (!SceneManager.GetSceneByName(newSceneName).IsValid() && !SceneManager.GetSceneByPath(newSceneName).IsValid())
			{
				loadingSceneAsync = SceneManager.LoadSceneAsync(newSceneName, LoadSceneMode.Additive);
				break;
			}
			Debug.LogWarning("Scene " + newSceneName + " is already loaded");
			NetworkClient.isLoadingScene = false;
			break;
		case SceneOperation.UnloadAdditive:
			if (SceneManager.GetSceneByName(newSceneName).IsValid() || SceneManager.GetSceneByPath(newSceneName).IsValid())
			{
				loadingSceneAsync = SceneManager.UnloadSceneAsync(newSceneName, UnloadSceneOptions.UnloadAllEmbeddedSceneObjects);
				break;
			}
			Debug.LogWarning("Cannot unload " + newSceneName + " with UnloadAdditive operation");
			NetworkClient.isLoadingScene = false;
			break;
		}
		if (sceneOperation == SceneOperation.Normal)
		{
			networkSceneName = newSceneName;
		}
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (mode == LoadSceneMode.Additive)
		{
			if (NetworkServer.active)
			{
				NetworkServer.SpawnObjects();
			}
			if (NetworkClient.active)
			{
				NetworkClient.PrepareToSpawnSceneObjects();
			}
		}
	}

	private void UpdateScene()
	{
		if (loadingSceneAsync != null && loadingSceneAsync.isDone)
		{
			try
			{
				FinishLoadScene();
			}
			finally
			{
				loadingSceneAsync.allowSceneActivation = true;
				loadingSceneAsync = null;
			}
		}
	}

	protected void FinishLoadScene()
	{
		NetworkServer.isLoadingScene = false;
		NetworkClient.isLoadingScene = false;
		if (mode == NetworkManagerMode.Host)
		{
			FinishLoadSceneHost();
		}
		else if (mode == NetworkManagerMode.ServerOnly)
		{
			FinishLoadSceneServerOnly();
		}
		else if (mode == NetworkManagerMode.ClientOnly)
		{
			FinishLoadSceneClientOnly();
		}
	}

	private void FinishLoadSceneHost()
	{
		if (clientReadyConnection != null)
		{
			clientLoadedScene = true;
			clientReadyConnection = null;
		}
		if (finishStartHostPending)
		{
			finishStartHostPending = false;
			FinishStartHost();
			OnServerSceneChanged(networkSceneName);
			return;
		}
		NetworkServer.SpawnObjects();
		OnServerSceneChanged(networkSceneName);
		if (NetworkClient.isConnected)
		{
			OnClientSceneChanged();
		}
	}

	private void FinishLoadSceneServerOnly()
	{
		NetworkServer.SpawnObjects();
		OnServerSceneChanged(networkSceneName);
	}

	private void FinishLoadSceneClientOnly()
	{
		if (clientReadyConnection != null)
		{
			clientLoadedScene = true;
			clientReadyConnection = null;
		}
		if (NetworkClient.isConnected)
		{
			OnClientSceneChanged();
		}
	}

	public static void RegisterStartPosition(Transform start)
	{
		startPositions.Add(start);
		startPositions = startPositions.OrderBy((Transform transform) => transform.GetSiblingIndex()).ToList();
	}

	public static void UnRegisterStartPosition(Transform start)
	{
		startPositions.Remove(start);
	}

	public virtual Transform GetStartPosition()
	{
		startPositions.RemoveAll((Transform t) => t == null);
		if (startPositions.Count == 0)
		{
			return null;
		}
		if (playerSpawnMethod == PlayerSpawnMethod.Random)
		{
			return startPositions[UnityEngine.Random.Range(0, startPositions.Count)];
		}
		Transform result = startPositions[startPositionIndex];
		startPositionIndex = (startPositionIndex + 1) % startPositions.Count;
		return result;
	}

	private void OnServerConnectInternal(NetworkConnectionToClient conn)
	{
		if (authenticator != null)
		{
			authenticator.OnServerAuthenticate(conn);
		}
		else
		{
			OnServerAuthenticated(conn);
		}
	}

	private void OnServerAuthenticated(NetworkConnectionToClient conn)
	{
		conn.isAuthenticated = true;
		if (networkSceneName != "" && networkSceneName != offlineScene)
		{
			SceneMessage message = new SceneMessage
			{
				sceneName = networkSceneName
			};
			conn.Send(message);
		}
		OnServerConnect(conn);
	}

	private void OnServerReadyMessageInternal(NetworkConnectionToClient conn, ReadyMessage msg)
	{
		OnServerReady(conn);
	}

	private void OnServerAddPlayerInternal(NetworkConnectionToClient conn, AddPlayerMessage msg)
	{
		NetworkIdentity component;
		if (autoCreatePlayer && playerPrefab == null)
		{
			Debug.LogError("The PlayerPrefab is empty on the NetworkManager. Please setup a PlayerPrefab object.");
		}
		else if (autoCreatePlayer && !playerPrefab.TryGetComponent<NetworkIdentity>(out component))
		{
			Debug.LogError("The PlayerPrefab does not have a NetworkIdentity. Please add a NetworkIdentity to the player prefab.");
		}
		else if (conn.identity != null)
		{
			Debug.LogError("There is already a player for this connection.");
		}
		else
		{
			OnServerAddPlayer(conn);
		}
	}

	private void OnClientConnectInternal()
	{
		if (authenticator != null)
		{
			authenticator.OnClientAuthenticate();
		}
		else
		{
			OnClientAuthenticated();
		}
	}

	private void OnClientAuthenticated()
	{
		NetworkClient.connection.isAuthenticated = true;
		if (string.IsNullOrWhiteSpace(onlineScene) || onlineScene == offlineScene || Utils.IsSceneActive(onlineScene))
		{
			clientLoadedScene = false;
		}
		else
		{
			clientLoadedScene = true;
			clientReadyConnection = NetworkClient.connection;
		}
		OnClientConnect();
	}

	private void OnClientDisconnectInternal()
	{
		if (mode == NetworkManagerMode.ServerOnly || mode == NetworkManagerMode.Offline)
		{
			return;
		}
		OnClientDisconnect();
		if (authenticator != null)
		{
			authenticator.OnClientAuthenticated.RemoveListener(OnClientAuthenticated);
			authenticator.OnStopClient();
		}
		if (mode == NetworkManagerMode.Host)
		{
			mode = NetworkManagerMode.ServerOnly;
		}
		else
		{
			mode = NetworkManagerMode.Offline;
		}
		OnStopClient();
		NetworkClient.Shutdown();
		if (mode != NetworkManagerMode.ServerOnly)
		{
			if (base.gameObject != null && base.gameObject.scene.name == "DontDestroyOnLoad" && !string.IsNullOrWhiteSpace(offlineScene) && SceneManager.GetActiveScene().path != offlineScene)
			{
				SceneManager.MoveGameObjectToScene(base.gameObject, SceneManager.GetActiveScene());
			}
			if (!string.IsNullOrWhiteSpace(offlineScene) && !Utils.IsSceneActive(offlineScene) && loadingSceneAsync == null && !NetworkServer.active)
			{
				Invoke("ClientChangeOfflineScene", offlineSceneLoadDelay);
			}
			networkSceneName = "";
		}
	}

	private void ClientChangeOfflineScene()
	{
		ClientChangeScene(offlineScene);
	}

	private void OnClientNotReadyMessageInternal(NotReadyMessage msg)
	{
		NetworkClient.ready = false;
		OnClientNotReady();
	}

	private void OnClientSceneInternal(SceneMessage msg)
	{
		if (NetworkClient.isConnected)
		{
			ClientChangeScene(msg.sceneName, msg.sceneOperation, msg.customHandling);
		}
	}

	public virtual void OnServerConnect(NetworkConnectionToClient conn)
	{
	}

	public virtual void OnServerDisconnect(NetworkConnectionToClient conn)
	{
		NetworkServer.DestroyPlayerForConnection(conn);
	}

	public virtual void OnServerReady(NetworkConnectionToClient conn)
	{
		_ = conn.identity == null;
		NetworkServer.SetClientReady(conn);
	}

	public virtual void OnServerAddPlayer(NetworkConnectionToClient conn)
	{
		Transform startPosition = GetStartPosition();
		GameObject gameObject = ((startPosition != null) ? UnityEngine.Object.Instantiate(playerPrefab, startPosition.position, startPosition.rotation) : UnityEngine.Object.Instantiate(playerPrefab));
		gameObject.name = $"{playerPrefab.name} [connId={conn.connectionId}]";
		NetworkServer.AddPlayerForConnection(conn, gameObject);
	}

	public virtual void OnServerError(NetworkConnectionToClient conn, TransportError error, string reason)
	{
	}

	public virtual void OnServerTransportException(NetworkConnectionToClient conn, Exception exception)
	{
	}

	public virtual void OnServerChangeScene(string newSceneName)
	{
	}

	public virtual void OnServerSceneChanged(string sceneName)
	{
	}

	public virtual void OnClientConnect()
	{
		if (!clientLoadedScene)
		{
			if (!NetworkClient.ready)
			{
				NetworkClient.Ready();
			}
			if (autoCreatePlayer)
			{
				NetworkClient.AddPlayer();
			}
		}
	}

	public virtual void OnClientDisconnect()
	{
	}

	public virtual void OnClientError(TransportError error, string reason)
	{
	}

	public virtual void OnClientTransportException(Exception exception)
	{
	}

	public virtual void OnClientNotReady()
	{
	}

	public virtual void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
	{
	}

	public virtual void OnClientSceneChanged()
	{
		if (NetworkClient.connection.isAuthenticated && !NetworkClient.ready)
		{
			NetworkClient.Ready();
		}
		if (NetworkClient.connection.isAuthenticated && clientSceneOperation == SceneOperation.Normal && autoCreatePlayer && NetworkClient.localPlayer == null)
		{
			NetworkClient.AddPlayer();
		}
	}

	public virtual void OnStartHost()
	{
	}

	public virtual void OnStartServer()
	{
	}

	public virtual void OnStartClient()
	{
	}

	public virtual void OnStopServer()
	{
	}

	public virtual void OnStopClient()
	{
	}

	public virtual void OnStopHost()
	{
	}
}
