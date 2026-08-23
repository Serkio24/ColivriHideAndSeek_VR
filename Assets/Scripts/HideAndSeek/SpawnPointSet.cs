using System.Collections.Generic;
using UnityEngine;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Conjunto de puntos de aparicion de la ronda. Si no se asigna ninguno a mano, usa
    /// sus propios hijos, asi que basta con crear GameObjects vacios colgando de este.
    /// Coloca los puntos sobre las superficies que ya son teleportables en la escena
    /// (FloorColivri1/2/3, PlaneOfficeS1, PlaneOfficeS101, PlaneOfficeS107...S113).
    /// </summary>
    public class SpawnPointSet : MonoBehaviour
    {
        [Tooltip("Si se deja vacio se usan los hijos directos de este GameObject.")]
        [SerializeField] private List<Transform> points = new List<Transform>();

        [Tooltip("Punto donde aparece el buscador. Si es null se usa uno cualquiera del conjunto.")]
        [SerializeField] private Transform seekerPoint;

        public Transform SeekerPoint => seekerPoint;

        public int Count => ResolvedPoints.Count;

        private List<Transform> ResolvedPoints
        {
            get
            {
                if (points == null) points = new List<Transform>();
                if (points.Count == 0)
                {
                    foreach (Transform child in transform)
                    {
                        if (child != seekerPoint) points.Add(child);
                    }
                }
                return points;
            }
        }

        /// <summary>
        /// Devuelve hasta <paramref name="count"/> puntos distintos en orden aleatorio.
        /// Si se piden mas puntos de los que hay, se repiten (nunca devuelve menos de lo pedido
        /// mientras exista al menos un punto).
        /// </summary>
        public List<Transform> TakeRandom(int count)
        {
            var available = new List<Transform>(ResolvedPoints);
            available.RemoveAll(p => p == null);

            var result = new List<Transform>(count);
            if (available.Count == 0) return result;

            // Fisher-Yates sobre la copia.
            for (int i = available.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (available[i], available[j]) = (available[j], available[i]);
            }

            for (int i = 0; i < count; i++)
            {
                result.Add(available[i % available.Count]);
            }
            return result;
        }

        /// <summary>Punto de aparicion del buscador; cae en uno aleatorio si no se configuro.</summary>
        public Transform GetSeekerPoint()
        {
            if (seekerPoint != null) return seekerPoint;
            var any = TakeRandom(1);
            return any.Count > 0 ? any[0] : transform;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            foreach (Transform child in transform)
            {
                if (child == null) continue;
                Gizmos.color = child == seekerPoint ? Color.red : Color.cyan;
                Gizmos.DrawWireSphere(child.position + Vector3.up * 0.9f, 0.25f);
                Gizmos.DrawLine(child.position, child.position + child.forward * 0.6f);
            }
        }
#endif
    }
}
