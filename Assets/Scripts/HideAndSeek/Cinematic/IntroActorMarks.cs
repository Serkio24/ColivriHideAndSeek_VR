using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Datos de un actor de la cinematica que necesita el builder del editor para regenerar el
    /// Timeline: que articulaciones se animan y su pose de reposo. En runtime no hace nada.
    /// </summary>
    public class IntroActorMarks : MonoBehaviour
    {
        public Transform head;
        public Transform armRight;
        public Transform muzzle;
        public Renderer[] renderers;

        [HideInInspector] public Quaternion headRest = Quaternion.identity;
        [HideInInspector] public Quaternion headParentInBody = Quaternion.identity;
        [HideInInspector] public Quaternion armRest = Quaternion.identity;
        [HideInInspector] public Quaternion armParentInBody = Quaternion.identity;
    }
}
