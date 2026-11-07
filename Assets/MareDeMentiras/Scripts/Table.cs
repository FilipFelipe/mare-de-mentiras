using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MareDeMentiras
{
    /// <summary>
    /// Motor da partida (roda só no anfitrião ou no modo solo): turnos, desafios, bloqueios e IA.
    /// Não conhece tela nem rede; tudo sai por <see cref="ITableOutput"/>.
    /// </summary>
    public class Table : MonoBehaviour
    {
        [Tooltip("Pausa (segundos) entre as jogadas da IA")]
        public float aiDelay = 1.0f;

        public readonly List<Player> players = new List<Player>();
        public int CurrentIndex { get; private set; } = -1;
        public bool Running { get; private set; }

        ITableOutput output;
        readonly List<Role> deck = new List<Role>();
        int answer = -1, pendingPrompt = -1, pendingPlayer = -1, promptSeq;
        Option[] pendingOpts;
        float baseDelay = -1;
        int copies = 3;

        // ================================================================== API

        public void Begin(IList<SeatInfo> seats, ITableOutput output)
        {
            StopAllCoroutines();
            if (baseDelay < 0) baseDelay = aiDelay;
            aiDelay = baseDelay;
            this.output = output;

            copies = Rules.CopiesFor(seats.Count);
            deck.Clear();
            foreach (Role r in System.Enum.GetValues(typeof(Role)))
                for (int i = 0; i < copies; i++) deck.Add(r);
            Shuffle(deck);

            players.Clear();
            foreach (var s in seats)
            {
                var p = new Player { name = s.name, isHuman = s.human, portrait = s.portrait, coins = Rules.StartingCoins };
                for (int c = 0; c < Rules.CardsPerPlayer; c++) p.cards.Add(new Card(Draw()));
                players.Add(p);
            }

            CurrentIndex = -1;
            pendingPrompt = -1;
            Running = true;
            output.Broadcast(GameEvent.Of(Ev.Reset));
            output.StateChanged();
            Log("Bem-vindo à mesa! Seja o último pirata com cartas.");
            Announce("Maré de Mentiras", "Blefe, desafie e fique com o tesouro!");
            StartCoroutine(GameLoop());
        }

        public void Stop()
        {
            StopAllCoroutines();
            Running = false;
        }

        /// <summary>A mesa do ponto de vista de um jogador (cartas dos outros escondidas).</summary>
        public Snapshot Snapshot(int viewer)
        {
            var s = new Snapshot { me = viewer, current = CurrentIndex };
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                var pv = new PlayerView { name = p.name, portrait = p.portrait, coins = p.coins, human = p.isHuman };
                foreach (var c in p.cards)
                    pv.cards.Add(new CardView { role = c.revealed || i == viewer ? (int)c.role : -1, revealed = c.revealed });
                s.players.Add(pv);
            }
            return s;
        }

        public void SubmitAnswer(int playerIdx, int promptId, int choice)
        {
            if (promptId != pendingPrompt || playerIdx != pendingPlayer || pendingOpts == null) return;
            if (choice < 0 || choice >= pendingOpts.Length || !pendingOpts[choice].enabled) return;
            answer = choice;
        }

        /// <summary>Um jogador saiu: a IA assume o lugar dele.</summary>
        public void PlayerLeft(int idx)
        {
            if (idx < 0 || idx >= players.Count || !players[idx].isHuman) return;
            players[idx].isHuman = false;
            Log($"{Who(idx)} saiu da mesa. Um pirata da IA assume o lugar.");
            output.StateChanged();
        }

        // ================================================================== fluxo

        IEnumerator GameLoop()
        {
            int idx = 0;
            var announcedOut = new HashSet<int>();
            yield return new WaitForSeconds(0.8f);

            while (players.Count(p => p.Alive) > 1)
            {
                var p = players[idx];
                if (p.Alive)
                {
                    CurrentIndex = idx;
                    output.StateChanged();
                    yield return PlayTurn(p);
                    output.StateChanged();

                    for (int i = 0; i < players.Count; i++)
                    {
                        if (players[i].Alive || !players[i].isHuman || !announcedOut.Add(i)) continue;
                        Log($"<color=#FF7070>{Who(i)} foi eliminado.</color>");
                        output.SendTo(i, new GameEvent { type = Ev.Announce, text = "Eliminado!", text2 = "Seus tripulantes foram ao mar… Assista ao fim." });
                    }
                    if (!players.Any(pl => pl.isHuman && pl.Alive)) aiDelay = baseDelay * 0.5f;
                }
                idx = (idx + 1) % players.Count;
            }

            CurrentIndex = -1;
            Running = false;
            int winner = players.FindIndex(p => p.Alive);
            Log($"<color=#FFD050>{Who(winner)} venceu a partida!</color>");
            output.StateChanged();
            output.Finished(winner);
        }

        IEnumerator PlayTurn(Player p)
        {
            int pi = players.IndexOf(p);
            Log($"<color=#8899AA>— Vez de {Who(pi)} —</color>");

            ActionType action;
            Player target;
            if (p.isHuman)
            {
                var box = new Box<(ActionType, Player)>();
                yield return HumanChooseAction(p, box);
                (action, target) = box.v;
            }
            else
            {
                yield return Pause();
                (action, target) = AiChooseAction(p);
            }
            int ti = target != null ? players.IndexOf(target) : -1;

            int cost = Rules.Cost(action);
            p.coins -= cost;
            Coin(p, -cost);
            var claim = Rules.ClaimedRole(action);
            Log($"{Who(pi)} usa <b>{Rules.ActionName(action)}</b>" +
                (claim.HasValue ? $", alegando ser <b>{Rules.RoleName(claim.Value)}</b>" : "") +
                (target != null ? $", contra {Who(ti)}" : "") + ".");
            Announce(Rules.ActionName(action),
                     Who(pi) + (claim.HasValue ? $" diz ser {Rules.RoleName(claim.Value)}" : $": {Rules.ActionShort(action)}") +
                     (target != null ? $" → {Low(ti)}" : ""),
                     claim);
            Bubble(p, Rules.ActionName(action) + "!", 0);
            output.StateChanged();
            yield return Pause(0.7f);

            // 1) Alguém duvida do personagem alegado?
            if (claim.HasValue)
            {
                var ok = new Box<bool>();
                yield return ChallengePhase(p, claim.Value, action, false, target, ok);
                if (!ok.v) { Log("A ação fracassa."); yield break; }
            }

            // 2) O alvo bloqueia?
            var blocker = Rules.Blocker(action);
            if (blocker.HasValue && target != null && target.Alive)
            {
                bool block;
                if (target.isHuman)
                {
                    yield return Ask(target, $"{Who(pi)} usa {Rules.ActionName(action)} contra você!", 1,
                                     new Option("Bloquear", $"{Rules.RoleName(blocker.Value)} ·{HonestyTag(target, blocker.Value)}", null, ButtonStyle.Azul),
                                     new Option("Aceitar", "deixar acontecer"));
                    block = answer == 0;
                }
                else
                {
                    yield return Pause(0.6f);
                    block = AiWantsBlock(target, action);
                }

                if (block)
                {
                    Log($"{Who(ti)} bloqueia, alegando ser <b>{Rules.RoleName(blocker.Value)}</b>.");
                    Bubble(target, "Bloqueio!", 3);
                    Announce("Bloqueio!", $"{Who(ti)} diz ser {Rules.RoleName(blocker.Value)}", blocker);
                    yield return Pause(0.7f);
                    var ok = new Box<bool>();
                    yield return ChallengePhase(target, blocker.Value, action, true, p, ok);
                    if (ok.v) { Log("O bloqueio se mantém. A ação foi impedida."); yield break; }
                    Log("O bloqueio caiu!");
                }
            }

            if (target != null && !target.Alive) yield break;
            yield return Resolve(p, action, target);
        }

        IEnumerator Resolve(Player p, ActionType action, Player target)
        {
            int pi = players.IndexOf(p), ti = target != null ? players.IndexOf(target) : -1;
            switch (action)
            {
                case ActionType.Pescar:
                    p.coins += 1;
                    Coin(p, 1);
                    Log($"{Who(pi)} pesca 1 dobrão.");
                    break;
                case ActionType.Tributo:
                    p.coins += Rules.TributoGain;
                    Coin(p, Rules.TributoGain);
                    Log($"{Who(pi)} recebe {Rules.TributoGain} dobrões de tributo.");
                    break;
                case ActionType.Desviar:
                    int amt = Mathf.Min(Rules.DesviarAmount, target.coins);
                    target.coins -= amt;
                    p.coins += amt;
                    Coin(target, -amt);
                    Coin(p, amt);
                    Log($"{Who(pi)} desvia {amt} dobrão(ões) de {Who(ti)}.");
                    break;
                case ActionType.Maldicao:
                case ActionType.Motim:
                    yield return LoseInfluence(target);
                    break;
                case ActionType.Recrutar:
                    yield return Exchange(p);
                    break;
            }
            output.StateChanged();
            yield return Pause(0.5f);
        }

        /// <summary>Pergunta aos outros jogadores se alguém desafia a alegação. result.v = alegação se manteve.</summary>
        IEnumerator ChallengePhase(Player claimant, Role role, ActionType action, bool isBlock, Player victim, Box<bool> result)
        {
            int ci = players.IndexOf(claimant);
            Player challenger = null;
            for (int k = 1; k < players.Count && challenger == null; k++)
            {
                var other = players[(ci + k) % players.Count];
                if (!other.Alive) continue;

                if (other.isHuman)
                {
                    yield return Ask(other, $"{Who(ci)} diz ser {Rules.RoleName(role)}. Você acredita?", 1,
                                     new Option("Desafiar!", "é mentira!", null, ButtonStyle.Vermelho),
                                     new Option("Deixar passar", "acredito"));
                    if (answer == 0) challenger = other;
                }
                else if (AiWantsChallenge(other, role, action, isBlock, victim))
                {
                    challenger = other;
                }
            }

            if (challenger == null) { result.v = true; yield break; }

            int chi = players.IndexOf(challenger);
            Log($"<color=#FFB060>{Who(chi)} desafia {Who(ci)}!</color>");
            Bubble(challenger, "Mentira!", 1);
            Announce("Desafio!", $"{Who(chi)} duvida de {Low(ci)}…");
            yield return Pause(1.2f);

            var card = claimant.cards.FirstOrDefault(c => !c.revealed && c.role == role);
            if (card != null)
            {
                Log($"{Who(ci)} mostra o <b>{Rules.RoleName(role)}</b>. Era verdade!");
                Announce("Era verdade!", $"{Who(ci)} tinha o {Rules.RoleName(role)}. A carta é trocada.", role);
                Bubble(claimant, "Viu só?", 2);
                deck.Add(card.role);
                Shuffle(deck);
                card.role = Draw();
                output.StateChanged();
                yield return Pause(0.9f);
                yield return LoseInfluence(challenger);
                result.v = true;
            }
            else
            {
                Log($"<color=#FF7070>Blefe!</color> {Who(ci)} não tinha {Rules.RoleName(role)}.");
                Announce("Blefe!", $"{Who(ci)} não tinha o {Rules.RoleName(role)}.");
                Bubble(claimant, "Argh!", 1);
                yield return Pause(0.9f);
                yield return LoseInfluence(claimant);
                result.v = false;
            }
        }

        IEnumerator LoseInfluence(Player p)
        {
            int pi = players.IndexOf(p);
            var hidden = p.Hidden;
            if (hidden.Count == 0) yield break;

            Card lost;
            if (hidden.Count == 1) lost = hidden[0];
            else if (p.isHuman)
            {
                yield return Ask(p, "Você perde um tripulante. Qual carta revelar?", 0,
                                 hidden.Select(c => new Option(Rules.RoleName(c.role), "revelar esta", null, ButtonStyle.Vermelho)).ToArray());
                lost = hidden[answer];
            }
            else lost = hidden.OrderBy(c => AiValue(c.role)).First();

            lost.revealed = true;
            output.Broadcast(new GameEvent { type = Ev.CardLost, a = pi, b = p.cards.IndexOf(lost) });
            Log($"{Who(pi)} perde o <b>{Rules.RoleName(lost.role)}</b>.");
            if (!p.Alive)
            {
                Log($"<color=#FF7070>{Who(pi)} está fora do jogo!</color>");
                Bubble(p, "Ao mar!", 1);
            }
            output.StateChanged();
            yield return Pause(0.8f);
        }

        IEnumerator Exchange(Player p)
        {
            var hidden = p.Hidden;
            var drawn = Draw();
            int give;

            if (p.isHuman)
            {
                var opts = hidden.Select(c => new Option(Rules.RoleName(c.role), "devolver esta")).ToList();
                opts.Add(new Option(Rules.RoleName(drawn), "devolver a nova", null, ButtonStyle.Azul));
                yield return Ask(p, $"Você comprou {Rules.RoleName(drawn)}. Qual carta volta para o baralho?", opts.Count - 1, opts.ToArray());
                give = answer;
            }
            else
            {
                var all = hidden.Select(c => c.role).Append(drawn).ToList();
                give = all.IndexOf(all.OrderBy(AiValue).First());
            }

            if (give < hidden.Count)
            {
                deck.Add(hidden[give].role);
                hidden[give].role = drawn;
            }
            else deck.Add(drawn);
            Shuffle(deck);
            Log($"{Who(players.IndexOf(p))} trocou uma carta com o baralho.");
        }

        // ================================================================== decisões humanas

        /// <summary>Envia uma pergunta ao jogador e espera a resposta. Se ele sair, usa defaultIdx.</summary>
        IEnumerator Ask(Player p, string question, int defaultIdx, params Option[] opts)
        {
            int idx = players.IndexOf(p);
            answer = -1;
            pendingPrompt = ++promptSeq;
            pendingPlayer = idx;
            pendingOpts = opts;
            output.Broadcast(new GameEvent { type = Ev.Waiting, a = idx });
            output.SendTo(idx, new GameEvent { type = Ev.Prompt, text = question, options = opts, promptId = pendingPrompt });

            while (answer < 0)
            {
                if (!p.isHuman)
                {
                    answer = defaultIdx >= 0 && defaultIdx < opts.Length && opts[defaultIdx].enabled
                        ? defaultIdx : System.Array.FindIndex(opts, o => o.enabled);
                    break;
                }
                yield return null;
            }

            pendingPrompt = -1;
            output.SendTo(idx, GameEvent.Of(Ev.ClearPrompt));
            output.Broadcast(new GameEvent { type = Ev.Waiting, a = -1 });
        }

        IEnumerator HumanChooseAction(Player p, Box<(ActionType, Player)> result)
        {
            var actions = (ActionType[])System.Enum.GetValues(typeof(ActionType));
            bool forced = p.coins >= Rules.ForcedMotimAt;

            while (true)
            {
                var opts = actions.Select(a =>
                {
                    var claim = Rules.ClaimedRole(a);
                    string tag = claim.HasValue ? " ·" + HonestyTag(p, claim.Value) : "";
                    bool enabled = forced ? a == ActionType.Motim : p.coins >= Rules.Cost(a);
                    return new Option(Rules.ActionName(a), Rules.ActionShort(a) + tag, "Icone_" + a,
                                      a == ActionType.Motim ? ButtonStyle.Vermelho : ButtonStyle.Madeira, enabled);
                }).ToArray();

                yield return Ask(p, forced ? "10+ dobrões: o Motim é obrigatório!" : "Sua vez! Escolha uma ação.", 0, opts);
                var action = actions[answer];

                Player target = null;
                if (Rules.NeedsTarget(action))
                {
                    var opps = players.Where(o => o != p && o.Alive).ToList();
                    var topts = opps.Select(o => new Option(o.name, $"{o.coins} dobrões · {o.Influence} carta(s)", o.portrait)).ToList();
                    topts.Add(new Option("Voltar", null, null, ButtonStyle.Azul));
                    yield return Ask(p, $"{Rules.ActionName(action)}: escolha o alvo", 0, topts.ToArray());
                    if (answer == opps.Count) continue;
                    target = opps[answer];
                }

                result.v = (action, target);
                yield break;
            }
        }

        static string HonestyTag(Player p, Role r) =>
            p.Has(r) ? " <color=#A8FFA8>verdade</color>" : " <color=#FFB0A0>blefe</color>";

        // ================================================================== IA

        static int AiValue(Role r) => r switch
        {
            Role.Bruxa => 4,
            Role.Capitao => 3,
            Role.Contrabandista => 2,
            _ => 1,
        };

        (ActionType, Player) AiChooseAction(Player p)
        {
            var opps = players.Where(o => o != p && o.Alive).ToList();
            Player Threat() => opps.OrderByDescending(o => o.Influence * 10 + o.coins + (o.isHuman ? 3 : 0) + Random.Range(0, 5)).First();
            Player Richest() => opps.OrderByDescending(o => o.coins + Random.value).First();

            if (p.coins >= Rules.ForcedMotimAt) return (ActionType.Motim, Threat());
            if (p.coins >= 7 && Random.value < 0.85f) return (ActionType.Motim, Threat());

            var rich = Richest();
            var options = new List<(ActionType a, float w)>
            {
                (ActionType.Pescar, 1.5f),
                (ActionType.Tributo, p.Has(Role.Capitao) ? 4f : 0.8f),
                (ActionType.Desviar, rich.coins == 0 ? 0f : p.Has(Role.Contrabandista) ? (rich.coins >= 2 ? 4f : 1.5f) : 0.5f),
                (ActionType.Recrutar, p.Has(Role.Medico) ? 1.2f : 0.2f),
            };
            if (p.coins >= 3) options.Add((ActionType.Maldicao, p.Has(Role.Bruxa) ? 5f : 0.6f));

            float roll = Random.value * options.Sum(o => o.w);
            var pick = options[0].a;
            foreach (var o in options)
            {
                if ((roll -= o.w) <= 0) { pick = o.a; break; }
            }

            Player target = pick == ActionType.Desviar ? rich : pick == ActionType.Maldicao ? Threat() : null;
            return (pick, target);
        }

        bool AiWantsChallenge(Player ai, Role role, ActionType action, bool isBlock, Player victim)
        {
            // Conta quantas cópias da carta a IA consegue ver (na própria mão + reveladas na mesa).
            int known = ai.cards.Count(c => !c.revealed && c.role == role)
                      + players.Sum(pl => pl.cards.Count(c => c.revealed && c.role == role));
            if (known >= copies) return true; // impossível ser verdade

            float chance = 0.07f + 0.12f * known;
            bool cursedMe = !isBlock && action == ActionType.Maldicao && victim == ai;

            if (victim == ai) chance += 0.12f;
            if (cursedMe) chance = ai.Has(Role.Medico) ? 0.03f : chance + 0.2f;
            if (ai.Influence == 1) chance = cursedMe && !ai.Has(Role.Medico) ? 0.5f : chance * 0.5f;

            return Random.value < chance;
        }

        bool AiWantsBlock(Player ai, ActionType action)
        {
            var blocker = Rules.Blocker(action).Value;
            if (ai.Has(blocker)) return true;
            float bluff = action == ActionType.Maldicao ? (ai.Influence == 1 ? 0.65f : 0.35f) : 0.25f;
            return Random.value < bluff;
        }

        // ================================================================== utilidades

        Role Draw()
        {
            var r = deck[deck.Count - 1];
            deck.RemoveAt(deck.Count - 1);
            return r;
        }

        static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        IEnumerator Pause(float mult = 1f)
        {
            yield return new WaitForSeconds(aiDelay * mult);
        }

        /// <summary>Marcador do jogador: cada tela troca por "Você" ou pelo nome.</summary>
        static string Who(int idx) => "{P" + idx + "}";
        static string Low(int idx) => "{p" + idx + "}";

        void Log(string msg) => output.Broadcast(new GameEvent { type = Ev.Log, text = msg });

        void Announce(string title, string sub, Role? role = null) =>
            output.Broadcast(new GameEvent { type = Ev.Announce, text = title, text2 = sub, role = role.HasValue ? (int)role.Value : -1 });

        void Bubble(Player p, string text, int color) =>
            output.Broadcast(new GameEvent { type = Ev.Bubble, a = players.IndexOf(p), text = text, b = color });

        void Coin(Player p, int delta)
        {
            if (delta != 0) output.Broadcast(new GameEvent { type = Ev.Coin, a = players.IndexOf(p), b = delta });
        }
    }
}
