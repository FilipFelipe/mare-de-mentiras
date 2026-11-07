using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MareDeMentiras.Tests
{
    /// <summary>
    /// Simula partidas inteiras com o motor real (Table), sem tela nem rede,
    /// e confere que o jogo termina e que ninguém recebe cartas escondidas dos outros.
    /// </summary>
    public class TableTests
    {
        /// <summary>Saída falsa: responde perguntas e audita cada atualização da mesa.</summary>
        class FakeOutput : ITableOutput
        {
            public Table table;
            public int? winner;
            public int prompts;
            public int lastPromptPlayer = -1;
            public readonly List<string> problems = new List<string>();
            public System.Func<int, GameEvent, int> answer;   // devolve o índice escolhido, ou -1 para não responder

            public void Broadcast(GameEvent ev)
            {
                if (ev.type == Ev.Prompt) problems.Add("Pergunta enviada para todos (deveria ir só para um jogador)");
            }

            public void SendTo(int playerIdx, GameEvent ev)
            {
                if (ev.type != Ev.Prompt) return;
                prompts++;
                lastPromptPlayer = playerIdx;
                if (!table.players[playerIdx].isHuman) problems.Add($"Pergunta enviada para a IA (jogador {playerIdx})");
                int c = answer?.Invoke(playerIdx, ev) ?? -1;
                if (c >= 0) table.SubmitAnswer(playerIdx, ev.promptId, c);
            }

            public void StateChanged()
            {
                int n = table.players.Count;
                for (int viewer = 0; viewer < n; viewer++)
                {
                    var snap = table.Snapshot(viewer);
                    for (int i = 0; i < n; i++)
                        for (int k = 0; k < snap.players[i].cards.Count; k++)
                        {
                            var c = snap.players[i].cards[k];
                            bool shouldSee = i == viewer || c.revealed;
                            if (shouldSee && c.role < 0) problems.Add($"Jogador {viewer} não vê carta {k} de {i}, que deveria ver");
                            if (!shouldSee && c.role >= 0) problems.Add($"VAZAMENTO: jogador {viewer} vê carta escondida {k} de {i}");
                        }
                }
                int coinsNeg = table.players.Count(p => p.coins < 0);
                if (coinsNeg > 0) problems.Add("Algum jogador ficou com dobrões negativos");
            }

            public void Finished(int winnerIdx) => winner = winnerIdx;
        }

        readonly List<GameObject> spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // O jogo se cria sozinho ao entrar em Play; nos testes usamos só o motor.
            GameManager gm;
            while ((gm = Object.FindAnyObjectByType<GameManager>()) != null) Object.DestroyImmediate(gm.gameObject);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned) if (go) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        (Table, FakeOutput) NewTable(int humans, int total, int seed)
        {
            Random.InitState(seed);
            var go = new GameObject("MesaDeTeste");
            spawned.Add(go);
            var table = go.AddComponent<Table>();
            table.aiDelay = 0f;
            var output = new FakeOutput { table = table };
            var seats = new List<SeatInfo>();
            for (int i = 0; i < total; i++)
                seats.Add(new SeatInfo { name = (i < humans ? "Humano" : "IA") + i, human = i < humans, portrait = "Capuz_0" });
            table.Begin(seats, output);
            return (table, output);
        }

        static IEnumerator WaitUntil(System.Func<bool> cond, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
        }

        static int RandomEnabled(GameEvent ev)
        {
            var enabled = Enumerable.Range(0, ev.options.Length).Where(i => ev.options[i].enabled).ToList();
            return enabled[Random.Range(0, enabled.Count)];
        }

        static void AssertFinished(Table table, FakeOutput output)
        {
            Assert.IsTrue(output.winner.HasValue, "A partida não terminou a tempo");
            Assert.AreEqual(1, table.players.Count(p => p.Alive), "Deveria sobrar exatamente um pirata");
            Assert.IsTrue(table.players[output.winner.Value].Alive, "O vencedor deveria estar vivo");
            Assert.IsFalse(table.Running);
            CollectionAssert.IsEmpty(output.problems.Distinct().ToList());
        }

        [UnityTest]
        public IEnumerator PartidaSoDeIA_Termina([Values(2, 3, 4, 5, 6)] int players)
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var (table, output) = NewTable(0, players, seed * 100 + players);
                yield return WaitUntil(() => output.winner.HasValue, 60f);
                AssertFinished(table, output);
                table.Stop();
            }
        }

        [UnityTest]
        public IEnumerator PartidaComHumanos_RespondendoAoAcaso_Termina([Values(1, 3)] int humans)
        {
            var (table, output) = NewTable(humans, 5, 7 + humans);
            output.answer = (idx, ev) => RandomEnabled(ev);
            yield return WaitUntil(() => output.winner.HasValue, 60f);
            AssertFinished(table, output);
            Assert.Greater(output.prompts, 0, "Os humanos deveriam ter recebido perguntas");
        }

        [UnityTest]
        public IEnumerator Mesa_EsperaOHumano_EIgnoraRespostasInvalidas()
        {
            var (table, output) = NewTable(1, 4, 11);
            GameEvent pending = null;
            output.answer = (idx, ev) => { pending = ev; return -1; };   // não responde

            yield return WaitUntil(() => pending != null, 5f);
            Assert.IsNotNull(pending, "O humano deveria receber a primeira pergunta");
            Assert.AreEqual(0, table.CurrentIndex);

            // Respostas inválidas: id errado, índice fora do limite, opção desabilitada.
            table.SubmitAnswer(0, pending.promptId + 1, 0);
            table.SubmitAnswer(0, pending.promptId, 99);
            int disabled = System.Array.FindIndex(pending.options, o => !o.enabled);
            if (disabled >= 0) table.SubmitAnswer(0, pending.promptId, disabled);
            table.SubmitAnswer(1, pending.promptId, 0);   // outro jogador tentando responder
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1, output.prompts, "Respostas inválidas não deveriam destravar a mesa");
            Assert.AreEqual(0, table.CurrentIndex);

            // Resposta válida: Pescar (sempre permitido no começo) → a vez passa adiante.
            table.SubmitAnswer(0, pending.promptId, (int)ActionType.Pescar);
            yield return WaitUntil(() => table.CurrentIndex != 0, 5f);
            Assert.AreNotEqual(0, table.CurrentIndex, "Depois de responder, a vez deveria passar");
            Assert.AreEqual(Rules.StartingCoins + 1, table.players[0].coins);
            table.Stop();
        }

        [UnityTest]
        public IEnumerator JogadorQueSai_EhAssumidoPelaIA()
        {
            var (table, output) = NewTable(2, 4, 21);
            bool left = false;
            output.answer = (idx, ev) =>
            {
                if (idx == 1 && !left) return -1;           // jogador 1 "trava"
                return RandomEnabled(ev);
            };
            yield return WaitUntil(() => output.lastPromptPlayer == 1, 30f);
            Assert.AreEqual(1, output.lastPromptPlayer, "O jogador 1 deveria ter recebido uma pergunta");

            left = true;
            table.PlayerLeft(1);
            Assert.IsFalse(table.players[1].isHuman);

            yield return WaitUntil(() => output.winner.HasValue, 60f);
            AssertFinished(table, output);
        }
    }
}
