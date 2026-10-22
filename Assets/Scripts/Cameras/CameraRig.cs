using System;
using Hindsight.Field;
using Hindsight.Playback;
using UnityEngine;

namespace Hindsight.Cameras
{
    /// <summary>
    /// Four views, cycled with Next():
    ///   TopDown      orthographic, frames the whole field (default)
    ///   Broadcast    perspective from the near sideline, follows the ball along x
    ///   FirstPerson  from the followed player's head, looking along their body orientation
    ///   ThirdPerson  close behind and above the followed player
    /// The first- and third-person views follow the ball carrier by default, and ride behind
    /// the ball while it is in the air or loose. FollowPlayer() pins them to one player instead.
    /// All cameras are tagged MainCamera; only the active one is enabled, so Camera.main tracks it.
    /// </summary>
    [DefaultExecutionOrder(-100)] // position cameras before billboards read them
    public class CameraRig : MonoBehaviour
    {
        public enum View { TopDown, Broadcast, FirstPerson, ThirdPerson }

        [Header("Top-down")]
        [Tooltip("Yards of space kept around the field.")]
        [SerializeField] float topDownMargin = 4f;

        [Header("Broadcast")]
        [SerializeField] float broadcastFov = 38f;
        [SerializeField] float broadcastHeight = 24f;
        [Tooltip("Distance behind the near sideline (data y = 0).")]
        [SerializeField] float broadcastSetback = 22f;
        [Tooltip("Field y the camera aims at.")]
        [SerializeField] float broadcastAimY = 22f;
        [SerializeField] float followSmoothTime = 0.35f;
        [SerializeField] float minFollowX = 25f;
        [SerializeField] float maxFollowX = 95f;

        [Header("First person")]
        [SerializeField] float firstPersonFov = 75f;
        [Tooltip("Camera height on a player. Bodies are 0.8 yd tall.")]
        [SerializeField] float eyeHeight = 0.75f;
        [Tooltip("Degrees the view is tilted down.")]
        [SerializeField] float firstPersonPitch = 4f;
        [Tooltip("Yards behind the ball while it is in the air or loose.")]
        [SerializeField] float ballChaseDistance = 1.5f;
        [SerializeField] float ballChaseHeight = 0.3f;

        [Header("Third person")]
        [SerializeField] float thirdPersonFov = 60f;
        [SerializeField] float thirdPersonDistance = 4f;
        [SerializeField] float thirdPersonHeight = 2.2f;
        [Tooltip("Yards ahead of the subject the camera aims at.")]
        [SerializeField] float thirdPersonLookAhead = 4f;

        [Header("Follow")]
        [Tooltip("Seconds to smooth the subject's heading (orientation data is jittery).")]
        [SerializeField] float headingSmoothTime = 0.15f;
        [Tooltip("Seconds to blend when the subject changes, e.g. QB to ball to receiver.")]
        [SerializeField] float subjectBlendTime = 0.35f;

        [SerializeField] Color background = new Color32(0x16, 0x1C, 0x24, 0xFF);

        public View Current { get; private set; } = View.TopDown;
        public bool IsFollowView => Current is View.FirstPerson or View.ThirdPerson;
        public Camera TopDown { get; private set; }
        public Camera Broadcast { get; private set; }
        public Camera Follow { get; private set; }

        /// <summary>nflId the follow views are pinned to, or 0 to follow the ball.</summary>
        public long FollowedPlayerId { get; private set; }
        /// <summary>Raised when the follow choice changes, including a reset on a new play.</summary>
        public event Action FollowChanged;

        PlaybackController playback;
        Transform target;
        float followX = FieldBuilder.Length / 2f;
        float followVelocity;

        int followedIndex = -1;        // index into Timeline.Players, -1 = follow the ball
        int lastSubject = int.MinValue; // player index, -1 for the ball
        float heading;
        float headingVelocity;
        bool snap = true;
        float blend = 1f;
        Vector3 blendFromPosition;
        Quaternion blendFromRotation;
        PlayerView hiddenView;

        public void Build(PlaybackController playbackController)
        {
            playback = playbackController;
            playback.PlayChanged += OnPlayChanged;

            TopDown = CreateCamera("TopDownCamera");
            TopDown.orthographic = true;
            TopDown.transform.SetPositionAndRotation(
                new Vector3(FieldBuilder.Length / 2f, 60f, FieldBuilder.Width / 2f),
                Quaternion.Euler(90f, 0f, 0f));

            Broadcast = CreateCamera("BroadcastCamera");
            Broadcast.orthographic = false;
            Broadcast.fieldOfView = broadcastFov;

            Follow = CreateCamera("FollowCamera");
            Follow.orthographic = false;

            SetView(View.TopDown);
        }

        void OnDestroy()
        {
            if (playback != null) playback.PlayChanged -= OnPlayChanged;
        }

        /// <summary>What the broadcast camera follows (normally the ball).</summary>
        public void SetTarget(Transform followTarget)
        {
            target = followTarget;
            if (target != null) followX = ClampX(target.position.x);
            followVelocity = 0f;
        }

        /// <summary>Cycle top-down > broadcast > first person > third person.</summary>
        public void Next(int direction = 1)
        {
            int count = Enum.GetValues(typeof(View)).Length;
            SetView((View)((((int)Current + direction) % count + count) % count));
        }

        public void SetView(View view)
        {
            Current = view;
            TopDown.gameObject.SetActive(view == View.TopDown);
            Broadcast.gameObject.SetActive(view == View.Broadcast);
            Follow.gameObject.SetActive(IsFollowView);
            Follow.fieldOfView = view == View.FirstPerson ? firstPersonFov : thirdPersonFov;
            Follow.nearClipPlane = view == View.FirstPerson ? 0.05f : 0.3f;
            snap = true;
            if (!IsFollowView) Hide(null);
        }

        /// <summary>Pin the follow views to one player, or pass 0 to follow the ball again.</summary>
        public void FollowPlayer(long nflId)
        {
            FollowedPlayerId = nflId;
            followedIndex = IndexOf(nflId);
            if (followedIndex < 0) FollowedPlayerId = 0;
            FollowChanged?.Invoke();
        }

        void OnPlayChanged(PlayTimeline timeline)
        {
            // Views are reused between plays, so un-hide before the subject changes under us.
            Hide(null);
            snap = true;
            heading = 90f; // the offense always moves toward +x
            // Keep following the same player if they are in this play too.
            followedIndex = IndexOf(FollowedPlayerId);
            if (followedIndex < 0) FollowedPlayerId = 0;
            FollowChanged?.Invoke();
        }

        void LateUpdate()
        {
            if (TopDown == null) return;

            // Fit the whole field for any aspect ratio.
            float halfLength = FieldBuilder.Length / 2f + topDownMargin;
            float halfWidth = FieldBuilder.Width / 2f + topDownMargin;
            TopDown.orthographicSize = Mathf.Max(halfWidth, halfLength / Mathf.Max(TopDown.aspect, 0.01f));

            if (target != null)
                followX = Mathf.SmoothDamp(followX, ClampX(target.position.x), ref followVelocity, followSmoothTime);
            var position = new Vector3(followX, broadcastHeight, -broadcastSetback);
            var aim = new Vector3(followX, 0f, broadcastAimY);
            Broadcast.transform.SetPositionAndRotation(position, Quaternion.LookRotation(aim - position));

            if (IsFollowView) UpdateFollow();
        }

        void UpdateFollow()
        {
            PlayTimeline timeline = playback != null ? playback.Timeline : null;
            if (timeline == null || playback.Ball == null) return;
            float frame = playback.CurrentFrame;

            int subject = followedIndex >= 0 ? followedIndex : timeline.CarrierAt(frame);
            if (subject >= playback.PlayerViews.Count) subject = -1;
            bool onBall = subject < 0;

            Vector3 anchor;
            float targetHeading;
            if (onBall)
            {
                anchor = playback.Ball.transform.position;
                targetHeading = BallHeading(timeline, frame, heading);
            }
            else
            {
                anchor = playback.PlayerViews[subject].transform.position;
                targetHeading = timeline.PlayerOrientation(subject, frame);
            }

            if (snap)
            {
                heading = targetHeading;
                headingVelocity = 0f;
                blend = 1f;
            }
            else
            {
                if (subject != lastSubject)
                {
                    blendFromPosition = Follow.transform.position;
                    blendFromRotation = Follow.transform.rotation;
                    blend = 0f;
                }
                heading = Mathf.SmoothDampAngle(heading, targetHeading, ref headingVelocity, headingSmoothTime);
            }
            lastSubject = subject;
            snap = false;

            Quaternion yaw = Quaternion.Euler(0f, heading, 0f);
            Vector3 forward = yaw * Vector3.forward;
            Vector3 position;
            Quaternion rotation;

            if (Current == View.FirstPerson && !onBall)
            {
                position = anchor + Vector3.up * eyeHeight;
                rotation = yaw * Quaternion.Euler(firstPersonPitch, 0f, 0f);
            }
            else if (Current == View.FirstPerson)
            {
                position = anchor - forward * ballChaseDistance + Vector3.up * ballChaseHeight;
                rotation = Quaternion.LookRotation(anchor + forward * 4f - position);
            }
            else
            {
                // Closer behind the ball than behind a player: it is much smaller.
                float distance = onBall ? thirdPersonDistance * 0.75f : thirdPersonDistance;
                float height = onBall ? thirdPersonHeight * 0.5f : thirdPersonHeight;
                position = anchor - forward * distance + Vector3.up * height;
                Vector3 look = anchor + forward * thirdPersonLookAhead + Vector3.up * 0.4f;
                rotation = Quaternion.LookRotation(look - position);
            }

            if (blend < 1f)
            {
                blend = Mathf.Min(1f, blend + Time.deltaTime / Mathf.Max(subjectBlendTime, 0.01f));
                float t = Mathf.SmoothStep(0f, 1f, blend);
                position = Vector3.Lerp(blendFromPosition, position, t);
                rotation = Quaternion.Slerp(blendFromRotation, rotation, t);
            }
            Follow.transform.SetPositionAndRotation(position, rotation);

            // Don't look out through the inside of the player we're riding.
            Hide(Current == View.FirstPerson && !onBall ? playback.PlayerViews[subject] : null);
        }

        /// <summary>Ball's direction of travel in degrees (0 = +y, clockwise), or fallback when it's still.</summary>
        static float BallHeading(PlayTimeline timeline, float frame, float fallback)
        {
            Vector2 delta = timeline.BallPosition(frame + 1f) - timeline.BallPosition(frame - 1f);
            if (delta.sqrMagnitude < 0.05f * 0.05f) return fallback;
            return Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
        }

        void Hide(PlayerView view)
        {
            if (hiddenView == view) return;
            if (hiddenView != null) hiddenView.SetHidden(false);
            hiddenView = view;
            if (hiddenView != null) hiddenView.SetHidden(true);
        }

        int IndexOf(long nflId)
        {
            var players = playback != null ? playback.Timeline?.Players : null;
            if (nflId == 0 || players == null) return -1;
            for (int p = 0; p < players.Length; p++)
                if (players[p].nflId == nflId) return p;
            return -1;
        }

        float ClampX(float x) => Mathf.Clamp(x, minFollowX, maxFollowX);

        Camera CreateCamera(string cameraName)
        {
            var go = new GameObject(cameraName) { tag = "MainCamera" };
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            return cam;
        }
    }
}
