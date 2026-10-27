using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hindsight.UI
{
    /// <summary>
    /// The keyboard shortcuts, in a dialog over a dimmed screen instead of always on screen.
    /// Opens with ? (Shift+/) or the control bar's ? button; closes with ?, Esc, or a click
    /// outside it. The list itself is PlaybackInput.Shortcuts.
    /// </summary>
    public class ShortcutsModal : MonoBehaviour
    {
        const float PanelWidth = 520f;

        GameObject root;

        public bool IsOpen => root != null && root.activeSelf;

        public void Init(float fontSize)
        {
            Build(fontSize);
            root.SetActive(false);
        }

        public void Toggle() => SetOpen(!IsOpen);

        public void SetOpen(bool open)
        {
            root.SetActive(open);
            if (open) transform.SetAsLastSibling(); // above the rest of the HUD
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.slashKey.wasPressedThisFrame && kb.shiftKey.isPressed) Toggle();
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
        }

        void Build(float fontSize)
        {
            var rect = (RectTransform)transform;
            root = new GameObject("Shortcuts", typeof(RectTransform));
            root.transform.SetParent(rect, false);
            Stretch((RectTransform)root.transform);

            // The scrim and the panel are siblings, so a click on the panel never reaches the
            // scrim's close handler (pointer events bubble up to parents).
            var scrim = new GameObject("Scrim", typeof(RectTransform));
            scrim.transform.SetParent(root.transform, false);
            Stretch((RectTransform)scrim.transform);
            scrim.AddComponent<Image>().color = UiStyle.Scrim;
            scrim.AddComponent<NavCell>().Clicked = () => SetOpen(false);

            var panel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);
            var panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.sprite = UiStyle.Rounded(16);
            panelImage.type = Image.Type.Sliced;
            panelImage.color = UiStyle.ModalPanel;
            var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(28, 28, 24, 24);
            column.spacing = 6f;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RectTransform title = Row(panel, 40f);
            Label(title, "Keyboard shortcuts", fontSize * 0.9f, UiStyle.Text, FontStyles.Bold, flexible: true);
            Label(title, "Esc to close", fontSize * 0.6f, UiStyle.TextMuted, FontStyles.Normal, flexible: false);

            foreach ((string[] keys, string action) in PlaybackInput.Shortcuts)
            {
                RectTransform row = Row(panel, 32f);
                Label(row, action, fontSize * 0.68f, UiStyle.TextMuted, FontStyles.Normal, flexible: true);
                foreach (string key in keys) Key(row, key, fontSize * 0.6f);
            }
        }

        static RectTransform Row(RectTransform parent, float height)
        {
            var row = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return row;
        }

        static void Label(RectTransform parent, string text, float size, Color color, FontStyles style, bool flexible)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = flexible ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            if (flexible) go.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        /// <summary>A key cap: the key's name on a rounded chip.</summary>
        static void Key(RectTransform parent, string key, float size)
        {
            var chip = new GameObject($"Key {key}", typeof(RectTransform));
            chip.transform.SetParent(parent, false);
            var image = chip.AddComponent<Image>();
            image.sprite = UiStyle.Rounded(6);
            image.type = Image.Type.Sliced;
            image.color = UiStyle.ActiveFill;
            image.raycastTarget = false;
            var layout = chip.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(9, 9, 3, 3);
            layout.childControlWidth = layout.childControlHeight = true;
            var element = chip.AddComponent<LayoutElement>();
            element.minWidth = 28f;
            element.preferredHeight = 24f;

            var text = new GameObject("Text", typeof(RectTransform));
            text.transform.SetParent(chip.transform, false);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            tmp.text = key;
            tmp.fontSize = size;
            tmp.color = UiStyle.Text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
