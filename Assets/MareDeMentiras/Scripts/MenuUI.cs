using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MareDeMentiras.UIKit;

namespace MareDeMentiras
{
    /// <summary>Telas fora da mesa: menu inicial (solo / criar sala / entrar / salas abertas) e sala de espera.</summary>
    public class MenuUI : MonoBehaviour
    {
        public System.Action OnSolo, OnCreate, OnStartGame, OnLeaveLobby, OnRefreshRooms, OnLobbyAiChanged;
        public System.Action<string> OnJoin, OnJoinRoom;

        public string Nickname => CleanName(nameInput.text, "Pirata");
        public void SetNickname(string nick) => nameInput.text = nick;

        /// <summary>Piratas da IA no modo solo (1 a 5).</summary>
        public int SoloAi { get; private set; }
        /// <summary>Piratas da IA que o anfitrião quer na mesa online.</summary>
        public int LobbyAi { get; private set; } = 2;

        const int Slots = Rules.MaxPlayers;

        RectTransform root, menuPanel, roomsPanel, roomList, lobbyPanel, busyPanel;
        InputField nameInput, codeInput;
        Text errorText, busyText, codeText, lobbyHint, roomsStatus, soloSub;
        readonly Text[] slotNames = new Text[Slots];
        readonly Image[] slotPortraits = new Image[Slots];
        Button startButton;
        Stepper soloStepper, lobbyStepper;
        float nextRefresh, refreshInterval = 8f;

        // dados da última sala mostrada (para redesenhar quando o seletor muda)
        string lobbyCode;
        string[] lobbyNames;
        bool lobbyIsHost;

        public void Init(Transform parent)
        {
            UIKit.Init();
            root = MakeCanvas(parent, "Menu", 10);
            Background(root);
            var dim = Img("Escurecer", root, null, new Color(0, 0, 0, 0.35f));
            Stretch(dim.rectTransform, 0, 0, 1, 1);
            dim.raycastTarget = false;

            var title = Txt("Titulo", root, "Maré de Mentiras", Title, 130, Gold, TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1400, 170), new Vector2(0.5f, 1));
            AddOutline(title.gameObject, new Color(0.15f, 0.07f, 0.02f, 1f), 4);
            AddShadow(title.gameObject, 8, 0.6f);
            var sub = Txt("Sub", root, "Um jogo de blefe entre piratas · 2 a 6 jogadores", Body, 34, Cream, TextAnchor.MiddleCenter);
            Place(sub.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -225), new Vector2(1200, 50), new Vector2(0.5f, 1));

            BuildMenu();
            BuildRooms();
            BuildLobby();
            BuildBusy();
            ShowMenu();
        }

        // ================================================================== menu principal

        void BuildMenu()
        {
            menuPanel = Img("MenuPrincipal", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(menuPanel, new Vector2(0.5f, 0.5f), new Vector2(-380, -90), new Vector2(720, 600));
            AddShadow(menuPanel.gameObject, 10);

            Label(menuPanel, "Seu nome de pirata", 255);
            nameInput = MakeInput(menuPanel, "Digite seu apelido…", 16);
            Place((RectTransform)nameInput.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 200), new Vector2(560, 66));
            nameInput.text = PlayerPrefs.GetString("mdm.nick", "");
            nameInput.onEndEdit.AddListener(v => { PlayerPrefs.SetString("mdm.nick", v); PlayerPrefs.Save(); });

            SoloAi = Mathf.Clamp(PlayerPrefs.GetInt("mdm.soloAi", 3), 1, Rules.MaxPlayers - 1);
            soloStepper = new Stepper(menuPanel, new Vector2(0, 130), "Piratas da IA", v =>
            {
                SoloAi = v;
                PlayerPrefs.SetInt("mdm.soloAi", v);
                soloSub.text = SoloSubtitle();
            });
            soloStepper.Set(SoloAi, 1, Rules.MaxPlayers - 1);

            var solo = MakeButton(menuPanel, new Option("Jogar contra piratas", SoloSubtitle(), "Retrato_1", ButtonStyle.Verde), () => OnSolo?.Invoke());
            Place((RectTransform)solo.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 52), new Vector2(560, 80));
            soloSub = solo.transform.Find("Sub").GetComponent<Text>();

            var create = MakeButton(menuPanel, new Option("Criar sala online", "chame até 5 amigos", "Icone_Tributo"), () => OnCreate?.Invoke());
            Place((RectTransform)create.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -38), new Vector2(560, 80));

            Label(menuPanel, "Entrar com código", -112);
            codeInput = MakeInput(menuPanel, "CÓDIGO", 12, 34);
            codeInput.characterValidation = InputField.CharacterValidation.Alphanumeric;
            codeInput.onValidateInput += (text, i, c) => char.ToUpperInvariant(c);
            Place((RectTransform)codeInput.transform, new Vector2(0.5f, 0.5f), new Vector2(-95, -170), new Vector2(370, 72));
            var join = MakeButton(menuPanel, new Option("Entrar", null, null, ButtonStyle.Azul), () => OnJoin?.Invoke(codeInput.text));
            Place((RectTransform)join.transform, new Vector2(0.5f, 0.5f), new Vector2(190, -170), new Vector2(180, 72));

            errorText = Txt("Erro", menuPanel, "", Bold, 21, Red, TextAnchor.MiddleCenter);
            Place(errorText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -250), new Vector2(660, 76));
        }

        string SoloSubtitle() => $"você e {SoloAi} pirata{(SoloAi > 1 ? "s" : "")} da IA";

        // ================================================================== salas abertas

        void BuildRooms()
        {
            roomsPanel = Img("Salas", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(roomsPanel, new Vector2(0.5f, 0.5f), new Vector2(380, -90), new Vector2(720, 600));
            AddShadow(roomsPanel.gameObject, 10);

            var title = Txt("Titulo", roomsPanel, "Salas abertas", Title, 40, Gold, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-90, 250), new Vector2(480, 56));
            var refresh = MakeButton(roomsPanel, new Option("Atualizar", null, null, ButtonStyle.Azul), () =>
            {
                nextRefresh = Time.unscaledTime + refreshInterval;
                OnRefreshRooms?.Invoke();
            });
            Place((RectTransform)refresh.transform, new Vector2(0.5f, 0.5f), new Vector2(245, 250), new Vector2(170, 58));

            roomList = new GameObject("Lista", typeof(RectTransform)).GetComponent<RectTransform>();
            roomList.SetParent(roomsPanel, false);
            Place(roomList, new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(660, 480));

            roomsStatus = Txt("Status", roomsPanel, "", Body, 26, Muted, TextAnchor.MiddleCenter);
            Place(roomsStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(620, 200));
        }

        void Update()
        {
            if (roomsPanel == null || !roomsPanel.gameObject.activeInHierarchy) return;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + refreshInterval;
                OnRefreshRooms?.Invoke();
            }
        }

        public void SetRoomsLoading()
        {
            if (roomList.childCount == 0) roomsStatus.text = "Procurando salas…";
        }

        public void ShowRooms(List<RoomInfo> rooms)
        {
            refreshInterval = 8f;
            foreach (Transform c in roomList) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            roomsStatus.text = rooms.Count == 0 ? "Nenhuma sala aberta agora.\nCrie uma e chame a tripulação!" : "";
            for (int i = 0; i < rooms.Count && i < 5; i++)
            {
                var r = rooms[i];
                var row = Img("Sala", roomList, Spr("Placa", 18), Color.white, sliced: true);
                Place(row.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -i * 92), new Vector2(660, 80), new Vector2(0.5f, 1));
                var name = Txt("Nome", row.rectTransform, CleanName(r.name, "Sala"), Title, 32, Cream, TextAnchor.MiddleLeft);
                Stretch(name.rectTransform, 0, 0, 1, 1, 24, 0, -300, 0);
                var count = Txt("Qtd", row.rectTransform, $"{r.players}/{r.max} piratas", Bold, 22, Gold, TextAnchor.MiddleRight);
                Stretch(count.rectTransform, 0, 0, 1, 1, 0, 0, -190, 0);
                string id = r.id;
                var join = MakeButton(row.rectTransform, new Option("Entrar", null, null, ButtonStyle.Verde), () => OnJoinRoom?.Invoke(id));
                Place((RectTransform)join.transform, new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(160, 60), new Vector2(1, 0.5f));
            }
        }

        public void ShowRoomsError(string message)
        {
            refreshInterval = 30f;   // não insiste quando o online não está ativado
            foreach (Transform c in roomList) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            roomsStatus.text = message;
        }

        // ================================================================== sala de espera

        void BuildLobby()
        {
            lobbyPanel = Img("Sala", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(lobbyPanel, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(1060, 620));
            AddShadow(lobbyPanel.gameObject, 10);

            Label(lobbyPanel, "Código da sala (mande para os amigos)", 262);
            var codeBox = Img("Codigo", lobbyPanel, Spr("Pergaminho", 34), Color.white, sliced: true);
            Place(codeBox.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-80, 195), new Vector2(460, 88));
            codeText = Txt("Texto", codeBox.rectTransform, "", Title, 62, Ink, TextAnchor.MiddleCenter);
            Stretch(codeText.rectTransform, 0, 0, 1, 1);
            var copy = MakeButton(lobbyPanel, new Option("Copiar", null, null, ButtonStyle.Azul), () =>
            {
                GUIUtility.systemCopyBuffer = codeText.text;
                lobbyHint.text = "Código copiado!";
            });
            Place((RectTransform)copy.transform, new Vector2(0.5f, 0.5f), new Vector2(250, 195), new Vector2(170, 72));

            for (int i = 0; i < Slots; i++)
            {
                float x = -425 + i * 170;
                slotPortraits[i] = Img("Retrato", lobbyPanel, null, Color.white);
                Place(slotPortraits[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 45), new Vector2(112, 112));
                var ring = Img("Anel", lobbyPanel, Spr("Anel"), Color.white);
                Place(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 45), new Vector2(130, 130));
                slotNames[i] = Txt("Nome", lobbyPanel, "", Title, 26, Cream, TextAnchor.MiddleCenter);
                slotNames[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                Place(slotNames[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -42), new Vector2(165, 40));
            }

            lobbyStepper = new Stepper(lobbyPanel, new Vector2(0, -105), "Piratas da IA", v =>
            {
                LobbyAi = v;
                if (lobbyNames != null) Redraw();
                OnLobbyAiChanged?.Invoke();
            });

            lobbyHint = Txt("Dica", lobbyPanel, "", Body, 24, Muted, TextAnchor.MiddleCenter);
            Place(lobbyHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -160), new Vector2(980, 40));

            startButton = MakeButton(lobbyPanel, new Option("Começar partida", null, null, ButtonStyle.Verde), () => OnStartGame?.Invoke());
            Place((RectTransform)startButton.transform, new Vector2(0.5f, 0.5f), new Vector2(-130, -238), new Vector2(400, 80));
            var leave = MakeButton(lobbyPanel, new Option("Sair", null, null, ButtonStyle.Vermelho), () => OnLeaveLobby?.Invoke());
            Place((RectTransform)leave.transform, new Vector2(0.5f, 0.5f), new Vector2(210, -238), new Vector2(220, 80));
        }

        void BuildBusy()
        {
            busyPanel = Img("Carregando", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(busyPanel, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(720, 200));
            busyText = Txt("Texto", busyPanel, "", Title, 44, Gold, TextAnchor.MiddleCenter);
            Stretch(busyText.rectTransform, 0, 0, 1, 1);
        }

        void Label(Transform parent, string text, float y)
        {
            var t = Txt("Rotulo", parent, text, Title, 32, Gold, TextAnchor.MiddleCenter);
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(900, 44));
        }

        // ================================================================== estados

        public void Show(bool on) => root.gameObject.SetActive(on);

        void Only(RectTransform a, RectTransform b = null)
        {
            Show(true);
            foreach (var p in new[] { menuPanel, roomsPanel, lobbyPanel, busyPanel })
                p.gameObject.SetActive(p == a || p == b);
        }

        public void ShowMenu(string error = null)
        {
            Only(menuPanel, roomsPanel);
            errorText.text = error ?? "";
            nextRefresh = Time.unscaledTime + 0.3f;
        }

        public void ShowBusy(string text)
        {
            Only(busyPanel);
            busyText.text = text;
        }

        /// <summary>Sala de espera. names = jogadores humanos; aiCount = piratas da IA escolhidos pelo anfitrião.</summary>
        public void ShowLobby(string code, string[] names, bool isHost, int aiCount)
        {
            Only(lobbyPanel);
            lobbyCode = code;
            lobbyNames = names ?? new string[0];
            lobbyIsHost = isHost;

            int humans = 0;
            foreach (var n in lobbyNames) if (!string.IsNullOrEmpty(n)) humans++;
            if (isHost)
            {
                int min = Mathf.Max(0, Rules.MinPlayers - humans), max = Mathf.Max(0, Rules.MaxPlayers - humans);
                lobbyStepper.Set(Mathf.Clamp(LobbyAi, min, max), min, max);
                LobbyAi = lobbyStepper.Value;
            }
            else LobbyAi = aiCount;
            Redraw();
        }

        void Redraw()
        {
            codeText.text = lobbyCode;
            int humans = 0;
            for (int i = 0; i < Slots; i++)
            {
                string n = i < lobbyNames.Length ? lobbyNames[i] : null;
                bool human = !string.IsNullOrEmpty(n);
                if (human) humans++;
            }
            int ai = Mathf.Clamp(LobbyAi, 0, Slots - humans);
            for (int i = 0; i < Slots; i++)
            {
                bool human = i < humans;
                bool bot = !human && i < humans + ai;
                slotNames[i].text = human ? lobbyNames[i] : bot ? GameManager.AiName(i - humans) : "vaga livre";
                slotNames[i].color = human ? Cream : bot ? Muted : new Color(0.5f, 0.48f, 0.44f);
                slotPortraits[i].sprite = Spr(human ? GameManager.HumanPortrait(i) : bot ? GameManager.AiPortrait(i - humans) : "Capuz_0");
                slotPortraits[i].color = human ? Color.white : bot ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.22f, 0.22f, 0.24f);
            }

            lobbyStepper.SetVisible(lobbyIsHost);
            startButton.gameObject.SetActive(lobbyIsHost);
            int total = humans + ai;
            lobbyHint.text = lobbyIsHost
                ? (humans < 2 ? $"Esperando amigos… (ou zarpe já: mesa com {total} piratas)" : $"{humans} jogadores + {ai} da IA = mesa com {total} piratas")
                : $"Mesa com {total} piratas. Aguardando o anfitrião começar…";
        }

        // ================================================================== seletor [-] N [+]

        class Stepper
        {
            public int Value { get; private set; }
            int min, max;
            readonly Text value;
            readonly Button minus, plus;
            readonly GameObject[] parts;
            readonly System.Action<int> onChange;

            public Stepper(RectTransform parent, Vector2 pos, string label, System.Action<int> onChange)
            {
                this.onChange = onChange;
                var lbl = Txt("Rotulo", parent, label, Title, 30, Gold, TextAnchor.MiddleRight);
                Place(lbl.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(-110, 0), new Vector2(260, 50));
                minus = MakeButton(parent, new Option("−", null, null, ButtonStyle.Azul), () => Change(-1));
                Place((RectTransform)minus.transform, new Vector2(0.5f, 0.5f), pos + new Vector2(55, 0), new Vector2(60, 52));
                var box = Img("Valor", parent, Spr("Painel", 22), Color.white, sliced: true);
                Place(box.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(125, 0), new Vector2(70, 52));
                value = Txt("Num", box.rectTransform, "0", Bold, 30, Gold, TextAnchor.MiddleCenter);
                Stretch(value.rectTransform, 0, 0, 1, 1);
                plus = MakeButton(parent, new Option("+", null, null, ButtonStyle.Azul), () => Change(+1));
                Place((RectTransform)plus.transform, new Vector2(0.5f, 0.5f), pos + new Vector2(195, 0), new Vector2(60, 52));
                parts = new[] { lbl.gameObject, minus.gameObject, box.gameObject, plus.gameObject };
            }

            public void Set(int v, int min, int max)
            {
                this.min = min;
                this.max = max;
                Value = Mathf.Clamp(v, min, max);
                Draw();
            }

            public void SetVisible(bool on)
            {
                foreach (var p in parts) p.SetActive(on);
            }

            void Change(int d)
            {
                int v = Mathf.Clamp(Value + d, min, max);
                if (v == Value) return;
                Value = v;
                Draw();
                onChange(v);
            }

            void Draw()
            {
                value.text = Value.ToString();
                minus.interactable = Value > min;
                plus.interactable = Value < max;
            }
        }
    }
}
