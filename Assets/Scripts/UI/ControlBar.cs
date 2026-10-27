using System;
using System.Collections.Generic;
using Hindsight.Cameras;
using Hindsight.Data;
using Hindsight.Playback;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hindsight.UI
{
    /// <summary>
    /// Clickable versions of the keyboard controls, in one rounded bar above the play navigator:
    /// play and frame transport, speed, a scrubber, recorded vs. suggested play, camera view, and
    /// the shortcuts list. Built in code like the rest of the HUD; the keys keep working alongside it.
    /// </summary>
    public class ControlBar : MonoBehaviour
    {
        const float IconButtonWidth = 34f;
        const float IconSize = 22f;

        /// <summary>One button. Colors are recomputed every frame from its state, so a key press
        /// and a click always leave the bar showing the same thing.</summary>
        class Control
        {
            public Image Background;
            public Graphic Foreground;
            public Color ActiveColor = UiStyle.Text;
            public Func<bool> IsActive;
            public Func<bool> IsEnabled;
            public Func<string> Hint;
            public bool Hovered;
        }

        PlaybackController playback;
        PlayLoader loader;
        CameraRig cameras;
        Action<string> hoverChanged;
        Action showShortcuts;
        float fontSize;

        readonly List<Control> controls = new();
        Image playPauseIcon;
        Slider scrubber;
        RectTransform predictionMarker;
        PlayTimeline shownTimeline;
        bool updatingScrubber;

        public void Init(PlaybackController playbackController, PlayLoader playLoader, CameraRig cameraRig,
            Action<string> onHoverChanged, Action onShowShortcuts, float textSize)
        {
            playback = playbackController;
            loader = playLoader;
            cameras = cameraRig;
            hoverChanged = onHoverChanged;
            showShortcuts = onShowShortcuts;
            fontSize = textSize;
            Build();
        }

        void Update()
        {
            foreach (Control c in controls)
            {
                bool enabled = c.IsEnabled == null || c.IsEnabled();
                bool active = enabled && c.IsActive != null && c.IsActive();
                bool hovered = c.Hovered && enabled;
                c.Background.color = active ? UiStyle.ActiveFill : (hovered ? UiStyle.HoverFill : Color.clear);
                c.Foreground.color = !enabled ? UiStyle.TextDisabled
                    : active ? c.ActiveColor
                    : hovered ? UiStyle.Text
                    : UiStyle.TextMuted;
            }
            playPauseIcon.sprite = UiStyle.Icon(playback.IsPlaying ? UiStyle.Glyph.Pause : UiStyle.Glyph.Play);
            UpdateScrubber();
        }

        // ---------- Scrubber ----------

        void UpdateScrubber()
        {
            PlayTimeline t = playback.Timeline;
            scrubber.interactable = t != null;
            if (t == null) return;

            if (t != shownTimeline)
            {
                shownTimeline = t;
                updatingScrubber = true;
                scrubber.maxValue = Mathf.Max(t.LastFrame, 0.01f);
                updatingScrubber = false;
                PlacePredictionMarker(t);
            }
            updatingScrubber = true;
            scrubber.SetValueWithoutNotify(playback.CurrentFrame);
            updatingScrubber = false;

            // A selected slider also takes the arrow keys, which already step frames.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == scrubber.gameObject &&
                UnityEngine.InputSystem.Mouse.current?.leftButton.isPressed != true)
                EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>On a suggested play, a gold tick where recorded frames end and the model's prediction begins.</summary>
        void PlacePredictionMarker(PlayTimeline t)
        {
            int start = -1;
            if (t.Play.suggested && t.Play.simulationStartFrameId > 0)
                start = Array.FindIndex(t.Play.frames, f => f.frameId >= t.Play.simulationStartFrameId);
            predictionMarker.gameObject.SetActive(start > 0 && t.LastFrame > 0);
            if (start <= 0 || t.LastFrame <= 0) return;
            float x = start / t.LastFrame;
            predictionMarker.anchorMin = new Vector2(x, 0.5f);
            predictionMarker.anchorMax = new Vector2(x, 0.5f);
        }

        void OnScrub(float frame)
        {
            if (!updatingScrubber) playback.Scrub(frame);
        }

        // ---------- Actions ----------

        string CurrentPlayId => loader.CurrentIndex >= 0 && loader.CurrentIndex < loader.Plays.Count
            ? loader.Plays[loader.CurrentIndex].id
            : null;

        void ShowRecorded()
        {
            if (loader.CurrentSuggestedId != null) _ = loader.LoadAtAsync(loader.CurrentIndex);
        }

        // The model reviews a different season than the replays, so most recorded plays have no
        // suggested version; then this opens the suggested plays instead of doing nothing.
        void ShowSuggested()
        {
            if (loader.CurrentSuggestedId != null) return;
            string id = CurrentPlayId;
            if (id != null && loader.HasSuggested(id)) _ = loader.LoadSuggestedAsync(id);
            else loader.NextSuggested(1);
        }

        string SuggestedHint()
        {
            int count = loader.Suggested.Count;
            if (count == 0) return "The API has no suggested plays (see SUGGESTED_PATH in its README).";
            if (loader.CurrentSuggestedId != null) return "Showing the model's suggested play (S)";
            string id = CurrentPlayId;
            return id != null && loader.HasSuggested(id)
                ? "The model's suggested version of this play (S)"
                : $"This play has no suggested version. Opens suggested play 1 of {count} (G)";
        }

        // ---------- Build ----------

        void Build()
        {
            var bar = gameObject.AddComponent<Image>();
            bar.sprite = UiStyle.Rounded(12);
            bar.type = Image.Type.Sliced;
            bar.color = UiStyle.Bar;

            var row = gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(8, 8, 5, 5);
            row.spacing = 2f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            AddIconButton(UiStyle.Glyph.SkipPrevious, loader.Previous, () => "Previous play (P)");
            AddIconButton(UiStyle.Glyph.Restart, playback.Restart, () => "Restart (R)");
            AddIconButton(UiStyle.Glyph.StepBack, () => playback.Step(-1), () => "Back one frame (Left)");
            playPauseIcon = (Image)AddIconButton(UiStyle.Glyph.Play, playback.TogglePlay, () => "Play / pause (Space)").Foreground;
            AddIconButton(UiStyle.Glyph.StepForward, () => playback.Step(+1), () => "Forward one frame (Right)");
            AddIconButton(UiStyle.Glyph.SkipNext, loader.Next, () => "Next play (N)");

            AddDivider();
            for (int i = 0; i < PlaybackController.Speeds.Length; i++)
            {
                int index = i;
                AddTextButton($"{PlaybackController.Speeds[i]:0.##}x", 46f, () => playback.SetSpeedIndex(index),
                    active: () => playback.SpeedIndex == index, hint: () => $"Speed ({index + 1})");
            }

            AddDivider();
            BuildScrubber();

            AddDivider();
            AddTextButton("Real", 52f, ShowRecorded,
                active: () => loader.CurrentSuggestedId == null,
                enabled: () => loader.Plays.Count > 0,
                hint: () => "The play as recorded (S)");
            Control suggested = AddTextButton("Suggested", 92f, ShowSuggested,
                active: () => loader.CurrentSuggestedId != null,
                enabled: () => loader.Suggested.Count > 0,
                hint: SuggestedHint);
            suggested.ActiveColor = UiStyle.Accent;
            AddIconButton(UiStyle.Glyph.ChevronLeft, () => loader.NextSuggested(-1), () => "Previous suggested play (Shift+G)",
                enabled: () => loader.Suggested.Count > 0, width: 26f);
            AddIconButton(UiStyle.Glyph.ChevronRight, () => loader.NextSuggested(+1), () => "Next suggested play (G)",
                enabled: () => loader.Suggested.Count > 0, width: 26f);

            AddDivider();
            AddView("Top", 46f, CameraRig.View.TopDown);
            AddView("Broadcast", 84f, CameraRig.View.Broadcast);
            AddView("1st person", 86f, CameraRig.View.FirstPerson);
            AddView("3rd person", 86f, CameraRig.View.ThirdPerson);

            AddDivider();
            AddTextButton("?", 30f, showShortcuts, hint: () => "Keyboard shortcuts (?)");
        }

        void AddView(string label, float width, CameraRig.View view)
        {
            AddTextButton(label, width, () => cameras.SetView(view),
                active: () => cameras.Current == view, hint: () => "Camera (C)");
        }

        Control AddIconButton(UiStyle.Glyph glyph, Action onClick, Func<string> hint,
            Func<bool> enabled = null, float width = IconButtonWidth)
        {
            GameObject go = CreateButtonRoot(glyph.ToString(), width);
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(IconSize, IconSize);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = UiStyle.Icon(glyph);
            icon.raycastTarget = false;
            return Register(go, icon, onClick, null, enabled, hint);
        }

        Control AddTextButton(string label, float width, Action onClick,
            Func<bool> active = null, Func<bool> enabled = null, Func<string> hint = null)
        {
            GameObject go = CreateButtonRoot(label, width);
            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = fontSize * 0.64f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return Register(go, text, onClick, active, enabled, hint);
        }

        GameObject CreateButtonRoot(string objectName, float width)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<LayoutElement>().preferredWidth = width;
            var background = go.AddComponent<Image>();
            background.sprite = UiStyle.Rounded(8);
            background.type = Image.Type.Sliced;
            background.color = Color.clear; // still receives clicks
            return go;
        }

        Control Register(GameObject go, Graphic foreground, Action onClick,
            Func<bool> active, Func<bool> enabled, Func<string> hint)
        {
            var control = new Control
            {
                Background = go.GetComponent<Image>(),
                Foreground = foreground,
                IsActive = active,
                IsEnabled = enabled,
                Hint = hint,
            };
            var cell = go.AddComponent<NavCell>();
            cell.Clicked = () =>
            {
                if (control.IsEnabled == null || control.IsEnabled()) onClick();
            };
            cell.Hovered = on =>
            {
                control.Hovered = on;
                hoverChanged?.Invoke(on ? control.Hint?.Invoke() : null);
            };
            controls.Add(control);
            return control;
        }

        void AddDivider()
        {
            var go = new GameObject("Divider", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.AddComponent<LayoutElement>().preferredWidth = 13f;
            var line = new GameObject("Line", typeof(RectTransform)).GetComponent<RectTransform>();
            line.SetParent(go.transform, false);
            line.anchorMin = new Vector2(0.5f, 0.25f);
            line.anchorMax = new Vector2(0.5f, 0.75f);
            line.sizeDelta = new Vector2(1f, 0f);
            var image = line.gameObject.AddComponent<Image>();
            image.color = UiStyle.Divider;
            image.raycastTarget = false;
        }

        void BuildScrubber()
        {
            GameObject root = DefaultControls.CreateSlider(new DefaultControls.Resources());
            root.name = "Scrubber";
            root.transform.SetParent(transform, false);
            var layout = root.AddComponent<LayoutElement>();
            layout.minWidth = 160f;
            layout.flexibleWidth = 1f;

            scrubber = root.GetComponent<Slider>();
            scrubber.navigation = new Navigation { mode = Navigation.Mode.None };
            scrubber.transition = Selectable.Transition.None;
            scrubber.minValue = 0f;
            scrubber.wholeNumbers = false;
            scrubber.onValueChanged.AddListener(OnScrub);

            // A thin rounded track with a filled part and a small round handle.
            var background = (RectTransform)root.transform.Find("Background");
            StyleBar(background, UiStyle.Track);
            var fillArea = (RectTransform)root.transform.Find("Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(fillArea.sizeDelta.x, 4f);
            var fill = (RectTransform)fillArea.Find("Fill");
            fill.GetComponent<Image>().sprite = UiStyle.Rounded(2);
            fill.GetComponent<Image>().type = Image.Type.Sliced;
            fill.GetComponent<Image>().color = UiStyle.TrackFill;

            var slideArea = (RectTransform)root.transform.Find("Handle Slide Area");
            var handle = (RectTransform)slideArea.Find("Handle");
            handle.anchorMin = new Vector2(0f, 0.5f);
            handle.anchorMax = new Vector2(0f, 0.5f);
            handle.sizeDelta = new Vector2(12f, 12f);
            var handleImage = handle.GetComponent<Image>();
            handleImage.sprite = UiStyle.Rounded(6);
            handleImage.color = Color.white;

            // Shares the handle's slide area, so its x matches the handle's at the same frame.
            predictionMarker = new GameObject("PredictionStart", typeof(RectTransform)).GetComponent<RectTransform>();
            predictionMarker.SetParent(slideArea, false);
            predictionMarker.SetSiblingIndex(0);
            predictionMarker.sizeDelta = new Vector2(3f, 12f);
            var markerImage = predictionMarker.gameObject.AddComponent<Image>();
            markerImage.sprite = UiStyle.Rounded(1);
            markerImage.type = Image.Type.Sliced;
            markerImage.color = UiStyle.Accent;
            markerImage.raycastTarget = false;
            predictionMarker.gameObject.SetActive(false);

            var cell = root.AddComponent<NavCell>();
            cell.Hovered = on => hoverChanged?.Invoke(on
                ? "Drag to scrub. On a suggested play, the gold tick is where the model's prediction starts."
                : null);
        }

        static void StyleBar(RectTransform rect, Color color)
        {
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 4f);
            var image = rect.GetComponent<Image>();
            image.sprite = UiStyle.Rounded(2);
            image.type = Image.Type.Sliced;
            image.color = color;
        }
    }
}
