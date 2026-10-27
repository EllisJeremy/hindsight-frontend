using Hindsight.Cameras;
using Hindsight.Data;
using Hindsight.Playback;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Hindsight.UI
{
    /// <summary>
    /// Minimal screen overlay built in code: play context at the top, the play navigator bar
    /// at the bottom, the control bar above it, and playback state above that.
    /// </summary>
    public class PlaybackHud : MonoBehaviour
    {
        [SerializeField] float fontSize = 22f;
        [SerializeField] float navigatorHeight = 116f;
        [SerializeField] float followPickerWidth = 360f;
        [SerializeField] float controlBarHeight = 38f;

        const float Margin = 16f;

        PlaybackController playback;
        PlayLoader loader;
        CameraRig cameras;
        TextMeshProUGUI header;
        TextMeshProUGUI status;
        ShortcutsModal shortcuts;
        string loadError;
        string hoverText;

        public void Init(PlaybackController playbackController, PlayLoader playLoader, CameraRig cameraRig)
        {
            playback = playbackController;
            loader = playLoader;
            cameras = cameraRig;
            BuildCanvas();
            playback.PlayChanged += OnPlayChanged;
            loader.LoadFailed += message => loadError = message;
        }

        void OnDestroy()
        {
            if (playback != null) playback.PlayChanged -= OnPlayChanged;
        }

        void OnPlayChanged(PlayTimeline timeline)
        {
            loadError = null;
            PlayData p = timeline.Play;
            if (p.suggested)
            {
                header.text = SuggestedHeader(timeline);
                return;
            }
            string matchup = p.possessionTeam == p.homeTeam
                ? $"{p.visitorTeam} @ <b>{p.homeTeam}</b>"
                : $"<b>{p.visitorTeam}</b> @ {p.homeTeam}";
            header.text =
                $"{matchup}   Q{p.quarter} {p.gameClock}   {Ordinal(p.down)} & {p.yardsToGo}   " +
                $"<size=80%>drive {p.driveNumber}, play {loader.CurrentIndex + 1}/{loader.Plays.Count}</size>\n" +
                $"<size=80%>{p.description}</size>";
        }

        // The suggested play's source has no teams, quarter, clock or drive, so the header shows
        // the model's verdict instead: who it would have thrown to, and why.
        string SuggestedHeader(PlayTimeline t)
        {
            PlayData p = t.Play;
            Recommendation rec = p.recommendation;
            string situation = p.down > 0 ? $"{Ordinal(p.down)} & {p.yardsToGo}   " : "";
            string verdict = "";
            if (rec != null && rec.targetNflId != 0)
            {
                verdict = rec.targetNflId == rec.targetedNflId
                    ? $"model agrees with the throw to {PlayerName(t, rec.targetNflId)}   "
                    : $"model throws to <color={RecommendedColor}>{PlayerName(t, rec.targetNflId)}</color>, " +
                      $"not {PlayerName(t, rec.targetedNflId)}" +
                      (rec.separationAdvantageYards > 0 ? $" (+{rec.separationAdvantageYards:0.0} yd open)   " : "   ");
            }
            return
                $"<color={RecommendedColor}><b>SUGGESTED</b></color> {p.gameId}_{p.playId}   {situation}{verdict}" +
                $"<size=80%>suggested {loader.CurrentSuggestedIndex + 1}/{loader.Suggested.Count}</size>\n" +
                $"<size=80%>{rec?.reason ?? p.description}</size>";
        }

        static string PlayerName(PlayTimeline t, long nflId)
        {
            PlayerInfo info = System.Array.Find(t.Players, p => p.nflId == nflId);
            if (info == null) return $"#{nflId}";
            return string.IsNullOrEmpty(info.name) ? $"{info.position} {nflId}" : info.name;
        }

        const string RecommendedColor = "#FFC62F";

        void Update()
        {
            if (status == null) return;
            if (loadError != null)
            {
                status.text = $"<color=#FF6B6B>{loadError}</color>";
                return;
            }
            PlayTimeline t = playback.Timeline;
            if (t == null)
            {
                status.text = "Loading...";
                return;
            }
            // Play state, speed and camera are on the control bar, so this line is only what it can't show.
            float frame = playback.CurrentFrame;
            string evt = t.EventAt(frame);
            status.text =
                $"<color=#FFFFFFAA>frame {t.FrameIdAt(frame)}/{t.Play.frames[t.FrameCount - 1].frameId}" +
                $"   {(frame - t.SnapIndex) / t.FrameRate:+0.0;-0.0}s from snap" +
                (evt != null ? $"   {evt}" : "") + "</color>" +
                SourceLabel(t, frame) +
                (loader.IsLoading ? "   loading..." : "") +
                (cameras != null && cameras.IsFollowView ? $"   <color=#FFFFFFAA>{CameraLabel(t, frame)}</color>" : "") +
                (hoverText != null ? $"\n<size=75%><color=#FFFFFF99>{hoverText}</color></size>" : "");
        }

        string CameraLabel(PlayTimeline t, float frame)
        {
            if (cameras == null) return "";
            string view = cameras.Current switch
            {
                CameraRig.View.TopDown => "top-down",
                CameraRig.View.Broadcast => "broadcast",
                CameraRig.View.FirstPerson => "first person",
                _ => "third person",
            };
            if (!cameras.IsFollowView) return $"[{view}]";

            int subject = -1;
            if (cameras.FollowedPlayerId != 0)
                subject = System.Array.FindIndex(t.Players, p => p.nflId == cameras.FollowedPlayerId);
            else
                subject = t.CarrierAt(frame);
            string who = subject < 0
                ? "ball"
                : $"{t.Players[subject].club} {t.Players[subject].NumberLabel} {t.Players[subject].name}";
            return $"[{view}: {who}]";
        }

        /// <summary>On a suggested play, whether this frame is recorded tracking or the model's prediction.</summary>
        static string SourceLabel(PlayTimeline t, float frame)
        {
            PlayData p = t.Play;
            if (!p.suggested || p.simulationStartFrameId <= 0) return "";
            return t.FrameIdAt(frame) >= p.simulationStartFrameId
                ? $"   <color={RecommendedColor}>MODEL PREDICTION</color>"
                : "   RECORDED";
        }

        static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

        void BuildCanvas()
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            header = CreateText(canvas.transform, "Header", top: true);
            // Leave the top-right corner for the follow picker.
            var headerRect = (RectTransform)header.transform;
            headerRect.offsetMax = new Vector2(headerRect.offsetMax.x - followPickerWidth - Margin, headerRect.offsetMax.y);

            var pickerRect = new GameObject("FollowPicker", typeof(RectTransform)).GetComponent<RectTransform>();
            pickerRect.SetParent(canvas.transform, false);
            pickerRect.anchorMin = pickerRect.anchorMax = pickerRect.pivot = Vector2.one;
            pickerRect.anchoredPosition = new Vector2(-Margin, -Margin);
            pickerRect.sizeDelta = new Vector2(followPickerWidth, 36f);
            pickerRect.gameObject.AddComponent<FollowPicker>().Init(playback, cameras, fontSize);
            var controlsRect = new GameObject("ControlBar", typeof(RectTransform)).GetComponent<RectTransform>();
            controlsRect.SetParent(canvas.transform, false);
            controlsRect.anchorMin = Vector2.zero;
            controlsRect.anchorMax = new Vector2(1f, 0f);
            controlsRect.pivot = new Vector2(0.5f, 0f);
            controlsRect.anchoredPosition = new Vector2(0f, Margin + navigatorHeight + 6f);
            controlsRect.sizeDelta = new Vector2(-2f * Margin, controlBarHeight);
            controlsRect.gameObject.AddComponent<ControlBar>()
                .Init(playback, loader, cameras, text => hoverText = text, () => shortcuts.Toggle(), fontSize);

            status = CreateText(canvas.transform, "Status", top: false);
            ((RectTransform)status.transform).anchoredPosition =
                new Vector2(0f, Margin + navigatorHeight + 6f + controlBarHeight + 6f);

            var navRect = new GameObject("PlayNavigator", typeof(RectTransform)).GetComponent<RectTransform>();
            navRect.SetParent(canvas.transform, false);
            navRect.anchorMin = Vector2.zero;
            navRect.anchorMax = new Vector2(1f, 0f);
            navRect.pivot = new Vector2(0.5f, 0f);
            navRect.anchoredPosition = new Vector2(0f, Margin);
            navRect.sizeDelta = new Vector2(-2f * Margin, navigatorHeight);
            navRect.gameObject.AddComponent<PlayNavigator>().Init(loader, text => hoverText = text);

            // Last, so it draws over everything else.
            var shortcutsRect = new GameObject("ShortcutsModal", typeof(RectTransform)).GetComponent<RectTransform>();
            shortcutsRect.SetParent(canvas.transform, false);
            shortcutsRect.anchorMin = Vector2.zero;
            shortcutsRect.anchorMax = Vector2.one;
            shortcutsRect.offsetMin = shortcutsRect.offsetMax = Vector2.zero;
            shortcuts = shortcutsRect.gameObject.AddComponent<ShortcutsModal>();
            shortcuts.Init(fontSize);

            // Clicks need an EventSystem; the Input System module picks up its default UI actions.
            if (EventSystem.current == null && FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule))
                    .transform.SetParent(transform, false);
        }

        TextMeshProUGUI CreateText(Transform parent, string objectName, bool top)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = new Vector2(0f, top ? -16f : 16f);
            rt.sizeDelta = new Vector2(-48f, 90f);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = top ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.BottomLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
    }
}
