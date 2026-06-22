using UnityEngine;

namespace CrashoutCrew6
{
    /// <summary>
    /// Holds the report clipboard shifted up by ModConfig.ReportYOffset. The clipboard's Animator
    /// drives anchoredPosition every frame, so we re-apply the offset in LateUpdate (which runs after
    /// the animator). We track the animator's base value so the offset never accumulates, whether or
    /// not the animator keeps writing the position in its steady "shown" state.
    /// </summary>
    internal class ReportClipboardOffset : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _lastSet;
        private Vector2 _lastBase;
        private bool _has;

        private void Awake() => _rt = GetComponent<RectTransform>();

        private void LateUpdate()
        {
            if (_rt == null) return;
            float off = ModConfig.ReportYOffset != null ? ModConfig.ReportYOffset.Value : 0f;

            Vector2 cur = _rt.anchoredPosition;
            // If something else (the animator) moved it since our last write, that's the new base.
            Vector2 baseP = (!_has || cur != _lastSet) ? cur : _lastBase;

            Vector2 target = baseP + new Vector2(0f, off);
            _rt.anchoredPosition = target;
            _lastBase = baseP;
            _lastSet = target;
            _has = true;
        }
    }
}
