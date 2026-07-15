using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Pure, rendering-independent geometry for the rope's hanging pendant (悬挂物).
    /// Deliberately has NO Win2D / Windows.* dependency so it can be unit-tested
    /// headlessly (see StarAttachment.Verify) and reused by the Win2D renderer.
    ///
    /// ────────────────────────────────────────────────────────────────────────
    /// NON-SEPARATION CONTRACT  (the guarantee the user asked for)
    /// ────────────────────────────────────────────────────────────────────────
    /// The pendant has NO independent "icon position" variable. Its attachment
    /// point is, by definition, the rope's last point (Bob). The icon is drawn
    /// entirely in a local frame whose ORIGIN is that attachment point, and the
    /// renderer applies an affine transform:
    ///
    ///        world = Rotation(angle) · localPoint + Tip
    ///
    /// Because this transform is "rotation ABOUT the attachment point" (the
    /// translation is added AFTER the rotation, and the rotation fixes the
    /// origin), the local origin (0,0) — where the star's top tip lives —
    /// maps EXACTLY to `Tip` for every frame and every swing angle. The star
    /// therefore cannot drift away from the rope: they share a single point by
    /// construction, not by tuning.
    ///
    /// The rope side of the contract is enforced by RopeSimulator: its distance
    /// constraints make every segment exactly _segLen long, so the bob (and thus
    /// `Tip`) can never exceed the rope's reach. Combined, the two halves prove
    /// end-to-end that the star and rope stay joined.
    /// </summary>
    public static class PendantGeometry
    {
        /// <summary>
        /// Immutable pendant layout. `Tip` is the rope's last point; `Direction`
        /// is the unit rope-end direction; `AngleRad` orients a local +Y-pointing
        /// shape along `Direction`; `BaseCenter` is the far end of the pendant.
        /// </summary>
        public readonly struct PendantState
        {
            public readonly Vector2 Tip;        // = rope's last point (Bob)
            public readonly Vector2 Direction;  // unit vector along rope end direction
            public readonly float AngleRad;     // Canvas rotation aligning +Y with Direction
            public readonly Vector2 BaseCenter; // far end of the pendant = Tip + Direction*Size

            public PendantState(Vector2 tip, Vector2 dir, float size)
            {
                Tip = tip;
                Direction = dir;
                // Angle so the pendant's local +Y axis (where the star extends)
                // aligns EXACTLY with the rope end direction `dir`. A column-vector
                // rotation by θ maps +Y=(0,1) to (-sinθ, cosθ); setting that equal
                // to dir=(c,s) gives θ = Atan2(-dir.X, dir.Y). With this, the
                // star's body continues the rope's final segment instead of hanging
                // 90° off it.
                AngleRad = MathF.Atan2(-dir.X, dir.Y);
                BaseCenter = tip + dir * size;
            }
        }

        /// <summary>
        /// Compute the pendant transform from the rope points. The FK rope
        /// guarantees every segment is exactly _segLen long, so a single last-segment
        /// direction is clean and jitter-free.
        /// </summary>
        public static PendantState ComputePendant(Vector2[] ropePoints, float iconSize)
        {
            if (ropePoints == null || ropePoints.Length < 2)
                return new PendantState(Vector2.Zero, new Vector2(0, 1), iconSize);

            Vector2 tip = ropePoints[ropePoints.Length - 1];
            Vector2 dir = tip - ropePoints[ropePoints.Length - 2];
            float len = dir.Length();
            if (len < 1e-4f)
                dir = new Vector2(0, 1);
            else
                dir /= len;

            if (float.IsNaN(tip.X) || float.IsNaN(tip.Y) ||
                float.IsNaN(dir.X) || float.IsNaN(dir.Y))
            {
                return new PendantState(Vector2.Zero, new Vector2(0, 1), iconSize);
            }

            return new PendantState(tip, dir, iconSize);
        }

        /// <summary>
        /// Apply the exact same affine transform the Win2D renderer uses:
        ///     world = Rotation(angle) · local + Tip
        /// Reconstructed from System.Numerics.Matrix3x2.CreateTranslation(Tip)
        /// multiplied by CreateRotation(angle) (column-vector convention, matching
        /// Win2D/Direct2D):
        ///     world.X =  c·x - s·y + Tip.X
        ///     world.Y =  s·x + c·y + Tip.Y      (c = cos angle, s = sin angle)
        /// Note: for local == (0,0) this always returns Tip, regardless of the
        /// rotation convention — which is precisely why the attachment is
        /// separation-proof.
        /// </summary>
        public static Vector2 TransformPoint(in PendantState p, Vector2 local)
        {
            float c = MathF.Cos(p.AngleRad);
            float s = MathF.Sin(p.AngleRad);
            float x = c * local.X - s * local.Y + p.Tip.X;
            float y = s * local.X + c * local.Y + p.Tip.Y;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Local-space polygon for a 5-pointed star whose TOP TIP is at the
        /// origin (0,0) and which extends in +Y. Mirrors the renderer's
        /// FillStarTip so the headless test proves the same geometry the screen
        /// draws. The top tip being the local origin is what makes the star's
        /// attachment point identical to the rope end.
        /// </summary>
        public static Vector2[] StarLocalPolygon(float size)
        {
            int points = 5;
            var pts = new Vector2[points * 2];
            float outer = size * 0.5f;
            float inner = outer * 0.45f;
            float cx = 0, cy = size / 2f;
            for (int i = 0; i < points * 2; i++)
            {
                float rad = (i % 2 == 0) ? outer : inner;
                double a = -Math.PI / 2 + i * Math.PI / points;
                pts[i] = new Vector2(cx + (float)(Math.Cos(a) * rad),
                                     cy + (float)(Math.Sin(a) * rad));
            }
            return pts;
        }
    }
}
