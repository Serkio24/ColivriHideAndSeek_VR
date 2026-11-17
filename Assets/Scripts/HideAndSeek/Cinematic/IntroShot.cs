using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Un plano de la cinematica: la camara va de <see cref="from"/> a <see cref="to"/> (posicion y
    /// rotacion) mientras dura el plano. Solo son datos para el builder del editor
    /// ("Colivri/Hide and Seek/Cinematica/Regenerar Timeline desde anclas"): para reencuadrar un
    /// plano se mueven sus dos anclas en la Scene view (p. ej. con "Align With View") y se regenera.
    /// </summary>
    public class IntroShot : MonoBehaviour
    {
        [Tooltip("Que se ve en el plano, para orientarse.")]
        [TextArea(1, 3)] public string description;
        [Tooltip("Segundo del Timeline en el que empieza el plano.")]
        public float start;
        public float duration = 3f;
        public float fovStart = 45f;
        public float fovEnd = 45f;
        public Transform from;
        public Transform to;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            DrawCamera(from, fovStart, new Color(0.3f, 0.8f, 1f));
            DrawCamera(to, fovEnd, new Color(1f, 0.6f, 0.2f));
            if (from != null && to != null)
            {
                Gizmos.color = Color.white;
                Gizmos.DrawLine(from.position, to.position);
            }
        }

        private static void DrawCamera(Transform t, float fov, Color color)
        {
            if (t == null) return;
            Gizmos.color = color;
            Gizmos.matrix = t.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fov, 1.2f, 0.05f, 16f / 9f);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
