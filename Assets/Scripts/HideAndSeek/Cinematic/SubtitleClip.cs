using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Colivri.HideAndSeek
{
    /// <summary>Un subtitulo del Timeline: el texto que se ve mientras dura el clip.</summary>
    [System.Serializable]
    public class SubtitleClip : PlayableAsset, ITimelineClipAsset
    {
        [TextArea(2, 4)]
        public string text;

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<SubtitleBehaviour>.Create(graph);
            playable.GetBehaviour().text = text;
            return playable;
        }
    }

    public class SubtitleBehaviour : PlayableBehaviour
    {
        public string text;
    }
}
