using System.Collections.Generic;
using System.Linq;
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
    /// Dropdown choosing who the first- and third-person cameras follow: the ball (whoever
    /// has it, or the ball itself in the air) or one specific player. Built in code with
    /// TMP_DefaultControls; only shown while a follow view is active.
    /// </summary>
    public class FollowPicker : MonoBehaviour
    {
        const string BallOption = "Ball carrier (auto)";
        const float ItemHeight = 28f;

        static readonly Color Panel = new Color32(0x1E, 0x26, 0x30, 0xF0);
        static readonly Color Hover = new Color32(0x34, 0x40, 0x4E, 0xFF);
        static readonly Color Text = Color.white;
        static readonly Color Accent = new Color32(0xF2, 0xC1, 0x4E, 0xFF);

        CameraRig cameras;
        PlaybackController playback;
        TMP_Dropdown dropdown;
        readonly List<long> optionIds = new(); // nflId per option, 0 = ball
        bool updating;

        public void Init(PlaybackController playbackController, CameraRig cameraRig, float fontSize)
        {
            playback = playbackController;
            cameras = cameraRig;
            Build(fontSize);
            cameras.FollowChanged += Refresh;
            dropdown.onValueChanged.AddListener(OnPicked);
            Refresh();
        }

        void OnDestroy()
        {
            if (cameras != null) cameras.FollowChanged -= Refresh;
        }

        void Update()
        {
            bool show = cameras != null && cameras.IsFollowView;
            if (dropdown.gameObject.activeSelf != show)
            {
                if (!show) dropdown.Hide();
                dropdown.gameObject.SetActive(show);
            }
        }

        /// <summary>Rebuild the options for the current play and select the current choice.</summary>
        void Refresh()
        {
            optionIds.Clear();
            var labels = new List<string> { BallOption };
            optionIds.Add(0);

            PlayTimeline timeline = playback.Timeline;
            if (timeline != null)
            {
                // Offense first, then defense; by jersey number within each side.
                var players = timeline.Players
                    .OrderBy(p => p.side == "offense" ? 0 : 1)
                    .ThenBy(p => p.jerseyNumber);
                foreach (PlayerInfo p in players)
                {
                    labels.Add($"{p.club} {p.NumberLabel} {p.position}  {p.name}");
                    optionIds.Add(p.nflId);
                }
            }

            updating = true;
            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.SetValueWithoutNotify(Mathf.Max(0, optionIds.IndexOf(cameras.FollowedPlayerId)));
            dropdown.RefreshShownValue();
            updating = false;
        }

        void OnPicked(int index)
        {
            if (updating || index < 0 || index >= optionIds.Count) return;
            cameras.FollowPlayer(optionIds[index]);
            // Keep keyboard playback controls from also driving the dropdown.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Build(float fontSize)
        {
            GameObject root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            root.name = "FollowDropdown";
            root.transform.SetParent(transform, false);
            // Fill this object's rect; the HUD sizes and places it.
            var rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            dropdown = root.GetComponent<TMP_Dropdown>();
            dropdown.navigation = new Navigation { mode = Navigation.Mode.None };
            root.GetComponent<Image>().color = Color.white;
            dropdown.colors = Tints(dropdown.colors);

            Style(dropdown.captionText, fontSize * 0.8f);
            dropdown.captionText.text = BallOption;
            ((RectTransform)dropdown.captionText.transform).offsetMax = new Vector2(-12f, 0f);
            root.transform.Find("Arrow").gameObject.SetActive(false);

            // The open list: taller, dark, larger rows.
            var template = dropdown.template;
            template.sizeDelta = new Vector2(0f, 460f);
            template.GetComponent<Image>().color = Panel;
            var content = (RectTransform)template.Find("Viewport/Content");
            content.sizeDelta = new Vector2(0f, ItemHeight);
            var item = (RectTransform)content.Find("Item");
            item.sizeDelta = new Vector2(0f, ItemHeight);
            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.colors = Tints(itemToggle.colors);
            item.Find("Item Background").GetComponent<Image>().color = Color.white;
            var check = (RectTransform)item.Find("Item Checkmark");
            check.GetComponent<Image>().color = Accent;
            check.sizeDelta = new Vector2(6f, 6f);
            Style(dropdown.itemText, fontSize * 0.75f);

            var scrollbar = template.Find("Scrollbar");
            if (scrollbar != null)
            {
                scrollbar.GetComponent<Image>().color = Panel;
                var handle = scrollbar.Find("Sliding Area/Handle");
                if (handle != null) handle.GetComponent<Image>().color = Hover;
            }
        }

        // Selectable tints multiply the image color, so images stay white and these carry the color.
        static ColorBlock Tints(ColorBlock colors)
        {
            colors.normalColor = Panel;
            colors.highlightedColor = Hover;
            colors.pressedColor = Hover;
            colors.selectedColor = Panel;
            return colors;
        }

        static void Style(TMP_Text text, float size)
        {
            text.color = Text;
            text.fontSize = size;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }
    }
}
