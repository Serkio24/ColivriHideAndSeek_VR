using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Colivri.HideAndSeek
{
    /// <summary>
    /// Pista de subtitulos: cada clip es una frase. Se enlaza a una <see cref="SubtitleBar"/> y le
    /// pasa el texto del clip activo; entre clips la franja se esconde.
    /// </summary>
    [TrackColor(0.5f, 0.8f, 0.95f)]
    [TrackClipType(typeof(SubtitleClip))]
    [TrackBindingType(typeof(SubtitleBar))]
    public class SubtitleTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            // El nombre del clip en la ventana Timeline es el propio texto, para leer el guion de un vistazo.
            foreach (var clip in GetClips())
            {
                if (clip.asset is SubtitleClip subtitle && !string.IsNullOrEmpty(subtitle.text))
                {
                    clip.displayName = subtitle.text;
                }
            }
            return ScriptPlayable<SubtitleMixerBehaviour>.Create(graph, inputCount);
        }
    }

    public class SubtitleMixerBehaviour : PlayableBehaviour
    {
        private SubtitleBar _bar;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            _bar = playerData as SubtitleBar;
            if (_bar == null) return;

            string text = string.Empty;
            float best = 0f;
            int count = playable.GetInputCount();
            for (int i = 0; i < count; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= best) continue;
                var input = (ScriptPlayable<SubtitleBehaviour>)playable.GetInput(i);
                best = weight;
                text = input.GetBehaviour().text;
            }

            if (_bar.CurrentText != text) _bar.Show(text);
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_bar != null) _bar.Hide();
        }
    }
}
