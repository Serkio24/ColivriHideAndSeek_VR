using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Colivri.HideAndSeek.EditorTools
{
    /// <summary>
    /// Menu Colivri > Hide and Seek > Modo de prueba. Anade o quita el define COLIVRI_HS_TEST_MODE
    /// (ver <see cref="HideAndSeekTestMode"/>) antes de hacer la build.
    ///
    /// Se toca Android (la build del Quest) y Standalone (el Play del editor con Link), para que
    /// lo que se prueba en el editor sea lo mismo que acaba en el casco. Cambiar un define obliga
    /// a Unity a recompilar los scripts.
    /// </summary>
    public static class HideAndSeekTestModeMenu
    {
        private const string MenuPath = "Colivri/Hide and Seek/Modo de prueba";
        private const string Define = "COLIVRI_HS_TEST_MODE";

        private static readonly NamedBuildTarget[] Targets =
        {
            NamedBuildTarget.Android,
            NamedBuildTarget.Standalone
        };

        [MenuItem(MenuPath, priority = 100)]
        private static void Toggle()
        {
            bool enable = !IsEnabled();
            foreach (var target in Targets)
            {
                SetDefine(target, enable);
            }

            Debug.Log(enable
                ? $"[HideAndSeek] Modo de prueba ACTIVADO ({Define}). Recuerda desactivarlo antes de la build final."
                : $"[HideAndSeek] Modo de prueba desactivado ({Define} quitado).");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, IsEnabled());
            return true;
        }

        /// El estado lo marca Android, que es el destino real de la build.
        private static bool IsEnabled()
        {
            return GetDefines(NamedBuildTarget.Android).Contains(Define);
        }

        private static List<string> GetDefines(NamedBuildTarget target)
        {
            PlayerSettings.GetScriptingDefineSymbols(target, out string[] defines);
            return new List<string>(defines);
        }

        private static void SetDefine(NamedBuildTarget target, bool enable)
        {
            var defines = GetDefines(target);
            defines.RemoveAll(d => d == Define);
            if (enable) defines.Add(Define);
            PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
        }
    }
}
