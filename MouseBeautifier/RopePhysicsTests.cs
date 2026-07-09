using System;
using System.Numerics;

namespace MouseBeautifier
{
    /// <summary>
    /// Self-contained unit tests for RopeSimulator. Run via "FunnyCursor --test-rope";
    /// results are written to the startup log + stdout. Verifies the rope stays
    /// within sane bounds under stationary, slow, fast, and chaotic cursor motion.
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

            // ---------- Test 1: stationary — rope should hang straight down ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(500, 300);
                for (int i = 0; i < 300; i++) rope.Update(1.0 / 60.0, anchor, s);
                var bob = rope.Bob;
                float dx = Math.Abs(bob.X - anchor.X);
                float dy = bob.Y - anchor.Y;
                bool stable = dx < 5f && dy > (s.RopeLength * 0.8) && dy < (s.RopeLength * 1.1);
                Check("静止下垂", stable, $"bob=({bob.X:F1},{bob.Y:F1}) anchor=({anchor.X},{anchor.Y}) dx={dx:F1} dy={dy:F1}");
            }

            // ---------- Test 2: slow horizontal movement — bob trails behind ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(100, 300);
                for (int i = 0; i < 120; i++)
                {
                    anchor += new Vector2(2, 0); // 120 px/s
                    rope.Update(1.0 / 60.0, anchor, s);
                }
                var bob = rope.Bob;
                float maxLen = (float)s.RopeLength * 1.15f;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist < maxLen;
                Check("慢速移动", ok, $"bob-anchor dist={dist:F1} (max {maxLen:F1})");
            }

            // ---------- Test 3: fast teleport — bob must NOT fly off ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(0, 0);
                for (int i = 0; i < 60; i++) rope.Update(1.0 / 60.0, anchor, s); // settle
                // Teleport cursor 800px right in one frame.
                anchor = new Vector2(800, 0);
                rope.Update(1.0 / 60.0, anchor, s);
                rope.Update(1.0 / 60.0, anchor, s);
                var bob = rope.Bob;
                float maxLen = (float)s.RopeLength * 1.2f;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist < maxLen;
                Check("瞬移800px", ok, $"bob-anchor dist={dist:F1} (max {maxLen:F1})");
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
                float limit = (float)s.RopeLength * 1.25f;
                bool ok = maxDist < limit;
                Check("连续抖动", ok, $"max bob-anchor dist={maxDist:F1} (limit {limit:F1})");
            }

            // ---------- Test 5: segment lengths stay near segLen ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(300, 200);
                var rng = new Random(7);
                for (int i = 0; i < 300; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 200 - 100), 0);
                    rope.Update(1.0 / 60.0, anchor, s);
                }
                var pts = rope.Points;
                float segLen = (float)(s.RopeLength / s.RopeSegments);
                float maxStretch = 0;
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float d = Vector2.Distance(pts[i], pts[i + 1]);
                    float stretch = Math.Abs(d - segLen) / segLen;
                    if (stretch > maxStretch) maxStretch = stretch;
                }
                bool ok = maxStretch < 0.3f; // <30% stretch
                Check("段长稳定性", ok, $"max stretch={maxStretch * 100:F1}% (segLen={segLen:F1})");
            }

            // ---------- Test 6: large frame dt (stutter simulation) ----------
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(200, 200);
                for (int i = 0; i < 60; i++) rope.Update(1.0 / 60.0, anchor, s);
                // Simulate a 100ms stutter with cursor jumping 300px.
                anchor += new Vector2(300, 50);
                rope.Update(0.1, anchor, s);
                var bob = rope.Bob;
                float maxLen = (float)s.RopeLength * 1.3f;
                float dist = Vector2.Distance(bob, anchor);
                bool ok = dist < maxLen;
                Check("卡顿+跳变", ok, $"bob-anchor dist={dist:F1} (max {maxLen:F1})");
            }

            string summary = $"=== 绳子物理测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }
    }
}
