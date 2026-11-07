using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MareDeMentiras
{
    public enum Role { Capitao, Contrabandista, Bruxa, Medico }

    public enum ActionType { Pescar, Tributo, Desviar, Maldicao, Recrutar, Motim }

    /// <summary>Todas as regras e textos do jogo num lugar só, fácil de balancear.</summary>
    public static class Rules
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 6;

        /// <summary>Cópias de cada personagem no baralho: mesas grandes precisam de mais cartas.</summary>
        public static int CopiesFor(int players) => players <= 4 ? 3 : 4;
        public const int StartingCoins = 2;
        public const int CardsPerPlayer = 2;
        public const int ForcedMotimAt = 10;
        public const int TributoGain = 3;
        public const int DesviarAmount = 2;

        public static string RoleName(Role r) => r switch
        {
            Role.Capitao => "Capitão",
            Role.Contrabandista => "Contrabandista",
            Role.Bruxa => "Bruxa do Mar",
            _ => "Médico de Bordo",
        };

        public static Color RoleColor(Role r) => r switch
        {
            Role.Capitao => new Color(0.85f, 0.65f, 0.20f),
            Role.Contrabandista => new Color(0.25f, 0.62f, 0.35f),
            Role.Bruxa => new Color(0.55f, 0.32f, 0.72f),
            _ => new Color(0.22f, 0.55f, 0.75f),
        };

        public static string ActionName(ActionType a) => a switch
        {
            ActionType.Pescar => "Pescar",
            ActionType.Tributo => "Tributo",
            ActionType.Desviar => "Desviar Carga",
            ActionType.Maldicao => "Maldição",
            ActionType.Recrutar => "Recrutar",
            _ => "Motim",
        };

        public static string ActionShort(ActionType a) => a switch
        {
            ActionType.Pescar => "+1 dobrão",
            ActionType.Tributo => "+3 dobrões",
            ActionType.Desviar => "rouba 2",
            ActionType.Maldicao => "custa 3",
            ActionType.Recrutar => "troca 1 carta",
            _ => "custa 7",
        };

        /// <summary>Personagem que é preciso alegar para usar a ação (null = ação livre).</summary>
        public static Role? ClaimedRole(ActionType a) => a switch
        {
            ActionType.Tributo => Role.Capitao,
            ActionType.Desviar => Role.Contrabandista,
            ActionType.Maldicao => Role.Bruxa,
            ActionType.Recrutar => Role.Medico,
            _ => null,
        };

        /// <summary>Personagem que o alvo pode alegar para bloquear a ação.</summary>
        public static Role? Blocker(ActionType a) => a switch
        {
            ActionType.Desviar => Role.Capitao,
            ActionType.Maldicao => Role.Medico,
            _ => null,
        };

        public static int Cost(ActionType a) => a switch
        {
            ActionType.Motim => 7,
            ActionType.Maldicao => 3,
            _ => 0,
        };

        public static bool NeedsTarget(ActionType a) =>
            a == ActionType.Motim || a == ActionType.Desviar || a == ActionType.Maldicao;

        public const string Reference =
            "<b>AÇÕES LIVRES</b>\n" +
            "<b>Pescar</b>: +1 dobrão. Ninguém contesta.\n" +
            "<b>Motim</b> (7): alvo perde uma carta. Obrigatório com 10+ dobrões.\n\n" +
            "<b>PERSONAGENS</b>: qualquer um pode alegar ter, e blefar vale!\n" +
            "<color=#E0B040><b>Capitão</b></color>: Tributo, +3 dobrões. Bloqueia Desvio.\n" +
            "<color=#50C070><b>Contrabandista</b></color>: rouba 2 dobrões de um alvo.\n" +
            "<color=#B070E0><b>Bruxa do Mar</b></color>: Maldição (3), alvo perde uma carta.\n" +
            "<color=#50A8E0><b>Médico de Bordo</b></color>: troca uma carta com o baralho. Bloqueia Maldição.\n\n" +
            "<b>DESAFIO</b>: se quem alegou tinha a carta, o desafiante perde uma carta. Se era blefe, o mentiroso perde.\n" +
            "Sem cartas = fora do jogo. O último pirata vence.";
    }

    public class Card
    {
        public Role role;
        public bool revealed;
        public Card(Role role) { this.role = role; }
    }

    public class Player
    {
        public string name;
        public bool isHuman;
        public string portrait;
        public int coins;
        public readonly List<Card> cards = new List<Card>();

        public bool Alive => cards.Any(c => !c.revealed);
        public int Influence => cards.Count(c => !c.revealed);
        public bool Has(Role r) => cards.Any(c => !c.revealed && c.role == r);
        public List<Card> Hidden => cards.Where(c => !c.revealed).ToList();
    }

    public class Box<T> { public T v; }
}
