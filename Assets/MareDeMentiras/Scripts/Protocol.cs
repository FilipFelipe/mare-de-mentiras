using System;
using System.Collections.Generic;

namespace MareDeMentiras
{
    // Tudo que passa pela rede (ou do motor para a tela local) é um GameEvent serializado em JSON.
    // Textos usam marcadores {P3}/{p3} que cada tela troca por "Você"/"você" ou pelo nome do pirata.

    public enum Ev { Reset, State, Log, Announce, Bubble, Coin, CardLost, Prompt, ClearPrompt, Waiting, Ended, Lobby }

    public enum ButtonStyle { Madeira, Vermelho, Verde, Azul }

    [Serializable]
    public struct Option
    {
        public string label, sub, icon;
        public ButtonStyle style;
        public bool enabled;

        public Option(string label, string sub = null, string icon = null, ButtonStyle style = ButtonStyle.Madeira, bool enabled = true)
        {
            this.label = label; this.sub = sub; this.icon = icon; this.style = style; this.enabled = enabled;
        }
    }

    [Serializable]
    public class CardView
    {
        public int role = -1;      // -1 = carta escondida
        public bool revealed;
    }

    [Serializable]
    public class PlayerView
    {
        public string name;
        public string portrait;
        public int coins;
        public bool human;
        public List<CardView> cards = new List<CardView>();
        public bool Alive => cards.Exists(c => !c.revealed);
    }

    /// <summary>A mesa vista por um jogador: só as cartas dele aparecem.</summary>
    [Serializable]
    public class Snapshot
    {
        public int me = -1;
        public int current = -1;
        public List<PlayerView> players = new List<PlayerView>();
    }

    [Serializable]
    public class GameEvent
    {
        public Ev type;
        public string text, text2;
        public int a = -1, b;
        public int role = -1;
        public int promptId;
        public Snapshot state;
        public Option[] options;
        public string[] names;

        public static GameEvent Of(Ev type) => new GameEvent { type = type };
    }

    [Serializable]
    public class Answer
    {
        public int promptId;
        public int choice;
    }

    [Serializable]
    public class Hello
    {
        public string name;
    }

    /// <summary>Saída do motor da partida: quem roda a mesa decide para quem cada evento vai.</summary>
    public interface ITableOutput
    {
        void Broadcast(GameEvent ev);
        void SendTo(int playerIdx, GameEvent ev);
        void StateChanged();
        void Finished(int winnerIdx);
    }

    public class SeatInfo
    {
        public string name;
        public bool human;
        public string portrait;
    }
}
