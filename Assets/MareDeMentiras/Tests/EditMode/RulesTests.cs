using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MareDeMentiras.Tests
{
    public class RulesTests
    {
        static readonly ActionType[] AllActions = (ActionType[])Enum.GetValues(typeof(ActionType));
        static readonly int RoleCount = Enum.GetValues(typeof(Role)).Length;

        [Test]
        public void Baralho_TemCartasSuficientesParaTodosOsTamanhosDeMesa(
            [Values(2, 3, 4, 5, 6)] int players)
        {
            int deck = RoleCount * Rules.CopiesFor(players);
            int dealt = players * Rules.CardsPerPlayer;
            // Recrutar compra 1 carta extra: precisa sobrar pelo menos uma no baralho.
            Assert.GreaterOrEqual(deck - dealt, 1, $"Mesa de {players}: baralho {deck}, distribuídas {dealt}");
        }

        [Test]
        public void MesaGrandeUsaMaisCopias()
        {
            Assert.AreEqual(3, Rules.CopiesFor(4));
            Assert.AreEqual(4, Rules.CopiesFor(5));
            Assert.AreEqual(4, Rules.CopiesFor(6));
        }

        [Test]
        public void LimitesDeJogadores()
        {
            Assert.AreEqual(2, Rules.MinPlayers);
            Assert.AreEqual(6, Rules.MaxPlayers);
        }

        [Test]
        public void AcoesDePersonagem_AlegamUmPersonagem()
        {
            Assert.AreEqual(Role.Capitao, Rules.ClaimedRole(ActionType.Tributo));
            Assert.AreEqual(Role.Contrabandista, Rules.ClaimedRole(ActionType.Desviar));
            Assert.AreEqual(Role.Bruxa, Rules.ClaimedRole(ActionType.Maldicao));
            Assert.AreEqual(Role.Medico, Rules.ClaimedRole(ActionType.Recrutar));
            Assert.IsNull(Rules.ClaimedRole(ActionType.Pescar));
            Assert.IsNull(Rules.ClaimedRole(ActionType.Motim));
        }

        [Test]
        public void SoAcoesComAlvoPodemSerBloqueadas()
        {
            foreach (var a in AllActions)
            {
                if (Rules.Blocker(a).HasValue)
                    Assert.IsTrue(Rules.NeedsTarget(a), $"{a} tem bloqueador mas não tem alvo");
            }
            Assert.AreEqual(Role.Capitao, Rules.Blocker(ActionType.Desviar));
            Assert.AreEqual(Role.Medico, Rules.Blocker(ActionType.Maldicao));
            Assert.IsNull(Rules.Blocker(ActionType.Motim), "Motim não pode ser bloqueado");
        }

        [Test]
        public void MotimObrigatorio_EhPagavel()
        {
            Assert.Less(Rules.Cost(ActionType.Motim), Rules.ForcedMotimAt);
            Assert.GreaterOrEqual(Rules.ForcedMotimAt, Rules.Cost(ActionType.Motim));
        }

        [Test]
        public void TodaAcaoTemNomeEDescricao()
        {
            foreach (var a in AllActions)
            {
                Assert.IsFalse(string.IsNullOrEmpty(Rules.ActionName(a)), a.ToString());
                Assert.IsFalse(string.IsNullOrEmpty(Rules.ActionShort(a)), a.ToString());
            }
            foreach (Role r in Enum.GetValues(typeof(Role)))
                Assert.IsFalse(string.IsNullOrEmpty(Rules.RoleName(r)), r.ToString());
        }
    }

    public class ProtocolTests
    {
        [Test]
        public void GameEvent_SobreviveAoJson()
        {
            var snap = new Snapshot { me = 1, current = 2 };
            snap.players.Add(new PlayerView { name = "Ana", portrait = "Capuz_0", coins = 3, human = true });
            snap.players[0].cards.Add(new CardView { role = -1, revealed = false });
            snap.players[0].cards.Add(new CardView { role = (int)Role.Bruxa, revealed = true });

            var ev = new GameEvent
            {
                type = Ev.Prompt,
                text = "{P0} diz ser Capitão",
                promptId = 42,
                state = snap,
                options = new[] { new Option("Desafiar!", "é mentira!", null, ButtonStyle.Vermelho), new Option("Motim", null, "Icone_Motim", ButtonStyle.Madeira, false) },
            };

            var back = JsonUtility.FromJson<GameEvent>(JsonUtility.ToJson(ev));

            Assert.AreEqual(Ev.Prompt, back.type);
            Assert.AreEqual(ev.text, back.text);
            Assert.AreEqual(42, back.promptId);
            Assert.AreEqual(1, back.state.me);
            Assert.AreEqual(2, back.state.current);
            Assert.AreEqual("Ana", back.state.players[0].name);
            Assert.AreEqual(-1, back.state.players[0].cards[0].role);
            Assert.AreEqual((int)Role.Bruxa, back.state.players[0].cards[1].role);
            Assert.IsTrue(back.state.players[0].cards[1].revealed);
            Assert.AreEqual(2, back.options.Length);
            Assert.AreEqual(ButtonStyle.Vermelho, back.options[0].style);
            Assert.IsFalse(back.options[1].enabled);
            Assert.AreEqual("Icone_Motim", back.options[1].icon);
        }

        [Test]
        public void JogadorVivo_DependeDeCartaEscondida()
        {
            var p = new PlayerView();
            p.cards.Add(new CardView { revealed = true });
            p.cards.Add(new CardView { revealed = true });
            Assert.IsFalse(p.Alive);
            p.cards[1].revealed = false;
            Assert.IsTrue(p.Alive);
        }
    }

    public class NameTests
    {
        [TestCase("Ana", "Ana")]
        [TestCase("  Beto  ", "Beto")]
        [TestCase("", "Pirata")]
        [TestCase("   ", "Pirata")]
        [TestCase("{}<>", "Pirata")]
        public void Apelido_EhLimpo(string input, string expected)
        {
            Assert.AreEqual(expected, UIKit.CleanName(input, "Pirata"));
        }

        [Test]
        public void Apelido_NaoAceitaMarcadoresNemTags()
        {
            string r = UIKit.CleanName("{P0}<color=red>Hacker</color>", "Pirata");
            Assert.IsFalse(r.IndexOfAny(new[] { '{', '}', '<', '>' }) >= 0, r);
        }

        [Test]
        public void Apelido_TemNoMaximo16Letras()
        {
            Assert.AreEqual(16, UIKit.CleanName(new string('x', 40), "Pirata").Length);
        }
    }
}
