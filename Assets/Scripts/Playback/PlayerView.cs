using Hindsight.Data;
using TMPro;
using UnityEngine;

namespace Hindsight.Playback
{
    /// <summary>
    /// Visual for one player. Lives on PlayerAnimated.prefab (Hindsight > Setup Animated Player).
    /// The root's forward (+z) is the player's facing. The Animator's locomotion blend tree
    /// follows the tracking velocity; a prefab without an Animator just slides.
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ForwardParam = Animator.StringToHash("Forward");
        static readonly int StrafeParam = Animator.StringToHash("Strafe");
        /// <summary>Seconds of play time for the blend parameters to catch up; the data is 10 Hz.</summary>
        const float MotionDamping = 0.15f;

        [Tooltip("Renderers tinted with the jersey color.")]
        [SerializeField] Renderer[] jerseyRenderers;
        [Tooltip("Renderers tinted with the number/trim color (e.g. the facing marker).")]
        [SerializeField] Renderer[] trimRenderers;
        [SerializeField] TMP_Text numberLabel;
        [Tooltip("First-person camera height above the turf, in yards.")]
        [SerializeField] float eyeHeight = 0.75f;
        [Tooltip("Where a carried ball sits, in the player's frame (x right, y up, z forward), yards: tucked under the right arm.")]
        [SerializeField] Vector3 ballHoldOffset = new(0.22f, 1.15f, 0.22f);

        [Header("Animation (optional)")]
        [Tooltip("Locomotion animator with Forward and Strafe floats (yd/s). Empty = no animation.")]
        [SerializeField] Animator animator;
        [Tooltip("Ground speed (yd/s) of the fastest clip in the blend tree. Faster than this, the clip plays faster.")]
        [SerializeField] float topClipSpeed = 7.5f;

        MaterialPropertyBlock block;

        public PlayerInfo Info { get; private set; }
        public float EyeHeight => eyeHeight;
        /// <summary>World position of a ball this player is carrying.</summary>
        public Vector3 BallHoldPosition => transform.TransformPoint(ballHoldOffset);

        public void Setup(PlayerInfo info, TeamColors.Kit kit)
        {
            Info = info;
            name = $"{info.club} {info.NumberLabel} {info.name}";
            Tint(jerseyRenderers, kit.Jersey);
            Tint(trimRenderers, kit.Number);
            if (numberLabel != null)
            {
                numberLabel.text = info.jerseyNumber > 0 ? info.jerseyNumber.ToString() : info.position;
                numberLabel.color = kit.Number;
            }
        }

        /// <param name="position">Unity world position.</param>
        /// <param name="yaw">Degrees about +Y; same convention as the data's o/dir.</param>
        public void SetPose(Vector3 position, float yaw)
        {
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <param name="velocity">Yards per second of play time in the player's own frame: x = right, y = forward.</param>
        /// <param name="rate">Playback speed; 0 while paused, which freezes the pose.</param>
        public void SetMotion(Vector2 velocity, float rate)
        {
            if (animator == null) return;
            float dt = Time.deltaTime * rate;
            animator.SetFloat(StrafeParam, velocity.x, MotionDamping, dt);
            animator.SetFloat(ForwardParam, velocity.y, MotionDamping, dt);
            animator.speed = rate * Mathf.Max(1f, velocity.magnitude / Mathf.Max(topClipSpeed, 0.1f));
        }

        /// <summary>Hide or show this player's meshes and number (e.g. for a first-person camera inside them).</summary>
        public void SetHidden(bool hidden)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                r.enabled = !hidden;
        }

        void Tint(Renderer[] renderers, Color color)
        {
            if (renderers == null) return;
            block ??= new MaterialPropertyBlock();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                r.SetPropertyBlock(block);
            }
        }
    }
}
