namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Rol de un jugador dentro de la ronda. El servidor es el único que lo asigna.
    /// </summary>
    public enum PlayerRole : byte
    {
        /// Todavía no ha empezado la ronda (o el jugador acaba de entrar).
        Unassigned = 0,
        /// El que busca: tamaño normal y con pistola.
        Seeker = 1,
        /// El que se esconde: tamaño reducido y sin arma.
        Hider = 2,
        /// Escondido que ya recibió un disparo. Sigue en la partida como observador.
        Spectator = 3
    }

    /// <summary>
    /// Fase actual de la partida. Vive en una <c>NetworkVariable</c> del
    /// <see cref="HideAndSeekManager"/> y sólo el servidor la escribe.
    /// </summary>
    public enum GameState : byte
    {
        /// Esperando a que haya suficientes jugadores conectados.
        WaitingForPlayers = 0,
        /// Cuenta atrás para esconderse. El buscador está cegado e inmovilizado.
        Hiding = 1,
        /// El buscador ya puede moverse y disparar.
        Seeking = 2,
        /// Pantalla de resultado antes de volver a empezar.
        RoundOver = 3
    }
}
