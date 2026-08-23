using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Utilidades de material compartidas por el juego de escondidas.
    /// El proyecto usa URP, asi que los shaders "Standard" / "Unlit/Color" de built-in no valen
    /// (saldrian rosados). Aqui se busca primero el shader de URP y se cae al de built-in solo
    /// como red de seguridad.
    /// </summary>
    public static class HideAndSeekMaterials
    {
        private static Shader s_unlitShader;
        private static Shader s_litShader;

        public static Shader UnlitShader
        {
            get
            {
                if (s_unlitShader == null)
                {
                    s_unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (s_unlitShader == null) s_unlitShader = Shader.Find("Unlit/Color");
                }
                return s_unlitShader;
            }
        }

        public static Shader LitShader
        {
            get
            {
                if (s_litShader == null)
                {
                    s_litShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (s_litShader == null) s_litShader = Shader.Find("Standard");
                }
                return s_litShader;
            }
        }

        public static Material CreateUnlit(Color color)
        {
            return Tint(new Material(UnlitShader), color);
        }

        public static Material CreateLit(Color color)
        {
            return Tint(new Material(LitShader), color);
        }

        /// <summary>Aplica un color al material cubriendo tanto la propiedad de URP como la de built-in.</summary>
        public static Material Tint(Material material, Color color)
        {
            if (material == null) return null;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }
    }
}
