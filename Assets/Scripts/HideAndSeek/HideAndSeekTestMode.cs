namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Interruptor del modo de prueba. Se enciende y apaga desde el menu
    /// Colivri > Hide and Seek > Modo de prueba, que anade o quita el define
    /// COLIVRI_HS_TEST_MODE: asi queda fijado en la build y no se puede activar desde el casco.
    ///
    /// Con el modo de prueba:
    ///  - se puede iniciar una ronda con un solo jugador;
    ///  - el HUD tiene una pagina extra para elegir BUSCAR o ESCONDERSE durante la ronda;
    ///  - la ronda no se cierra sola por quedarse sin escondidos (solo por tiempo o por disparo).
    ///
    /// Es una propiedad y no una const para que el compilador no avise de codigo inalcanzable
    /// en cada sitio que la consulta.
    /// </summary>
    public static class HideAndSeekTestMode
    {
        public static bool Enabled
        {
            get
            {
#if COLIVRI_HS_TEST_MODE
                return true;
#else
                return false;
#endif
            }
        }
    }
}
