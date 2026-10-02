using UnityEngine;

namespace CPW
{
    /// <summary>
    /// Orthographic battle camera looking down +Z at the z = 0 plane. Follows the active penguin, then the shot
    /// (FireHandle.CameraTarget), then explosions; the player can pinch-zoom (BattleOptions.CameraZoomMin/Max) and
    /// drag to pan. Stays inside the level's camera bounds and applies Fx camera shake.
    /// </summary>
    public class BattleCamera : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public float Zoom { get; private set; } = 0.8f;

        LevelData level;
        Rect bounds;
        Transform follow;
        Penguin followPenguin;
        FireHandle shot;
        Vector2 explosionPos;
        float explosionUntil;
        Vector2 center, velocity;
        float manualUntil;          // user panned: don't auto-follow until then
        float targetZoom = 0.8f;
        float overviewUntil;

        public static BattleCamera Create(Transform parent, LevelData level)
        {
            var go = new GameObject("BattleCamera");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<BattleCamera>();
            c.level = level;
            c.Cam = go.AddComponent<Camera>();
            c.Cam.orthographic = true;
            c.Cam.clearFlags = CameraClearFlags.SolidColor;
            c.Cam.backgroundColor = new Color(0.55f, 0.78f, 0.95f);
            c.Cam.nearClipPlane = 0.1f;
            c.Cam.farClipPlane = 400f;
            c.Cam.depth = 0;
            go.AddComponent<AudioListener>();
            c.bounds = level.cameraBounds.width > 1 ? level.cameraBounds : new Rect(-5, -5, level.size.x + 10, level.size.y + 10);
            c.center = new Vector2(level.size.x * 0.5f, level.size.y * 0.5f);
            c.Zoom = c.targetZoom = Mathf.Clamp(0.8f, BattleRules.CameraZoomMin, BattleRules.CameraZoomMax);
            c.Apply(Vector2.zero);
            return c;
        }

        float OrthoFor(float zoom) => BattleRules.OrthoAtZoom1 / Mathf.Max(0.05f, zoom);

        /// <summary>Show the whole level for a moment at the start (original CameraStartZoomTimer).</summary>
        public void Overview()
        {
            float aspect = Mathf.Max(0.5f, Cam.aspect);
            float needed = Mathf.Max(bounds.height * 0.5f, bounds.width * 0.5f / aspect);
            Zoom = Mathf.Clamp(BattleRules.OrthoAtZoom1 / needed, BattleRules.CameraZoomMin, BattleRules.CameraZoomMax);
            center = bounds.center;
            overviewUntil = Time.time + 1.6f;
        }

        public void FollowPenguin(Penguin p)
        {
            followPenguin = p;
            follow = p != null ? p.transform : null;
            shot = null;
            manualUntil = 0;
            explosionUntil = 0;   // a new turn's penguin wins over the last turn's explosion
        }

        public void FollowShot(FireHandle h)
        {
            shot = h;
            manualUntil = 0;
        }

        public void LookAtExplosion(Vector2 pos, float radius)
        {
            explosionPos = pos;
            explosionUntil = Time.time + 1.4f;
            manualUntil = 0;
        }

        /// <summary>Drag pan by a world-space delta (finger moved by -delta).</summary>
        public void Pan(Vector2 worldDelta)
        {
            center += worldDelta;
            manualUntil = Time.time + 4f;
            velocity = Vector2.zero;
        }

        /// <summary>Pinch zoom: multiply the zoom (bigger = closer).</summary>
        public void ZoomBy(float factor)
        {
            targetZoom = Mathf.Clamp(targetZoom * factor, BattleRules.CameraZoomMin, BattleRules.CameraZoomMax);
            Zoom = targetZoom;
            manualUntil = Time.time + 4f;
        }

        /// <summary>Screen pixels → world units on the z = 0 plane.</summary>
        public Vector2 ScreenToWorld(Vector2 screen)
        {
            var w = Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, -Cam.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        public Vector2 WorldToScreen(Vector2 world) => Cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0));

        public float WorldPerPixel => Cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Vector2 target = center;
            bool auto = Time.time >= manualUntil && Time.time >= overviewUntil;
            if (auto)
            {
                if (shot != null && !shot.Done && shot.CameraTarget != null) target = shot.CameraTarget.position;
                else if (Time.time < explosionUntil) target = explosionPos;
                else if (follow != null && followPenguin != null && followPenguin.Alive) target = follow.position + Vector3.up * 2f;
                else if (follow != null && followPenguin == null) target = follow.position;
                float smooth = shot != null && !shot.Done ? 0.12f : 0.35f;
                center = Vector2.SmoothDamp(center, target, ref velocity, smooth, 200f, dt);
            }
            if (Time.time >= overviewUntil) Zoom = Mathf.Lerp(Zoom, targetZoom, 1f - Mathf.Exp(-4f * dt));

            // camera shake from Fx (decays here)
            Vector2 shake = Vector2.zero;
            if (Fx.ShakeTime > 0)
            {
                Fx.ShakeTime -= dt;
                float s = Fx.ShakeStrength * Mathf.Clamp01(Fx.ShakeTime * 3f);
                shake = new Vector2(Random.Range(-s, s), Random.Range(-s, s));
                if (Fx.ShakeTime <= 0) { Fx.ShakeTime = 0; Fx.ShakeStrength = 0; }
            }
            Apply(shake);
        }

        void Apply(Vector2 shake)
        {
            float ortho = OrthoFor(Zoom);
            float aspect = Mathf.Max(0.5f, Cam.aspect);
            // never show more than the camera bounds
            float maxOrtho = Mathf.Min(bounds.height * 0.5f, bounds.width * 0.5f / aspect);
            if (maxOrtho > 2f && ortho > maxOrtho) ortho = maxOrtho;
            Cam.orthographicSize = ortho;
            float halfW = ortho * aspect;
            Vector2 c = center;
            c.x = bounds.width > halfW * 2 ? Mathf.Clamp(c.x, bounds.xMin + halfW, bounds.xMax - halfW) : bounds.center.x;
            c.y = bounds.height > ortho * 2 ? Mathf.Clamp(c.y, bounds.yMin + ortho, bounds.yMax - ortho) : bounds.center.y;
            center = c;
            transform.position = new Vector3(c.x + shake.x, c.y + shake.y, -60f);
            transform.rotation = Quaternion.identity;
        }
    }
}
