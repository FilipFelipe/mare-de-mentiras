using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace MareDeMentiras
{
    /// <summary>
    /// Controla o aplicativo: menu, sala online, partida solo ou em rede.
    /// No modo solo e no anfitrião, o motor (<see cref="Table"/>) roda aqui e os eventos vão
    /// para a tela local e para os jogadores remotos. No convidado, só recebe eventos e manda respostas.
    /// Se cria sozinho ao apertar Play em qualquer cena.
    /// </summary>
    public class GameManager : MonoBehaviour, ITableOutput
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameManager>() == null)
                new GameObject("Maré de Mentiras").AddComponent<GameManager>();
        }

        public static GameManager Instance { get; private set; }

        static readonly string[] AiNames = { "Barba Ruiva", "Dona Coral", "Zé Gancho", "Capitã Brisa", "Velho Lobo" };
        static readonly string[] AiPortraits = { "Retrato_1", "Retrato_2", "Retrato_3", "Retrato_4", "Retrato_5" };
        public static string HumanPortrait(int i) => "Capuz_" + Mathf.Clamp(i, 0, Rules.MaxPlayers - 1);
        public static string AiName(int i) => AiNames[Mathf.Clamp(i, 0, AiNames.Length - 1)];
        public static string AiPortrait(int i) => AiPortraits[Mathf.Clamp(i, 0, AiPortraits.Length - 1)];
        const ulong LocalId = ulong.MaxValue;

        public enum Mode { Menu, Solo, Host, Client }
        public Mode CurrentMode { get; private set; }
        public string RoomCode => net != null ? net.Code : null;

        GameUI ui;
        MenuUI menu;
        Table table;
        NetSession net;

        int localIndex = -1;
        bool inGame;
        readonly List<(ulong id, string name)> roster = new List<(ulong, string)>();
        readonly Dictionary<int, ulong> remoteByPlayer = new Dictionary<int, ulong>();

        void Awake()
        {
            Instance = this;
            // O anfitrião não pode pausar quando a janela perde o foco (congelaria a partida de todos).
            Application.runInBackground = true;
            UIKit.Init();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                es.transform.SetParent(transform);
            }

            table = gameObject.AddComponent<Table>();
            net = gameObject.AddComponent<NetSession>();

            ui = gameObject.AddComponent<GameUI>();
            ui.Init(transform);
            ui.OnAnswer = OnLocalAnswer;
            ui.OnLeave = Leave;
            ui.Show(false);

            menu = gameObject.AddComponent<MenuUI>();
            menu.Init(transform);
            menu.OnSolo = PlaySolo;
            menu.OnCreateRoom = CreateRoom;
            menu.OnJoin = code => JoinRoom(code);
            menu.OnJoinRoom = room =>
            {
                if (room.hasPassword) menu.AskPassword(room.name, null, pwd => JoinRoomById(room, pwd));
                else JoinRoomById(room, null);
            };
            menu.OnRefreshRooms = RefreshRooms;
            menu.OnLobbyAiChanged = RefreshLobby;
            menu.OnStartGame = StartOnlineGame;
            menu.OnLeaveLobby = Leave;

            net.HelloReceived += OnHello;
            net.ClientLeft += OnClientLeft;
            net.AnswerReceived += OnRemoteAnswer;
            net.EventReceived += OnNetEvent;
            net.ConnectionLost += () => BackToMenu("A conexão com a sala caiu.");
        }

        // ================================================================== menu

        public void PlaySolo() => PlaySolo(menu.SoloAi);

        public void PlaySolo(int aiCount)
        {
            CurrentMode = Mode.Solo;
            localIndex = 0;
            remoteByPlayer.Clear();
            StartTable(new List<SeatInfo> { new SeatInfo { name = menu.Nickname, human = true, portrait = HumanPortrait(0) } }, aiCount);
        }

        /// <summary>O projeto precisa estar vinculado à Unity Cloud para o online funcionar.</summary>
        static bool OnlineConfigured => !string.IsNullOrEmpty(Application.cloudProjectId);
        const string OnlineOff = "O online está desativado neste projeto.\nNo Unity: Edit > Project Settings > Services e vincule o projeto.";

        public async void CreateRoom(RoomVisibility visibility, string password)
        {
            if (!OnlineConfigured) { menu.ShowMenu(OnlineOff); return; }
            menu.ShowBusy("Criando sala…");
            try { await net.HostAsync($"Sala de {menu.Nickname}", visibility, password); }
            catch (Exception e)
            {
                Debug.LogException(e);
                net.Leave();
                menu.ShowMenu(FriendlyError(e));
                return;
            }
            AfterHosted();
        }

        public async void JoinRoom(string code, string password = null)
        {
            if (string.IsNullOrWhiteSpace(code)) { menu.ShowMenu("Digite o código da sala."); return; }
            if (!OnlineConfigured) { menu.ShowMenu(OnlineOff); return; }
            menu.ShowBusy("Entrando na sala…");
            try { await net.JoinAsync(code, menu.Nickname, password); }
            catch (Exception e)
            {
                net.Leave();
                // Sala com senha (entrando pelo código): pede a senha e tenta de novo.
                if (NetSession.IsWrongPassword(e))
                {
                    menu.AskPassword("Sala " + code.ToUpperInvariant(), password == null ? null : "Senha incorreta. Tente de novo.",
                                     pwd => JoinRoom(code, pwd));
                    return;
                }
                Debug.LogException(e);
                menu.ShowMenu(FriendlyError(e));
                return;
            }
            AfterJoined(code);
        }

        public async void JoinRoomById(RoomInfo room, string password)
        {
            if (!OnlineConfigured) { menu.ShowMenu(OnlineOff); return; }
            menu.ShowBusy("Entrando na sala…");
            try { await net.JoinByIdAsync(room.id, menu.Nickname, password); }
            catch (Exception e)
            {
                net.Leave();
                if (NetSession.IsWrongPassword(e))
                {
                    menu.AskPassword(room.name, "Senha incorreta. Tente de novo.", pwd => JoinRoomById(room, pwd));
                    return;
                }
                Debug.LogException(e);
                menu.ShowMenu(FriendlyError(e));
                return;
            }
            AfterJoined(net.Code ?? "");
        }

        bool refreshing;

        async void RefreshRooms()
        {
            if (refreshing) return;
            if (!OnlineConfigured) { menu.ShowRoomsError(OnlineOff); return; }
            refreshing = true;
            menu.SetRoomsLoading();
            try { menu.ShowRooms(await net.ListRoomsAsync()); }
            catch (Exception e)
            {
                Debug.LogWarning("Lista de salas: " + e.Message);
                menu.ShowRoomsError(FriendlyError(e));
            }
            finally { refreshing = false; }
        }

        /// <summary>Rede local / testes: anfitrião por IP, sem Unity Gaming Services.</summary>
        public void HostLan(ushort port = 7777)
        {
            net.HostDirect(port);
            AfterHosted();
        }

        public void JoinLan(string ip, ushort port = 7777)
        {
            net.JoinDirect(ip, menu.Nickname, port);
            AfterJoined("LAN");
        }

        public void SetNickname(string nick) => menu.SetNickname(nick);

        void AfterHosted()
        {
            CurrentMode = Mode.Host;
            localIndex = 0;
            inGame = false;
            roster.Clear();
            roster.Add((LocalId, menu.Nickname));
            RefreshLobby();
        }

        void AfterJoined(string code)
        {
            CurrentMode = Mode.Client;
            inGame = false;
            menu.ShowLobby(code.ToUpperInvariant(), new[] { "…" }, false, 0);
        }

        public void Leave()
        {
            table.Stop();
            if (CurrentMode == Mode.Host || CurrentMode == Mode.Client) net.Leave();
            BackToMenu(null);
        }

        void BackToMenu(string error)
        {
            table.Stop();
            inGame = false;
            CurrentMode = Mode.Menu;
            roster.Clear();
            remoteByPlayer.Clear();
            ui.ClearPrompt();
            ui.Show(false);
            menu.ShowMenu(error);
        }

        static string FriendlyError(Exception e)
        {
            string m = (e.Message ?? "").ToLowerInvariant();
            if (m.Contains("project") || m.Contains("cloud") || m.Contains("environment") || m.Contains("unauthorized") || m.Contains("initializ"))
                return OnlineOff;
            if (m.Contains("not found") || m.Contains("notfound") || m.Contains("invalid") || m.Contains("code"))
                return "Sala não encontrada. Confira o código.";
            if (m.Contains("full"))
                return "Essa sala está cheia.";
            return "Não foi possível conectar.\n" + e.Message;
        }

        // ================================================================== sala (anfitrião)

        void RefreshLobby()
        {
            var names = roster.Select(r => r.name).ToArray();
            if (!inGame) menu.ShowLobby(net.Code, names, true, menu.LobbyAi);
            foreach (var r in roster)
                if (r.id != LocalId)
                    net.SendEvent(r.id, new GameEvent { type = Ev.Lobby, text = net.Code, names = names, b = menu.LobbyAi });
        }

        void OnHello(ulong id, string name)
        {
            if (CurrentMode != Mode.Host) return;
            if (inGame || roster.Count >= Rules.MaxPlayers) { net.Kick(id); return; }
            roster.Add((id, UIKit.CleanName(name, "Pirata " + (roster.Count + 1))));
            RefreshLobby();
        }

        void OnClientLeft(ulong id)
        {
            if (CurrentMode != Mode.Host) return;
            roster.RemoveAll(r => r.id == id);
            foreach (var kv in remoteByPlayer.Where(kv => kv.Value == id).ToList())
            {
                remoteByPlayer.Remove(kv.Key);
                table.PlayerLeft(kv.Key);
            }
            if (!inGame) RefreshLobby();
        }

        void OnRemoteAnswer(ulong id, Answer a)
        {
            foreach (var kv in remoteByPlayer)
                if (kv.Value == id) table.SubmitAnswer(kv.Key, a.promptId, a.choice);
        }

        public void StartOnlineGame()
        {
            if (CurrentMode != Mode.Host) return;
            net.SetLocked(true);
            remoteByPlayer.Clear();
            var humans = new List<SeatInfo>();
            for (int i = 0; i < roster.Count && i < Rules.MaxPlayers; i++)
            {
                humans.Add(new SeatInfo { name = roster[i].name, human = true, portrait = HumanPortrait(i) });
                if (roster[i].id != LocalId) remoteByPlayer[i] = roster[i].id;
            }
            StartTable(humans, menu.LobbyAi);
        }

        void StartTable(List<SeatInfo> humans, int aiCount)
        {
            var seats = new List<SeatInfo>(humans);
            int target = Mathf.Clamp(humans.Count + aiCount, Rules.MinPlayers, Rules.MaxPlayers);
            for (int k = 0; seats.Count < target; k++)
                seats.Add(new SeatInfo { name = AiName(k), human = false, portrait = AiPortrait(k) });

            inGame = true;
            menu.Show(false);
            ui.Show(true);
            ui.SetStatus(null);
            table.Begin(seats, this);
        }

        // ================================================================== convidado

        void OnNetEvent(GameEvent ev)
        {
            if (CurrentMode != Mode.Client) return;
            if (ev.type == Ev.Lobby)
            {
                inGame = false;
                ui.Show(false);
                menu.ShowLobby(ev.text, ev.names, false, ev.b);
                return;
            }
            if (ev.type == Ev.Reset)
            {
                inGame = true;
                menu.Show(false);
                ui.Show(true);
                ui.SetStatus(null);
            }
            ui.Apply(ev);
            if (ev.type == Ev.Ended) ui.SetStatus("Aguardando o anfitrião…");
        }

        void OnLocalAnswer(int promptId, int choice)
        {
            if (CurrentMode == Mode.Client) net.SendAnswer(promptId, choice);
            else table.SubmitAnswer(localIndex, promptId, choice);
        }

        // ================================================================== ITableOutput

        public void Broadcast(GameEvent ev)
        {
            ui.Apply(ev);
            foreach (var kv in remoteByPlayer) net.SendEvent(kv.Value, ev);
        }

        public void SendTo(int playerIdx, GameEvent ev)
        {
            if (playerIdx == localIndex) ui.Apply(ev);
            else if (remoteByPlayer.TryGetValue(playerIdx, out var id)) net.SendEvent(id, ev);
        }

        public void StateChanged()
        {
            ui.Apply(new GameEvent { type = Ev.State, state = table.Snapshot(localIndex) });
            foreach (var kv in remoteByPlayer)
                net.SendEvent(kv.Value, new GameEvent { type = Ev.State, state = table.Snapshot(kv.Key) });
        }

        public void Finished(int winnerIdx)
        {
            Broadcast(new GameEvent { type = Ev.Ended, a = winnerIdx });
            inGame = false;
            var opts = CurrentMode == Mode.Host
                ? new[] { new Option("Jogar de novo", "mesma turma", null, ButtonStyle.Verde), new Option("Voltar à sala") }
                : new[] { new Option("Jogar de novo", null, null, ButtonStyle.Verde), new Option("Menu") };
            ui.ShowPrompt("Fim de jogo! E agora?", opts, i =>
            {
                ui.ClearPrompt();
                if (i == 0)
                {
                    if (CurrentMode == Mode.Host) StartOnlineGame(); else PlaySolo();
                }
                else if (CurrentMode == Mode.Host)
                {
                    ui.Show(false);
                    net.SetLocked(false);
                    RefreshLobby();
                }
                else BackToMenu(null);
            });
        }
    }
}
