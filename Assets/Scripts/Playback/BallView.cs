using UnityEngine;

namespace Hindsight.Playback
{
    /// <summary>
    /// Visual for the football. Lives on BallModel.prefab. Points its long axis (+z) along
    /// its direction of travel while moving, and rolls about that axis by the given spin.
    /// </summary>
    public class BallView : MonoBehaviour
    {
        const float MinMoveForHeading = 0.02f;

        Vector3 lastPosition;
        bool hasLast;
        Quaternion heading = Quaternion.identity;

        /// <param name="spin">Degrees of roll about the long axis (the spiral of a thrown ball).</param>
        public void SetPosition(Vector3 position, float spin = 0f)
        {
            Vector3 delta = position - lastPosition;
            if (hasLast && delta.sqrMagnitude > MinMoveForHeading * MinMoveForHeading)
                heading = Quaternion.LookRotation(delta);
            transform.SetPositionAndRotation(position, heading * Quaternion.Euler(0f, 0f, spin));
            lastPosition = position;
            hasLast = true;
        }

        /// <summary>Forget the previous position, e.g. after a jump to another frame or play.</summary>
        public void ResetHeading()
        {
            hasLast = false;
            heading = Quaternion.identity;
            transform.rotation = heading;
        }
    }
}
