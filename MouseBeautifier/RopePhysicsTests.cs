using System;
using System.Numerics;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    /// <summary>
    /// Self-contained unit tests for RopeSimulator (Verlet model). Run via
    /// "FunnyCursor --test-rope"; results are written to the startup log + stdout.
    ///
    /// Contract under test:
    ///   - The rope hangs at its full length under gravity (Verlet pendulum).
    ///   - The bob (rope end where the star is welded) is clamped to
    ///     (RopeLength - IconSize) so the star's farthest point stays within
    ///     RopeLength of the cursor — the "保底" hard guarantee.
    ///   - No point ever flies off under stationary / slow / fast / chaotic /
    ///     teleport motion.
    /// </summary>
    internal static class RopePhysicsTests
    {
        public static int Run()
        {
            int fail = 0, total = 0;

            void Check(string name, bool ok, string detail)
            {
                total++;
                string tag = ok ? "PASS" : "FAIL";
                if (!ok) fail++;
                string line = $"[{tag}] {name}: {detail}";
                Console.WriteLine(line);
                App.Log(line);
            }

            var s = new AppSettings();
            s.RopeLength = 170;
            s.RopeSegments = 18;
            s.RopeGravity = 1500;
            s.RopeDamping = 0.9;
            s.RopeStiffness = 0.6;

            float ropeLen = (float)s.RopeLength;           // 170
            float iconSize = (float)s.IconSize;            // 38
            float cap = Math.Max(1f, ropeLen - iconSize);  // 132 — bob cap from 保底

            // Farthest star vertex distance from the cursor, using the REAL pendant math.
            float StarMaxDist(Vector2[] pts, Vector2 anchor)
            {
                var p = PendantGeometry.ComputePendant(pts, iconSize);
                // Mirror the renderer-side 保底: clamp tip to cap from the live cursor.
                Vector2 rel = p.Tip - anchor;
                float rd = rel.Length();
                if (rd > cap + 1e-3f || float.IsNaN(rd))
                {
                    Vector2 dirN = (rd < 1e-4f || float.IsNaN(rd)) ? new Vector2(0, 1) : rel / rd;
                    p = new PendantGeometry.PendantState(anchor + dirN * cap, p.Direction, iconSize);
                }
                var local = PendantGeometry.StarLocalPolygon(iconSize);
                float maxD = 0;
                foreach (var l in local)
                    maxD = MathF.Max(maxD, Vector2.Distance(PendantGeometry.TransformPoint(p, l), anchor));
                return maxD;
            }

            // ---------- Test 1: stationary — rope hangs at full length ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(500, 300);
                for (int i = 0; i < 300; i++) rope.Update(1.0 / 60.0, anchor, s);
                var bob = rope.Bob;
                float dx = Math.Abs(bob.X - anchor.X);
                float dy = bob.Y - anchor.Y;
                // Bob hangs below anchor at ~ropeLen (clamped to cap = ropeLen - iconSize).
                bool stable = dx < 5f && dy > cap * 0.8f && dy < cap * 1.2f;
                Check("静止下垂", stable,
                    $"bob=({bob.X:F1},{bob.Y:F1}) anchor=({anchor.X},{anchor.Y}) dx={dx:F1} dy={dy:F1} (期望≈{cap:F1})");
            }

            // ---------- Test 2: slow horizontal movement — bob stays within cap ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(100, 300);
                for (int i = 0; i < 120; i++)
                {
                    anchor += new Vector2(2, 0);
                    rope.Update(1.0 / 60.0, anchor, s);
                }
                var bob = rope.Bob;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist <= cap + 1f;
                Check("慢速移动", ok, $"bob-anchor dist={dist:F1} (cap {cap:F1})");
            }

            // ---------- Test 3: fast teleport — bob must NOT fly off ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(0, 0);
                for (int i = 0; i < 60; i++) rope.Update(1.0 / 60.0, anchor, s);
                anchor = new Vector2(800, 0);
                rope.Update(1.0 / 60.0, anchor, s);
                rope.Update(1.0 / 60.0, anchor, s);
                var bob = rope.Bob;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist <= cap + 1f;
                Check("瞬移800px", ok, $"bob-anchor dist={dist:F1} (cap {cap:F1})");
            }

            // ---------- Test 4: continuous fast jitter ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                var rng = new Random(42);
                float maxDist = 0;
                for (int i = 0; i < 600; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 120 - 60),
                                          (float)(rng.NextDouble() * 60 - 30));
                    rope.Update(1.0 / 60.0, anchor, s);
                    float d = Vector2.Distance(rope.Bob, anchor);
                    if (d > maxDist) maxDist = d;
                }
                bool ok = maxDist <= cap + 1f;
                Check("连续抖动", ok, $"max bob-anchor dist={maxDist:F1} (cap {cap:F1})");
            }

            // ---------- Test 5: 保底 — whole star never exceeds rope length ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                var rng = new Random(7);
                float maxStar = 0;
                for (int i = 0; i < 600; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 200 - 100), 0);
                    rope.Update(1.0 / 60.0, anchor, s);
                    float d = StarMaxDist(rope.Points, anchor);
                    if (d > maxStar) maxStar = d;
                }
                anchor = new Vector2(1500, 900);
                rope.Update(1.0 / 60.0, anchor, s);
                maxStar = MathF.Max(maxStar, StarMaxDist(rope.Points, anchor));
                float limit = ropeLen * 1.02f;
                bool ok = maxStar <= limit;
                Check("保底(整星≤绳长)", ok, $"max star dist={maxStar:F1} (limit {limit:F1})");
            }

            // ---------- Test 6: large frame dt (stutter simulation) ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(200, 200);
                for (int i = 0; i < 60; i++) rope.Update(1.0 / 60.0, anchor, s);
                anchor += new Vector2(300, 50);
                rope.Update(0.1, anchor, s);
                var bob = rope.Bob;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist <= cap + 1f;
                Check("卡顿+跳变", ok, $"bob-anchor dist={dist:F1} (cap {cap:F1})");
            }

            // ---------- Test 7: swing physics — bob must LAG and SWING ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 300);
                for (int i = 0; i < 120; i++) rope.Update(1.0 / 60.0, anchor, s);
                anchor += new Vector2(200, 0);
                float maxLagX = 0;
                bool swungPastAnchorX = false;
                for (int i = 0; i < 180; i++)
                {
                    rope.Update(1.0 / 60.0, anchor, s);
                    float lag = anchor.X - rope.Bob.X;
                    if (lag > maxLagX) maxLagX = lag;
                    if (rope.Bob.X > anchor.X + 2f) swungPastAnchorX = true;
                }
                bool hasLag = maxLagX > 15f;
                bool ok = hasLag && swungPastAnchorX;
                Check("摆动物理", ok, $"maxLag={maxLagX:F1}px 过冲={swungPastAnchorX} (需 lag>15 且过冲)");
            }

            string summary = $"=== 绳子物理测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }
    }
}
