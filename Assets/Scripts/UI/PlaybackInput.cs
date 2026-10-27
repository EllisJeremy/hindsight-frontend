using Hindsight.Cameras;
using Hindsight.Data;
using Hindsight.Playback;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Hindsight.UI
{
    /// <summary>
    /// Keyboard controls (Input System):
    ///   Space        play / pause
    ///   Left/Right   step one frame back / forward
    ///   1-4          speed 0.25x / 0.5x / 1x / 2x
    ///   R            restart
    ///   N or ]       next play       P or [   previous play
    ///   S            toggle the recorded play and the model's suggested version
    ///   G            next suggested play     Shift+G   previous suggested play
    ///   C            next camera: top-down > broadcast > first person > third person
    ///   Shift+C      previous camera
    ///   ?            keyboard shortcuts (ShortcutsModal handles this one)
    /// </summary>
    public class PlaybackInput : MonoBehaviour
    {
        /// <summary>What ShortcutsModal lists. Keep in step with Update below.</summary>
        public static readonly (string[] keys, string action)[] Shortcuts =
        {
            (new[] { "Space" }, "Play / pause"),
            (new[] { "Left", "Right" }, "Step one frame back / forward"),
            (new[] { "1", "2", "3", "4" }, "Speed 0.25x / 0.5x / 1x / 2x"),
            (new[] { "R" }, "Restart from just before the snap"),
            (new[] { "N", "]" }, "Next play"),
            (new[] { "P", "[" }, "Previous play"),
            (new[] { "S" }, "Switch between the recorded and suggested play"),
            (new[] { "G" }, "Next suggested play"),
            (new[] { "Shift+G" }, "Previous suggested play"),
            (new[] { "C" }, "Next camera"),
            (new[] { "Shift+C" }, "Previous camera"),
            (new[] { "?" }, "Show or hide these shortcuts"),
        };

        PlaybackController playback;
        PlayLoader loader;
        CameraRig cameras;

        public void Init(PlaybackController playbackController, PlayLoader playLoader, CameraRig cameraRig)
        {
            playback = playbackController;
            loader = playLoader;
            cameras = cameraRig;
        }

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || playback == null) return;

            if (kb.spaceKey.wasPressedThisFrame) playback.TogglePlay();
            if (kb.rightArrowKey.wasPressedThisFrame) playback.Step(+1);
            if (kb.leftArrowKey.wasPressedThisFrame) playback.Step(-1);
            if (kb.rKey.wasPressedThisFrame) playback.Restart();

            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) playback.SetSpeedIndex(0);
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) playback.SetSpeedIndex(1);
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) playback.SetSpeedIndex(2);
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) playback.SetSpeedIndex(3);

            if (loader != null)
            {
                if (kb.nKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame) loader.Next();
                if (kb.pKey.wasPressedThisFrame || kb.leftBracketKey.wasPressedThisFrame) loader.Previous();
                if (kb.sKey.wasPressedThisFrame) loader.ToggleSuggested();
                if (kb.gKey.wasPressedThisFrame) loader.NextSuggested(kb.shiftKey.isPressed ? -1 : 1);
            }

            if (cameras != null && kb.cKey.wasPressedThisFrame)
                cameras.Next(kb.shiftKey.isPressed ? -1 : 1);
        }
    }
}
