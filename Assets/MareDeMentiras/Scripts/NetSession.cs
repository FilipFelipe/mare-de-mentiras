using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace MareDeMentiras
{
    /// <summary>
    /// Rede: cria/entra em salas pelo Unity Gaming Services (Sessions + Relay) e troca
    /// mensagens JSON via Netcode for GameObjects. Não há objetos sincronizados: o anfitrião
    /// roda a partida e manda a cada jogador só o que ele pode ver.
    /// </summary>
    /// <summary>Uma sala pública que aparece na lista do menu.</summary>
    public struct RoomInfo
    {
        public string id, name;
        public int players, max;
        public bool hasPassword;
    }

    /// <summary>Quem pode entrar na sala.</summary>
    public enum RoomVisibility
    {
        Publica,   // aparece na lista
        Privada,   // não aparece na lista; só com o código
        Senha,     // aparece na lista com cadeado; pede senha
    }

    public class NetSession : MonoBehaviour
    {
        const string MsgEvent = "mdm.ev";      // anfitrião -> jogador
        const string MsgAnswer = "mdm.ans";    // jogador -> anfitrião
        const string MsgHello = "mdm.hello";   // jogador -> anfitrião (apelido)

        // Anfitrião
        public event Action<ulong, string> HelloReceived;
        public event Action<ulong, Answer> AnswerReceived;
        public event Action<ulong> ClientLeft;
        // Jogador
        public event Action<GameEvent> EventReceived;
        public event Action ConnectionLost;

        public string Code { get; private set; }
        public bool Active => nm != null && nm.IsListening;
        public bool IsHost => nm != null && nm.IsHost;

        NetworkManager nm;
        ISession session;
        string pendingHelloName;
        bool leaving;

        // ================================================================== conexão

        /// <summary>A sala pediu senha e ela estava errada (ou faltou).</summary>
        public static bool IsWrongPassword(Exception e)
        {
            for (var x = e; x != null; x = x.InnerException)
            {
                if (x is RequestFailedException rfe && rfe.ErrorCode == 16009) return true;   // LobbyExceptionReason.IncorrectPassword
                if (x is AggregateException ag && ag.InnerExceptions.Any(IsWrongPassword)) return true;
                if ((x.Message ?? "").IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>A Unity exige senhas de 8 a 64 caracteres.</summary>
        public const int MinPassword = 8, MaxPassword = 64;

        /// <summary>Cria uma sala. O código sempre funciona; a lista mostra só as públicas e as com senha.</summary>
        public async Task HostAsync(string roomName, RoomVisibility visibility, string password)
        {
            await InitServicesAsync();
            EnsureNetworkManager();
            UseWebSocketTransport(true);
            leaving = false;
            var options = new SessionOptions
            {
                Name = roomName,
                MaxPlayers = Rules.MaxPlayers,
                IsPrivate = visibility == RoomVisibility.Privada,
                Password = visibility == RoomVisibility.Senha ? password : null,
            }
                .WithRelayNetwork()
                .WithNetworkOptions(WebSockets());
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            Code = session.Code;
        }

        public async Task JoinByIdAsync(string sessionId, string nickname, string password = null)
        {
            await InitServicesAsync();
            EnsureNetworkManager();
            UseWebSocketTransport(true);
            leaving = false;
            pendingHelloName = nickname;
            session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, JoinOptions(password));
            Code = session.Code;
        }

        /// <summary>
        /// Todo mundo conecta ao Relay por WebSockets: é o único protocolo do navegador (WebGL),
        /// e usando o mesmo em Mac/Windows/Android todos podem jogar juntos na mesma sala.
        /// </summary>
        static NetworkOptions WebSockets() => new NetworkOptions { RelayProtocol = RelayProtocol.WSS };

        static JoinSessionOptions JoinOptions(string password) =>
            new JoinSessionOptions { Password = string.IsNullOrEmpty(password) ? null : password }.WithNetworkOptions(WebSockets());

        /// <summary>Salas públicas com vaga e que ainda não começaram.</summary>
        public async Task<List<RoomInfo>> ListRoomsAsync()
        {
            await InitServicesAsync();
            var res = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
            {
                Count = 20,
                FilterOptions = new List<FilterOption> { new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater) },
            });
            return res.Sessions
                .Where(s => !s.IsLocked)
                .OrderByDescending(s => s.Created)
                .Select(s => new RoomInfo { id = s.Id, name = s.Name, max = s.MaxPlayers, players = s.MaxPlayers - s.AvailableSlots, hasPassword = s.HasPassword })
                .ToList();
        }

        /// <summary>Trava a sala (some da lista e ninguém entra) enquanto a partida rola.</summary>
        public async void SetLocked(bool locked)
        {
            if (!(session is IHostSession host)) return;
            try
            {
                host.IsLocked = locked;
                await host.SavePropertiesAsync();
            }
            catch (Exception e) { Debug.LogWarning("Não foi possível atualizar a sala: " + e.Message); }
        }

        public async Task JoinAsync(string code, string nickname, string password = null)
        {
            await InitServicesAsync();
            EnsureNetworkManager();
            UseWebSocketTransport(true);
            leaving = false;
            pendingHelloName = nickname;
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), JoinOptions(password));
            Code = session.Code;
        }

        /// <summary>Conexão direta por IP (rede local / testes), sem Unity Gaming Services.</summary>
        public void HostDirect(ushort port = 7777)
        {
            EnsureNetworkManager();
            UseWebSocketTransport(false);
            leaving = false;
            nm.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", port, "0.0.0.0");
            nm.StartHost();
            Code = "LAN:" + port;
        }

        public void JoinDirect(string ip, string nickname, ushort port = 7777)
        {
            EnsureNetworkManager();
            UseWebSocketTransport(false);
            leaving = false;
            pendingHelloName = nickname;
            nm.GetComponent<UnityTransport>().SetConnectionData(ip, port);
            nm.StartClient();
            Code = "LAN";
        }

        public async void Leave()
        {
            leaving = true;
            var s = session;
            session = null;
            Code = null;
            try { if (s != null) await s.LeaveAsync(); }
            catch (Exception e) { Debug.LogWarning("Erro ao sair da sessão: " + e.Message); }
            if (nm != null && nm.IsListening) nm.Shutdown();
        }

        /// <summary>O transporte precisa usar o mesmo protocolo do Relay (WebSockets), senão a conexão é recusada.</summary>
        void UseWebSocketTransport(bool on)
        {
            var transport = nm.GetComponent<UnityTransport>();
            if (transport != null) transport.UseWebSockets = on;
        }

        static async Task InitServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var opts = new InitializationOptions();
                // Perfil aleatório: permite abrir duas cópias do jogo no mesmo computador.
                opts.SetProfile("p" + UnityEngine.Random.Range(0, 1_000_000_000));
                await UnityServices.InitializeAsync(opts);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        void EnsureNetworkManager()
        {
            if (nm != null) return;
            var go = new GameObject("Rede");
            var transport = go.AddComponent<UnityTransport>();
            nm = go.AddComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                EnableSceneManagement = false,
                ConnectionApproval = false,
            };
            nm.SetSingleton();

            nm.OnServerStarted += () =>
            {
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgHello, (sender, r) =>
                {
                    var h = JsonUtility.FromJson<Hello>(Read(r));
                    HelloReceived?.Invoke(sender, h?.name);
                });
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgAnswer, (sender, r) =>
                {
                    var a = JsonUtility.FromJson<Answer>(Read(r));
                    if (a != null) AnswerReceived?.Invoke(sender, a);
                });
            };

            nm.OnClientConnectedCallback += id =>
            {
                if (nm.IsServer || id != nm.LocalClientId) return;
                nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgEvent, (sender, r) =>
                {
                    var ev = JsonUtility.FromJson<GameEvent>(Read(r));
                    if (ev != null) EventReceived?.Invoke(ev);
                });
                Send(MsgHello, NetworkManager.ServerClientId, JsonUtility.ToJson(new Hello { name = pendingHelloName }));
            };

            nm.OnClientDisconnectCallback += id =>
            {
                if (nm.IsServer)
                {
                    if (id != NetworkManager.ServerClientId) ClientLeft?.Invoke(id);
                }
                else if (!leaving)
                {
                    ConnectionLost?.Invoke();
                }
            };
        }

        // ================================================================== mensagens

        public void SendEvent(ulong clientId, GameEvent ev) => Send(MsgEvent, clientId, JsonUtility.ToJson(ev));

        public void SendAnswer(int promptId, int choice) =>
            Send(MsgAnswer, NetworkManager.ServerClientId, JsonUtility.ToJson(new Answer { promptId = promptId, choice = choice }));

        public void Kick(ulong clientId)
        {
            if (nm != null && nm.IsServer) nm.DisconnectClient(clientId);
        }

        void Send(string msg, ulong target, string json)
        {
            if (nm == null || !nm.IsListening || nm.CustomMessagingManager == null) return;
            using var w = new FastBufferWriter(FastBufferWriter.GetWriteSize(json), Allocator.Temp, 1 << 20);
            w.WriteValueSafe(json);
            nm.CustomMessagingManager.SendNamedMessage(msg, target, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        static string Read(FastBufferReader r)
        {
            r.ReadValueSafe(out string s);
            return s;
        }

        void OnDestroy()
        {
            if (nm != null && nm.IsListening) nm.Shutdown();
        }
    }
}
