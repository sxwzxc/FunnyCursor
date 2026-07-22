using System;
using System.Numerics;
using MouseBeautifier.Core;

namespace MouseBeautifier
{
    /// <summary>
    /// Extreme-motion stress tests for the rope physics. Simulates the exact
    /// failure modes reported by users: rapid upward cursor movement, violent
    /// diagonal jitter, and instantaneous teleportation. Verifies:
    ///   - No point ever contains NaN or Infinity
    ///   - No point ever flies beyond ropeLen * 2 from the anchor
    ///   - The bob never goes ABOVE the anchor (rope can't swing past vertical)
    /// Run via "FunnyCursor.exe --test-stress".
    /// </summary>
    internal static class StressTests
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

            float ropeLen = (float)s.RopeLength;
            float hardLimit = ropeLen * 2f; // absolute max: 2x rope length

            // ---------- Test 1: rapid upward movement ----------
            // Cursor moves up 50px/frame for 3 seconds. The rope must NOT fly
            // above the anchor — gravity keeps it below.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(500, 600);
                bool anyNaN = false;
                bool anyAbove = false;
                float maxBobY = 0;
                for (int i = 0; i < 180; i++)
                {
                    anchor.Y -= 50f; // move UP 50px/frame (3000px/s)
                    rope.Update(1.0 / 60.0, anchor, s);
                    if (HasNaN(rope)) anyNaN = true;
                    var bob = rope.Bob;
                    if (bob.Y < anchor.Y - 5f) anyAbove = true; // bob above anchor
                    if (bob.Y > maxBobY) maxBobY = bob.Y;
                    // check all points within hard limit
                    for (int p = 0; p < rope.Count; p++)
                    {
                        float d = Vector2.Distance(rope.Points[p], anchor);
                        if (d > hardLimit) anyAbove = true;
                    }
                }
                bool ok = !anyNaN && !anyAbove;
                Check("快速向上移动", ok, $"NaN={anyNaN} 飞出={anyAbove} maxBobY={maxBobY:F1}");
            }

            // ---------- Test 2: violent diagonal jitter ----------
            // Random ±300px/frame movement — far beyond any real mouse speed.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(960, 540);
                var rng = new Random(42);
                bool anyNaN = false;
                bool anyOutOfRange = false;
                float maxDist = 0;
                for (int i = 0; i < 600; i++)
                {
                    anchor.X += (float)(rng.NextDouble() * 600 - 300);
                    anchor.Y += (float)(rng.NextDouble() * 600 - 300);
                    rope.Update(1.0 / 60.0, anchor, s);
                    if (HasNaN(rope)) { anyNaN = true; break; }
                    for (int p = 0; p < rope.Count; p++)
                    {
                        float d = Vector2.Distance(rope.Points[p], anchor);
                        if (d > maxDist) maxDist = d;
                        if (d > hardLimit) anyOutOfRange = true;
                    }
                }
                bool ok = !anyNaN && !anyOutOfRange;
                Check("剧烈对角抖动", ok, $"NaN={anyNaN} 超范围={anyOutOfRange} maxDist={maxDist:F1} (limit {hardLimit:F1})");
            }

            // ---------- Test 3: instantaneous teleport ----------
            // Cursor jumps 2000px in a single frame, repeatedly.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(100, 100);
                bool anyNaN = false;
                bool anyOutOfRange = false;
                float maxDist = 0;
                for (int i = 0; i < 60; i++)
                {
                    anchor += new Vector2(2000, 500);
                    rope.Update(1.0 / 60.0, anchor, s);
                    if (HasNaN(rope)) { anyNaN = true; break; }
                    var bob = rope.Bob;
                    float d = Vector2.Distance(bob, anchor);
                    if (d > maxDist) maxDist = d;
                    if (d > hardLimit) anyOutOfRange = true;
                }
                bool ok = !anyNaN && !anyOutOfRange;
                Check("瞬移2000px×60", ok, $"NaN={anyNaN} 超范围={anyOutOfRange} maxDist={maxDist:F1} (limit {hardLimit:F1})");
            }

            // ---------- Test 4: circular motion at high speed ----------
            // Simulates a user spinning the mouse in fast circles.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var center = new Vector2(960, 540);
                bool anyNaN = false;
                bool anyOutOfRange = false;
                float maxDist = 0;
                for (int i = 0; i < 600; i++)
                {
                    float ang = i * 0.3f;
                    var anchor = center + new Vector2((float)Math.Cos(ang) * 400, (float)Math.Sin(ang) * 400);
                    rope.Update(1.0 / 60.0, anchor, s);
                    if (HasNaN(rope)) { anyNaN = true; break; }
                    for (int p = 0; p < rope.Count; p++)
                    {
                        float d = Vector2.Distance(rope.Points[p], anchor);
                        if (d > maxDist) maxDist = d;
                        if (d > hardLimit) anyOutOfRange = true;
                    }
                }
                bool ok = !anyNaN && !anyOutOfRange;
                Check("高速圆周运动", ok, $"NaN={anyNaN} 超范围={anyOutOfRange} maxDist={maxDist:F1} (limit {hardLimit:F1})");
            }

            // ---------- Test 5: stutter (large dt) + fast movement ----------
            // Simulates frame drops where dt spikes to 100ms while cursor moves fast.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(500, 500);
                bool anyNaN = false;
                bool anyOutOfRange = false;
                float maxDist = 0;
                for (int i = 0; i < 120; i++)
                {
                    anchor += new Vector2(150, -100);
                    double dt = (i % 10 == 0) ? 0.1 : 1.0 / 60.0; // periodic stutter
                    rope.Update(dt, anchor, s);
                    if (HasNaN(rope)) { anyNaN = true; break; }
                    for (int p = 0; p < rope.Count; p++)
                    {
                        float d = Vector2.Distance(rope.Points[p], anchor);
                        if (d > maxDist) maxDist = d;
                        if (d > hardLimit) anyOutOfRange = true;
                    }
                }
                bool ok = !anyNaN && !anyOutOfRange;
                Check("卡顿+快速移动", ok, $"NaN={anyNaN} 超范围={anyOutOfRange} maxDist={maxDist:F1} (limit {hardLimit:F1})");
            }

            // ---------- Test 6: rope settles below anchor when cursor stops ----------
            // After vigorous motion + a settling period, all points must hang
            // BELOW the anchor (gravity). During motion the rope can lag above
            // the anchor (correct pendulum behavior), but once the cursor stops
            // it must settle down.
            {
                var rope = new RopeSimulator();
                rope.ApplySettings(s);
                var anchor = new Vector2(400, 400);
                var rng = new Random(99);
                // vigorous motion phase
                for (int i = 0; i < 300; i++)
                {
                    anchor += new Vector2((float)(rng.NextDouble() * 200 - 100), (float)(rng.NextDouble() * 100 - 50));
                    rope.Update(1.0 / 60.0, anchor, s);
                }
                // settling phase: cursor holds still
                for (int i = 0; i < 180; i++)
                    rope.Update(1.0 / 60.0, anchor, s);

                bool allBelow = true;
                float maxAbove = 0;
                for (int p = 1; p < rope.Count; p++)
                {
                    float above = anchor.Y - rope.Points[p].Y;
                    if (above > maxAbove) maxAbove = above;
                    if (above > 2f) allBelow = false;
                }
                bool ok = allBelow;
                Check("静止后下垂", ok, $"maxAbove={maxAbove:F1}px (需 <2px)");
            }

            string summary = $"=== 极限压力测试完成: {total - fail}/{total} 通过, {fail} 失败 ===";
            Console.WriteLine(summary);
            App.Log(summary);
            return fail;
        }

        private static bool HasNaN(RopeSimulator rope)
        {
            for (int i = 0; i < rope.Count; i++)
            {
                var p = rope.Points[i];
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) ||
                    float.IsInfinity(p.X) || float.IsInfinity(p.Y))
                    return true;
            }
            return false;
        }
    }
}
