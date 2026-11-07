using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MareDeMentiras.UIKit;

namespace MareDeMentiras
{
    /// <summary>Telas fora da mesa: menu inicial (solo / criar sala / entrar / salas abertas) e sala de espera.</summary>
    public class MenuUI : MonoBehaviour
    {
        public System.Action OnSolo, OnStartGame, OnLeaveLobby, OnRefreshRooms, OnLobbyAiChanged;
        public System.Action<string> OnJoin;
        public System.Action<RoomInfo> OnJoinRoom;
        public System.Action<RoomVisibility, string> OnCreateRoom;

        /// <summary>Texto acima do código na sala de espera (ex.: "Sala privada").</summary>
        public string LobbyNote = "Código da sala (mande para os amigos)";

        public string Nickname => CleanName(nameInput.text, "Pirata");
        public void SetNickname(string nick) => nameInput.text = nick;

        /// <summary>Piratas da IA no modo solo (1 a 5).</summary>
        public int SoloAi { get; private set; }
        /// <summary>Piratas da IA que o anfitrião quer na mesa online.</summary>
        public int LobbyAi { get; private set; } = 2;

        const int Slots = Rules.MaxPlayers;

        RectTransform root, menuPanel, roomsPanel, roomList, lobbyPanel, busyPanel, createPanel, passwordPanel;
        RectTransform visibilityRow;
        InputField createPassword, joinPassword;
        Text createHint, createError, passwordTitle, passwordError, lobbyNoteText;
        RoomVisibility createVisibility = RoomVisibility.Publica;
        System.Action<string> passwordOk;
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
            BuildCreate();
            BuildPassword();
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

            var create = MakeButton(menuPanel, new Option("Criar sala online", "pública, privada ou com senha", "Icone_Tributo"), ShowCreate);
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
                if (r.hasPassword)
                {
                    var lockIcon = Img("Cadeado", row.rectTransform, Spr("Icone_Cadeado"), Color.white);
                    lockIcon.preserveAspect = true;
                    Place(lockIcon.rectTransform, new Vector2(1, 0.5f), new Vector2(-320, 0), new Vector2(40, 40));
                }
                var room = r;
                var join = MakeButton(row.rectTransform, new Option("Entrar", null, null, ButtonStyle.Verde), () => OnJoinRoom?.Invoke(room));
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

            lobbyNoteText = Label(lobbyPanel, LobbyNote, 262);
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

        Text Label(Transform parent, string text, float y)
        {
            var t = Txt("Rotulo", parent, text, Title, 32, Gold, TextAnchor.MiddleCenter);
            Place(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(900, 44));
            return t;
        }

        // ================================================================== nova sala

        void BuildCreate()
        {
            createPanel = Img("NovaSala", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(createPanel, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(900, 600));
            AddShadow(createPanel.gameObject, 10);

            var title = Txt("Titulo", createPanel, "Nova sala", Title, 52, Gold, TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(800, 70));
            Label(createPanel, "Quem pode entrar?", 175);

            visibilityRow = new GameObject("Opcoes", typeof(RectTransform)).GetComponent<RectTransform>();
            visibilityRow.SetParent(createPanel, false);
            Place(visibilityRow, new Vector2(0.5f, 0.5f), new Vector2(0, 100), new Vector2(840, 84));

            createHint = Txt("Dica", createPanel, "", Body, 24, Cream, TextAnchor.MiddleCenter);
            Place(createHint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(800, 60));

            createPassword = MakeInput(createPanel, "Senha (8 a 64 caracteres)", NetSession.MaxPassword);
            createPassword.contentType = InputField.ContentType.Password;
            Place((RectTransform)createPassword.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(520, 70));

            createError = Txt("Erro", createPanel, "", Bold, 22, Red, TextAnchor.MiddleCenter);
            Place(createError.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(800, 40));

            var ok = MakeButton(createPanel, new Option("Criar sala", null, null, ButtonStyle.Verde), ConfirmCreate);
            Place((RectTransform)ok.transform, new Vector2(0.5f, 0.5f), new Vector2(-130, -220), new Vector2(340, 80));
            var back = MakeButton(createPanel, new Option("Voltar", null, null, ButtonStyle.Vermelho), () => ShowMenu());
            Place((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(170, -220), new Vector2(220, 80));
        }

        void ShowCreate()
        {
            Only(createPanel);
            createError.text = "";
            createPassword.text = "";
            SelectVisibility(createVisibility);
        }

        void SelectVisibility(RoomVisibility v)
        {
            createVisibility = v;
            foreach (Transform c in visibilityRow) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            var opts = new[]
            {
                (RoomVisibility.Publica, "Pública", "aparece na lista"),
                (RoomVisibility.Privada, "Privada", "só com o código"),
                (RoomVisibility.Senha, "Com senha", "pede uma senha"),
            };
            for (int i = 0; i < opts.Length; i++)
            {
                var (value, label, sub) = opts[i];
                var b = MakeButton(visibilityRow, new Option(label, sub, value == RoomVisibility.Senha ? "Icone_Cadeado" : null,
                                                             value == v ? ButtonStyle.Verde : ButtonStyle.Madeira), () => SelectVisibility(value));
                Place((RectTransform)b.transform, new Vector2(0.5f, 0.5f), new Vector2(-280 + i * 280, 0), new Vector2(264, 80));
            }
            createHint.text = v switch
            {
                RoomVisibility.Privada => "A sala não aparece em \"Salas abertas\". Só entra quem receber o código.",
                RoomVisibility.Senha => "A sala aparece na lista com cadeado. Para entrar, é preciso a senha.",
                _ => "Qualquer pessoa vê a sala na lista e pode entrar.",
            };
            createPassword.gameObject.SetActive(v == RoomVisibility.Senha);
            createError.text = "";
        }

        void ConfirmCreate()
        {
            string pwd = createPassword.text ?? "";
            if (createVisibility == RoomVisibility.Senha && (pwd.Length < NetSession.MinPassword || pwd.Length > NetSession.MaxPassword))
            {
                createError.text = $"A senha precisa ter de {NetSession.MinPassword} a {NetSession.MaxPassword} caracteres.";
                return;
            }
            LobbyNote = createVisibility switch
            {
                RoomVisibility.Privada => "Sala privada · código (mande para os amigos)",
                RoomVisibility.Senha => "Sala com senha · código (mande junto com a senha)",
                _ => "Código da sala (mande para os amigos)",
            };
            OnCreateRoom?.Invoke(createVisibility, createVisibility == RoomVisibility.Senha ? pwd : null);
        }

        // ================================================================== pedir senha

        void BuildPassword()
        {
            passwordPanel = Img("Senha", root, Spr("Painel", 26), Color.white, sliced: true).rectTransform;
            Place(passwordPanel, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(760, 440));
            AddShadow(passwordPanel.gameObject, 10);

            var icon = Img("Cadeado", passwordPanel, Spr("Icone_Cadeado"), Color.white);
            icon.preserveAspect = true;
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(80, 80));
            passwordTitle = Txt("Titulo", passwordPanel, "", Title, 40, Gold, TextAnchor.MiddleCenter);
            Place(passwordTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(700, 56));

            joinPassword = MakeInput(passwordPanel, "Senha da sala", NetSession.MaxPassword);
            joinPassword.contentType = InputField.ContentType.Password;
            Place((RectTransform)joinPassword.transform, new Vector2(0.5f, 0.5f), new Vector2(0, 15), new Vector2(520, 70));
            joinPassword.onSubmit.AddListener(_ => ConfirmPassword());

            passwordError = Txt("Erro", passwordPanel, "", Bold, 22, Red, TextAnchor.MiddleCenter);
            Place(passwordError.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -55), new Vector2(700, 40));

            var ok = MakeButton(passwordPanel, new Option("Entrar", null, null, ButtonStyle.Verde), ConfirmPassword);
            Place((RectTransform)ok.transform, new Vector2(0.5f, 0.5f), new Vector2(-120, -145), new Vector2(260, 76));
            var back = MakeButton(passwordPanel, new Option("Voltar", null, null, ButtonStyle.Vermelho), () => ShowMenu());
            Place((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(140, -145), new Vector2(220, 76));
        }

        /// <summary>Pede a senha de uma sala. error != null mostra um aviso (ex.: senha errada).</summary>
        public void AskPassword(string roomName, string error, System.Action<string> onOk)
        {
            Only(passwordPanel);
            passwordTitle.text = CleanName(roomName, "Sala") ;
            passwordError.text = error ?? "";
            joinPassword.text = "";
            passwordOk = onOk;
            joinPassword.Select();
            joinPassword.ActivateInputField();
        }

        void ConfirmPassword()
        {
            string pwd = joinPassword.text ?? "";
            if (pwd.Length < NetSession.MinPassword) { passwordError.text = $"A senha tem pelo menos {NetSession.MinPassword} caracteres."; return; }
            passwordOk?.Invoke(pwd);
        }

        // ================================================================== estados

        public void Show(bool on) => root.gameObject.SetActive(on);

        void Only(RectTransform a, RectTransform b = null)
        {
            Show(true);
            foreach (var p in new[] { menuPanel, roomsPanel, lobbyPanel, busyPanel, createPanel, passwordPanel })
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
            lobbyNoteText.text = lobbyIsHost ? LobbyNote : "Código da sala";
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
