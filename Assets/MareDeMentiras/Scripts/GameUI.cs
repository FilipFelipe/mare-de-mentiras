using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static MareDeMentiras.UIKit;

namespace MareDeMentiras
{
    /// <summary>
    /// A mesa na tela. Recebe <see cref="GameEvent"/>s (do motor local ou da rede) e desenha
    /// do ponto de vista do jogador: ele sempre fica embaixo, os outros em volta.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        /// <summary>Jogador escolheu uma opção (promptId, índice).</summary>
        public System.Action<int, int> OnAnswer;
        /// <summary>Jogador clicou em "Sair".</summary>
        public System.Action OnLeave;

        RectTransform root;
        Snapshot snap;
        int Me => snap != null && snap.me >= 0 ? snap.me : 0;

        class Seat
        {
            public RectTransform root, cards, coinRt, bubbleAt;
            public Image glow, portrait, ring;
            public Text name, coins;
            public string sig;
            public bool bottom;
            public Vector2 basePos;
        }

        readonly List<Seat> seats = new List<Seat>();
        readonly HashSet<long> pendingFlip = new HashSet<long>();

        CanvasGroup bannerGroup;
        RectTransform bannerRt, bannerTextRt;
        Text bannerTitle, bannerSub, statusText;
        Image bannerCard;
        Coroutine bannerAnim, promptAnim;

        Text logText;
        readonly List<string> logLines = new List<string>();
        const int MaxLog = 8;

        Text promptText;
        RectTransform grid;
        CanvasGroup promptGroup;
        bool promptActive;
        int waitingFor = -1;
        string fixedStatus;

        GameObject rulesOverlay;

        // ================================================================== construção

        public void Init(Transform parent)
        {
            UIKit.Init();
            root = MakeCanvas(parent, "Mesa", 0);
            Build();
        }

        public void Show(bool on) => root.gameObject.SetActive(on);

        void Build()
        {
            Background(root);

            var title = Txt("Titulo", root, "Maré de Mentiras", Title, 60, Gold, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0, 1), new Vector2(30, -14), new Vector2(640, 84), new Vector2(0, 1));
            AddOutline(title.gameObject, new Color(0.15f, 0.07f, 0.02f, 0.9f), 2.5f);

            var leave = MakeButton(root, new Option("Sair", null, null, ButtonStyle.Vermelho), () => OnLeave?.Invoke());
            Place((RectTransform)leave.transform, new Vector2(1, 1), new Vector2(-22, -22), new Vector2(170, 64), new Vector2(1, 1));
            var rules = MakeButton(root, new Option("Regras", null, null, ButtonStyle.Azul), () => rulesOverlay.SetActive(true));
            Place((RectTransform)rules.transform, new Vector2(1, 1), new Vector2(-206, -22), new Vector2(170, 64), new Vector2(1, 1));

            BuildDeck();
            BuildBanner();

            // Assentos na tela: 0 = embaixo (você); os rivais seguem no sentido horário
            // (esquerda → topo → direita). As posições dependem do tamanho da mesa.
            seats.Add(BuildBottomSeat());
            for (int i = 1; i < Rules.MaxPlayers; i++) seats.Add(BuildSeat("Rival" + i, new Vector2(0.5f, 0.8f)));

            BuildLog();
            BuildPrompt();
            BuildRules();
        }

        void BuildDeck()
        {
            var deck = new GameObject("Baralho", typeof(RectTransform)).GetComponent<RectTransform>();
            deck.SetParent(root, false);
            Place(deck, new Vector2(0.5f, 0.5f), new Vector2(-440, 20), new Vector2(130, 200));
            for (int i = 0; i < 4; i++)
            {
                var c = Img("Carta", deck, Spr("Verso", folder: "Cards"), Color.white);
                Place(c.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-i * 3, 14 + i * 3), new Vector2(108, 140));
                c.rectTransform.localRotation = Quaternion.Euler(0, 0, -4 + i * 2);
                AddShadow(c.gameObject, 3);
            }
            var lbl = Txt("Rotulo", deck, "Baralho", Title, 26, Muted, TextAnchor.MiddleCenter);
            Place(lbl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(200, 30));
        }

        void BuildBanner()
        {
            var panel = Img("Anuncio", root, Spr("Painel", 26), Color.white, sliced: true);
            bannerRt = panel.rectTransform;
            Place(bannerRt, new Vector2(0.5f, 0.5f), new Vector2(70, 20), new Vector2(700, 190));
            bannerGroup = panel.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0;
            bannerGroup.blocksRaycasts = false;
            AddShadow(panel.gameObject, 8);

            bannerCard = Img("CartaAlegada", bannerRt, null, Color.white);
            Place(bannerCard.rectTransform, new Vector2(0, 0.5f), new Vector2(84, 0), new Vector2(124, 161));
            bannerCard.rectTransform.localRotation = Quaternion.Euler(0, 0, 4);
            AddShadow(bannerCard.gameObject, 4);

            bannerTextRt = new GameObject("Textos", typeof(RectTransform)).GetComponent<RectTransform>();
            bannerTextRt.SetParent(bannerRt, false);
            bannerTitle = Txt("Titulo", bannerTextRt, "", Title, 54, Gold, TextAnchor.LowerCenter);
            Stretch(bannerTitle.rectTransform, 0, 0.45f, 1, 1, 10, 0, -10, -10);
            AddOutline(bannerTitle.gameObject, new Color(0, 0, 0, 0.7f), 2);
            bannerSub = Txt("Sub", bannerTextRt, "", Body, 26, Cream, TextAnchor.UpperCenter);
            Stretch(bannerSub.rectTransform, 0, 0, 1, 0.45f, 14, 10, -14, -4);

            statusText = Txt("Status", root, "", Title, 32, Muted, TextAnchor.MiddleCenter);
            Place(statusText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(70, -110), new Vector2(760, 44));
            AddShadow(statusText.gameObject, 2, 0.8f);
        }

        Seat BuildSeat(string name, Vector2 anchor)
        {
            var s = new Seat();
            s.root = new GameObject("Assento" + name, typeof(RectTransform)).GetComponent<RectTransform>();
            s.root.SetParent(root, false);
            Place(s.root, anchor, Vector2.zero, new Vector2(300, 320));
            s.basePos = s.root.anchoredPosition;

            BuildPortrait(s, new Vector2(0, 78), 140);

            var plate = Img("Placa", s.root, Spr("Placa", 18), Color.white, sliced: true);
            Place(plate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(230, 50));
            AddShadow(plate.gameObject, 4);
            s.name = Txt("Nome", plate.rectTransform, "", Title, 32, Cream, TextAnchor.MiddleCenter);
            s.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(s.name.rectTransform, 0, 0, 1, 1, 0, 2, 0, 0);

            BuildCoinBadge(s, new Vector2(104, 128));

            s.cards = new GameObject("Cartas", typeof(RectTransform)).GetComponent<RectTransform>();
            s.cards.SetParent(s.root, false);
            Place(s.cards, new Vector2(0.5f, 0.5f), new Vector2(0, -100), new Vector2(240, 130));

            s.bubbleAt = new GameObject("Balao", typeof(RectTransform)).GetComponent<RectTransform>();
            s.bubbleAt.SetParent(s.root, false);
            // balão para o lado de dentro da tela
            Place(s.bubbleAt, new Vector2(0.5f, 0.5f), new Vector2(anchor.x < 0.3f ? 112 : -112, 150), Vector2.zero);
            return s;
        }

        Seat BuildBottomSeat()
        {
            var s = new Seat { bottom = true };
            s.root = new GameObject("AssentoVoce", typeof(RectTransform)).GetComponent<RectTransform>();
            s.root.SetParent(root, false);
            Place(s.root, new Vector2(0.5f, 0), new Vector2(-40, 10), new Vector2(760, 300), new Vector2(0.5f, 0));
            s.basePos = s.root.anchoredPosition;

            BuildPortrait(s, new Vector2(-270, 30), 124);

            var plate = Img("Placa", s.root, Spr("Placa", 18), Color.white, sliced: true);
            Place(plate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-270, -50), new Vector2(200, 46));
            AddShadow(plate.gameObject, 4);
            s.name = Txt("Nome", plate.rectTransform, "", Title, 30, Cream, TextAnchor.MiddleCenter);
            s.name.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(s.name.rectTransform, 0, 0, 1, 1, 0, 2, 0, 0);

            BuildCoinBadge(s, new Vector2(-270, -106));

            s.cards = new GameObject("Mao", typeof(RectTransform)).GetComponent<RectTransform>();
            s.cards.SetParent(s.root, false);
            Place(s.cards, new Vector2(0.5f, 0.5f), new Vector2(60, -10), new Vector2(420, 260));

            s.bubbleAt = new GameObject("Balao", typeof(RectTransform)).GetComponent<RectTransform>();
            s.bubbleAt.SetParent(s.root, false);
            Place(s.bubbleAt, new Vector2(0.5f, 0.5f), new Vector2(-270, 120), Vector2.zero);
            return s;
        }

        void BuildPortrait(Seat s, Vector2 pos, float size)
        {
            s.glow = Img("Brilho", s.root, Spr("Brilho"), new Color(1f, 0.8f, 0.3f, 0));
            Place(s.glow.rectTransform, new Vector2(0.5f, 0.5f), pos, Vector2.one * size * 1.9f);
            s.glow.raycastTarget = false;

            var shadow = Img("Sombra", s.root, Spr("Brilho"), new Color(0, 0, 0, 0.6f));
            Place(shadow.rectTransform, new Vector2(0.5f, 0.5f), pos + new Vector2(4, -8), Vector2.one * size * 1.3f);

            s.portrait = Img("Retrato", s.root, null, Color.white);
            Place(s.portrait.rectTransform, new Vector2(0.5f, 0.5f), pos, Vector2.one * size);
            s.ring = Img("Anel", s.root, Spr("Anel"), Color.white);
            Place(s.ring.rectTransform, new Vector2(0.5f, 0.5f), pos, Vector2.one * size * 1.16f);
        }

        void BuildCoinBadge(Seat s, Vector2 pos)
        {
            var badge = Img("Moedas", s.root, Spr("Painel", 22), Color.white, sliced: true);
            s.coinRt = badge.rectTransform;
            Place(s.coinRt, new Vector2(0.5f, 0.5f), pos, new Vector2(104, 52));
            AddShadow(badge.gameObject, 3);
            var icon = Img("Icone", s.coinRt, Spr("Moeda"), Color.white);
            Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(26, 1), new Vector2(40, 40));
            s.coins = Txt("Qtd", s.coinRt, "0", Bold, 30, Gold, TextAnchor.MiddleCenter);
            Stretch(s.coins.rectTransform, 0, 0, 1, 1, 46, 0, -8, 0);
        }

        void BuildLog()
        {
            var panel = Img("Diario", root, Spr("Painel", 26), Color.white, sliced: true);
            Place(panel.rectTransform, new Vector2(0, 0), new Vector2(18, 18), new Vector2(480, 290), new Vector2(0, 0));
            AddShadow(panel.gameObject, 6);
            var t = Txt("Titulo", panel.rectTransform, "Diário de Bordo", Title, 30, Gold, TextAnchor.UpperLeft);
            Stretch(t.rectTransform, 0, 0, 1, 1, 22, 0, -22, -10);
            logText = Txt("Log", panel.rectTransform, "", Body, 19, Cream, TextAnchor.LowerLeft);
            Stretch(logText.rectTransform, 0, 0, 1, 1, 22, 16, -18, -50);
        }

        void BuildPrompt()
        {
            var panel = Img("Decisao", root, Spr("Pergaminho", 34), Color.white, sliced: true);
            Place(panel.rectTransform, new Vector2(1, 0), new Vector2(-18, 18), new Vector2(630, 350), new Vector2(1, 0));
            AddShadow(panel.gameObject, 8);
            promptGroup = panel.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0;
            promptGroup.blocksRaycasts = false;

            promptText = Txt("Pergunta", panel.rectTransform, "", Bold, 25, Ink, TextAnchor.MiddleCenter);
            Stretch(promptText.rectTransform, 0, 1, 1, 1, 26, -92, -26, -12);

            grid = new GameObject("Botoes", typeof(RectTransform), typeof(GridLayoutGroup)).GetComponent<RectTransform>();
            grid.SetParent(panel.rectTransform, false);
            Stretch(grid, 0, 0, 1, 1, 18, 16, -18, -96);
            var g = grid.GetComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(287, 70);
            g.spacing = new Vector2(12, 9);
            g.childAlignment = TextAnchor.UpperCenter;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
        }

        void BuildRules()
        {
            rulesOverlay = new GameObject("Regras", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)rulesOverlay.transform;
            rt.SetParent(root, false);
            Stretch(rt, 0, 0, 1, 1);
            rulesOverlay.GetComponent<Image>().color = new Color(0, 0, 0, 0.72f);
            rulesOverlay.GetComponent<Button>().onClick.AddListener(() => rulesOverlay.SetActive(false));

            var panel = Img("Painel", rt, Spr("Painel", 26), Color.white, sliced: true);
            Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 680));
            var t = Txt("Titulo", panel.rectTransform, "Como Jogar", Title, 60, Gold, TextAnchor.UpperCenter);
            Stretch(t.rectTransform, 0, 0, 1, 1, 0, 0, 0, -18);
            var body = Txt("Texto", panel.rectTransform, Rules.Reference, Body, 26, Cream, TextAnchor.UpperLeft);
            body.lineSpacing = 1.05f;
            Stretch(body.rectTransform, 0, 0, 1, 1, 50, 100, -50, -100);
            var close = MakeButton(panel.rectTransform, new Option("Fechar", null, null, ButtonStyle.Azul), () => rulesOverlay.SetActive(false));
            Place((RectTransform)close.transform, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(200, 62), new Vector2(0.5f, 0));
            rulesOverlay.SetActive(false);
        }

        // ================================================================== eventos

        public void Apply(GameEvent ev)
        {
            switch (ev.type)
            {
                case Ev.Reset:
                    logLines.Clear();
                    logText.text = "";
                    pendingFlip.Clear();
                    foreach (var s in seats) s.sig = null;
                    waitingFor = -1;
                    fixedStatus = null;
                    HideBanner();
                    ClearPrompt();
                    break;
                case Ev.State:
                    snap = ev.state;
                    Refresh();
                    break;
                case Ev.Log:
                    AddLog(Resolve(ev.text, true));
                    break;
                case Ev.Announce:
                    Announce(ev.text, Resolve(ev.text2, false), ev.role >= 0 ? (Role?)ev.role : null);
                    break;
                case Ev.Bubble:
                    Bubble(ev.a, ev.text, ev.b switch { 1 => Red, 2 => Green, 3 => Blue, _ => Gold });
                    break;
                case Ev.Coin:
                    CoinFx(ev.a, ev.b);
                    break;
                case Ev.CardLost:
                    pendingFlip.Add(((long)ev.a << 8) | (uint)ev.b);
                    break;
                case Ev.Prompt:
                {
                    int id = ev.promptId;
                    ShowPrompt(Resolve(ev.text, false), ev.options, i => OnAnswer?.Invoke(id, i));
                    break;
                }
                case Ev.ClearPrompt:
                    ClearPrompt();
                    break;
                case Ev.Waiting:
                    waitingFor = ev.a;
                    UpdateStatus();
                    break;
                case Ev.Ended:
                    bool won = ev.a == Me;
                    Announce(won ? "Vitória!" : "Fim de jogo",
                             won ? "Você domina os sete mares!" : Resolve("{P" + ev.a + "} ficou com o tesouro.", false));
                    Bubble(ev.a, won ? "Yo-ho-ho!" : "Hahaha!", Gold);
                    break;
            }
        }

        /// <summary>Texto fixo no centro da mesa (ex.: "Aguardando o anfitrião…"). null remove.</summary>
        public void SetStatus(string text)
        {
            fixedStatus = text;
            UpdateStatus();
        }

        /// <summary>Troca {P3}/{p3} por "Você"/"você" ou pelo nome do jogador.</summary>
        string Resolve(string s, bool colored)
        {
            if (string.IsNullOrEmpty(s) || snap == null) return s;
            for (int i = 0; i < snap.players.Count; i++)
            {
                bool me = i == Me;
                string name = snap.players[i].name;
                string cap = me ? "Você" : name, low = me ? "você" : name;
                if (colored)
                {
                    string col = me ? "#7FD4FF" : "#FFE0A0";
                    cap = $"<color={col}>{cap}</color>";
                    low = $"<color={col}>{low}</color>";
                }
                s = s.Replace("{P" + i + "}", cap).Replace("{p" + i + "}", low);
            }
            return s;
        }

        /// <summary>Posições (âncoras na tela) dos rivais para cada quantidade de rivais (1 a 5).</summary>
        static readonly Vector2[][] Layouts =
        {
            new Vector2[0],
            new[] { new Vector2(0.5f, 0.80f) },
            new[] { new Vector2(0.16f, 0.66f), new Vector2(0.84f, 0.66f) },
            new[] { new Vector2(0.105f, 0.60f), new Vector2(0.5f, 0.80f), new Vector2(0.895f, 0.60f) },
            new[] { new Vector2(0.105f, 0.58f), new Vector2(0.33f, 0.80f), new Vector2(0.67f, 0.80f), new Vector2(0.895f, 0.58f) },
            new[] { new Vector2(0.105f, 0.56f), new Vector2(0.27f, 0.80f), new Vector2(0.5f, 0.80f), new Vector2(0.73f, 0.80f), new Vector2(0.895f, 0.56f) },
        };
        int layoutFor = -1;

        void ApplyLayout(int players)
        {
            if (players == layoutFor) return;
            layoutFor = players;
            var pos = Layouts[Mathf.Clamp(players - 1, 0, Layouts.Length - 1)];
            for (int k = 1; k < seats.Count; k++)
            {
                if (k - 1 >= pos.Length) continue;
                var s = seats[k];
                var a = pos[k - 1];
                s.root.anchorMin = s.root.anchorMax = a;
                s.root.anchoredPosition = s.basePos = Vector2.zero;
                // balão para o lado de dentro da tela
                s.bubbleAt.anchoredPosition = new Vector2(a.x < 0.3f ? 112 : -112, 150);
            }
        }

        Seat SeatOf(int playerIdx)
        {
            if (snap == null || playerIdx < 0 || playerIdx >= snap.players.Count) return null;
            int n = snap.players.Count;
            int si = ((playerIdx - Me) % n + n) % n;
            return si < seats.Count ? seats[si] : null;
        }

        // ================================================================== atualização

        void Refresh()
        {
            if (snap == null) return;
            ApplyLayout(snap.players.Count);
            foreach (var s in seats) s.root.gameObject.SetActive(false);

            for (int i = 0; i < snap.players.Count; i++)
            {
                var p = snap.players[i];
                var s = SeatOf(i);
                if (s == null) continue;
                s.root.gameObject.SetActive(true);
                s.name.text = p.name;
                s.coins.text = p.coins.ToString();
                s.portrait.sprite = Spr(p.portrait);
                bool alive = p.Alive;
                s.portrait.color = alive ? Color.white : new Color(0.35f, 0.35f, 0.38f);
                s.ring.color = alive ? Color.white : new Color(0.5f, 0.5f, 0.5f);
                s.name.color = alive ? Cream : Muted;

                string sig = p.portrait + ":";
                foreach (var c in p.cards) sig += c.role + (c.revealed ? "R" : "H");
                if (sig != s.sig)
                {
                    s.sig = sig;
                    RebuildCards(s, i, p);
                }
            }
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (statusText == null) return;
            if (promptActive) { statusText.text = ""; return; }
            if (fixedStatus != null) { statusText.text = fixedStatus; return; }
            if (snap == null) { statusText.text = ""; return; }
            if (waitingFor >= 0 && waitingFor != Me && waitingFor < snap.players.Count)
                statusText.text = $"Aguardando {snap.players[waitingFor].name}…";
            else if (snap.current >= 0 && snap.current != Me && snap.current < snap.players.Count)
                statusText.text = $"Vez de {snap.players[snap.current].name}…";
            else statusText.text = "";
        }

        void RebuildCards(Seat s, int playerIdx, PlayerView p)
        {
            foreach (Transform c in s.cards) { c.gameObject.SetActive(false); Destroy(c.gameObject); }

            int n = p.cards.Count;
            for (int k = 0; k < n; k++)
            {
                var card = p.cards[k];
                float t = k - (n - 1) / 2f;
                Vector2 size = s.bottom ? new Vector2(180, 234) : new Vector2(94, 122);
                float spacing = s.bottom ? 160 : 82;
                var view = MakeCardView(s.cards, card, size);
                view.anchoredPosition = new Vector2(t * spacing, -Mathf.Abs(t) * (s.bottom ? 10 : 6));
                view.localRotation = Quaternion.Euler(0, 0, -t * (s.bottom ? 6 : 8));
                if (s.bottom) view.gameObject.AddComponent<Hover>().Setup(1.07f, 26);

                if (pendingFlip.Remove(((long)playerIdx << 8) | (uint)k))
                {
                    StartCoroutine(FlipIn(view));
                    StartCoroutine(Shake(s));
                }
            }
        }

        RectTransform MakeCardView(Transform parent, CardView card, Vector2 size)
        {
            bool faceUp = card.role >= 0;
            var sprite = Spr(faceUp ? ((Role)card.role).ToString() : "Verso", folder: "Cards");
            var img = Img("Carta", parent, sprite, sprite != null ? Color.white : (faceUp ? Rules.RoleColor((Role)card.role) : new Color(0.36f, 0.23f, 0.14f)));
            img.preserveAspect = true;
            var rt = img.rectTransform;
            Place(rt, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            AddShadow(img.gameObject, size.x > 120 ? 6 : 4);

            if (sprite == null)
            {
                var n = Txt("Nome", rt, faceUp ? Rules.RoleName((Role)card.role) : "?", Title, size.x > 120 ? 30 : 22, Color.white, TextAnchor.MiddleCenter);
                Stretch(n.rectTransform, 0, 0, 1, 1);
            }
            if (card.revealed)
            {
                img.color = new Color(0.42f, 0.42f, 0.46f);
                var band = Img("Faixa", rt, null, new Color(0.45f, 0.06f, 0.05f, 0.92f));
                Stretch(band.rectTransform, -0.04f, 0.42f, 1.04f, 0.58f);
                band.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
                var x = Txt("Fora", band.rectTransform, "PERDIDA", Title, size.x > 120 ? 34 : 22, new Color(1f, 0.88f, 0.84f), TextAnchor.MiddleCenter);
                Stretch(x.rectTransform, 0, 0, 1, 1);
            }
            return rt;
        }

        void Update()
        {
            if (snap == null) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < snap.players.Count; i++)
            {
                var s = SeatOf(i);
                if (s == null) continue;
                bool active = snap.current == i && snap.players[i].Alive;
                var c = s.glow.color;
                float target = active ? 0.55f + 0.3f * Mathf.Sin(t * 4f) : 0f;
                c.a = Mathf.MoveTowards(c.a, target, Time.unscaledDeltaTime * 3f);
                s.glow.color = c;
                s.glow.rectTransform.localScale = Vector3.one * (1f + 0.05f * Mathf.Sin(t * 4f));
            }
        }

        // ================================================================== efeitos

        void Announce(string title, string sub, Role? role = null)
        {
            bannerTitle.text = title;
            bannerSub.text = sub;
            var sp = role.HasValue ? Spr(role.Value.ToString(), folder: "Cards") : null;
            bannerCard.gameObject.SetActive(sp != null);
            bannerCard.sprite = sp;
            Stretch(bannerTextRt, 0, 0, 1, 1, sp != null ? 160 : 10, 0, -10, 0);

            if (bannerAnim != null) StopCoroutine(bannerAnim);
            if (!isActiveAndEnabled) { bannerGroup.alpha = 1; bannerRt.localScale = Vector3.one; return; }
            bannerAnim = StartCoroutine(Tween(0.28f, k =>
            {
                bannerGroup.alpha = Mathf.Min(1, k * 1.6f);
                bannerRt.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, BackOut(k));
            }));
        }

        void HideBanner()
        {
            if (bannerAnim != null) StopCoroutine(bannerAnim);
            if (bannerGroup != null) bannerGroup.alpha = 0;
        }

        void Bubble(int playerIdx, string text, Color color)
        {
            var s = SeatOf(playerIdx);
            if (s == null || !isActiveAndEnabled) return;
            var img = Img("Fala", s.root, Spr("Balao", 30), Color.white, sliced: true);
            var rt = img.rectTransform;
            float w = Mathf.Max(150, 34 + text.Length * 17);
            Place(rt, new Vector2(0.5f, 0.5f), s.bubbleAt.anchoredPosition, new Vector2(w, 66), new Vector2(0.5f, 0f));
            AddShadow(img.gameObject, 4);
            var t = Txt("Texto", rt, text, Title, 36, color, TextAnchor.MiddleCenter);
            Stretch(t.rectTransform, 0, 0, 1, 1, 8, 2, -8, 0);
            AddOutline(t.gameObject, new Color(0.15f, 0.08f, 0.04f, 0.9f), 1.5f);
            var g = img.gameObject.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            StartCoroutine(BubbleLife(rt, g));
        }

        IEnumerator BubbleLife(RectTransform rt, CanvasGroup g)
        {
            yield return Tween(0.22f, k => rt.localScale = Vector3.one * BackOut(k));
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Tween(0.3f, k => { g.alpha = 1 - k; rt.anchoredPosition += Vector2.up * 0.6f; });
            Destroy(rt.gameObject);
        }

        void CoinFx(int playerIdx, int delta)
        {
            var s = SeatOf(playerIdx);
            if (s == null || delta == 0 || !isActiveAndEnabled) return;
            var t = Txt("Moedas", s.root, (delta > 0 ? "+" : "") + delta, Title, 46, delta > 0 ? Gold : Red, TextAnchor.MiddleCenter);
            var rt = t.rectTransform;
            Place(rt, new Vector2(0.5f, 0.5f), s.coinRt.anchoredPosition + (s.bottom ? new Vector2(110, 0) : new Vector2(0, 30)), new Vector2(140, 60));
            AddOutline(t.gameObject, new Color(0.1f, 0.05f, 0.02f, 1f), 2);
            StartCoroutine(FloatUp(rt, t, rt.anchoredPosition));
        }

        IEnumerator FloatUp(RectTransform rt, Text t, Vector2 start)
        {
            yield return Tween(1.2f, k =>
            {
                rt.anchoredPosition = start + Vector2.up * (80 * k);
                rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.5f, 1.2f, k / 0.15f) : Mathf.Lerp(1.2f, 1f, (k - 0.15f) * 3));
                var c = t.color; c.a = k < 0.6f ? 1 : 1 - (k - 0.6f) / 0.4f; t.color = c;
            });
            Destroy(rt.gameObject);
        }

        IEnumerator FlipIn(RectTransform rt)
        {
            yield return Tween(0.18f, k => rt.localScale = new Vector3(Mathf.Lerp(1, 0, k), 1.1f, 1));
            yield return Tween(0.25f, k => rt.localScale = new Vector3(BackOut(k), Mathf.Lerp(1.1f, 1, k), 1));
            rt.localScale = Vector3.one;
        }

        IEnumerator Shake(Seat s)
        {
            yield return Tween(0.4f, k =>
            {
                float a = (1 - k) * 12;
                s.root.anchoredPosition = s.basePos + new Vector2(Random.Range(-a, a), Random.Range(-a, a));
            });
            s.root.anchoredPosition = s.basePos;
        }

        // ================================================================== decisão e diário

        public void ShowPrompt(string question, IList<Option> options, System.Action<int> onChoose)
        {
            ClearButtons();
            promptActive = true;
            promptText.text = question;
            SetPromptVisible(true);
            UpdateStatus();
            bool answered = false;
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                var b = MakeButton(grid, options[i], () =>
                {
                    if (answered) return;   // evita clique duplo
                    answered = true;
                    onChoose(idx);
                });
                b.transform.localScale = Vector3.zero;
                if (isActiveAndEnabled) StartCoroutine(PopIn(b.transform, i * 0.04f));
                else b.transform.localScale = Vector3.one;
            }
        }

        IEnumerator PopIn(Transform t, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            yield return Tween(0.2f, k => { if (t) t.localScale = Vector3.one * BackOut(k); });
        }

        public void ClearPrompt()
        {
            ClearButtons();
            promptActive = false;
            if (promptGroup != null) SetPromptVisible(false);
            UpdateStatus();
        }

        void SetPromptVisible(bool show)
        {
            if (promptAnim != null) StopCoroutine(promptAnim);
            var rt = (RectTransform)promptGroup.transform;
            float from = promptGroup.alpha, to = show ? 1 : 0;
            promptGroup.blocksRaycasts = show;
            if (!isActiveAndEnabled)
            {
                promptGroup.alpha = to;
                rt.anchoredPosition = new Vector2(-18, 18);
                return;
            }
            promptAnim = StartCoroutine(Tween(show ? 0.22f : 0.15f, k =>
            {
                float a = Mathf.Lerp(from, to, k);
                promptGroup.alpha = a;
                rt.anchoredPosition = new Vector2(-18, 18 - 40 * (1 - a));
            }));
        }

        void ClearButtons()
        {
            if (grid == null) return;
            foreach (Transform c in grid) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
        }

        void AddLog(string line)
        {
            logLines.Add(line);
            while (logLines.Count > MaxLog) logLines.RemoveAt(0);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < logLines.Count; i++)
            {
                int alpha = (int)Mathf.Lerp(110, 255, (i + 1f) / logLines.Count);
                sb.Append($"<color=#FFFFFF{alpha:X2}>").Append(logLines[i]).Append("</color>");
                if (i < logLines.Count - 1) sb.Append('\n');
            }
            logText.text = sb.ToString();
        }
    }
}
